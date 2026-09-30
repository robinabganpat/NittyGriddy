using System;
using System.Collections.Generic;
using System.Linq;
using App.Models;

namespace App.Services
{
    public enum GuideStepId
    {
        OpenTable,
        UseGrid,
        Hotkey,
        KeepRunning
    }

    /// <summary>
    /// What a button in the guide does
    /// </summary>
    public enum GuideAction
    {
        None,
        OpenLayoutPage,
        OpenTablesPage,
        OpenHotkeysPage,
        TurnGridOn,
        Arrange,
        Finish
    }

    /// <summary>
    /// A hotkey as the guide needs it: the keys, and why they do not work if they do not
    /// </summary>
    public sealed record GuideHotkey(string Gesture, string? Problem = null)
    {
        public static readonly GuideHotkey Unset = new(string.Empty);

        public bool Works => Gesture.Length > 0 && Problem == null;
    }

    /// <summary>
    /// The state the guide is derived from
    /// </summary>
    public sealed record GuideFacts
    {
        /// <summary>
        /// Open windows recognised as tables, in a slot or not
        /// </summary>
        public int TableCount { get; init; }

        public int TablesOutsideGrid { get; init; }

        /// <summary>
        /// Names of the rules that are switched on
        /// </summary>
        public IReadOnlyList<string> WatchedClients { get; init; } = Array.Empty<string>();

        /// <summary>
        /// Watched programs that have windows open, none of which is a table
        /// </summary>
        public IReadOnlyList<string> ClientsWithoutTables { get; init; } = Array.Empty<string>();

        public bool GridEnabled { get; init; }
        public int SlotCount { get; init; }

        public GuideHotkey GoToSlot1 { get; init; } = GuideHotkey.Unset;
        public GuideHotkey GoToSlot2 { get; init; } = GuideHotkey.Unset;
        public GuideHotkey NextTable { get; init; } = GuideHotkey.Unset;

        /// <summary>
        /// The slots that hold a table and have a hotkey, lowest first, with their "go to" key
        /// </summary>
        public IReadOnlyList<(int Slot, GuideHotkey Key)> OccupiedSlotHotkeys { get; init; } = Array.Empty<(int, GuideHotkey)>();

        public bool HotkeyUsed { get; init; }
        public bool CloseToTray { get; init; }
    }

    /// <summary>
    /// One step of the guide, ready to show
    /// </summary>
    /// <param name="Status">A few words under the title: where this step stands</param>
    /// <param name="Detail">What to do, or what happened</param>
    /// <param name="Note">Something to know before acting; empty if nothing</param>
    /// <param name="Caution">The note is a problem that needs attention, not just information</param>
    public sealed record GuideStep(
        GuideStepId Id,
        string Title,
        string Status,
        bool Done,
        string Detail,
        string Note = "",
        bool Caution = false,
        GuideAction Action = GuideAction.None,
        string ActionLabel = "",
        GuideAction SecondaryAction = GuideAction.None,
        string SecondaryLabel = "");

    /// <summary>
    /// The getting-started guide: the shortest way from a fresh install to tables in numbered slots that a hotkey
    /// can reach. Pure: every step is derived from the state of the application, so the guide follows what the
    /// user does instead of walking them through screens.
    /// </summary>
    public static class SetupGuide
    {
        public static IReadOnlyList<GuideStep> Build(GuideFacts facts)
        {
            return new[] { OpenTable(facts), UseGrid(facts), Hotkey(facts), KeepRunning(facts) };
        }

        /// <summary>
        /// The step to work on: the first one not done. Once a hotkey has worked the user has seen what the
        /// product does, so only the closing choice is left, whether or not tables are open at that moment.
        /// </summary>
        public static GuideStepId Current(IReadOnlyList<GuideStep> steps)
        {
            if (steps.Any(s => s.Id == GuideStepId.Hotkey && s.Done))
                return GuideStepId.KeepRunning;

            return steps.FirstOrDefault(s => !s.Done)?.Id ?? GuideStepId.KeepRunning;
        }

