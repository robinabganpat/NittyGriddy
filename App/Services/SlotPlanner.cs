using System.Collections.Generic;
using System.Linq;
using App.Models;

namespace App.Services
{
    /// <summary>
    /// Decides which slot a table should go to. Pure: works on the registry and pin rules only.
    /// Slots here are 0-based.
    /// </summary>
    public static class SlotPlanner
    {
        /// <summary>
        /// The slot a table is pinned to, or null. The first matching pin whose slot exists in the current grid wins.
        /// </summary>
        public static int? PinnedSlot(string title, IEnumerable<TablePin> pins, int slotCount)
        {
            foreach (var pin in pins)
            {
                if (pin.SlotNumber >= 1 && pin.SlotNumber <= slotCount && pin.Matches(title))
                    return pin.SlotNumber - 1;
            }

            return null;
        }

        /// <summary>
        /// Slots that some pin points at, whether or not its table is open
        /// </summary>
        public static HashSet<int> ReservedSlots(IEnumerable<TablePin> pins, int slotCount)
        {
            return pins
                .Where(p => p.SlotNumber >= 1 && p.SlotNumber <= slotCount)
                .Select(p => p.SlotNumber - 1)
                .ToHashSet();
        }

        /// <summary>
        /// Choose a slot for a table that is not pinned. In order of preference:
        /// an empty slot nobody has reserved, an empty reserved slot (better than stacking),
        /// then the least occupied unreserved slot. The table's own monitor wins ties.
        /// </summary>
        /// <param name="monitorGroup">Group key of the monitor the table is on, if known</param>
        /// <param name="reserved">Slots kept for pinned tables</param>
        /// <param name="exclude">Slots that must not be chosen at all</param>
        public static int? PickSlot(TableRegistry registry, string? monitorGroup, IReadOnlySet<int> reserved, IReadOnlySet<int>? exclude = null)
        {
            var unreserved = LeastOccupied(registry, monitorGroup, exclude == null ? reserved : reserved.Concat(exclude).ToHashSet());
            if (unreserved != null && registry.TablesIn(unreserved.Value).Count == 0)
                return unreserved;

            var any = LeastOccupied(registry, monitorGroup, exclude);
            if (any != null && registry.TablesIn(any.Value).Count == 0)
                return any;

            return unreserved ?? any;
        }

        private static int? LeastOccupied(TableRegistry registry, string? monitorGroup, IReadOnlySet<int>? avoid)
        {
            var anywhere = registry.FirstAvailableSlot(null, avoid);
            if (anywhere == null || monitorGroup == null)
                return anywhere;

            var sameMonitor = registry.FirstAvailableSlot(monitorGroup, avoid);
            if (sameMonitor != null && registry.TablesIn(sameMonitor.Value).Count <= registry.TablesIn(anywhere.Value).Count)
                return sameMonitor;

            return anywhere;
        }
    }
}
