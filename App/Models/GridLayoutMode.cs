namespace App.Models
{
    /// <summary>
    /// Defines the different grid layout calculation modes
    /// </summary>
    public enum GridLayoutMode
    {
        /// <summary>
        /// Fixed grid with specified rows and columns (e.g., 3x2 = 6 equal cells)
        /// </summary>
        FixedGrid,

        /// <summary>
        /// Grid calculated based on number of windows (e.g., 9 windows = 3x3 grid)
        /// </summary>
        NWindowOptimized,

        /// <summary>
        /// Custom cell sizes and positions defined by user
        /// </summary>
        CustomCells,

        /// <summary>
        /// Grid is disabled for this monitor
        /// </summary>
        Disabled
    }
}
