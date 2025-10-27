using System;
using System.Collections.Generic;
using System.Windows;

namespace App.Models
{
    /// <summary>
    /// Represents a single cell in the grid layout
    /// </summary>
    public class GridCell
    {
        public int Id { get; set; }
        public Rect Bounds { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
        public List<IntPtr> OccupiedWindows { get; set; }

        /// <summary>
        /// Returns true if this cell contains at least one window
        /// </summary>
        public bool IsOccupied => OccupiedWindows.Count > 0;

        /// <summary>
        /// Count of windows in this cell
        /// </summary>
        public int WindowCount => OccupiedWindows.Count;

        public GridCell(int id, Rect bounds, int row, int column)
        {
            Id = id;
            Bounds = bounds;
            Row = row;
            Column = column;
            OccupiedWindows = new List<IntPtr>();
        }

        /// <summary>
        /// Check if a point is within this cell
        /// </summary>
        public bool Contains(Point point)
        {
            return Bounds.Contains(point);
        }

        /// <summary>
        /// Check if a rectangle intersects with this cell
        /// </summary>
        public bool IntersectsWith(Rect rect)
        {
            return Bounds.IntersectsWith(rect);
        }
    }
}
