using App.Services;

namespace NittyGriddy.Tests;

public class TableRegistryTests
{
    private static IntPtr H(int value) => new(value);

    private static TableRegistry Registry(params (string key, int count)[] groups)
    {
        var registry = new TableRegistry();
        registry.SetGroups(groups.Select(g => new SlotGroup(g.key, g.count)).ToList());
        return registry;
    }

    [Fact]
    public void Slots_are_numbered_consecutively_across_groups()
    {
        var registry = Registry(("A", 2), ("B", 3));

        Assert.Equal(5, registry.SlotCount);
        Assert.Equal("A", registry.GroupOf(1));
        Assert.Equal("B", registry.GroupOf(2));
        Assert.Equal(2, registry.FirstSlotOf("B"));
        Assert.Null(registry.GroupOf(5));
        Assert.Null(registry.GroupOf(-1));
    }

    [Fact]
    public void Place_moves_a_table_rather_than_duplicating_it()
    {
        var registry = Registry(("A", 3));

        registry.Place(H(1), 0);
        registry.Place(H(1), 2);

        Assert.Equal(2, registry.SlotOf(H(1)));
        Assert.Empty(registry.TablesIn(0));
        Assert.Equal(new[] { H(1) }, registry.TablesIn(2));
    }

    [Fact]
    public void Placing_in_the_same_slot_keeps_a_single_entry()
    {
        var registry = Registry(("A", 2));

        registry.Place(H(1), 1);
        registry.Place(H(1), 1);

        Assert.Single(registry.TablesIn(1));
    }

