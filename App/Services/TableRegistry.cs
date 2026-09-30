using System;
using System.Collections.Generic;
using System.Linq;

namespace App.Services
{
    /// <summary>
    /// A run of slots that belong together (one monitor's grid)
    /// </summary>
    public readonly record struct SlotGroup(string Key, int Count);

    /// <summary>
    /// Tracks which table window sits in which slot.
    /// Slots are numbered from 0 consecutively across groups, in group order.
    /// Holds no Win32 state: callers apply the results to real windows.
    /// </summary>
    public class TableRegistry
    {
        private sealed class Group
        {
            public string Key = string.Empty;

            // One stack per slot; the last entry of a stack is the table on top
            public List<List<IntPtr>> Slots = new();
        }

        private List<Group> _groups = new();

        public int SlotCount => _groups.Sum(g => g.Slots.Count);

        /// <summary>
        /// All tables in slot order, then bottom-to-top within a slot
        /// </summary>
        public IReadOnlyList<IntPtr> AllTables => _groups.SelectMany(g => g.Slots).SelectMany(s => s).ToList();

        /// <summary>
        /// Replace the slot groups. Tables keep their position within a group that still exists;
        /// tables whose group or slot is gone are removed and returned.
        /// </summary>
        public IReadOnlyList<IntPtr> SetGroups(IReadOnlyList<SlotGroup> groups)
        {
            var previous = _groups.ToDictionary(g => g.Key);
            var next = new List<Group>();
            var displaced = new List<IntPtr>();

            foreach (var definition in groups)
            {
                var group = new Group { Key = definition.Key };
                previous.Remove(definition.Key, out var old);

                for (var i = 0; i < definition.Count; i++)
                    group.Slots.Add(old != null && i < old.Slots.Count ? old.Slots[i] : new List<IntPtr>());

                if (old != null)
                    displaced.AddRange(old.Slots.Skip(definition.Count).SelectMany(s => s));

                next.Add(group);
            }

            // Groups that no longer exist, in their original order
            displaced.AddRange(_groups.Where(g => previous.ContainsKey(g.Key)).SelectMany(g => g.Slots).SelectMany(s => s));

            _groups = next;
            return displaced;
        }

        public bool Contains(IntPtr table) => SlotOf(table) != null;

        public int? SlotOf(IntPtr table)
        {
            var slot = 0;
            foreach (var stack in _groups.SelectMany(g => g.Slots))
            {
                if (stack.Contains(table))
                    return slot;
                slot++;
            }
            return null;
        }

        /// <summary>
        /// Key of the group a slot belongs to, or null if the slot does not exist
        /// </summary>
        public string? GroupOf(int slot)
        {
            if (slot < 0)
                return null;

            foreach (var group in _groups)
            {
                if (slot < group.Slots.Count)
                    return group.Key;
                slot -= group.Slots.Count;
            }
            return null;
        }

        /// <summary>
        /// Global number of a group's first slot, or null if the group does not exist
        /// </summary>
        public int? FirstSlotOf(string groupKey)
        {
            var offset = 0;
            foreach (var group in _groups)
            {
                if (group.Key == groupKey)
                    return offset;
                offset += group.Slots.Count;
            }
            return null;
        }

        /// <summary>
        /// All tables of one group, in slot order then bottom to top
        /// </summary>
        public IReadOnlyList<IntPtr> TablesInGroup(string groupKey)
        {
            return _groups.Where(g => g.Key == groupKey).SelectMany(g => g.Slots).SelectMany(s => s).ToList();
        }

        public IReadOnlyList<IntPtr> TablesIn(int slot)
        {
            return (IReadOnlyList<IntPtr>?)StackAt(slot)?.ToList() ?? Array.Empty<IntPtr>();
        }

        /// <summary>
        /// Put a table on top of a slot, removing it from wherever it was
        /// </summary>
        public void Place(IntPtr table, int slot)
        {
            var stack = StackAt(slot) ?? throw new ArgumentOutOfRangeException(nameof(slot));

            Remove(table);
            stack.Add(table);
        }

        public bool Remove(IntPtr table)
        {
            return _groups.SelectMany(g => g.Slots).Any(stack => stack.Remove(table));
        }

        public void Clear()
        {
            foreach (var stack in _groups.SelectMany(g => g.Slots))
                stack.Clear();
        }

        public void ClearGroup(string groupKey)
        {
            foreach (var stack in _groups.Where(g => g.Key == groupKey).SelectMany(g => g.Slots))
                stack.Clear();
        }

        /// <summary>
        /// The slot holding the fewest tables (an empty one if there is any), lowest number first.
        /// Optionally restricted to one group and leaving out some slots.
        /// </summary>
        public int? FirstAvailableSlot(string? groupKey = null, IReadOnlySet<int>? avoid = null)
        {
            int? best = null;
            var bestCount = int.MaxValue;
            var slot = 0;

            foreach (var group in _groups)
            {
                foreach (var stack in group.Slots)
                {
                    if ((groupKey == null || group.Key == groupKey) && avoid?.Contains(slot) != true && stack.Count < bestCount)
                    {
                        best = slot;
                        bestCount = stack.Count;
                    }
                    slot++;
                }
            }

            return best;
        }

