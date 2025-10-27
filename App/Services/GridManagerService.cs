using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using App.Models;
using App.Windows;

namespace App.Services
{
    /// <summary>
    /// Main service that manages grid overlays, window tracking, and snapping
    /// </summary>
    public class GridManagerService
    {
        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_ASYNCWINDOWPOS = 0x4000;

        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_ERASE = 0x0004;
        private const uint RDW_ALLCHILDREN = 0x0080;
        private const uint RDW_FRAME = 0x0400;
        private const uint RDW_UPDATENOW = 0x0100;

        private const uint WM_PAINT = 0x000F;
        private const uint WM_NCPAINT = 0x0085;
        private const uint WM_SIZE = 0x0005;
        private const uint WM_SIZING = 0x0214;
        private const uint WM_ENTERSIZEMOVE = 0x0231;
        private const uint WM_EXITSIZEMOVE = 0x0232;

        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        private const int SIZE_RESTORED = 0;

        private readonly MonitorService _monitorService;
        private readonly WindowTrackingService _windowTrackingService;
        private readonly WindowEnumerationService _windowEnumerationService;
        private readonly Dictionary<string, GridOverlayWindow> _overlays = new();
        private readonly DispatcherTimer _cleanupTimer;

        private MonitorGridConfig? _currentConfig;
        private bool _isEnabled;
        private bool _isPreviewMode;
        private GridCell? _currentHighlightedCell;
        private string? _currentMonitorDevice;

        public bool IsEnabled => _isEnabled;
        public bool IsPreviewMode => _isPreviewMode;

        public GridManagerService(Dispatcher dispatcher)
        {
            _monitorService = new MonitorService();
            _windowTrackingService = new WindowTrackingService(dispatcher);
            _windowEnumerationService = new WindowEnumerationService();

            // Wire up window tracking events
            _windowTrackingService.WindowDragStarted += OnWindowDragStarted;
            _windowTrackingService.WindowDragMoved += OnWindowDragMoved;
            _windowTrackingService.WindowDragEnded += OnWindowDragEnded;
            _windowTrackingService.WindowShown += OnWindowShown;

            // Set up periodic cleanup of occupied windows (every 2 seconds)
            _cleanupTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            _cleanupTimer.Interval = TimeSpan.FromSeconds(2);
            _cleanupTimer.Tick += OnCleanupTimer;
            _cleanupTimer.Start();
        }

