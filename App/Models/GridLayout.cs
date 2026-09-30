using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Newtonsoft.Json;

namespace App.Models
{
    /// <summary>
    /// Represents a grid layout configuration
    /// </summary>
    public class GridLayout
    {
        public string Name { get; set; }
        public GridLayoutMode Mode { get; set; }

        // Calculated at runtime from the monitor's work area; not part of the stored configuration
        [JsonIgnore]
        public List<GridCell> Cells { get; set; }

        // For FixedGrid mode
        public int Rows { get; set; }
        public int Columns { get; set; }

        // For NWindowOptimized mode
        public int NumberOfWindows { get; set; }

        /// <summary>
        /// AutoFit: the most slots the display grows to; further tables stack
        /// </summary>
        public int AutoFitMaxTables { get; set; }

        /// <summary>
        /// AutoFit: how many tables are on the display right now. Runtime state, set by the grid manager.
        /// </summary>
        [JsonIgnore]
        public int AutoFitCount { get; set; } = 1;

        /// <summary>
        /// AutoFit: width / height of the tables, used to choose the grid shape. Runtime state.
        /// </summary>
        [JsonIgnore]
        public double TableAspectRatio { get; set; } = DefaultTableAspectRatio;

        /// <summary>
        /// Poker tables are roughly 4:3 to 3:2; used until the real tables' shape is known
        /// </summary>
        public const double DefaultTableAspectRatio = 1.4;

        // Display area this layout applies to
        [JsonIgnore]
        public Rect DisplayBounds { get; set; }

        // Margins and spacing
        public Thickness CellMargins { get; set; }
        public double CellSpacing { get; set; }

        public GridLayout(string name)
        {
            Name = name;
            Cells = new List<GridCell>();
            CellMargins = new Thickness(5);
            CellSpacing = 10;
            Rows = 2;
            Columns = 3;
            NumberOfWindows = 6;
            AutoFitMaxTables = 9;
        }

        /// <summary>
        /// Calculate grid cells based on the layout mode
        /// </summary>
        public void CalculateCells()
        {
            Cells = BuildCells();
        }

        /// <summary>
        /// Work out the cells for the current settings without changing <see cref="Cells"/>.
        /// Used to preview a layout while it is being edited.
        /// </summary>
        public List<GridCell> BuildCells()
        {
            switch (Mode)
            {
                case GridLayoutMode.FixedGrid:
                    return BuildGridCells(Rows, Columns);
                case GridLayoutMode.NWindowOptimized:
                    return BuildOptimizedGridCells();
                case GridLayoutMode.AutoFit:
                    return BuildAutoFitCells();
                default:
                    // Disabled has no cells; custom cells have no editor yet
                    return new List<GridCell>();
            }
        }

        private List<GridCell> BuildGridCells(int rows, int columns)
        {
            var cells = new List<GridCell>();
            if (rows <= 0 || columns <= 0 || DisplayBounds.IsEmpty) return cells;

            var totalWidth = DisplayBounds.Width;
            var totalHeight = DisplayBounds.Height;

            // Spacing that would leave no room for the cells (hand-edited settings) is reduced to what fits
            var spacing = Math.Max(0, CellSpacing);
            if (columns > 1) spacing = Math.Min(spacing, (totalWidth - columns) / (columns - 1));
            if (rows > 1) spacing = Math.Min(spacing, (totalHeight - rows) / (rows - 1));
            spacing = Math.Max(0, spacing);

            // Calculate cell dimensions including spacing
            var cellWidth = Math.Max(1, (totalWidth - (columns - 1) * spacing) / columns);
            var cellHeight = Math.Max(1, (totalHeight - (rows - 1) * spacing) / rows);

            var cellId = 0;
            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < columns; col++)
                {
                    var x = DisplayBounds.Left + col * (cellWidth + spacing);
                    var y = DisplayBounds.Top + row * (cellHeight + spacing);

                    var cellBounds = new Rect(x, y, cellWidth, cellHeight);
                    cells.Add(new GridCell(cellId++, cellBounds, row, col));
                }
            }

