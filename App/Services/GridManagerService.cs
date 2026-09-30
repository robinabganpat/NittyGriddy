using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using App.Models;
using App.Native;
using App.Windows;

namespace App.Services
{
    /// <summary>
    /// A table window and the slot it occupies (null when it is not in the grid)
    /// </summary>
    public sealed record TableInfo(IntPtr Handle, string Title, int? SlotNumber, bool IsPinned);

    /// <summary>
    /// A window of a recognised poker program that is not treated as a table (lobby, cashier, ...)
    /// </summary>
    public sealed record OtherClientWindow(string Client, string Title, string ClassName);

    /// <summary>
    /// Main service that manages grid overlays, window tracking, and snapping.
    /// Works on windows only: it moves, resizes and activates them, and never sends input to them.
    /// </summary>
    public class GridManagerService
    {
        private sealed record Slot(string MonitorDevice, GridLayout Layout, GridCell Cell);

        private readonly MonitorService _monitorService;
        private readonly WindowTrackingService _windowTrackingService;
        private readonly WindowEnumerationService _windowEnumerationService;
        private readonly ProcessNameCache _processNames = new();
        private readonly TableRegistry _registry = new();
        private readonly Dictionary<string, GridOverlayWindow> _overlays = new();
        private readonly List<Slot> _slots = new();

        // Windows already seen as tables, in a slot or not. A table the user took out of the grid stays out
        // when its title changes; only a window that newly becomes a table is placed.
        private readonly HashSet<IntPtr> _knownTables = new();

        // The slot each table was last pinned to (null = no pin matched). A pin is applied when this changes,
        // so a pinned table the user dragged elsewhere is not pulled back on every title update.
        private readonly Dictionary<IntPtr, int?> _appliedPins = new();

        // "Fill with the open tables" displays: a display to give one extra slot while a new table is placed,
        // counts to use instead of the registry's while arranging, and the pause before shrinking a display
        private string? _autoFitGrowFor;
        private readonly Dictionary<string, int> _autoFitCountOverride = new();
        private readonly DispatcherTimer _autoFitTimer;
        private bool _rebuildingSlots;
        private HashSet<string> _autoFitDisplays = new();

        private readonly DispatcherTimer _cleanupTimer;
        private readonly DispatcherTimer _flashTimer;
        private ActiveTableBorderWindow? _activeBorder;

        // Cascading: offset between stacked tables, the setting last applied, and each slot's table count at the
        // last sync (a stack that grew or shrank is refitted)
        private const double CascadeStep = 32;
        private bool _cascadeApplied;
        private int[] _lastSlotCounts = Array.Empty<int>();

        // The table that last had focus, to return to after bringing all tables forward
        private IntPtr _lastActiveTable;

        private MonitorGridConfig? _currentConfig;
        private BehaviorSettings _behavior = new();
        private bool _isEnabled;
        private bool _isPreviewMode;
        private string? _currentMonitorDevice;
        private Rect _dragStartBounds;
        private bool _dragIsResize;

        public bool IsEnabled => _isEnabled;
        public bool IsPreviewMode => _isPreviewMode;

        /// <summary>
        /// Total number of slots across all monitors
        /// </summary>
        public int SlotCount => _slots.Count;

        /// <summary>
        /// How many overlay windows have been created so far; stays put while settings are merely adjusted
        /// </summary>
        internal int OverlayWindowsCreated { get; private set; }

        /// <summary>
        /// Raised when tables enter, leave or move between slots
        /// </summary>
        public event Action? TablesChanged;

        public GridManagerService(Dispatcher dispatcher)
        {
            _monitorService = new MonitorService();
            _windowTrackingService = new WindowTrackingService(IsTargetWindow);
            _windowEnumerationService = new WindowEnumerationService();

            // Wire up window tracking events
            _windowTrackingService.WindowDragStarted += OnWindowDragStarted;
            _windowTrackingService.WindowDragMoved += OnWindowDragMoved;
            _windowTrackingService.WindowDragEnded += OnWindowDragEnded;
            _windowTrackingService.WindowShown += OnWindowShown;
            _windowTrackingService.WindowTitleChanged += OnWindowTitleChanged;
            _windowTrackingService.WindowRestored += OnWindowRestored;
            _windowTrackingService.WindowDestroyed += OnWindowDestroyed;
            _windowTrackingService.ForegroundChanged += hwnd => UpdateActiveBorder();
            _windowTrackingService.FollowedWindowChanged += hwnd => UpdateActiveBorder();

            // Set up periodic cleanup of occupied windows (every 2 seconds)
            _cleanupTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
            _cleanupTimer.Interval = TimeSpan.FromSeconds(2);
            _cleanupTimer.Tick += OnCleanupTimer;
            _cleanupTimer.Start();

            // Closing tables in quick succession (a tournament breaking up) causes one rearrangement, not several
            _autoFitTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
            _autoFitTimer.Interval = TimeSpan.FromMilliseconds(1500);
            _autoFitTimer.Tick += (s, e) =>
            {
                _autoFitTimer.Stop();
                ReflowAutoFitDisplays();
            };

            _flashTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
            _flashTimer.Interval = TimeSpan.FromMilliseconds(900);
            _flashTimer.Tick += (s, e) =>
            {
                _flashTimer.Stop();
                RestoreOverlayOpacity();
            };
        }

