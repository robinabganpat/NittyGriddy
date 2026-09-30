using App.Services;

namespace NittyGriddy.Tests;

public class SetupGuideTests
{
    private static readonly GuideFacts FreshInstall = new()
    {
        WatchedClients = new[] { "GGPoker", "HC Online (iPoker)", "CoinPoker" },
        SlotCount = 6,
        GoToSlot1 = new GuideHotkey("Alt+1"),
        GoToSlot2 = new GuideHotkey("Alt+2"),
        NextTable = new GuideHotkey("Alt+PgDn"),
    };

    private static GuideStep Step(GuideFacts facts, GuideStepId id) => SetupGuide.Build(facts).Single(s => s.Id == id);

    private static GuideStepId Current(GuideFacts facts) => SetupGuide.Current(SetupGuide.Build(facts));

    [Fact]
    public void Steps_come_in_the_order_they_are_done()
    {
        Assert.Equal(
            new[] { GuideStepId.OpenTable, GuideStepId.UseGrid, GuideStepId.Hotkey, GuideStepId.KeepRunning },
            SetupGuide.Build(FreshInstall).Select(s => s.Id));
    }

    [Fact]
    public void A_fresh_install_starts_at_opening_a_table_and_names_the_watched_clients()
    {
        var step = Step(FreshInstall, GuideStepId.OpenTable);

        Assert.Equal(GuideStepId.OpenTable, Current(FreshInstall));
        Assert.False(step.Done);
        Assert.Contains("GGPoker, HC Online (iPoker) or CoinPoker", step.Detail);
    }

    [Fact]
    public void A_running_client_without_tables_is_named_and_points_at_the_rule()
    {
        var step = Step(FreshInstall with { ClientsWithoutTables = new[] { "GGPoker" } }, GuideStepId.OpenTable);

        Assert.False(step.Done);
        Assert.StartsWith("GGPoker is running, but none of its windows is a table.", step.Detail);
        Assert.Contains("rule", step.Note);
        Assert.Equal(GuideAction.OpenTablesPage, step.Action);
    }

    [Fact]
    public void With_no_rule_switched_on_the_first_step_says_nothing_is_watched()
    {
        var step = Step(FreshInstall with { WatchedClients = Array.Empty<string>() }, GuideStepId.OpenTable);

        Assert.False(step.Done);
        Assert.Equal(GuideAction.OpenTablesPage, step.Action);
        Assert.Contains("No program is switched on", step.Detail);
    }

    [Fact]
    public void Found_tables_complete_the_first_step_and_move_on_to_the_grid()
    {
        var facts = FreshInstall with { TableCount = 3, TablesOutsideGrid = 3 };

        Assert.True(Step(facts, GuideStepId.OpenTable).Done);
        Assert.Equal("3 tables found", Step(facts, GuideStepId.OpenTable).Status);
        Assert.Equal(GuideStepId.UseGrid, Current(facts));
    }

    [Fact]
    public void Turning_the_grid_on_says_that_it_moves_the_tables()
    {
        var step = Step(FreshInstall with { TableCount = 3, TablesOutsideGrid = 3 }, GuideStepId.UseGrid);

        Assert.Equal(GuideAction.TurnGridOn, step.Action);
        Assert.Equal("Turn on and arrange", step.ActionLabel);
        Assert.Contains("moves your 3 tables", step.Detail);
    }

    [Fact]
    public void Without_tables_the_grid_can_still_be_turned_on_and_nothing_is_said_to_move()
    {
        var step = Step(FreshInstall, GuideStepId.UseGrid);

        Assert.Equal(GuideAction.TurnGridOn, step.Action);
        Assert.Equal("Turn the grid on", step.ActionLabel);
        Assert.DoesNotContain("moves", step.Detail);
    }

    [Fact]
    public void A_grid_that_is_on_with_tables_outside_it_asks_to_arrange()
    {
        var step = Step(FreshInstall with { GridEnabled = true, TableCount = 3, TablesOutsideGrid = 2 }, GuideStepId.UseGrid);

        Assert.False(step.Done);
        Assert.Equal(GuideAction.Arrange, step.Action);
        Assert.Equal("2 not in a slot", step.Status);
    }

    [Fact]
    public void The_grid_step_is_done_when_the_grid_is_on_and_every_table_has_a_slot()
    {
        Assert.True(Step(FreshInstall with { GridEnabled = true, TableCount = 3 }, GuideStepId.UseGrid).Done);
        Assert.True(Step(FreshInstall with { GridEnabled = true }, GuideStepId.UseGrid).Done);
    }

    [Fact]
    public void A_layout_without_slots_sends_the_user_to_the_layout_page()
    {
        var step = Step(FreshInstall with { SlotCount = 0, TableCount = 2, TablesOutsideGrid = 2 }, GuideStepId.UseGrid);

        Assert.False(step.Done);
        Assert.Equal(GuideAction.OpenLayoutPage, step.Action);
    }

    [Fact]
    public void More_slots_than_slot_hotkeys_is_mentioned_before_the_tables_are_spread_out()
    {
        Assert.Contains("18 slots", Step(FreshInstall with { SlotCount = 18 }, GuideStepId.UseGrid).Note);
        Assert.Equal(string.Empty, Step(FreshInstall with { SlotCount = 9 }, GuideStepId.UseGrid).Note);
    }