        /// <summary>
        /// Load and activate a grid configuration
        /// </summary>
        public void LoadConfiguration(MonitorGridConfig config)
        {
            _currentConfig = config;

            // Collect currently occupied windows before clearing overlays
            var occupiedWindows = new Dictionary<string, List<(int cellIndex, IntPtr windowHandle)>>();

            if (_isEnabled && _overlays.Count > 0)
            {
                foreach (var kvp in _overlays)
                {
                    var monitorDevice = kvp.Key;
                    var overlay = kvp.Value;

                    // Find the current layout for this monitor
                    if (_currentConfig?.MonitorLayouts.TryGetValue(monitorDevice, out var currentLayout) == true)
                    {
                        var windows = new List<(int cellIndex, IntPtr windowHandle)>();

                        for (var i = 0; i < currentLayout.Cells.Count; i++)
                        {
                            var cell = currentLayout.Cells[i];
                            foreach (var windowHandle in cell.OccupiedWindows)
                            {
                                windows.Add((i, windowHandle));
                                System.Diagnostics.Debug.WriteLine($"LoadConfiguration: Saving window {windowHandle} at cell index {i} on {monitorDevice}");
                            }
                        }

                        if (windows.Count > 0)
                        {
                            occupiedWindows[monitorDevice] = windows;
                        }
                    }
                }
            }

            // Clear existing overlays
            ClearOverlays();

            // Migrate legacy filters if needed
            config.MigrateLegacyFilters();

            // Clear and set up target windows
            _windowTrackingService.ClearTargetFilters();

            foreach (var filter in config.WindowFilters)
            {
                _windowTrackingService.AddWindowFilter(filter);
            }

            System.Diagnostics.Debug.WriteLine($"Filters configured: {config.WindowFilters.Count} filter(s)");

            // Create overlays for each monitor
            var monitors = _monitorService.GetAllMonitors();
            System.Diagnostics.Debug.WriteLine($"LoadConfiguration: Creating overlays for {monitors.Count} monitor(s)");

            foreach (var monitor in monitors)
            {
                var layout = config.GetOrCreateLayoutForMonitor(monitor.DeviceName);
                layout.DisplayBounds = monitor.WorkArea;
                layout.CalculateCells();

                // Only create overlay if the layout is not disabled
                if (layout.Mode != GridLayoutMode.Disabled)
                {
                    var overlay = new GridOverlayWindow(monitor);
                    overlay.SetGridLayout(layout);
                    _overlays[monitor.DeviceName] = overlay;

                    System.Diagnostics.Debug.WriteLine($"  Created overlay for {monitor.DeviceName} with {layout.Cells.Count} cells (Mode: {layout.Mode})");

                    // If grid is already enabled, prepare the overlay (but keep it hidden)
                    if (_isEnabled)
                    {
                        overlay.SetVisible(true);
                        overlay.SetOpacity(0.0); // Hidden until drag starts
                        System.Diagnostics.Debug.WriteLine($"  Overlay prepared (grid already enabled, hidden until drag)");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  Skipped overlay for {monitor.DeviceName} (Disabled)");
                }
            }

            System.Diagnostics.Debug.WriteLine($"LoadConfiguration complete: {_overlays.Count} overlays created");

            // Re-snap previously occupied windows to their new cell positions
            if (_isEnabled && occupiedWindows.Count > 0)
            {
                System.Diagnostics.Debug.WriteLine($"LoadConfiguration: Re-snapping {occupiedWindows.Sum(kvp => kvp.Value.Count)} window(s) to new layout");

                foreach (var kvp in occupiedWindows)
                {
                    var monitorDevice = kvp.Key;
                    var windows = kvp.Value;

                    if (!config.MonitorLayouts.TryGetValue(monitorDevice, out var newLayout))
                        continue;

                    if (!_overlays.TryGetValue(monitorDevice, out var overlay))
                        continue;

                    foreach (var (cellIndex, windowHandle) in windows)
                    {
                        // Re-snap to same cell index in new layout (if it exists)
                        if (cellIndex < newLayout.Cells.Count)
                        {
                            var newCell = newLayout.Cells[cellIndex];
                            SnapWindowToCell(windowHandle, newCell, newLayout);
                            if (!newCell.OccupiedWindows.Contains(windowHandle))
                            {
                                newCell.OccupiedWindows.Add(windowHandle);
                            }
                            System.Diagnostics.Debug.WriteLine($"  Re-snapped window {windowHandle} to cell {cellIndex}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"  Cell index {cellIndex} no longer exists in new layout (only {newLayout.Cells.Count} cells)");
                        }
                    }

                    overlay.UpdateOccupiedCells();
                }
            }
        }

        /// <summary>
        /// Enable the grid system
        /// </summary>
        public void Enable()
        {
            if (_isEnabled) return;

            if (_currentConfig == null)
            {
                System.Diagnostics.Debug.WriteLine("WARNING: Trying to enable grid without configuration!");
                // Don't return - allow enabling, config might be applied later
            }

            _isEnabled = true;
            _windowTrackingService.StartTracking();

            // Show all overlays but make them invisible until drag starts
            foreach (var overlay in _overlays.Values)
            {
                overlay.SetVisible(true);
                overlay.SetOpacity(0.0); // Hide until dragging starts
            }

            System.Diagnostics.Debug.WriteLine($"Grid enabled - {_overlays.Count} overlays ready (hidden until drag)");

            if (_overlays.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("WARNING: No overlays! Make sure to click 'Apply Grid Configuration' first!");
            }
        }

        /// <summary>
        /// Disable the grid system
        /// </summary>
        public void Disable()
        {
            if (!_isEnabled) return;

            _isEnabled = false;
            _windowTrackingService.StopTracking();

            // Hide all overlays
            foreach (var overlay in _overlays.Values)
            {
                overlay.SetVisible(false);
            }
        }

        /// <summary>
        /// Toggle grid system on/off
        /// </summary>
        public void Toggle()
        {
            if (_isEnabled)
                Disable();
            else
                Enable();
        }

        /// <summary>
        /// Toggle grid preview mode (shows grid without enabling snapping)
        /// </summary>
        public void TogglePreview()
        {
            if (_isPreviewMode)
                DisablePreview();
            else
                EnablePreview();
        }

        /// <summary>
        /// Enable preview mode - shows grid overlay without snapping functionality
        /// </summary>
        public void EnablePreview()
        {
            if (_isPreviewMode) return;

            System.Diagnostics.Debug.WriteLine($"EnablePreview called - currently have {_overlays.Count} overlays");

            if (_overlays.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("WARNING: No overlays exist! Configuration may not be loaded.");
                return;
            }

            _isPreviewMode = true;

            // Show all overlays at full opacity for preview
            foreach (var overlay in _overlays.Values)
            {
                System.Diagnostics.Debug.WriteLine($"  Setting overlay visible and opacity to 0.6");
                overlay.SetVisible(true);
                overlay.SetOpacity(0.6); // Visible but not too intrusive
            }

            System.Diagnostics.Debug.WriteLine($"Grid preview enabled - {_overlays.Count} overlays shown");
        }

        /// <summary>
        /// Disable preview mode
        /// </summary>
        public void DisablePreview()
        {
            if (!_isPreviewMode) return;

            _isPreviewMode = false;

            // Hide all overlays
            foreach (var overlay in _overlays.Values)
            {
                overlay.SetVisible(false);
            }

            System.Diagnostics.Debug.WriteLine("Grid preview disabled");
        }

        private void OnWindowDragStarted(object? sender, WindowDragEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"GridManager: Drag started! Enabled={_isEnabled}");

            if (!_isEnabled) return;

            // Show overlays during drag
            foreach (var overlay in _overlays.Values)
            {
                overlay.SetOpacity(0.8); // Make visible during drag
            }

            System.Diagnostics.Debug.WriteLine($"GridManager: Overlays now visible");
        }

        private void OnWindowDragMoved(object? sender, WindowDragEventArgs e)
        {
            if (!_isEnabled || _currentConfig == null)
            {
                return;
            }

            // Use mouse position for cell detection
            var monitor = _monitorService.GetMonitorFromPoint(e.MousePosition);
            if (monitor == null)
            {
                return;
            }

            // Get the grid layout for this monitor
            if (!_currentConfig.MonitorLayouts.TryGetValue(monitor.DeviceName, out var layout))
            {
                return;
            }

            // Find cell at mouse position
            var cell = layout.FindCellAtPoint(e.MousePosition);

            // Highlight the cell
            if (_overlays.TryGetValue(monitor.DeviceName, out var overlay))
            {
                // Clear highlight on previous monitor if we switched
                if (_currentMonitorDevice != null && _currentMonitorDevice != monitor.DeviceName)
                {
                    if (_overlays.TryGetValue(_currentMonitorDevice, out var prevOverlay))
                    {
                        prevOverlay.HighlightCell(null);
                    }
                }

                overlay.HighlightCell(cell);
                _currentHighlightedCell = cell;
                _currentMonitorDevice = monitor.DeviceName;
            }
        }

        private void OnWindowDragEnded(object? sender, WindowDragEventArgs e)
        {
            if (!_isEnabled || _currentConfig == null) return;

            try
            {
                // Use mouse position to determine target cell
                var monitor = _monitorService.GetMonitorFromPoint(e.MousePosition);
                if (monitor != null)
                {
                    if (_currentConfig.MonitorLayouts.TryGetValue(monitor.DeviceName, out var layout))
                    {
                        var targetCell = layout.FindCellAtPoint(e.MousePosition);

                        System.Diagnostics.Debug.WriteLine($"GridManager: Drag ended at mouse {e.MousePosition}, monitor {monitor.DeviceName}, cell {targetCell?.Id ?? -1}");

                        if (targetCell != null)
                        {
                            SnapWindowToCell(e.WindowHandle, targetCell, layout);

                            // Add window to cell's occupied list
                            if (!targetCell.OccupiedWindows.Contains(e.WindowHandle))
                            {
                                targetCell.OccupiedWindows.Add(e.WindowHandle);
                            }

                            // Update overlay
                            if (_overlays.TryGetValue(monitor.DeviceName, out var overlay))
                            {
                                overlay.UpdateOccupiedCells();
                            }
                        }
                    }
                }
            }
            finally
            {
                // Clear highlighting and hide overlays
                foreach (var overlay in _overlays.Values)
                {
                    overlay.HighlightCell(null);
                    overlay.SetOpacity(0.0); // Hide overlays when not dragging
                }

                _currentHighlightedCell = null;
                _currentMonitorDevice = null;
            }
        }

        private void OnWindowShown(object? sender, WindowDragEventArgs e)
        {
            if (!_isEnabled || _currentConfig == null)
            {
                System.Diagnostics.Debug.WriteLine($"GridManager: New window shown but grid not enabled or no config");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"GridManager: New target window shown, finding first available cell...");

            // Find which monitor the window is on
            var windowCenter = new Point(
                e.WindowBounds.Left + e.WindowBounds.Width / 2,
                e.WindowBounds.Top + e.WindowBounds.Height / 2);

            var monitor = _monitorService.GetMonitorFromPoint(windowCenter);
            if (monitor == null)
            {
                System.Diagnostics.Debug.WriteLine($"  -> No monitor found");
                return;
            }

            // Get the grid layout for this monitor
            if (!_currentConfig.MonitorLayouts.TryGetValue(monitor.DeviceName, out var layout))
            {
                System.Diagnostics.Debug.WriteLine($"  -> No layout for monitor {monitor.DeviceName}");
                return;
            }

            // Find first available cell (prefers empty cells)
            var cell = layout.FindFirstAvailableCell();
            if (cell == null)
            {
                System.Diagnostics.Debug.WriteLine($"  -> No cells available");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"  -> Auto-snapping to cell {cell.Id} (currently has {cell.WindowCount} window(s))");

            // Snap window to the cell
            SnapWindowToCell(e.WindowHandle, cell, layout);

            // Add window to cell's occupied list
            if (!cell.OccupiedWindows.Contains(e.WindowHandle))
            {
                cell.OccupiedWindows.Add(e.WindowHandle);
            }

            // Update overlay
            if (_overlays.TryGetValue(monitor.DeviceName, out var overlay))
            {
                overlay.UpdateOccupiedCells();
            }
        }

        /// <summary>
        /// Snap a window to a specific cell
        /// </summary>
        private void SnapWindowToCell(IntPtr hWnd, GridCell cell, GridLayout layout)
        {
            // Apply margins from the layout
            var cellBounds = new Rect(
                cell.Bounds.Left + layout.CellMargins.Left,
                cell.Bounds.Top + layout.CellMargins.Top,
                cell.Bounds.Width - layout.CellMargins.Left - layout.CellMargins.Right,
                cell.Bounds.Height - layout.CellMargins.Top - layout.CellMargins.Bottom);

            Rect targetBounds;

            // Check if we should maintain aspect ratio
            if (_currentConfig?.MaintainAspectRatio == true)
            {
                // Get current window position to detect aspect ratio
                RECT currentRect;
                if (!GetWindowRect(hWnd, out currentRect))
                {
                    // Fallback to fill cell if we can't get current position
                    targetBounds = cellBounds;
                }
                else
                {
                    // Calculate current aspect ratio
                    double currentWidth = currentRect.Right - currentRect.Left;
                    double currentHeight = currentRect.Bottom - currentRect.Top;
                    double aspectRatio = currentWidth / currentHeight;

                    System.Diagnostics.Debug.WriteLine($"SnapWindowToCell: Cell {cell.Id}, maintaining aspect ratio: {aspectRatio:F3}");

                    // Calculate target size while maintaining aspect ratio to fit in cell
                    double targetWidth = cellBounds.Width;
                    double targetHeight = cellBounds.Height;

                    // Adjust to maintain aspect ratio (fit within cell bounds)
                    double cellAspectRatio = cellBounds.Width / cellBounds.Height;

                    if (aspectRatio > cellAspectRatio)
                    {
                        // Window is wider than cell - constrain by width
                        targetWidth = cellBounds.Width;
                        targetHeight = targetWidth / aspectRatio;
                    }
                    else
                    {
                        // Window is taller than cell - constrain by height
                        targetHeight = cellBounds.Height;
                        targetWidth = targetHeight * aspectRatio;
                    }

                    // Center the window in the cell
                    double targetX = cellBounds.Left + (cellBounds.Width - targetWidth) / 2;
                    double targetY = cellBounds.Top + (cellBounds.Height - targetHeight) / 2;

                    targetBounds = new Rect(targetX, targetY, targetWidth, targetHeight);
                }
            }
            else
            {
                // Fill the entire cell without maintaining aspect ratio
                System.Diagnostics.Debug.WriteLine($"SnapWindowToCell: Cell {cell.Id}, filling cell (ignoring aspect ratio)");
                targetBounds = cellBounds;
            }

            System.Diagnostics.Debug.WriteLine($"  Target: ({targetBounds.Left:F0}, {targetBounds.Top:F0}) size ({targetBounds.Width:F0}x{targetBounds.Height:F0})");

            // Move and resize window
            SetWindowPos(
                hWnd,
                IntPtr.Zero,
                (int)targetBounds.Left,
                (int)targetBounds.Top,
                (int)targetBounds.Width,
                (int)targetBounds.Height,
                SWP_NOZORDER | SWP_SHOWWINDOW);
        }

        /// <summary>
        /// Periodic cleanup timer - removes closed or moved windows from cells
        /// </summary>
        private void OnCleanupTimer(object? sender, EventArgs e)
        {
            if (_currentConfig == null || !_isEnabled)
                return;

            // Clean up each monitor's layout
            foreach (var kvp in _currentConfig.MonitorLayouts)
            {
                var monitorDevice = kvp.Key;
                var layout = kvp.Value;

                CleanupOccupiedWindows(layout);

                // Update the overlay to reflect changes
                if (_overlays.TryGetValue(monitorDevice, out var overlay))
                {
                    overlay.UpdateOccupiedCells();
                }
            }
        }

        /// <summary>
        /// Clean up occupied windows list - remove closed or moved windows
        /// </summary>
        private void CleanupOccupiedWindows(GridLayout layout)
        {
            foreach (var cell in layout.Cells)
            {
                var windowsToRemove = new List<IntPtr>();

                foreach (var windowHandle in cell.OccupiedWindows)
                {
                    // Check if window still exists
                    if (!IsWindow(windowHandle))
                    {
                        windowsToRemove.Add(windowHandle);
                        continue;
                    }

                    // Check if window is still in this cell
                    RECT rect;
                    if (GetWindowRect(windowHandle, out rect))
                    {
                        var windowCenter = new Point(
                            (rect.Left + rect.Right) / 2.0,
                            (rect.Top + rect.Bottom) / 2.0);

                        // If window center is no longer in this cell, remove it
                        if (!cell.Contains(windowCenter))
                        {
                            windowsToRemove.Add(windowHandle);
                        }
                    }
                    else
                    {
                        // Failed to get window rect, probably closed
                        windowsToRemove.Add(windowHandle);
                    }
                }

                // Remove invalid windows
                foreach (var handle in windowsToRemove)
                {
                    cell.OccupiedWindows.Remove(handle);
                }
            }
        }

        /// <summary>
        /// Manually snap a window to the nearest cell
        /// </summary>
        public void SnapWindowToNearestCell(IntPtr hWnd)
        {
            if (_currentConfig == null) return;

            var monitor = _monitorService.GetMonitorFromWindow(hWnd);
            if (monitor == null) return;

            if (!_currentConfig.MonitorLayouts.TryGetValue(monitor.DeviceName, out var layout))
                return;

            // Get window bounds
            RECT rect;
            if (!GetWindowRect(hWnd, out rect))
                return;

            var windowBounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            var cell = layout.FindBestMatchingCell(windowBounds);

            if (cell != null)
            {
                SnapWindowToCell(hWnd, cell, layout);

                if (!cell.OccupiedWindows.Contains(hWnd))
                {
                    cell.OccupiedWindows.Add(hWnd);
                }

                if (_overlays.TryGetValue(monitor.DeviceName, out var overlay))
                {
                    overlay.UpdateOccupiedCells();
                }
            }
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private void ClearOverlays()
        {
            foreach (var overlay in _overlays.Values)
            {
                overlay.Close();
            }
            _overlays.Clear();
        }

        /// <summary>
        /// Get count of all target windows matching the current filters
        /// </summary>
        public int GetTargetWindowCount()
        {
            if (_currentConfig == null)
                return 0;

            var allWindows = _windowEnumerationService.GetAllWindows();
            var count = 0;

            foreach (var window in allWindows)
            {
                if (IsWindowMatchingFilters(window.ClassName, window.Title))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Auto-arrange all target windows across the grid
        /// </summary>
        public int AutoArrangeWindows()
        {
            if (!_isEnabled || _currentConfig == null)
            {
                System.Diagnostics.Debug.WriteLine("AutoArrange: Grid not enabled or no config");
                return 0;
            }

            var allWindows = _windowEnumerationService.GetAllWindows();
            var targetWindows = new List<WindowEnumerationService.WindowInfo>();

            // Find all matching windows
            foreach (var window in allWindows)
            {
                if (IsWindowMatchingFilters(window.ClassName, window.Title))
                {
                    targetWindows.Add(window);
                }
            }

            System.Diagnostics.Debug.WriteLine($"AutoArrange: Found {targetWindows.Count} target window(s)");

            if (targetWindows.Count == 0)
                return 0;

            var arranged = 0;

            // Group windows by monitor
            var windowsByMonitor = new Dictionary<string, List<WindowEnumerationService.WindowInfo>>();

            foreach (var window in targetWindows)
            {
                RECT rect;
                if (GetWindowRect(window.Handle, out rect))
                {
                    var windowCenter = new Point(
                        (rect.Left + rect.Right) / 2.0,
                        (rect.Top + rect.Bottom) / 2.0);

                    var monitor = _monitorService.GetMonitorFromPoint(windowCenter);
                    if (monitor != null)
                    {
                        if (!windowsByMonitor.ContainsKey(monitor.DeviceName))
                        {
                            windowsByMonitor[monitor.DeviceName] = new List<WindowEnumerationService.WindowInfo>();
                        }
                        windowsByMonitor[monitor.DeviceName].Add(window);
                    }
                }
            }

            // Arrange windows on each monitor
            foreach (var kvp in windowsByMonitor)
            {
                var monitorDevice = kvp.Key;
                var windows = kvp.Value;

                if (!_currentConfig.MonitorLayouts.TryGetValue(monitorDevice, out var layout))
                    continue;

                if (!_overlays.TryGetValue(monitorDevice, out var overlay))
                    continue;

                // Clear all occupied windows for this monitor first
                foreach (var cell in layout.Cells)
                {
                    cell.OccupiedWindows.Clear();
                }

                // Distribute windows across cells
                var windowIndex = 0;
                foreach (var window in windows)
                {
                    // Find cell with fewest windows
                    var cell = layout.FindFirstAvailableCell();
                    if (cell != null)
                    {
                        SnapWindowToCell(window.Handle, cell, layout);
                        cell.OccupiedWindows.Add(window.Handle);
                        arranged++;

                        System.Diagnostics.Debug.WriteLine($"  Arranged window '{window.Title}' to cell {cell.Id}");
                    }

                    windowIndex++;
                }

                overlay.UpdateOccupiedCells();
            }

            System.Diagnostics.Debug.WriteLine($"AutoArrange: Arranged {arranged} window(s)");
            return arranged;
        }

        private bool IsWindowMatchingFilters(string className, string title)
        {
            if (_currentConfig == null)
                return false;

            // If no filters configured, don't match any windows
            if (_currentConfig.WindowFilters.Count == 0)
                return false;

            // Check if window matches ANY of the filters
            foreach (var filter in _currentConfig.WindowFilters)
            {
                if (filter.Matches(className, title))
                    return true;
            }

            return false;
        }

        public void Dispose()
        {
            _cleanupTimer.Stop();
            Disable();
            ClearOverlays();
            _windowTrackingService.Dispose();
        }
    }
}