        /// <summary>
        /// Apply behaviour options (drop behaviour, active-table border, compaction)
        /// </summary>
        public void ApplyBehavior(BehaviorSettings behavior)
        {
            var cascadeChanged = behavior.CascadeStacks != _cascadeApplied;
            _behavior = behavior;
            _cascadeApplied = behavior.CascadeStacks;

            // Stacked tables change shape when cascading is switched on or off
            if (cascadeChanged && _isEnabled)
            {
                for (var slot = 0; slot < _slots.Count; slot++)
                {
                    if (_registry.TablesIn(slot).Count > 1)
                        SnapSlot(slot);
                }
            }

            UpdateActiveBorder();
        }

        /// <summary>
        /// Snap every table in one slot, e.g. after the stack in it changed
        /// </summary>
        private void SnapSlot(int slot)
        {
            foreach (var table in _registry.TablesIn(slot))
                SnapTableToItsSlot(table);
        }

        /// <summary>
        /// Load and activate a grid configuration
        /// </summary>
        /// <param name="replaceDisplaced">
        /// Whether tables whose slot no longer exists are moved to another slot. True when the user changed the
        /// grid; false when a monitor disappeared, where tables are left where Windows puts them.
        /// </param>
        public void LoadConfiguration(MonitorGridConfig config, bool replaceDisplaced = true)
        {
            _currentConfig = config;
            config.MigrateLegacyFilters();

            var displaced = RebuildSlots();

            ShowOverlaysForCurrentState();

            if (_isEnabled)
            {
                // The rules may have changed: a window that is no longer a table leaves the grid
                _registry.Prune(IsLiveTable);

                if (replaceDisplaced)
                {
                    foreach (var table in displaced.Where(IsLiveTable))
                        PlaceTable(table, snap: false);
                }

                // Pins may have changed too
                foreach (var table in _registry.AllTables)
                    ApplyPinIfChanged(table, force: true, snap: false);

                // Re-snap every table to its (possibly resized) slot
                foreach (var table in _registry.AllTables)
                    SnapTableToItsSlot(table);

                AdoptTablesAlreadyInSlots();
            }

            SyncCells();
        }

        /// <summary>
        /// Work out the slots of every display from the current configuration, update the overlays and the registry.
        /// Returns the tables whose slot no longer exists.
        /// </summary>
        private IReadOnlyList<IntPtr> RebuildSlots()
        {
            var config = _currentConfig!;
            _rebuildingSlots = true;
            _slots.Clear();

            // Primary monitor first, then by device name: this order defines the slot numbers
            var monitors = _monitorService.GetAllMonitors()
                .OrderByDescending(m => m.IsPrimary)
                .ThenBy(m => m.DeviceName, StringComparer.Ordinal)
                .ToList();

            var groups = new List<SlotGroup>();
            var monitorsWithGrid = new HashSet<string>();

            // Connected displays that fill themselves with their tables
            _autoFitDisplays = monitors
                .Where(m => config.MonitorLayouts.TryGetValue(m.DeviceName, out var l) && l.Mode == GridLayoutMode.AutoFit)
                .Select(m => m.DeviceName)
                .ToHashSet();

            foreach (var monitor in monitors)
            {
                var layout = config.GetOrCreateLayoutForMonitor(monitor.DeviceName);
                layout.DisplayBounds = monitor.WorkArea;
                if (layout.Mode == GridLayoutMode.AutoFit)
                {
                    layout.AutoFitCount = AutoFitCountFor(monitor.DeviceName);
                    layout.TableAspectRatio = TableAspectRatioOn(monitor.DeviceName);
                }
                layout.CalculateCells();

                // Only create overlay if the layout is not disabled
                if (layout.Mode == GridLayoutMode.Disabled || layout.Cells.Count == 0)
                    continue;

                foreach (var cell in layout.Cells)
                {
                    _slots.Add(new Slot(monitor.DeviceName, layout, cell));
                    cell.SlotNumber = _slots.Count;
                }

                groups.Add(new SlotGroup(monitor.DeviceName, layout.Cells.Count));
                monitorsWithGrid.Add(monitor.DeviceName);

                // Keep the overlay window a display already has and only redraw it. Closing and reopening it on
                // every change made the whole display flicker while settings were being adjusted.
                if (_overlays.TryGetValue(monitor.DeviceName, out var overlay) && overlay.MonitorBounds != monitor.Bounds)
                {
                    overlay.Close();
                    overlay = null;
                }

                if (overlay == null)
                {
                    overlay = new GridOverlayWindow(monitor);
                    OverlayWindowsCreated++;
                }
                overlay.SetGridLayout(layout);
                _overlays[monitor.DeviceName] = overlay;
            }

            // Displays that no longer have a grid (switched to "No grid", or unplugged)
            foreach (var device in _overlays.Keys.Where(d => !monitorsWithGrid.Contains(d)).ToList())
            {
                _overlays[device].Close();
                _overlays.Remove(device);
            }

            // A display that fills itself with its tables packs them into its first slots before shrinking,
            // so none of them loses its slot, and spreads a stack out after growing
            var autoFit = AutoFitDisplays().ToList();
            foreach (var device in autoFit)
                _registry.Compact(device);

            // Tables keep their slot on a monitor whose grid still has it
            var displaced = _registry.SetGroups(groups);

            foreach (var device in autoFit)
                _registry.Distribute(device);

            _rebuildingSlots = false;
            return displaced;
        }

        // ----- Displays that fill themselves with the open tables -----

        private IEnumerable<string> AutoFitDisplays() => _autoFitDisplays;

        private bool IsAutoFit(string? device) => device != null && _autoFitDisplays.Contains(device);

        private int AutoFitMax(string device) => Math.Max(1, _currentConfig!.MonitorLayouts[device].AutoFitMaxTables);

