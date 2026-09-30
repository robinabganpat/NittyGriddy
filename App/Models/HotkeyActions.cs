using System;
using System.Collections.Generic;
using System.Linq;

namespace App.Models
{
    public enum SlotActionKind
    {
        Focus,
        Move
    }

    /// <summary>
    /// Describes one bindable hotkey action
    /// </summary>
    /// <param name="AlwaysActive">Registered even while the grid is disabled</param>
    public sealed record HotkeyActionInfo(string Id, string Label, string Group, string DefaultGesture, bool AlwaysActive = false);

    /// <summary>
    /// Catalogue of hotkey actions and their default bindings
    /// </summary>
    public static class HotkeyActions
    {
        public const int SlotHotkeyCount = 9;

        public const string ToggleGrid = "ToggleGrid";
        public const string AutoArrange = "AutoArrange";
        public const string SnapActive = "SnapActive";
        public const string NextTable = "NextTable";
        public const string PreviousTable = "PreviousTable";
        public const string NextProfile = "NextProfile";
        public const string AllTablesToFront = "AllTablesToFront";

        private const string FocusSlotPrefix = "FocusSlot";
        private const string MoveToSlotPrefix = "MoveToSlot";

        public static string FocusSlot(int slotNumber) => FocusSlotPrefix + slotNumber;
        public static string MoveToSlot(int slotNumber) => MoveToSlotPrefix + slotNumber;

        // Defaults avoid Ctrl+Alt+<character key>: Ctrl+Alt is AltGr on many layouts (AltGr+5 types the euro sign on US-International)
        public static IReadOnlyList<HotkeyActionInfo> All { get; } = Build();

        private static List<HotkeyActionInfo> Build()
        {
            var actions = new List<HotkeyActionInfo>
            {
                new(ToggleGrid, "Turn grid on / off", "General", "Ctrl+Alt+G", AlwaysActive: true),
                // Unbound or Alt-based on purpose: Ctrl+Shift+S / Ctrl+Shift+A are everyday shortcuts in other
                // programs and would stop working there while the grid is on
                new(AutoArrange, "Arrange all tables", "General", "Alt+Home"),
                new(SnapActive, "Snap active table to nearest slot", "General", ""),
                new(NextProfile, "Switch to next profile", "General", ""),
                new(NextTable, "Next table", "Select table", "Alt+PgDn"),
                new(PreviousTable, "Previous table", "Select table", "Alt+PgUp"),
                // Alt+0 next to Alt+1..9: "all slots"
                new(AllTablesToFront, "Bring all tables to the front", "Select table", "Alt+0"),
            };

            for (var slot = 1; slot <= SlotHotkeyCount; slot++)
                actions.Add(new(FocusSlot(slot), $"Go to table in slot {slot}", "Select table", $"Alt+{slot}"));

            for (var slot = 1; slot <= SlotHotkeyCount; slot++)
                actions.Add(new(MoveToSlot(slot), $"Move active table to slot {slot}", "Move table", $"Alt+Shift+{slot}"));

            return actions;
        }

        public static HotkeyActionInfo? Find(string id) => All.FirstOrDefault(a => a.Id == id);

        /// <summary>
        /// Default bindings as action id to gesture text (empty text = unbound)
        /// </summary>
        public static Dictionary<string, string> Defaults() => All.ToDictionary(a => a.Id, a => a.DefaultGesture);

        /// <summary>
        /// Decode a slot action id into its kind and 1-based slot number
        /// </summary>
        public static bool TryGetSlot(string id, out SlotActionKind kind, out int slotNumber)
        {
            kind = SlotActionKind.Focus;
            slotNumber = 0;

            if (id.StartsWith(FocusSlotPrefix))
                return int.TryParse(id.AsSpan(FocusSlotPrefix.Length), out slotNumber);

            if (id.StartsWith(MoveToSlotPrefix))
            {
                kind = SlotActionKind.Move;
                return int.TryParse(id.AsSpan(MoveToSlotPrefix.Length), out slotNumber);
            }

            return false;
        }
    }
}
