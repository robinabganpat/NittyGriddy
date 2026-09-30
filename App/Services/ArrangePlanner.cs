using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace App.Services
{
    /// <summary>
    /// A table to arrange: where its centre is now (null when minimised), which display that is,
    /// and the slot it had before (0-based)
    /// </summary>
    public sealed record ArrangeTable(IntPtr Handle, Point? Center, string? Monitor, int? PreviousSlot = null);

    /// <summary>
    /// A slot to arrange into: global 0-based index, the display it belongs to, and where it is on screen
    /// </summary>
    public sealed record ArrangeSlot(int Index, string Group, Rect Bounds);

    /// <summary>
    /// Result of arranging with the least movement: the slot for each table that could stay or move nearby,
    /// and the tables left for the normal placement rules
    /// </summary>
    public sealed record ArrangePlan(IReadOnlyDictionary<IntPtr, int> Assigned, IReadOnlyList<IntPtr> Unplaced);

    /// <summary>
    /// Arranging while moving tables as little as possible. Pure: works on positions and slots only.
    /// </summary>
    public static class ArrangePlanner
    {
        /// <summary>
        /// Tables claim, in the order given:
        /// 1. the slot their centre is in (a minimised table: its previous slot), if that slot is free;
        /// 2. otherwise the free slot nearest to their centre on their own display.
        /// A table that gets neither is returned as unplaced. <paramref name="unavailable"/> slots are never used
        /// (slots of pinned tables, slots kept free for pins).
        /// </summary>
        public static ArrangePlan KeepPositions(IReadOnlyList<ArrangeTable> tables, IReadOnlyList<ArrangeSlot> slots, IReadOnlySet<int> unavailable)
        {
            var assigned = new Dictionary<IntPtr, int>();
            var taken = new HashSet<int>(unavailable);
            var remaining = new List<ArrangeTable>();

            bool IsFree(int slot) => !taken.Contains(slot) && slots.Any(s => s.Index == slot);

            void Assign(ArrangeTable table, int slot)
            {
                assigned[table.Handle] = slot;
                taken.Add(slot);
            }

            // Stay where you are
            foreach (var table in tables)
            {
                var here = table.Center is { } center
                    ? slots.FirstOrDefault(s => s.Bounds.Contains(center))?.Index
                    : table.PreviousSlot;

                if (here != null && IsFree(here.Value))
                    Assign(table, here.Value);
                else
                    remaining.Add(table);
            }

            // Otherwise the nearest free slot on the same display
            var unplaced = new List<IntPtr>();
            foreach (var table in remaining)
            {
                var nearest = table.Center is { } center && table.Monitor != null
                    ? slots
                        .Where(s => s.Group == table.Monitor && IsFree(s.Index))
                        .OrderBy(s => DistanceSquared(s.Bounds, center))
                        .ThenBy(s => s.Index)
                        .FirstOrDefault()
                    : null;

                if (nearest != null)
                    Assign(table, nearest.Index);
                else
                    unplaced.Add(table.Handle);
            }

            return new ArrangePlan(assigned, unplaced);
        }

        private static double DistanceSquared(Rect rect, Point point)
        {
            var dx = Math.Max(Math.Max(rect.Left - point.X, 0), point.X - rect.Right);
            var dy = Math.Max(Math.Max(rect.Top - point.Y, 0), point.Y - rect.Bottom);
            return dx * dx + dy * dy;
        }
    }
}