        /// <summary>
        /// How many slots a filling display should have: one per table on it
        /// </summary>
        private int AutoFitCountFor(string device)
        {
            int count;
            if (_autoFitCountOverride.TryGetValue(device, out var overridden))
                count = overridden;
            else if (_isEnabled)
                count = _registry.TablesInGroup(device).Count + (_autoFitGrowFor == device ? 1 : 0);
            else
                // Grid off: size the preview to the tables that are on that display now
                count = EnumerateTargetWindows().Count(w => MonitorGroupOf(w.Handle) == device);

            return Math.Clamp(count, 1, AutoFitMax(device));
        }

        /// <summary>
        /// Shape of the tables on a display, which decides the grid shape. Only meaningful when tables keep their
        /// shape; otherwise they have taken the shape of their slots and a typical table shape is used.
        /// </summary>
        private double TableAspectRatioOn(string device)
        {
            if (_currentConfig?.MaintainAspectRatio != true)
                return GridLayout.DefaultTableAspectRatio;

            var ratios = _registry.TablesInGroup(device)
                .Where(h => NativeMethods.IsWindow(h) && !NativeMethods.IsIconic(h))
                .Select(NativeMethods.GetVisibleFrameBounds)
                .Where(r => !r.IsEmpty && r.Height > 0)
                .Select(r => r.Width / r.Height)
                .ToList();

            return ratios.Count > 0 ? ratios.Average() : GridLayout.DefaultTableAspectRatio;
        }

        /// <summary>
        /// A filling display whose slot count no longer matches its tables is rearranged after a short pause
        /// </summary>
        private void ScheduleAutoFitIfNeeded()
        {
            if (!_isEnabled || _rebuildingSlots)
                return;

            var needed = AutoFitDisplays().Any(device =>
            {
                var layout = _currentConfig!.MonitorLayouts[device];
                var wanted = Math.Clamp(_registry.TablesInGroup(device).Count, 1, AutoFitMax(device));
                return wanted != layout.Cells.Count;
            });

            if (needed)
            {
                _autoFitTimer.Stop();
                _autoFitTimer.Start();
            }
        }

        private void ReflowAutoFitDisplays()
        {
            if (!_isEnabled || _currentConfig == null)
                return;

            RebuildSlots();
            foreach (var device in AutoFitDisplays())
            {
                foreach (var table in _registry.TablesInGroup(device))
                    SnapTableToItsSlot(table);
            }
            SyncCells();
        }

        /// <summary>
        /// Give a filling display one more slot for a new table. Returns that slot, or null if no filling display
        /// has room. The display the table appeared on is preferred.
        /// </summary>
        private int? GrowAutoFitDisplayFor(string? tableMonitor)
        {
            if (_rebuildingSlots)
                return null;

            bool HasRoom(string device) => _registry.TablesInGroup(device).Count < AutoFitMax(device);

            var device = IsAutoFit(tableMonitor) && HasRoom(tableMonitor!)
                ? tableMonitor
                : AutoFitDisplays().FirstOrDefault(HasRoom);
            if (device == null)
                return null;

            _autoFitTimer.Stop();
            _autoFitGrowFor = device;
            RebuildSlots();
            _autoFitGrowFor = null;

            // The tables already there shrink to make room
            foreach (var table in _registry.TablesInGroup(device))
                SnapTableToItsSlot(table);

            var slot = _registry.FirstAvailableSlot(device);
            return slot != null && _registry.TablesIn(slot.Value).Count == 0 ? slot : null;
        }

        /// <summary>
        /// Enable the grid system
        /// </summary>
        public void Enable()
        {
            if (_isEnabled) return;

            _isEnabled = true;
            _windowTrackingService.StartTracking();

            // Slots may hold handles from before the grid was turned off; drop the ones that are gone or moved
            _registry.Prune(StillInItsSlot);
            _knownTables.RemoveWhere(h => !NativeMethods.IsWindow(h));
            AdoptTablesAlreadyInSlots();

            ShowOverlaysForCurrentState();
            UpdateActiveBorder();
            SyncCells();
        }

        /// <summary>
        /// Disable the grid system
        /// </summary>
        public void Disable()
        {
            if (!_isEnabled) return;

            _isEnabled = false;
            _windowTrackingService.StopTracking();

            ShowOverlaysForCurrentState();
            UpdateActiveBorder();
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
            _isPreviewMode = !_isPreviewMode;
            ShowOverlaysForCurrentState();
        }

        /// <summary>
        /// Briefly show the grid, e.g. after switching layout, so the new slots are visible
        /// </summary>
        public void FlashOverlays()
        {
            // With the grid off there is nothing to orient on; the preview button shows the slots on demand
            if (_overlays.Count == 0 || !_isEnabled)
                return;

            foreach (var overlay in _overlays.Values)
            {
                overlay.SetOpacity(0.7);
                overlay.SetVisible(true);
            }

            _flashTimer.Stop();
            _flashTimer.Start();
        }

        /// <summary>
        /// Overlays stay loaded but transparent while the grid is enabled, visible in preview, hidden otherwise
        /// </summary>
        private void ShowOverlaysForCurrentState()
        {
            // A flash in progress (grid just turned on, profile switched) runs to its end
            if (_flashTimer.IsEnabled)
                return;

            RestoreOverlayOpacity();
        }

        private void RestoreOverlayOpacity()
        {
            foreach (var overlay in _overlays.Values)
            {
                // Opacity first, then visibility, so an overlay never appears at the wrong opacity
                if (_isPreviewMode)
                {
                    overlay.SetOpacity(0.6); // Visible but not too intrusive
                    overlay.SetVisible(true);
                }
                else if (_isEnabled)
                {
                    overlay.SetOpacity(0.0); // Hidden until a drag starts
                    overlay.SetVisible(true);
                }
                else
                {
                    overlay.SetVisible(false);
                }
            }
        }

