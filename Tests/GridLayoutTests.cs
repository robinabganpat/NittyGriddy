using System.Windows;
using App.Models;

namespace NittyGriddy.Tests;

public class GridLayoutTests
{
    private static GridLayout Layout(GridLayoutMode mode, Rect bounds, double spacing = 0)
    {
        return new GridLayout("test")
        {
            Mode = mode,
            DisplayBounds = bounds,
            CellSpacing = spacing,
        };
    }

    [Fact]
    public void FixedGrid_creates_rows_times_columns_cells_in_row_major_order()
    {
        var layout = Layout(GridLayoutMode.FixedGrid, new Rect(0, 0, 300, 200));
        layout.Rows = 2;
        layout.Columns = 3;

        layout.CalculateCells();

        Assert.Equal(6, layout.Cells.Count);
        Assert.Equal(new Rect(0, 0, 100, 100), layout.Cells[0].Bounds);
        Assert.Equal(new Rect(200, 0, 100, 100), layout.Cells[2].Bounds);
        Assert.Equal(new Rect(0, 100, 100, 100), layout.Cells[3].Bounds);
    }

    [Fact]
    public void FixedGrid_respects_display_origin_and_spacing()
    {
        var layout = Layout(GridLayoutMode.FixedGrid, new Rect(-2560, 0, 210, 100), spacing: 10);
        layout.Rows = 1;
        layout.Columns = 2;

        layout.CalculateCells();

        Assert.Equal(new Rect(-2560, 0, 100, 100), layout.Cells[0].Bounds);
        Assert.Equal(new Rect(-2450, 0, 100, 100), layout.Cells[1].Bounds);
    }

    [Fact]
    public void NWindowOptimized_trims_cells_to_requested_count()
    {
        var layout = Layout(GridLayoutMode.NWindowOptimized, new Rect(0, 0, 300, 300));
        layout.NumberOfWindows = 7;

        layout.CalculateCells();

        Assert.Equal(7, layout.Cells.Count);
        Assert.Equal(3, layout.Cells.Max(c => c.Column) + 1);
        Assert.Equal(3, layout.Cells.Max(c => c.Row) + 1);
    }

    [Fact]
    public void NWindowOptimized_leaves_the_fixed_grid_setting_alone()
    {
        var layout = Layout(GridLayoutMode.NWindowOptimized, new Rect(0, 0, 300, 300));
        layout.Rows = 2;
        layout.Columns = 5;
        layout.NumberOfWindows = 9;

        layout.CalculateCells();

        Assert.Equal(2, layout.Rows);
        Assert.Equal(5, layout.Columns);
    }

    [Fact]
    public void Spacing_larger_than_the_display_does_not_throw()
    {
        var layout = Layout(GridLayoutMode.FixedGrid, new Rect(0, 0, 300, 200), spacing: 5000);
        layout.Rows = 2;
        layout.Columns = 3;

        layout.CalculateCells();

        Assert.Equal(6, layout.Cells.Count);
        Assert.All(layout.Cells, c => Assert.True(c.Bounds.Width >= 1 && c.Bounds.Height >= 1));
        Assert.All(layout.Cells, c => Assert.True(c.Bounds.Right <= 300.5 && c.Bounds.Bottom <= 200.5));
    }

    [Fact]
    public void Negative_spacing_is_treated_as_zero()
    {
        var layout = Layout(GridLayoutMode.FixedGrid, new Rect(0, 0, 300, 200), spacing: -40);
        layout.Rows = 1;
        layout.Columns = 3;

        layout.CalculateCells();

        Assert.Equal(new Rect(100, 0, 100, 200), layout.Cells[1].Bounds);
    }

    [Fact]
    public void Disabled_mode_has_no_cells()
    {
        var layout = Layout(GridLayoutMode.Disabled, new Rect(0, 0, 300, 300));
        layout.Rows = 2;
        layout.Columns = 2;

        layout.CalculateCells();

        Assert.Empty(layout.Cells);
    }
}