        private static GuideStep OpenTable(GuideFacts f)
        {
            const string title = "Open a table";

            if (f.TableCount > 0)
            {
                return new GuideStep(GuideStepId.OpenTable, title, $"{Count(f.TableCount, "table")} found", Done: true,
                    f.TableCount == 1
                        ? "NittyGriddy recognises 1 open table. Lobbies and other windows are left alone."
                        : $"NittyGriddy recognises {f.TableCount} open tables. Lobbies and other windows are left alone.",
                    SecondaryAction: GuideAction.OpenTablesPage, SecondaryLabel: "See them on Tables");
            }

            if (f.WatchedClients.Count == 0)
            {
                return new GuideStep(GuideStepId.OpenTable, title, "Nothing is watched", Done: false,
                    "No program is switched on, so no window counts as a table. Switch a poker client on, or add your own program, on Tables.",
                    Action: GuideAction.OpenTablesPage, ActionLabel: "Open Tables");
            }

            if (f.ClientsWithoutTables.Count > 0)
            {
                var clients = JoinNames(f.ClientsWithoutTables, "and");
                var verb = f.ClientsWithoutTables.Count == 1 ? "is" : "are";
                var its = f.ClientsWithoutTables.Count == 1 ? "its" : "their";

                return new GuideStep(GuideStepId.OpenTable, title, "No table yet", Done: false,
                    $"{clients} {verb} running, but none of {its} windows is a table. Open a table and it shows up here within a second.",
                    Note: "Is a table already open? Then the rule for that client misses it. Tables lists the windows that were left out.",
                    Action: GuideAction.OpenTablesPage, ActionLabel: "Open Tables");
            }

            return new GuideStep(GuideStepId.OpenTable, title, "No table yet", Done: false,
                $"Open a table in {JoinNames(f.WatchedClients, "or")}. It shows up here within a second; there is nothing to set up.",
                Note: "Playing somewhere else? Add that program on Tables.",
                SecondaryAction: GuideAction.OpenTablesPage, SecondaryLabel: "Open Tables");
        }

        private static GuideStep UseGrid(GuideFacts f)
        {
            const string title = "Put tables in the grid";

            if (f.SlotCount == 0)
            {
                return new GuideStep(GuideStepId.UseGrid, title, "No slots", Done: false,
                    "No display has a grid, so there is no slot to put a table in. Give at least one display a grid on Layout.",
                    Action: GuideAction.OpenLayoutPage, ActionLabel: "Open Layout");
            }

            // Only slots 1 to 9 can be reached with a hotkey; worth knowing before the tables are spread out
            var note = f.SlotCount > HotkeyActions.SlotHotkeyCount
                ? $"This layout has {f.SlotCount} slots; hotkeys reach slots 1 to {HotkeyActions.SlotHotkeyCount}. Fewer, larger slots are set on Layout."
                : string.Empty;

            if (!f.GridEnabled)
            {
                return f.TableCount > 0
                    ? new GuideStep(GuideStepId.UseGrid, title, "Grid is off", Done: false,
                        $"Turning the grid on moves your {Count(f.TableCount, "table")} into {(f.TableCount == 1 ? "a slot" : "slots")} and resizes {(f.TableCount == 1 ? "it" : "them")} to fit. After that, a new table takes the first free slot by itself.",
                        note,
                        Action: GuideAction.TurnGridOn, ActionLabel: "Turn on and arrange",
                        SecondaryAction: GuideAction.OpenLayoutPage, SecondaryLabel: "Change the layout")
                    : new GuideStep(GuideStepId.UseGrid, title, "Grid is off", Done: false,
                        $"The grid has {Count(f.SlotCount, "slot")}. Turn it on now and each table takes the first free slot as it opens.",
                        note,
                        Action: GuideAction.TurnGridOn, ActionLabel: "Turn the grid on",
                        SecondaryAction: GuideAction.OpenLayoutPage, SecondaryLabel: "Change the layout");
            }

            if (f.TablesOutsideGrid > 0)
            {
                return new GuideStep(GuideStepId.UseGrid, title, $"{f.TablesOutsideGrid} not in a slot", Done: false,
                    f.TablesOutsideGrid == 1
                        ? "The grid is on, but 1 table is not in a slot. Arrange puts every table in a slot, filling the grid from slot 1."
                        : $"The grid is on, but {f.TablesOutsideGrid} tables are not in a slot. Arrange puts every table in a slot, filling the grid from slot 1.",
                    note,
                    Action: GuideAction.Arrange, ActionLabel: "Arrange tables",
                    SecondaryAction: GuideAction.OpenLayoutPage, SecondaryLabel: "Change the layout");
            }

            return new GuideStep(GuideStepId.UseGrid, title, "Grid is on", Done: true,
                f.TableCount == 0
                    ? "The grid is on. Each table takes the first free slot as it opens."
                    : $"The grid is on and {(f.TableCount == 1 ? "your table is in its slot" : $"your {f.TableCount} tables are in their slots")}. Drag a table onto another slot to move it there.",
                note,
                SecondaryAction: GuideAction.OpenLayoutPage, SecondaryLabel: "Change the layout");
        }