        // ----- Dragging -----

        private void OnWindowDragStarted(object? sender, WindowDragEventArgs e)
        {
            if (!_isEnabled) return;

            _dragStartBounds = e.WindowBounds;
            _dragIsResize = false;
            _flashTimer.Stop();

            // Show overlays during drag
            foreach (var overlay in _overlays.Values)
            {
                overlay.SetOpacity(0.8); // Make visible during drag
            }
        }

        private void OnWindowDragMoved(object? sender, WindowDragEventArgs e)
        {
            if (!_isEnabled || _currentConfig == null || _dragIsResize)
            {
                return;
            }

            // The same event reports resizing by the window edge; that is not a move between slots
            if (SizeChanged(e.WindowBounds.Size, _dragStartBounds.Size))
            {
                _dragIsResize = true;
                ClearHighlights();
                RestoreOverlayOpacity();
                return;
            }

            // Use mouse position for cell detection
            var slot = FindDropSlot(e.MousePosition);

            if (_currentMonitorDevice != null && _currentMonitorDevice != slot?.MonitorDevice &&
                _overlays.TryGetValue(_currentMonitorDevice, out var previousOverlay))
            {
                previousOverlay.HighlightCell(null);
            }

            _currentMonitorDevice = slot?.MonitorDevice;

            if (slot != null && _overlays.TryGetValue(slot.MonitorDevice, out var overlay))
            {
                overlay.HighlightCell(slot.Cell);
            }
        }

        private void OnWindowDragEnded(object? sender, WindowDragEventArgs e)
        {
            if (!_isEnabled || _currentConfig == null) return;

            try
            {
                if (_dragIsResize || SizeChanged(e.WindowBounds.Size, _dragStartBounds.Size))
                    return;

                // Back where it started: the drag was cancelled (Esc) or the title bar was only clicked
                if (e.WindowBounds.Location == _dragStartBounds.Location)
                    return;

                var slot = FindDropSlot(e.MousePosition);

                if (slot == null)
                {
                    // Dropped on a display without a grid: the table leaves its slot
                    _registry.Remove(e.WindowHandle);
                    return;
                }

                MoveTableToSlot(e.WindowHandle, slot.Cell.SlotNumber - 1);
            }
            finally
            {
                ClearHighlights();
                RestoreOverlayOpacity();
                SyncCells();
            }
        }

        private static bool SizeChanged(Size current, Size original)
        {
            return Math.Abs(current.Width - original.Width) > 2 || Math.Abs(current.Height - original.Height) > 2;
        }

        private void ClearHighlights()
        {
            foreach (var overlay in _overlays.Values)
                overlay.HighlightCell(null);

            _currentMonitorDevice = null;
        }

        /// <summary>
        /// The slot under the cursor. In the gap between slots, or over the taskbar, the nearest slot on that
        /// display counts, so a drop there is not mistaken for taking the table out of the grid.
        /// </summary>
        private Slot? FindDropSlot(Point point)
        {
            var direct = _slots.FirstOrDefault(s => s.Cell.Contains(point));
            if (direct != null)
                return direct;

            var monitor = _monitorService.GetMonitorFromPoint(point);
            if (monitor == null)
                return null;

            return _slots
                .Where(s => s.MonitorDevice == monitor.DeviceName)
                .OrderBy(s => DistanceSquared(s.Cell.Bounds, point))
                .FirstOrDefault();
        }

        private static double DistanceSquared(Rect rect, Point point)
        {
            var dx = Math.Max(Math.Max(rect.Left - point.X, 0), point.X - rect.Right);
            var dy = Math.Max(Math.Max(rect.Top - point.Y, 0), point.Y - rect.Bottom);
            return dx * dx + dy * dy;
        }

        // ----- Windows appearing, changing and closing -----

        private void OnWindowShown(object? sender, WindowDragEventArgs e)
        {
            if (!_isEnabled || _currentConfig == null)
                return;

            // A table that already has a slot was merely shown again (restored, or re-shown by its client): leave it
            if (_registry.Contains(e.WindowHandle))
                return;

            PlaceTable(e.WindowHandle, snap: true);
            SyncCells();
        }

        /// <summary>
        /// Titles change during play (blinds, table number) and some clients only set the title after the window
        /// appears. A window that newly becomes a table is placed; a table that newly matches a pin moves to it.
        /// </summary>
        private void OnWindowTitleChanged(IntPtr hwnd)
        {
            if (!_isEnabled || _currentConfig == null)
                return;

            if (_registry.Contains(hwnd))
            {
                if (ApplyPinIfChanged(hwnd, force: false, snap: true))
                    SyncCells();
                return;
            }

            if (_knownTables.Contains(hwnd))
                return;

            if (!NativeMethods.IsTopLevelWindow(hwnd) || !NativeMethods.IsShown(hwnd) || NativeMethods.IsIconic(hwnd))
                return;

            // A rule with only a title would pull in any program whose title happens to change to match
            // (a browser tab, a document), so late placement needs a rule that names a program or class
            if (MatchingRule(hwnd, specificOnly: true) != null)
            {
                PlaceTable(hwnd, snap: true);
                SyncCells();
            }
        }

        /// <summary>
        /// A minimised table is not moved when its slot changes; put it in its slot when it comes back
        /// </summary>
        private void OnWindowRestored(IntPtr hwnd)
        {
            if (_isEnabled && _registry.Contains(hwnd))
                SnapTableToItsSlot(hwnd);
        }