    private static (int, GuideHotkey)[] Slots(params int[] slots) => slots.Select(s => (s, new GuideHotkey($"Alt+{s}"))).ToArray();

    [Fact]
    public void The_hotkey_step_asks_for_the_second_occupied_slot_or_the_only_one()
    {
        var inGrid = FreshInstall with { GridEnabled = true };

        Assert.StartsWith("Press Alt+2 now. The table in slot 2",
            Step(inGrid with { TableCount = 2, OccupiedSlotHotkeys = Slots(1, 2) }, GuideStepId.Hotkey).Detail);
        Assert.StartsWith("Press Alt+1 now. The table in slot 1",
            Step(inGrid with { TableCount = 1, OccupiedSlotHotkeys = Slots(1) }, GuideStepId.Hotkey).Detail);
    }

    [Fact]
    public void The_hotkey_step_never_asks_for_a_slot_that_holds_no_table()
    {
        var inGrid = FreshInstall with { GridEnabled = true };

        // Two tables stacked in slot 1: slot 2 is empty
        Assert.StartsWith("Press Alt+1 now.",
            Step(inGrid with { TableCount = 2, OccupiedSlotHotkeys = Slots(1) }, GuideStepId.Hotkey).Detail);

        // Tables pinned or dragged to slots 4 and 7
        Assert.StartsWith("Press Alt+7 now. The table in slot 7",
            Step(inGrid with { TableCount = 2, OccupiedSlotHotkeys = Slots(4, 7) }, GuideStepId.Hotkey).Detail);

        // Tables only in slots beyond the ninth, which have no hotkey
        Assert.StartsWith("Press Alt+PgDn now. The next table",
            Step(inGrid with { TableCount = 2 }, GuideStepId.Hotkey).Detail);
    }

    [Fact]
    public void The_hotkey_step_does_not_ask_for_a_key_press_before_tables_are_in_the_grid()
    {
        Assert.DoesNotContain("now", Step(FreshInstall with { TableCount = 2, TablesOutsideGrid = 2 }, GuideStepId.Hotkey).Detail);
        Assert.DoesNotContain("now", Step(FreshInstall with { GridEnabled = true, TableCount = 2, TablesOutsideGrid = 2 }, GuideStepId.Hotkey).Detail);
    }

    [Fact]
    public void A_slot_hotkey_that_is_taken_falls_back_to_next_table()
    {
        var facts = FreshInstall with
        {
            GridEnabled = true,
            TableCount = 2,
            GoToSlot1 = new GuideHotkey("Alt+1", "In use by another program"),
            GoToSlot2 = new GuideHotkey("Alt+2", "In use by another program"),
            OccupiedSlotHotkeys = new[]
            {
                (1, new GuideHotkey("Alt+1", "In use by another program")),
                (2, new GuideHotkey("Alt+2", "In use by another program")),
            },
        };

        Assert.StartsWith("Press Alt+PgDn now. The next table", Step(facts, GuideStepId.Hotkey).Detail);
    }

    [Fact]
    public void With_no_working_table_hotkey_the_step_says_why_and_opens_the_hotkeys_page()
    {
        var taken = Step(FreshInstall with
        {
            GoToSlot1 = new GuideHotkey("Alt+1", "In use by another program"),
            GoToSlot2 = GuideHotkey.Unset,
            NextTable = GuideHotkey.Unset,
        }, GuideStepId.Hotkey);

        Assert.True(taken.Caution);
        Assert.Equal(GuideAction.OpenHotkeysPage, taken.Action);
        Assert.Equal("Alt+1 cannot be used: in use by another program. Choose other keys on Hotkeys.", taken.Note);

        var unset = Step(FreshInstall with { GoToSlot1 = GuideHotkey.Unset, GoToSlot2 = GuideHotkey.Unset, NextTable = GuideHotkey.Unset },
            GuideStepId.Hotkey);

        Assert.StartsWith("No key is set", unset.Note);
    }

    [Fact]
    public void Using_a_hotkey_completes_the_step_and_leaves_only_the_closing_choice()
    {
        var facts = FreshInstall with { GridEnabled = true, TableCount = 2, HotkeyUsed = true };

        Assert.True(Step(facts, GuideStepId.Hotkey).Done);
        Assert.Equal(GuideStepId.KeepRunning, Current(facts));
    }

    [Fact]
    public void The_last_step_says_what_closing_the_window_does_and_finishes_the_guide()
    {
        var quits = Step(FreshInstall, GuideStepId.KeepRunning);
        var stays = Step(FreshInstall with { CloseToTray = true }, GuideStepId.KeepRunning);

        Assert.Contains("quits it", quits.Detail);
        Assert.Contains("notification area", stays.Detail);
        Assert.Equal(GuideAction.Finish, quits.Action);
        Assert.False(stays.Done);
    }

    [Fact]
    public void Closing_the_tables_after_a_hotkey_worked_does_not_send_the_guide_back_to_the_start()
    {
        var facts = FreshInstall with { GridEnabled = true, HotkeyUsed = true };

        Assert.False(Step(facts, GuideStepId.OpenTable).Done);
        Assert.Equal(GuideStepId.KeepRunning, Current(facts));
    }
}