        /// <summary>
        /// The table after (direction +1) or before (-1) the current one, wrapping around.
        /// Tables in <paramref name="unslotted"/> that have no slot are visited after the slotted ones.
        /// </summary>
        public IntPtr? Next(IntPtr current, int direction, IEnumerable<IntPtr>? unslotted = null)
        {
            var order = AllTables.ToList();
            if (unslotted != null)
                order.AddRange(unslotted.Where(t => !order.Contains(t)).ToList());

            if (order.Count == 0)
                return null;

            var index = order.IndexOf(current);
            if (index < 0)
                return direction >= 0 ? order[0] : order[^1];

            var step = direction >= 0 ? 1 : -1;
            return order[((index + step) % order.Count + order.Count) % order.Count];
        }

        /// <summary>
        /// The table to focus in a slot: the top one, or, if the current table is already in that slot, the one beneath it
        /// </summary>
        public IntPtr? NextInSlot(int slot, IntPtr current)
        {
            var stack = StackAt(slot);
            if (stack == null || stack.Count == 0)
                return null;

            var index = stack.IndexOf(current);
            if (index < 0)
                return stack[^1];

            return stack[(index - 1 + stack.Count) % stack.Count];
        }

        /// <summary>
        /// Move a table to a slot. With swap, the table on top of an occupied target takes the mover's old slot
        /// and is returned. A table that has no slot to give up is stacked instead.
        /// </summary>
        public IntPtr? MoveOrSwap(IntPtr table, int slot, bool swap)
        {
            var target = StackAt(slot) ?? throw new ArgumentOutOfRangeException(nameof(slot));
            var origin = SlotOf(table);

            if (origin == slot)
                return null;

            IntPtr? displaced = null;
            if (swap && origin != null && target.Count > 0)
            {
                displaced = target[^1];
                Place(displaced.Value, origin.Value);
            }

            Place(table, slot);
            return displaced;
        }

        /// <summary>
        /// Close gaps in one group: occupied slots move down to the lowest numbers, keeping their order and stacks.
        /// Slots in <paramref name="fixedSlots"/> (global numbers) are neither moved nor filled.
        /// Returns each table whose slot changed with its new slot.
        /// </summary>
        public IReadOnlyList<(IntPtr Table, int Slot)> Compact(string groupKey, IReadOnlySet<int>? fixedSlots = null)
        {
            var moves = new List<(IntPtr, int)>();
            var group = _groups.FirstOrDefault(g => g.Key == groupKey);
            var offset = FirstSlotOf(groupKey);
            if (group == null || offset == null)
                return moves;

            // Positions within the group that take part, in order
            var movable = Enumerable.Range(0, group.Slots.Count)
                .Where(i => fixedSlots?.Contains(offset.Value + i) != true)
                .ToList();

            var occupied = movable.Where(i => group.Slots[i].Count > 0).Select(i => (Index: i, Stack: group.Slots[i])).ToList();
            var empty = movable.Where(i => group.Slots[i].Count == 0).Select(i => group.Slots[i]).ToList();
            var packed = occupied.Select(o => o.Stack).Concat(empty).ToList();

            for (var n = 0; n < movable.Count; n++)
            {
                group.Slots[movable[n]] = packed[n];

                if (n < occupied.Count && occupied[n].Index != movable[n])
                    moves.AddRange(packed[n].Select(table => (table, offset.Value + movable[n])));
            }

            return moves;
        }

        /// <summary>
        /// Give every table of a group its own slot, in order: the tables (slot order, then bottom to top within a
        /// slot) go to the group's slots one by one, and any that do not fit stack from the first slot again.
        /// Returns each table whose slot changed with its new slot.
        /// </summary>
        public IReadOnlyList<(IntPtr Table, int Slot)> Distribute(string groupKey)
        {
            var moves = new List<(IntPtr, int)>();
            var group = _groups.FirstOrDefault(g => g.Key == groupKey);
            var offset = FirstSlotOf(groupKey);
            if (group == null || offset == null || group.Slots.Count == 0)
                return moves;

            var tables = group.Slots.SelectMany(s => s).ToList();
            var previous = tables.ToDictionary(t => t, t => SlotOf(t)!.Value);

            foreach (var stack in group.Slots)
                stack.Clear();

            for (var i = 0; i < tables.Count; i++)
            {
                var index = i % group.Slots.Count;
                group.Slots[index].Add(tables[i]);

                if (previous[tables[i]] != offset.Value + index)
                    moves.Add((tables[i], offset.Value + index));
            }

            return moves;
        }

        /// <summary>
        /// Remove tables for which <paramref name="isAlive"/> is false; returns the removed tables
        /// </summary>
        public IReadOnlyList<IntPtr> Prune(Func<IntPtr, bool> isAlive)
        {
            var dead = AllTables.Where(t => !isAlive(t)).ToList();
            foreach (var table in dead)
                Remove(table);
            return dead;
        }

        private List<IntPtr>? StackAt(int slot)
        {
            if (slot < 0)
                return null;

            foreach (var group in _groups)
            {
                if (slot < group.Slots.Count)
                    return group.Slots[slot];
                slot -= group.Slots.Count;
            }
            return null;
        }
    }
}