        private void OnWindowDestroyed(IntPtr hwnd)
        {
            _knownTables.Remove(hwnd);
            _appliedPins.Remove(hwnd);

            var slot = _registry.SlotOf(hwnd);
            if (slot == null)
                return;

            var group = _registry.GroupOf(slot.Value);
            _registry.Remove(hwnd);

            if (_behavior.CompactOnClose && group != null)
            {
                // Slots that belong to a pin stay as they are
                foreach (var (table, _) in _registry.Compact(group, ReservedSlots()))
                    SnapTableToItsSlot(table);
            }

            SyncCells();
        }

        // ----- Placement -----

        private IReadOnlyList<TablePin> Pins => (IReadOnlyList<TablePin>?)_currentConfig?.Pins ?? Array.Empty<TablePin>();

        private HashSet<int> ReservedSlots() => SlotPlanner.ReservedSlots(Pins, _slots.Count);

        private int? PinnedSlotOf(IntPtr hwnd) => SlotPlanner.PinnedSlot(NativeMethods.GetWindowTitle(hwnd), Pins, _slots.Count);

        /// <summary>
        /// Put a table that has no slot into the grid: its pinned slot if a pin matches, otherwise the best free slot
        /// </summary>
        private void PlaceTable(IntPtr hwnd, bool snap)
        {
            _knownTables.Add(hwnd);

            var pinned = PinnedSlotOf(hwnd);
            _appliedPins[hwnd] = pinned;

            if (pinned != null)
            {
                MoveToPinnedSlot(hwnd, pinned.Value, snap);
                return;
            }

            var monitor = MonitorGroupOf(hwnd);
            var slot = SlotPlanner.PickSlot(_registry, monitor, ReservedSlots());

            // A table that appears on a filling display with room gets a new slot there. Elsewhere, a filling
            // display grows only when there is no empty slot anywhere: better than stacking.
            var noEmptySlot = slot == null || _registry.TablesIn(slot.Value).Count > 0;
            var ownDisplayFills = IsAutoFit(monitor) && _registry.TablesInGroup(monitor!).Count < AutoFitMax(monitor!);
            if (ownDisplayFills || noEmptySlot)
                slot = GrowAutoFitDisplayFor(monitor) ?? slot;

            if (slot == null)
                return;

            _registry.Place(hwnd, slot.Value);
            if (snap)
                SnapTableToItsSlot(hwnd);
        }

        /// <summary>
        /// Move a table to its pinned slot if the pin it matches changed since it was last applied
        /// (or unconditionally with <paramref name="force"/>). Returns whether anything moved.
        /// </summary>
        private bool ApplyPinIfChanged(IntPtr hwnd, bool force, bool snap)
        {
            var pinned = PinnedSlotOf(hwnd);
            var changed = !_appliedPins.TryGetValue(hwnd, out var previous) || previous != pinned;
            _appliedPins[hwnd] = pinned;

            if (pinned == null || (!changed && !force) || _registry.SlotOf(hwnd) == pinned)
                return false;

            MoveToPinnedSlot(hwnd, pinned.Value, snap);
            return true;
        }

        /// <summary>
        /// Place a table in its pinned slot. Tables already there that are not pinned to it make room.
        /// </summary>
        private void MoveToPinnedSlot(IntPtr hwnd, int slot, bool snap)
        {
            var occupants = _registry.TablesIn(slot).Where(t => t != hwnd && PinnedSlotOf(t) != slot).ToList();

            _registry.Place(hwnd, slot);
            if (snap)
                SnapTableToItsSlot(hwnd);

            var thisSlot = new HashSet<int> { slot };
            foreach (var occupant in occupants)
            {
                var target = SlotPlanner.PickSlot(_registry, MonitorGroupOf(occupant), ReservedSlots(), exclude: thisSlot);
                if (target == null)
                    continue; // Nowhere else to go: it stays stacked under the pinned table

                _registry.Place(occupant, target.Value);
                if (snap)
                    SnapTableToItsSlot(occupant);
            }
        }

        /// <summary>
        /// Centre of a window on screen, or null while it is minimised
        /// </summary>
        private static Point? CenterOf(IntPtr hwnd)
        {
            if (NativeMethods.IsIconic(hwnd))
                return null;

            var bounds = NativeMethods.GetWindowBounds(hwnd);
            return bounds.IsEmpty ? null : new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        }

        private string? MonitorGroupOf(IntPtr hwnd)
        {
            return NativeMethods.IsIconic(hwnd) ? null : _monitorService.GetMonitorFromWindow(hwnd)?.DeviceName;
        }

        /// <summary>
        /// Move a table into a slot, swapping or stacking according to the drop behaviour, and reposition the windows involved
        /// </summary>
        private void MoveTableToSlot(IntPtr hwnd, int slotIndex)
        {
            _knownTables.Add(hwnd);
            var displaced = _registry.MoveOrSwap(hwnd, slotIndex, _behavior.DropOnOccupied == DropBehavior.Swap);

            SnapTableToItsSlot(hwnd);
            if (displaced != null)
                SnapTableToItsSlot(displaced.Value);
        }

        private void SnapTableToItsSlot(IntPtr hwnd)
        {
            var slotIndex = _registry.SlotOf(hwnd);
            if (slotIndex == null || slotIndex.Value >= _slots.Count)
                return;

            var slot = _slots[slotIndex.Value];
            var stack = _registry.TablesIn(slotIndex.Value);

            if (_behavior.CascadeStacks && stack.Count > 1)
                SnapWindowToCell(hwnd, slot.Cell, slot.Layout, stack.ToList().IndexOf(hwnd), stack.Count);
            else
                SnapWindowToCell(hwnd, slot.Cell, slot.Layout);
        }