            return cells;
        }

        private List<GridCell> BuildOptimizedGridCells()
        {
            if (NumberOfWindows <= 0) return new List<GridCell>();

            // Calculate optimal grid dimensions. Rows and Columns are left alone: they are the user's fixed-grid setting.
            var cols = (int)Math.Ceiling(Math.Sqrt(NumberOfWindows));
            var rows = (int)Math.Ceiling((double)NumberOfWindows / cols);

            var cells = BuildGridCells(rows, cols);

            // Remove extra cells if NumberOfWindows is not a perfect rectangle
            while (cells.Count > NumberOfWindows)
            {
                cells.RemoveAt(cells.Count - 1);
            }

            return cells;
        }

        private List<GridCell> BuildAutoFitCells()
        {
            var count = Math.Clamp(AutoFitCount, 1, Math.Max(1, AutoFitMaxTables));
            var (rows, columns) = BestFit(count, DisplayBounds.Width, DisplayBounds.Height, Math.Max(0, CellSpacing), TableAspectRatio);

            var cells = BuildGridCells(rows, columns);
            while (cells.Count > count)
                cells.RemoveAt(cells.Count - 1);

            return cells;
        }

        /// <summary>
        /// The rows and columns for <paramref name="tables"/> tables in an area, chosen so that a table of the given
        /// shape, fitted into one cell, is as large as possible. Ties go to fewer empty cells, then fewer rows.
        /// </summary>
        public static (int Rows, int Columns) BestFit(int tables, double width, double height, double spacing, double tableAspectRatio)
        {
            tables = Math.Max(1, tables);
            if (tableAspectRatio <= 0 || double.IsNaN(tableAspectRatio))
                tableAspectRatio = DefaultTableAspectRatio;

            var best = (Rows: 1, Columns: tables);
            var bestArea = -1.0;
            var bestEmpty = int.MaxValue;

            for (var rows = 1; rows <= tables; rows++)
            {
                var columns = (int)Math.Ceiling((double)tables / rows);
                var cellWidth = (width - (columns - 1) * spacing) / columns;
                var cellHeight = (height - (rows - 1) * spacing) / rows;
                if (cellWidth <= 0 || cellHeight <= 0)
                    continue;

                var tableWidth = Math.Min(cellWidth, cellHeight * tableAspectRatio);
                var area = tableWidth * (tableWidth / tableAspectRatio);
                var empty = rows * columns - tables;

                // Within half a percent counts as equal: then waste fewer cells
                var clearlyLarger = area > bestArea * 1.005;
                var aboutEqual = !clearlyLarger && area >= bestArea * 0.995;
                if (clearlyLarger || (aboutEqual && empty < bestEmpty))
                {
                    best = (rows, columns);
                    bestArea = area;
                    bestEmpty = empty;
                }
            }

            return best;
        }

        /// <summary>
        /// Find the cell that contains the given point
        /// </summary>
        public GridCell? FindCellAtPoint(Point point)
        {
            foreach (var cell in Cells)
            {
                if (cell.Contains(point))
                {
                    return cell;
                }
            }
            return null;
        }

        /// <summary>
        /// Find the cell that has the most overlap with the given rectangle
        /// </summary>
        public GridCell? FindBestMatchingCell(Rect rect)
        {
            GridCell? bestMatch = null;
            double maxOverlap = 0;

            foreach (var cell in Cells)
            {
                if (cell.IntersectsWith(rect))
                {
                    var intersection = Rect.Intersect(cell.Bounds, rect);
                    var overlap = intersection.Width * intersection.Height;

                    if (overlap > maxOverlap)
                    {
                        maxOverlap = overlap;
                        bestMatch = cell;
                    }
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// Find the cell with the fewest windows (prefers empty cells)
        /// </summary>
        public GridCell? FindFirstAvailableCell()
        {
            if (Cells.Count == 0)
                return null;

            // Return the cell with the minimum number of windows
            return Cells.OrderBy(c => c.WindowCount).FirstOrDefault();
        }
    }
}
