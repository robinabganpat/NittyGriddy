using System.Windows;
using App.Models;
using App.Services;

namespace NittyGriddy.Tests;

/// <summary>
/// "Fill with the open tables": the grid of a display has one slot per table, shaped so the tables come out largest
/// </summary>
public class AutoFitTests
{
    private static IntPtr H(int value) => new(value);

    // A 2560x1440 primary display's work area, the size tables are placed in on the author's setup
    private const double Width = 2560;
    private const double Height = 1392;

    [Theory]
    [InlineData(1, 1, 1)]   // one table fills the display
    [InlineData(2, 1, 2)]   // side by side
    [InlineData(3, 2, 2)]   // 2x2 with one empty: larger tables than three in a row
    [InlineData(4, 2, 2)]
    [InlineData(5, 2, 3)]
    [InlineData(6, 2, 3)]
    [InlineData(9, 3, 3)]
    public void Shape_makes_the_tables_largest_on_a_wide_display(int tables, int rows, int columns)
    {
        Assert.Equal((rows, columns), GridLayout.BestFit(tables, Width, Height, spacing: 10, tableAspectRatio: 1.4));
    }

    [Fact]
    public void Two_tables_on_a_portrait_display_are_stacked_vertically()
    {
        Assert.Equal((2, 1), GridLayout.BestFit(2, 1080, 1920, spacing: 10, tableAspectRatio: 1.4));
    }

    [Fact]
    public void Shape_of_nothing_is_one_slot()
    {
        Assert.Equal((1, 1), GridLayout.BestFit(0, Width, Height, spacing: 10, tableAspectRatio: 1.4));
    }

    [Fact]
    public void Layout_has_one_slot_per_open_table_up_to_the_maximum()
    {
        var layout = new GridLayout("g")
        {
            Mode = GridLayoutMode.AutoFit,
            DisplayBounds = new Rect(0, 0, Width, Height),
            CellSpacing = 10,
            AutoFitMaxTables = 4
        };

        layout.AutoFitCount = 3;
        Assert.Equal(3, layout.BuildCells().Count);

        layout.AutoFitCount = 0;
        Assert.Single(layout.BuildCells());

        layout.AutoFitCount = 7;
        Assert.Equal(4, layout.BuildCells().Count);
    }

    [Fact]
    public void Single_table_gets_the_whole_display()
    {
        var layout = new GridLayout("g") { Mode = GridLayoutMode.AutoFit, DisplayBounds = new Rect(-2560, 0, Width, Height), AutoFitCount = 1 };

        Assert.Equal(new Rect(-2560, 0, Width, Height), Assert.Single(layout.BuildCells()).Bounds);
    }

    [Fact]
    public void The_open_table_count_is_not_saved_but_the_maximum_is()
    {
        var settings = new AppSettings();
        var layout = settings.ActiveProfile.GetOrCreateLayoutForMonitor("M");
        layout.Mode = GridLayoutMode.AutoFit;
        layout.AutoFitMaxTables = 5;
        layout.AutoFitCount = 3;

        var json = SettingsService.Serialize(settings);
        var loaded = SettingsService.Deserialize(json).ActiveProfile.MonitorLayouts["M"];

        Assert.DoesNotContain("AutoFitCount", json);
        Assert.Equal(GridLayoutMode.AutoFit, loaded.Mode);
        Assert.Equal(5, loaded.AutoFitMaxTables);
    }

    [Fact]
    public void Distribute_spreads_a_stack_into_the_empty_slots_of_its_display()
    {
        var registry = new TableRegistry();
        registry.SetGroups(new[] { new SlotGroup("A", 4), new SlotGroup("B", 2) });
        registry.Place(H(1), 0);
        registry.Place(H(2), 1);
        registry.Place(H(3), 1);
        registry.Place(H(9), 4);

        var moves = registry.Distribute("A");

        Assert.Equal(new[] { (H(3), 2) }, moves);
        Assert.Equal(new[] { H(1) }, registry.TablesIn(0));
        Assert.Equal(new[] { H(2) }, registry.TablesIn(1));
        Assert.Equal(new[] { H(3) }, registry.TablesIn(2));
        Assert.Equal(4, registry.SlotOf(H(9)));
    }

    [Fact]
    public void Distribute_closes_gaps_and_keeps_the_order()
    {
        var registry = new TableRegistry();
        registry.SetGroups(new[] { new SlotGroup("A", 3) });
        registry.Place(H(1), 1);
        registry.Place(H(2), 2);

        var moves = registry.Distribute("A");

        Assert.Equal(new[] { (H(1), 0), (H(2), 1) }, moves);
    }

    [Fact]
    public void Distribute_stacks_what_does_not_fit_starting_from_the_first_slot()
    {
        var registry = new TableRegistry();
        registry.SetGroups(new[] { new SlotGroup("A", 2) });
        registry.Place(H(1), 0);
        registry.Place(H(2), 0);
        registry.Place(H(3), 0);

        registry.Distribute("A");

        Assert.Equal(new[] { H(1), H(3) }, registry.TablesIn(0));
        Assert.Equal(new[] { H(2) }, registry.TablesIn(1));
    }

    [Fact]
    public void Distribute_of_an_unknown_group_does_nothing()
    {
        var registry = new TableRegistry();
        registry.SetGroups(new[] { new SlotGroup("A", 2) });

        Assert.Empty(registry.Distribute("Missing"));
    }
}
