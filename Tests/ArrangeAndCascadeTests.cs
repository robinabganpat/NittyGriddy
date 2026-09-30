using System.Windows;
using App.Services;

namespace NittyGriddy.Tests;

public class ArrangeKeepingPositionsTests
{
    private static IntPtr H(int value) => new(value);

    // A 2 x 2 grid on one display (A) and a single slot on another (B)
    private static readonly IReadOnlyList<ArrangeSlot> Slots = new[]
    {
        new ArrangeSlot(0, "A", new Rect(0, 0, 100, 100)),
        new ArrangeSlot(1, "A", new Rect(100, 0, 100, 100)),
        new ArrangeSlot(2, "A", new Rect(0, 100, 100, 100)),
        new ArrangeSlot(3, "A", new Rect(100, 100, 100, 100)),
        new ArrangeSlot(4, "B", new Rect(1000, 0, 100, 100)),
    };

    private static readonly HashSet<int> None = new();

    private static ArrangeTable At(int handle, double x, double y, string monitor, int? previousSlot = null) =>
        new(H(handle), new Point(x, y), monitor, previousSlot);

    [Fact]
    public void A_table_inside_a_slot_stays_in_that_slot()
    {
        var plan = ArrangePlanner.KeepPositions(new[] { At(1, 150, 150, "A") }, Slots, None);

        Assert.Equal(3, plan.Assigned[H(1)]);
        Assert.Empty(plan.Unplaced);
    }

    [Fact]
    public void A_loose_table_goes_to_the_nearest_free_slot_on_its_display()
    {
        // Between the slots, nearest to the bottom-right one
        var plan = ArrangePlanner.KeepPositions(new[] { At(1, 190, 210, "A") }, Slots, None);

        Assert.Equal(3, plan.Assigned[H(1)]);
    }

    [Fact]
    public void The_first_table_keeps_a_contested_slot_and_the_second_takes_the_nearest_free_one()
    {
        var plan = ArrangePlanner.KeepPositions(new[] { At(1, 50, 50, "A"), At(2, 60, 60, "A") }, Slots, None);

        Assert.Equal(0, plan.Assigned[H(1)]);
        // Slots 1 and 2 are equally near; the lower number wins
        Assert.Equal(1, plan.Assigned[H(2)]);
    }

    [Fact]
    public void Nearest_free_slot_is_searched_on_the_tables_own_display_only()
    {
        var tables = new[] { At(1, 1050, 50, "B"), At(2, 1060, 60, "B") };

        var plan = ArrangePlanner.KeepPositions(tables, Slots, None);

        Assert.Equal(4, plan.Assigned[H(1)]);
        Assert.Equal(new[] { H(2) }, plan.Unplaced);
    }

    [Fact]
    public void Unavailable_slots_are_neither_kept_nor_chosen()
    {
        // Slot 0 is held for a pinned table
        var plan = ArrangePlanner.KeepPositions(new[] { At(1, 50, 50, "A") }, Slots, new HashSet<int> { 0 });

        Assert.NotEqual(0, plan.Assigned[H(1)]);
        Assert.Contains(plan.Assigned[H(1)], new[] { 1, 2 });
    }

    [Fact]
    public void A_minimised_table_keeps_its_previous_slot_if_it_is_free()
    {
        var minimised = new ArrangeTable(H(1), null, null, PreviousSlot: 2);

        var plan = ArrangePlanner.KeepPositions(new[] { minimised }, Slots, None);

        Assert.Equal(2, plan.Assigned[H(1)]);
    }

    [Fact]
    public void A_minimised_table_without_a_free_previous_slot_is_left_for_the_normal_placement()
    {
        var tables = new[] { At(2, 150, 150, "A"), new ArrangeTable(H(1), null, null, PreviousSlot: 3) };

        var plan = ArrangePlanner.KeepPositions(tables, Slots, None);

        Assert.Equal(3, plan.Assigned[H(2)]);
        Assert.Equal(new[] { H(1) }, plan.Unplaced);
    }

    [Fact]
    public void A_table_on_a_display_without_slots_is_left_for_the_normal_placement()
    {
        var plan = ArrangePlanner.KeepPositions(new[] { At(1, 5000, 50, "C") }, Slots, None);

        Assert.Equal(new[] { H(1) }, plan.Unplaced);
    }
}

public class CascadeTests
{
    private static readonly Rect Cell = new(0, 0, 800, 600);

    [Fact]
    public void A_single_table_gets_the_whole_area()
    {
        Assert.Equal(Cell, FrameMath.CascadeArea(Cell, index: 0, count: 1, step: 30));
    }

    [Fact]
    public void Each_table_in_a_stack_is_offset_and_the_cascade_fits_the_area()
    {
        var bottom = FrameMath.CascadeArea(Cell, 0, 3, 30);
        var middle = FrameMath.CascadeArea(Cell, 1, 3, 30);
        var top = FrameMath.CascadeArea(Cell, 2, 3, 30);

        Assert.Equal(new Rect(0, 0, 740, 540), bottom);
        Assert.Equal(new Rect(30, 30, 740, 540), middle);
        Assert.Equal(new Rect(60, 60, 740, 540), top);
        Assert.True(top.Right <= Cell.Right && top.Bottom <= Cell.Bottom);
    }

    [Fact]
    public void A_deep_stack_uses_a_smaller_step_so_tables_stay_usable()
    {
        // Twenty tables at 30 px would leave 800 - 570 = 230 px; the step shrinks so each keeps three quarters
        var top = FrameMath.CascadeArea(Cell, 19, 20, 30);

        Assert.True(top.Width >= Cell.Width * 0.75 - 0.001);
        Assert.True(top.Height >= Cell.Height * 0.75 - 0.001);
        Assert.True(top.Right <= Cell.Right + 0.001 && top.Bottom <= Cell.Bottom + 0.001);
    }

    [Fact]
    public void An_index_outside_the_stack_is_clamped()
    {
        Assert.Equal(FrameMath.CascadeArea(Cell, 2, 3, 30), FrameMath.CascadeArea(Cell, 7, 3, 30));
    }
}