    [Fact]
    public void Place_outside_the_slot_range_throws()
    {
        var registry = Registry(("A", 2));

        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Place(H(1), 2));
    }

    [Fact]
    public void Unknown_table_has_no_slot()
    {
        var registry = Registry(("A", 2));

        Assert.Null(registry.SlotOf(H(99)));
        Assert.False(registry.Contains(H(99)));
        Assert.False(registry.Remove(H(99)));
    }

    [Fact]
    public void First_available_prefers_empty_then_least_stacked_then_lowest_index()
    {
        var registry = Registry(("A", 3));
        Assert.Equal(0, registry.FirstAvailableSlot());

        registry.Place(H(1), 0);
        registry.Place(H(2), 2);
        Assert.Equal(1, registry.FirstAvailableSlot());

        registry.Place(H(3), 1);
        registry.Place(H(4), 0);
        Assert.Equal(1, registry.FirstAvailableSlot());
    }

    [Fact]
    public void First_available_can_be_scoped_to_a_group()
    {
        var registry = Registry(("A", 2), ("B", 2));
        registry.Place(H(1), 2);

        Assert.Equal(3, registry.FirstAvailableSlot("B"));
        Assert.Null(registry.FirstAvailableSlot("Missing"));
    }

    [Fact]
    public void First_available_is_null_without_slots()
    {
        Assert.Null(Registry().FirstAvailableSlot());
    }

    [Fact]
    public void All_tables_are_in_slot_order_then_stack_order()
    {
        var registry = Registry(("A", 2), ("B", 1));
        registry.Place(H(5), 2);
        registry.Place(H(3), 0);
        registry.Place(H(4), 0);
        registry.Place(H(1), 1);

        Assert.Equal(new[] { H(3), H(4), H(1), H(5) }, registry.AllTables);
    }

    [Fact]
    public void Next_and_previous_wrap_around()
    {
        var registry = Registry(("A", 3));
        registry.Place(H(1), 0);
        registry.Place(H(2), 1);
        registry.Place(H(3), 2);

        Assert.Equal(H(2), registry.Next(H(1), +1));
        Assert.Equal(H(1), registry.Next(H(3), +1));
        Assert.Equal(H(3), registry.Next(H(1), -1));
    }

    [Fact]
    public void Next_includes_unsnapped_tables_after_snapped_ones()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 0);

        var extra = new[] { H(1), H(7), H(8) };

        Assert.Equal(H(7), registry.Next(H(1), +1, extra));
        Assert.Equal(H(8), registry.Next(H(7), +1, extra));
        Assert.Equal(H(1), registry.Next(H(8), +1, extra));
    }

    [Fact]
    public void Next_from_a_non_table_window_starts_at_the_first_and_previous_at_the_last()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 0);
        registry.Place(H(2), 1);

        Assert.Equal(H(1), registry.Next(H(99), +1));
        Assert.Equal(H(2), registry.Next(H(99), -1));
    }

    [Fact]
    public void Next_with_no_tables_is_null()
    {
        Assert.Null(Registry(("A", 2)).Next(H(1), +1));
    }

    [Fact]
    public void Next_with_a_single_table_returns_that_table()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 0);

        Assert.Equal(H(1), registry.Next(H(1), +1));
    }

    [Fact]
    public void Focusing_a_slot_picks_the_top_table_then_cycles_the_stack()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 0);
        registry.Place(H(2), 0);
        registry.Place(H(3), 0);

        Assert.Equal(H(3), registry.NextInSlot(0, H(99)));
        Assert.Equal(H(2), registry.NextInSlot(0, H(3)));
        Assert.Equal(H(1), registry.NextInSlot(0, H(2)));
        Assert.Equal(H(3), registry.NextInSlot(0, H(1)));
    }

    [Fact]
    public void Focusing_an_empty_or_missing_slot_is_null()
    {
        var registry = Registry(("A", 2));

        Assert.Null(registry.NextInSlot(1, H(1)));
        Assert.Null(registry.NextInSlot(7, H(1)));
    }

    [Fact]
    public void Swap_exchanges_slots_with_the_top_table_of_the_target()
    {
        var registry = Registry(("A", 3));
        registry.Place(H(1), 0);
        registry.Place(H(2), 2);

        var displaced = registry.MoveOrSwap(H(1), 2, swap: true);

        Assert.Equal(H(2), displaced);
        Assert.Equal(2, registry.SlotOf(H(1)));
        Assert.Equal(0, registry.SlotOf(H(2)));
    }

    [Fact]
    public void Swap_into_an_empty_slot_just_moves()
    {
        var registry = Registry(("A", 3));
        registry.Place(H(1), 0);

        Assert.Null(registry.MoveOrSwap(H(1), 1, swap: true));
        Assert.Equal(1, registry.SlotOf(H(1)));
    }

    [Fact]
    public void Swap_onto_its_own_slot_changes_nothing()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 0);
        registry.Place(H(2), 0);

        Assert.Null(registry.MoveOrSwap(H(2), 0, swap: true));
        Assert.Equal(new[] { H(1), H(2) }, registry.TablesIn(0));
    }

    [Fact]
    public void A_table_without_a_slot_stacks_instead_of_swapping()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 0);

        var displaced = registry.MoveOrSwap(H(2), 0, swap: true);

        Assert.Null(displaced);
        Assert.Equal(new[] { H(1), H(2) }, registry.TablesIn(0));
    }

    [Fact]
    public void Stack_mode_never_displaces()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 0);
        registry.Place(H(2), 1);

        Assert.Null(registry.MoveOrSwap(H(1), 1, swap: false));
        Assert.Equal(new[] { H(2), H(1) }, registry.TablesIn(1));
    }

    [Fact]
    public void Shrinking_a_group_displaces_only_tables_beyond_the_new_count()
    {
        var registry = Registry(("A", 3), ("B", 2));
        registry.Place(H(1), 0);
        registry.Place(H(2), 2);
        registry.Place(H(3), 4);

        var displaced = registry.SetGroups(new[] { new SlotGroup("A", 2), new SlotGroup("B", 2) });

        Assert.Equal(new[] { H(2) }, displaced);
        Assert.Equal(0, registry.SlotOf(H(1)));
        Assert.Null(registry.SlotOf(H(2)));
        // B's second cell keeps its table; its global number shifts from 4 to 3.
        Assert.Equal(3, registry.SlotOf(H(3)));
    }

    [Fact]
    public void Removing_a_group_displaces_its_tables()
    {
        var registry = Registry(("A", 1), ("B", 1));
        registry.Place(H(1), 0);
        registry.Place(H(2), 1);

        var displaced = registry.SetGroups(new[] { new SlotGroup("A", 1) });

        Assert.Equal(new[] { H(2) }, displaced);
        Assert.Equal(1, registry.SlotCount);
    }

    [Fact]
    public void Growing_a_group_keeps_every_assignment()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 1);

        var displaced = registry.SetGroups(new[] { new SlotGroup("A", 6) });

        Assert.Empty(displaced);
        Assert.Equal(1, registry.SlotOf(H(1)));
    }

    [Fact]
    public void Compact_closes_gaps_in_order_within_one_group_only()
    {
        var registry = Registry(("A", 4), ("B", 2));
        registry.Place(H(1), 1);
        registry.Place(H(2), 3);
        registry.Place(H(3), 5);

        var moves = registry.Compact("A");

        Assert.Equal(new[] { (H(1), 0), (H(2), 1) }, moves);
        Assert.Equal(0, registry.SlotOf(H(1)));
        Assert.Equal(1, registry.SlotOf(H(2)));
        Assert.Equal(5, registry.SlotOf(H(3)));
    }

    [Fact]
    public void Compact_reports_every_table_of_a_moved_stack()
    {
        var registry = Registry(("A", 3));
        registry.Place(H(1), 1);
        registry.Place(H(2), 1);
        registry.Place(H(3), 2);

        var moves = registry.Compact("A");

        Assert.Equal(new[] { (H(1), 0), (H(2), 0), (H(3), 1) }, moves);
    }

    [Fact]
    public void Compact_leaves_fixed_slots_and_their_tables_where_they_are()
    {
        var registry = Registry(("A", 5));
        registry.Place(H(1), 1);   // pinned table in its pinned slot
        registry.Place(H(2), 3);
        registry.Place(H(3), 4);

        // Slot 1 holds a pinned table; slot 2 is reserved for a pin whose table is not open
        var moves = registry.Compact("A", new HashSet<int> { 1, 2 });

        Assert.Equal(new[] { (H(2), 0), (H(3), 3) }, moves);
        Assert.Equal(1, registry.SlotOf(H(1)));
        Assert.Empty(registry.TablesIn(2));
    }

    [Fact]
    public void First_available_skips_avoided_slots()
    {
        var registry = Registry(("A", 3));

        Assert.Equal(1, registry.FirstAvailableSlot(avoid: new HashSet<int> { 0 }));
        Assert.Equal(2, registry.FirstAvailableSlot("A", new HashSet<int> { 0, 1 }));
        Assert.Null(registry.FirstAvailableSlot(avoid: new HashSet<int> { 0, 1, 2 }));
    }

    [Fact]
    public void Swap_with_a_stacked_target_trades_only_the_top_table()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 0);
        registry.Place(H(2), 1);
        registry.Place(H(3), 1);

        var displaced = registry.MoveOrSwap(H(1), 1, swap: true);

        Assert.Equal(H(3), displaced);
        Assert.Equal(new[] { H(3) }, registry.TablesIn(0));
        Assert.Equal(new[] { H(2), H(1) }, registry.TablesIn(1));
    }

    [Fact]
    public void Swap_from_a_stacked_origin_leaves_the_rest_of_the_stack()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 0);
        registry.Place(H(2), 0);
        registry.Place(H(3), 1);

        registry.MoveOrSwap(H(2), 1, swap: true);

        Assert.Equal(new[] { H(1), H(3) }, registry.TablesIn(0));
        Assert.Equal(new[] { H(2) }, registry.TablesIn(1));
    }

    [Fact]
    public void Reordering_groups_keeps_each_groups_tables()
    {
        var registry = Registry(("A", 2), ("B", 2));
        registry.Place(H(1), 1);
        registry.Place(H(2), 2);

        // The primary monitor changed, so B now comes first
        var displaced = registry.SetGroups(new[] { new SlotGroup("B", 2), new SlotGroup("A", 2) });

        Assert.Empty(displaced);
        Assert.Equal(0, registry.SlotOf(H(2)));
        Assert.Equal(3, registry.SlotOf(H(1)));
    }

    [Fact]
    public void Compact_reports_nothing_when_already_packed()
    {
        var registry = Registry(("A", 3));
        registry.Place(H(1), 0);
        registry.Place(H(2), 1);

        Assert.Empty(registry.Compact("A"));
    }

    [Fact]
    public void Compact_keeps_stacks_together_when_tables_outnumber_slots()
    {
        var registry = Registry(("A", 3));
        registry.Place(H(1), 1);
        registry.Place(H(2), 1);
        registry.Place(H(3), 2);

        registry.Compact("A");

        Assert.Equal(new[] { H(1), H(2) }, registry.TablesIn(0));
        Assert.Equal(new[] { H(3) }, registry.TablesIn(1));
    }

    [Fact]
    public void Prune_drops_dead_handles_and_reports_them()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 0);
        registry.Place(H(2), 1);

        var removed = registry.Prune(h => h == H(2));

        Assert.Equal(new[] { H(1) }, removed);
        Assert.False(registry.Contains(H(1)));
        Assert.True(registry.Contains(H(2)));
    }

    [Fact]
    public void ClearGroup_only_clears_that_group()
    {
        var registry = Registry(("A", 1), ("B", 1));
        registry.Place(H(1), 0);
        registry.Place(H(2), 1);

        registry.ClearGroup("A");

        Assert.False(registry.Contains(H(1)));
        Assert.True(registry.Contains(H(2)));
    }
}
