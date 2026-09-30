namespace App.Models
{
    /// <summary>
    /// What happens when a table is dropped on a slot that already holds one
    /// </summary>
    public enum DropBehavior
    {
        /// <summary>
        /// The two tables trade slots
        /// </summary>
        Swap,

        /// <summary>
        /// The dropped table is stacked on top of the one already there
        /// </summary>
        Stack
    }

    /// <summary>
    /// Application-wide behaviour options (not part of a layout profile)
    /// </summary>
    public class BehaviorSettings
    {
        public DropBehavior DropOnOccupied { get; set; } = DropBehavior.Swap;

        /// <summary>
        /// When a table closes, move later tables on that monitor down to fill the gap.
        /// Off by default: tables moving under the cursor during play invite misclicks.
        /// </summary>
        public bool CompactOnClose { get; set; }

        /// <summary>
        /// Arrange tables leaves a table in the slot it is in and puts others in the nearest free slot.
        /// When false, Arrange packs all tables into slots 1, 2, 3... in order.
        /// </summary>
        public bool ArrangeKeepsPositions { get; set; } = true;

        /// <summary>
        /// Several tables in one slot are cascaded, each offset so its title bar shows, instead of lying exactly on
        /// top of each other
        /// </summary>
        public bool CascadeStacks { get; set; }

        public bool ShowActiveBorder { get; set; } = true;
        public string ActiveBorderColor { get; set; } = "#FF3DDC84";
        public int ActiveBorderThickness { get; set; } = 4;

        public bool MinimizeToTray { get; set; }
        public bool CloseToTray { get; set; }
        public bool StartMinimized { get; set; }
    }

    /// <summary>
    /// Last position of the main window, in WPF units
    /// </summary>
    public class WindowPlacement
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }
}
