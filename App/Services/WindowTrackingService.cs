using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace App.Services
{
    /// <summary>
    /// Service for tracking window movements and detecting drag operations
    /// </summary>
    public class WindowTrackingService
    {
        // Windows hook delegates and constants
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        // Event constants
        private const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
        private const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        private const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint WINEVENT_OUTOFCONTEXT = 0;

        private IntPtr _moveStartHook;
        private IntPtr _moveEndHook;
        private IntPtr _locationHook;
        private IntPtr _showHook;
        private WinEventDelegate? _moveStartDelegate;
        private WinEventDelegate? _moveEndDelegate;
        private WinEventDelegate? _locationDelegate;
        private WinEventDelegate? _showDelegate;

        private readonly List<App.Models.WindowFilter> _windowFilters = new();

        private IntPtr _currentDraggedWindow = IntPtr.Zero;
        private readonly Dispatcher _dispatcher;

        // Events
        public event EventHandler<WindowDragEventArgs>? WindowDragStarted;
        public event EventHandler<WindowDragEventArgs>? WindowDragMoved;
        public event EventHandler<WindowDragEventArgs>? WindowDragEnded;
        public event EventHandler<WindowDragEventArgs>? WindowShown;

        public WindowTrackingService(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        /// <summary>
        /// Clear all target window filters
        /// </summary>
        public void ClearTargetFilters()
        {
            _windowFilters.Clear();
            System.Diagnostics.Debug.WriteLine("WindowTrackingService: Filters cleared");
        }

        /// <summary>
        /// Add a window filter to track
        /// </summary>
        public void AddWindowFilter(App.Models.WindowFilter filter)
        {
            _windowFilters.Add(filter);
            System.Diagnostics.Debug.WriteLine($"WindowTrackingService: Added filter '{filter}'");
        }

        /// <summary>
        /// Start tracking window movements
        /// </summary>
        public void StartTracking()
        {
            if (_moveStartHook != IntPtr.Zero)
                return; // Already tracking

            // Create delegates (must be stored to prevent garbage collection)
            _moveStartDelegate = new WinEventDelegate(OnWindowMoveStart);
            _moveEndDelegate = new WinEventDelegate(OnWindowMoveEnd);
            _locationDelegate = new WinEventDelegate(OnWindowLocationChange);
            _showDelegate = new WinEventDelegate(OnWindowShow);

            // Set hooks
            _moveStartHook = SetWinEventHook(EVENT_SYSTEM_MOVESIZESTART, EVENT_SYSTEM_MOVESIZESTART, IntPtr.Zero, _moveStartDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            _moveEndHook = SetWinEventHook(EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZEEND, IntPtr.Zero, _moveEndDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            _locationHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _locationDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
            _showHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _showDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        /// <summary>
        /// Stop tracking window movements
        /// </summary>
        public void StopTracking()
        {
            if (_moveStartHook != IntPtr.Zero)
            {
                UnhookWinEvent(_moveStartHook);
                _moveStartHook = IntPtr.Zero;
            }

            if (_moveEndHook != IntPtr.Zero)
            {
                UnhookWinEvent(_moveEndHook);
                _moveEndHook = IntPtr.Zero;
            }

            if (_locationHook != IntPtr.Zero)
            {
                UnhookWinEvent(_locationHook);
                _locationHook = IntPtr.Zero;
            }

            if (_showHook != IntPtr.Zero)
            {
                UnhookWinEvent(_showHook);
                _showHook = IntPtr.Zero;
            }

            _moveStartDelegate = null;
            _moveEndDelegate = null;
            _locationDelegate = null;
            _showDelegate = null;
        }

        private void OnWindowMoveStart(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);

            System.Diagnostics.Debug.WriteLine($"Window move start detected: '{title}' (Class: {className})");

            if (!IsTargetWindow(hwnd))
            {
                System.Diagnostics.Debug.WriteLine($"  -> Window not in target filters, ignoring");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"  -> Window IS a target! Starting tracking...");
            _currentDraggedWindow = hwnd;

            _dispatcher.BeginInvoke(() =>
            {
                var rect = GetWindowBounds(hwnd);
                var mousePos = GetMousePosition();
                WindowDragStarted?.Invoke(this, new WindowDragEventArgs(hwnd, rect, mousePos));
            });
        }

        private void OnWindowLocationChange(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd != _currentDraggedWindow || _currentDraggedWindow == IntPtr.Zero)
                return;

            _dispatcher.BeginInvoke(() =>
            {
                var rect = GetWindowBounds(hwnd);
                var mousePos = GetMousePosition();
                WindowDragMoved?.Invoke(this, new WindowDragEventArgs(hwnd, rect, mousePos));
            });
        }

        private void OnWindowMoveEnd(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (hwnd != _currentDraggedWindow || _currentDraggedWindow == IntPtr.Zero)
                return;

            _dispatcher.BeginInvoke(() =>
            {
                var rect = GetWindowBounds(hwnd);
                var mousePos = GetMousePosition();
                WindowDragEnded?.Invoke(this, new WindowDragEventArgs(hwnd, rect, mousePos));
                _currentDraggedWindow = IntPtr.Zero;
            });
        }

        private void OnWindowShow(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);

            System.Diagnostics.Debug.WriteLine($"Window shown: '{title}' (Class: {className})");

            if (!IsTargetWindow(hwnd))
            {
                System.Diagnostics.Debug.WriteLine($"  -> Window not in target filters, ignoring");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"  -> Window IS a target! Auto-snapping to first available cell...");

            _dispatcher.BeginInvoke(() =>
            {
                var rect = GetWindowBounds(hwnd);
                var mousePos = GetMousePosition();
                WindowShown?.Invoke(this, new WindowDragEventArgs(hwnd, rect, mousePos));
            });
        }

        private bool IsTargetWindow(IntPtr hwnd)
        {
            if (_windowFilters.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("  -> No filters configured, not tracking any windows");
                return false;
            }

            var className = new StringBuilder(256);
            GetClassName(hwnd, className, className.Capacity);
            var classNameStr = className.ToString();

            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            var titleStr = title.ToString();

            System.Diagnostics.Debug.WriteLine($"  -> Checking window '{titleStr}' (Class: {classNameStr}) against {_windowFilters.Count} filter(s)");

            // Check if window matches any filter
            foreach (var filter in _windowFilters)
            {
                if (filter.Matches(classNameStr, titleStr))
                {
                    System.Diagnostics.Debug.WriteLine($"  -> MATCH! Filter: {filter}");
                    return true;
                }
            }

            return false;
        }

        private Rect GetWindowBounds(IntPtr hwnd)
        {
            RECT rect;
            if (GetWindowRect(hwnd, out rect))
            {
                return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
            return Rect.Empty;
        }

        private Point GetMousePosition()
        {
            POINT point;
            if (GetCursorPos(out point))
            {
                return new Point(point.X, point.Y);
            }
            return new Point(0, 0);
        }

        public void Dispose()
        {
            StopTracking();
        }
    }

    /// <summary>
    /// Event args for window drag events
    /// </summary>
    public class WindowDragEventArgs : EventArgs
    {
        public IntPtr WindowHandle { get; }
        public Rect WindowBounds { get; }
        public Point MousePosition { get; }

        public WindowDragEventArgs(IntPtr windowHandle, Rect windowBounds, Point mousePosition)
        {
            WindowHandle = windowHandle;
            WindowBounds = windowBounds;
            MousePosition = mousePosition;
        }
    }
}