        /// <summary>
        /// Snap a window to a specific cell
        /// </summary>
        private void SnapWindowToCell(IntPtr hWnd, GridCell cell, GridLayout layout, int stackIndex = 0, int stackCount = 1)
        {
            // A minimised window cannot be positioned; it is snapped when it is restored (OnWindowRestored)
            if (!NativeMethods.IsWindow(hWnd) || NativeMethods.IsIconic(hWnd))
                return;

            // A maximised window ignores a new size; bring it back to normal first, without activating it
            if (NativeMethods.IsZoomed(hWnd))
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOWNOACTIVATE);

            // Apply margins from the layout
            var cellBounds = new Rect(
                cell.Bounds.Left + layout.CellMargins.Left,
                cell.Bounds.Top + layout.CellMargins.Top,
                Math.Max(1, cell.Bounds.Width - layout.CellMargins.Left - layout.CellMargins.Right),
                Math.Max(1, cell.Bounds.Height - layout.CellMargins.Top - layout.CellMargins.Bottom));

            // Cascaded stack: this table's share of the slot, offset from the ones beneath it
            cellBounds = FrameMath.CascadeArea(cellBounds, stackIndex, stackCount, CascadeStep);

            var windowRect = NativeMethods.GetWindowBounds(hWnd);
            var visibleFrame = NativeMethods.GetVisibleFrameBounds(hWnd);

            // Either fill the cell, or keep the table's current shape and centre it in the cell
            var visibleTarget = _currentConfig?.MaintainAspectRatio == true && !visibleFrame.IsEmpty
                ? FrameMath.FitPreservingAspect(visibleFrame.Size, cellBounds)
                : cellBounds;

            // Place the visible frame on the target; the window rectangle may include an invisible resize border
            var targetBounds = FrameMath.WindowRectForVisibleTarget(visibleTarget, windowRect, visibleFrame);

            // Move and resize without activating: a table that is moved aside must not take focus
            NativeMethods.SetWindowPos(
                hWnd,
                IntPtr.Zero,
                (int)Math.Round(targetBounds.Left),
                (int)Math.Round(targetBounds.Top),
                (int)Math.Round(targetBounds.Width),
                (int)Math.Round(targetBounds.Height),
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        /// <summary>
        /// Register tables that already sit inside a slot, without moving them.
        /// Makes slot hotkeys work for tables that were open before the grid was turned on.
        /// </summary>
        private void AdoptTablesAlreadyInSlots()
        {
            foreach (var window in EnumerateTargetWindows())
            {
                _knownTables.Add(window.Handle);

                if (_registry.Contains(window.Handle) || NativeMethods.IsIconic(window.Handle))
                    continue;

                var bounds = NativeMethods.GetWindowBounds(window.Handle);
                if (bounds.IsEmpty)
                    continue;

                var slot = _slots.FirstOrDefault(s => s.Cell.Contains(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)));
                if (slot != null)
                {
                    _registry.Place(window.Handle, slot.Cell.SlotNumber - 1);
                    _appliedPins[window.Handle] = PinnedSlotOf(window.Handle);
                }
            }
        }

        /// <summary>
        /// Periodic cleanup timer - removes closed or moved windows from cells
        /// </summary>
        private void OnCleanupTimer(object? sender, EventArgs e)
        {
            if (_currentConfig == null || !_isEnabled)
                return;

            var removed = _registry.Prune(StillInItsSlot);
            if (removed.Count > 0)
                SyncCells();

            UpdateActiveBorder();
        }

        /// <summary>
        /// A window that exists, is shown and still matches a rule
        /// </summary>
        private bool IsLiveTable(IntPtr hwnd)
        {
            return NativeMethods.IsWindow(hwnd) && IsTargetWindow(hwnd);
        }

        private bool StillInItsSlot(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindow(hwnd))
                return false;

            // A minimised table keeps its slot; one being dragged is judged when the drag ends
            if (NativeMethods.IsIconic(hwnd) || hwnd == _windowTrackingService.DraggedWindow)
                return true;

            // A client that hides a table instead of closing it must not leave a ghost in the slot
            if (!NativeMethods.IsShown(hwnd))
                return false;

            var slotIndex = _registry.SlotOf(hwnd);
            var bounds = NativeMethods.GetWindowBounds(hwnd);
            if (slotIndex == null || slotIndex.Value >= _slots.Count || bounds.IsEmpty)
                return false;

            // If window center is no longer in its cell, something else moved it away
            var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            return _slots[slotIndex.Value].Cell.Contains(center);
        }

        // ----- Operations for hotkeys and the UI -----

        /// <summary>
        /// Snap the table that has focus to the slot it overlaps most. Does nothing for a window that is not a table.
        /// </summary>
        public bool SnapActiveTableToNearestCell()
        {
            var hWnd = NativeMethods.GetForegroundWindow();
            if (_currentConfig == null || !IsTargetWindow(hWnd)) return false;

            var bounds = NativeMethods.GetWindowBounds(hWnd);
            var slot = _slots
                .Where(s => s.Cell.IntersectsWith(bounds))
                .OrderByDescending(s => { var overlap = Rect.Intersect(s.Cell.Bounds, bounds); return overlap.Width * overlap.Height; })
                .FirstOrDefault();

            if (slot == null)
                return false;

            _knownTables.Add(hWnd);
            _registry.Place(hWnd, slot.Cell.SlotNumber - 1);
            SnapTableToItsSlot(hWnd);
            SyncCells();
            return true;
        }