        private static GuideStep Hotkey(GuideFacts f)
        {
            const string title = "Jump to a table";

            if (f.HotkeyUsed)
            {
                return new GuideStep(GuideStepId.Hotkey, title, "Tried", Done: true,
                    f.GoToSlot1.Works && f.GoToSlot2.Works
                        ? $"{f.GoToSlot1.Gesture} brings the table in slot 1 to the front, {f.GoToSlot2.Gesture} the one in slot 2, and so on up to slot {HotkeyActions.SlotHotkeyCount}. They work from any program while the grid is on."
                        : "Your table hotkeys work from any program while the grid is on.",
                    SecondaryAction: GuideAction.OpenHotkeysPage, SecondaryLabel: "Change the keys");
            }

            // Only a slot that holds a table can be asked for. Of those, the second shows more than the first:
            // the table in the first slot is often in front already.
            var reachable = f.OccupiedSlotHotkeys.Where(s => s.Key.Works).ToList();
            var (key, target) =
                reachable.Count > 0 ? (reachable[Math.Min(1, reachable.Count - 1)].Key, $"the table in slot {reachable[Math.Min(1, reachable.Count - 1)].Slot}")
                : f.TableCount > f.TablesOutsideGrid && f.NextTable.Works ? (f.NextTable, "the next table")
                : f.GoToSlot1.Works ? (f.GoToSlot1, "the table in slot 1")
                : f.NextTable.Works ? (f.NextTable, "the next table")
                : ((GuideHotkey?)null, string.Empty);

            if (key == null)
            {
                // Say what is wrong with the key the user would expect to work
                var broken = new[] { f.GoToSlot1, f.GoToSlot2, f.NextTable }.FirstOrDefault(k => k.Gesture.Length > 0);

                return new GuideStep(GuideStepId.Hotkey, title, "Needs a key", Done: false,
                    "A hotkey brings a table to the front from whatever program you are in.",
                    broken == null
                        ? "No key is set for going to a table. Set one on Hotkeys."
                        : $"{broken.Gesture} cannot be used: {LowerFirst(broken.Problem!)}. Choose other keys on Hotkeys.",
                    Caution: true,
                    Action: GuideAction.OpenHotkeysPage, ActionLabel: "Open Hotkeys");
            }

            var tablesInGrid = f.TableCount - f.TablesOutsideGrid;
            if (!f.GridEnabled || tablesInGrid == 0)
            {
                return new GuideStep(GuideStepId.Hotkey, title, key.Gesture, Done: false,
                    $"With tables in the grid, {key.Gesture} brings {target} to the front, from whatever program you are in.",
                    "Table hotkeys are only held while the grid is on, so they stay out of the way when you are not playing.",
                    SecondaryAction: GuideAction.OpenHotkeysPage, SecondaryLabel: "Change the keys");
            }

            return new GuideStep(GuideStepId.Hotkey, title, $"Press {key.Gesture}", Done: false,
                $"Press {key.Gesture} now. {UpperFirst(target)} comes to the front, from whatever program you are in.",
                "This step ticks itself once a hotkey has brought a table forward.",
                SecondaryAction: GuideAction.OpenHotkeysPage, SecondaryLabel: "Change the keys");
        }

        private static GuideStep KeepRunning(GuideFacts f)
        {
            const string title = "Leave it running";

            return f.CloseToTray
                ? new GuideStep(GuideStepId.KeepRunning, title, "Stays running", Done: false,
                    "NittyGriddy has to keep running while you play. Closing this window now leaves it in the notification area; exit from the menu of its icon there.",
                    Action: GuideAction.Finish, ActionLabel: "Finish")
                : new GuideStep(GuideStepId.KeepRunning, title, "Quits when closed", Done: false,
                    "NittyGriddy has to keep running while you play. As it is set now, closing this window quits it and the grid stops.",
                    Action: GuideAction.Finish, ActionLabel: "Finish");
        }

        private static string Count(int number, string noun) => number == 1 ? $"1 {noun}" : $"{number} {noun}s";

        /// <summary>
        /// "A", "A or B", "A, B or C"
        /// </summary>
        private static string JoinNames(IReadOnlyList<string> names, string conjunction)
        {
            return names.Count switch
            {
                0 => string.Empty,
                1 => names[0],
                _ => $"{string.Join(", ", names.Take(names.Count - 1))} {conjunction} {names[^1]}"
            };
        }

        private static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

        private static string UpperFirst(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
