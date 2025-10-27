using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace App.Models
{
    /// <summary>
    /// Represents a grid layout configuration
    /// </summary>
    public class GridLayout
    {
        public string Name { get; set; }
        public GridLayoutMode Mode { get; set; }
        public List<GridCell> Cells { get; set; }

        // For FixedGrid mode
        public int Rows { get; set; }
        public int Columns { get; set; }

        // For NWindowOptimized mode
        public int NumberOfWindows { get; set; }

        // Display area this layout applies to
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
        }

        /// <summary>
        /// Calculate grid cells based on the layout mode
        /// </summary>
        public void CalculateCells()
        {
            Cells.Clear();

            switch (Mode)
            {
                case GridLayoutMode.FixedGrid:
                    CalculateFixedGridCells();
                    break;
                case GridLayoutMode.NWindowOptimized:
                    CalculateOptimizedGridCells();
                    break;
                case GridLayoutMode.CustomCells:
                    // Custom cells are manually defined, no calculation needed
                    break;
            }
        }

        private void CalculateFixedGridCells()
        {
            if (Rows <= 0 || Columns <= 0) return;

            var totalWidth = DisplayBounds.Width;
            var totalHeight = DisplayBounds.Height;

            // Calculate cell dimensions including spacing
            var cellWidth = (totalWidth - (Columns - 1) * CellSpacing) / Columns;
            var cellHeight = (totalHeight - (Rows - 1) * CellSpacing) / Rows;

            var cellId = 0;
            for (var row = 0; row < Rows; row++)
            {
                for (var col = 0; col < Columns; col++)
                {
                    var x = DisplayBounds.Left + col * (cellWidth + CellSpacing);
                    var y = DisplayBounds.Top + row * (cellHeight + CellSpacing);

                    var cellBounds = new Rect(x, y, cellWidth, cellHeight);
                    Cells.Add(new GridCell(cellId++, cellBounds, row, col));
                }
            }
        }

        private void CalculateOptimizedGridCells()
        {
            if (NumberOfWindows <= 0) return;

            // Calculate optimal grid dimensions
            var cols = (int)Math.Ceiling(Math.Sqrt(NumberOfWindows));
            var rows = (int)Math.Ceiling((double)NumberOfWindows / cols);

            Rows = rows;
            Columns = cols;

            // Use fixed grid calculation with calculated dimensions
            CalculateFixedGridCells();

            // Remove extra cells if NumberOfWindows is not a perfect rectangle
            while (Cells.Count > NumberOfWindows)
            {
                Cells.RemoveAt(Cells.Count - 1);
            }
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