        /// <summary>
        /// Bring the table in a slot to the front. Repeating it on a slot with several tables cycles through them.
        /// </summary>
        /// <param name="slotNumber">1-based slot number as shown on the overlay</param>
        public bool FocusSlot(int slotNumber)
        {
            var table = _registry.NextInSlot(slotNumber - 1, NativeMethods.GetForegroundWindow());
            return table != null && Activate(table.Value);
        }

        /// <summary>
        /// Move the table that has focus to a slot
        /// </summary>
        /// <param name="slotNumber">1-based slot number as shown on the overlay</param>
        public bool MoveActiveToSlot(int slotNumber)
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (slotNumber < 1 || slotNumber > _slots.Count || !IsTargetWindow(hwnd))
                return false;

            MoveTableToSlot(hwnd, slotNumber - 1);
            SyncCells();
            return true;
        }

        /// <summary>
        /// Bring the next (+1) or previous (-1) table to the front
        /// </summary>
        public bool FocusNext(int direction)
        {
            var table = _registry.Next(NativeMethods.GetForegroundWindow(), direction, EnumerateTargetWindows().Select(w => w.Handle));
            return table != null && Activate(table.Value);
        }

        /// <summary>
        /// Raise every table above other windows, restoring minimised ones, then give focus to the table used last
        /// (or the first one). Nothing is moved or resized, and it works whether or not the grid is on.
        /// Returns the number of tables brought forward.
        /// </summary>
        public int BringAllTablesToFront()
        {
            // Slot order, bottom of each stack first, then tables outside the grid: the last one raised ends on top,
            // so within a stack the table that was on top stays on top
            var tables = _registry.AllTables.Where(NativeMethods.IsWindow).ToList();
            tables.AddRange(EnumerateTargetWindows().Select(w => w.Handle).Where(h => !tables.Contains(h)));
            if (tables.Count == 0)
                return 0;

            var focus = tables.Contains(NativeMethods.GetForegroundWindow()) ? NativeMethods.GetForegroundWindow()
                : tables.Contains(_lastActiveTable) ? _lastActiveTable
                : tables[0];

            foreach (var table in tables)
            {
                if (NativeMethods.IsIconic(table))
                    NativeMethods.ShowWindow(table, NativeMethods.SW_SHOWNOACTIVATE);

                // Z-order only: no move, no resize, no activation
                NativeMethods.RaiseWithoutActivating(table);
            }

            Activate(focus);
            return tables.Count;
        }

        private static bool Activate(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindow(hwnd))
                return false;

            if (NativeMethods.IsIconic(hwnd))
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

            // Allowed because a hotkey press is the most recent input. Windows can still refuse, for example
            // while the program in front has a menu open; retrying later does not help, as the permission
            // has lapsed by then.
            return NativeMethods.SetForegroundWindow(hwnd);
        }

        /// <summary>
        /// Show, move or hide the frame around the focused table
        /// </summary>
        private void UpdateActiveBorder()
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground != IntPtr.Zero && IsTargetWindow(foreground))
                _lastActiveTable = foreground;

            var show = _isEnabled
                       && _behavior.ShowActiveBorder
                       && foreground != IntPtr.Zero
                       && !NativeMethods.IsIconic(foreground)
                       && NativeMethods.IsWindowVisible(foreground)
                       && IsTargetWindow(foreground);

            _windowTrackingService.FollowedWindow = show ? foreground : IntPtr.Zero;

            if (!show)
            {
                _activeBorder?.HideBorder();
                return;
            }

            _activeBorder ??= new ActiveTableBorderWindow();
            _activeBorder.SetStyle(ParseColor(_behavior.ActiveBorderColor), _behavior.ActiveBorderThickness);
            _activeBorder.ShowAround(NativeMethods.GetVisibleFrameBounds(foreground));
        }

        private static Color ParseColor(string text)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(text);
            }
            catch (Exception ex) when (ex is FormatException or NullReferenceException or ArgumentException)
            {
                return Color.FromRgb(0x3D, 0xDC, 0x84);
            }
        }

        /// <summary>
        /// Copy registry state into the cells the overlays draw from, and tell listeners
        /// </summary>
        private void SyncCells()
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                _slots[i].Cell.OccupiedWindows = _registry.TablesIn(i).ToList();
            }

            // With cascading, the size of every table in a slot depends on how many share it
            var counts = Enumerable.Range(0, _slots.Count).Select(i => _registry.TablesIn(i).Count).ToArray();
            if (_isEnabled && _behavior.CascadeStacks && counts.Length == _lastSlotCounts.Length)
            {
                for (var i = 0; i < counts.Length; i++)
                {
                    if (counts[i] != _lastSlotCounts[i] && counts[i] > 0)
                        SnapSlot(i);
                }
            }
            _lastSlotCounts = counts;

            _knownTables.UnionWith(_registry.AllTables);
            ScheduleAutoFitIfNeeded();

            foreach (var overlay in _overlays.Values)
                overlay.UpdateOccupiedCells();

            TablesChanged?.Invoke();
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
            return EnumerateTargetWindows().Count;
        }

        /// <summary>
        /// All table windows: those in slots first (in slot order), then matching windows outside the grid
        /// </summary>
        public IReadOnlyList<TableInfo> GetTables()
        {
            var tables = _registry.AllTables
                .Where(NativeMethods.IsWindow)
                .Select(h => Describe(h, NativeMethods.GetWindowTitle(h)))
                .ToList();

            tables.AddRange(EnumerateTargetWindows()
                .Where(w => !_registry.Contains(w.Handle))
                .Select(w => Describe(w.Handle, w.Title)));

            return tables;
        }

        private TableInfo Describe(IntPtr hwnd, string title)
        {
            return new TableInfo(hwnd, title, _registry.SlotOf(hwnd) + 1, SlotPlanner.PinnedSlot(title, Pins, _slots.Count) != null);
        }

        /// <summary>
        /// Windows of recognised poker programs that are not treated as tables, so the user can see
        /// what was left out (and spot a table that a rule misses)
        /// </summary>
        public IReadOnlyList<OtherClientWindow> GetOtherClientWindows()
        {
            var result = new List<OtherClientWindow>();
            if (_currentConfig == null)
                return result;

            foreach (var window in _windowEnumerationService.GetAllWindows())
            {
                if (string.IsNullOrWhiteSpace(window.Title))
                    continue;

                var process = _processNames.GetName(window.ProcessId);
                var rule = _currentConfig.WindowFilters.FirstOrDefault(f => f.AppliesToProcess(process));
                if (rule == null || IsTargetWindow(window.Handle))
                    continue;

                result.Add(new OtherClientWindow(string.IsNullOrEmpty(rule.Name) ? process : rule.Name, window.Title, window.ClassName));
            }

            return result;
        }

        /// <summary>
        /// Auto-arrange all target windows across the grid
        /// </summary>
        public int AutoArrangeWindows()
        {
            if (!_isEnabled || _currentConfig == null || _slots.Count == 0)
                return 0;

            // Tables already in the grid keep their relative order; the rest follow.
            // Minimised tables get a slot too and are positioned when they are restored.
            var previous = _registry.AllTables.ToList();
            var targets = EnumerateTargetWindows()
                .Select(w => w.Handle)
                .OrderBy(h => previous.IndexOf(h) is var index && index >= 0 ? index : int.MaxValue)
                .ToList();
            var previousSlots = targets.ToDictionary(t => t, t => _registry.SlotOf(t));

            _registry.Clear();
            _appliedPins.Clear();

            // Filling displays are sized once, to the tables on them, before anything is placed
            var autoFit = AutoFitDisplays().ToList();
            if (autoFit.Count > 0)
            {
                foreach (var device in autoFit)
                    _autoFitCountOverride[device] = targets.Count(t => MonitorGroupOf(t) == device);

                RebuildSlots();
                _autoFitCountOverride.Clear();
            }

            // Pinned tables first, so the others are arranged around them
            foreach (var table in targets.Where(t => PinnedSlotOf(t) != null))
                PlaceTable(table, snap: false);

            // Keeping positions: a table stays in the slot it is in, or takes the nearest free slot on its display.
            // Tables neither applies to (display full, no grid there) go through the normal placement below.
            if (_behavior.ArrangeKeepsPositions)
            {
                var unavailable = ReservedSlots();
                unavailable.UnionWith(Enumerable.Range(0, _slots.Count).Where(i => _registry.TablesIn(i).Count > 0));

                var tables = targets
                    .Where(t => !_registry.Contains(t))
                    .Select(t => new ArrangeTable(t, CenterOf(t), MonitorGroupOf(t), previousSlots[t]))
                    .ToList();
                var slots = _slots.Select((s, i) => new ArrangeSlot(i, s.MonitorDevice, s.Cell.Bounds)).ToList();

                foreach (var (table, slot) in ArrangePlanner.KeepPositions(tables, slots, unavailable).Assigned)
                {
                    _registry.Place(table, slot);
                    _knownTables.Add(table);
                    _appliedPins[table] = null;
                }
            }

            foreach (var table in targets.Where(t => !_registry.Contains(t)))
                PlaceTable(table, snap: false);

            foreach (var table in _registry.AllTables)
                SnapTableToItsSlot(table);

            SyncCells();
            return targets.Count;
        }

        // ----- Recognising tables -----

        private List<WindowEnumerationService.WindowInfo> EnumerateTargetWindows()
        {
            if (_currentConfig == null || _currentConfig.WindowFilters.Count == 0)
                return new List<WindowEnumerationService.WindowInfo>();

            return _windowEnumerationService.GetAllWindows()
                .Where(w => MatchingRule(w.Handle, w.ClassName, w.Title, specificOnly: false) != null)
                .ToList();
        }

        private bool IsTargetWindow(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && MatchingRule(hwnd, specificOnly: false) != null;
        }

        private WindowFilter? MatchingRule(IntPtr hwnd, bool specificOnly)
        {
            return MatchingRule(hwnd, NativeMethods.GetClassName(hwnd), NativeMethods.GetWindowTitle(hwnd), specificOnly);
        }

        /// <summary>
        /// The first rule that makes this window a table, or null.
        /// The owning program is only looked up when a rule's class and title already match.
        /// </summary>
        private WindowFilter? MatchingRule(IntPtr hwnd, string className, string title, bool specificOnly)
        {
            if (_currentConfig == null)
                return null;

            string? process = null;
            string Process() => process ??= _processNames.GetName(NativeMethods.GetWindowProcessId(hwnd));

            foreach (var filter in _currentConfig.WindowFilters)
            {
                if (specificOnly && !filter.IsSpecific)
                    continue;

                if (filter.Matches(className, title, Process))
                    return filter;
            }

            return null;
        }

        public void Dispose()
        {
            _cleanupTimer.Stop();
            _flashTimer.Stop();
            _autoFitTimer.Stop();
            Disable();
            ClearOverlays();
            _activeBorder?.Close();
            _windowTrackingService.Dispose();
        }
    }
}
