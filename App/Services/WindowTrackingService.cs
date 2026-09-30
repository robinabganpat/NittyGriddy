using System;
using System.Collections.Generic;
using System.Windows;
using App.Native;

namespace App.Services
{
    /// <summary>
    /// Observes top-level window events system-wide: drags, new windows, closed windows, and foreground changes.
    /// Callbacks arrive on the thread that called StartTracking (the UI thread) through its message loop.
    /// </summary>
    public class WindowTrackingService
    {
        private readonly Func<IntPtr, bool> _isTargetWindow;
        private readonly List<IntPtr> _hooks = new();

        // Kept for the lifetime of this object: an event already queued when the hooks are removed may still be
        // delivered, and must not reach a collected delegate
        private readonly NativeMethods.WinEventDelegate _callback;

        private IntPtr _currentDraggedWindow = IntPtr.Zero;

        // Events
        public event EventHandler<WindowDragEventArgs>? WindowDragStarted;
        public event EventHandler<WindowDragEventArgs>? WindowDragMoved;
        public event EventHandler<WindowDragEventArgs>? WindowDragEnded;
        public event EventHandler<WindowDragEventArgs>? WindowShown;

        /// <summary>
        /// A visible top-level window's title changed (any window; subscribers filter)
        /// </summary>
        public event Action<IntPtr>? WindowTitleChanged;

        /// <summary>
        /// A top-level window was destroyed (any window; subscribers filter)
        /// </summary>
        public event Action<IntPtr>? WindowDestroyed;

        /// <summary>
        /// A window was restored from minimised (any window; subscribers filter)
        /// </summary>
        public event Action<IntPtr>? WindowRestored;

        /// <summary>
        /// The foreground window changed (any window; subscribers filter)
        /// </summary>
        public event Action<IntPtr>? ForegroundChanged;

        /// <summary>
        /// The window set as <see cref="FollowedWindow"/> moved, resized, minimised or restored
        /// </summary>
        public event Action<IntPtr>? FollowedWindowChanged;

        /// <summary>
        /// A single window whose position changes are reported through FollowedWindowChanged
        /// </summary>
        public IntPtr FollowedWindow { get; set; }

        public IntPtr DraggedWindow => _currentDraggedWindow;

        public WindowTrackingService(Func<IntPtr, bool> isTargetWindow)
        {
            _isTargetWindow = isTargetWindow;
            _callback = OnWinEvent;
        }

        /// <summary>
        /// Start tracking window events
        /// </summary>
        public void StartTracking()
        {
            if (_hooks.Count > 0)
                return; // Already tracking

            Hook(NativeMethods.EVENT_SYSTEM_FOREGROUND);
            Hook(NativeMethods.EVENT_SYSTEM_MOVESIZESTART);
            Hook(NativeMethods.EVENT_SYSTEM_MOVESIZEEND);
            Hook(NativeMethods.EVENT_SYSTEM_MINIMIZESTART);
            Hook(NativeMethods.EVENT_SYSTEM_MINIMIZEEND);
            Hook(NativeMethods.EVENT_OBJECT_DESTROY);
            Hook(NativeMethods.EVENT_OBJECT_SHOW);
            Hook(NativeMethods.EVENT_OBJECT_LOCATIONCHANGE);
            Hook(NativeMethods.EVENT_OBJECT_NAMECHANGE);
        }

        private void Hook(uint eventId)
        {
            var hook = NativeMethods.SetWinEventHook(eventId, eventId, IntPtr.Zero, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            if (hook != IntPtr.Zero)
                _hooks.Add(hook);
        }

        /// <summary>
        /// Stop tracking window events
        /// </summary>
        public void StopTracking()
        {
            foreach (var hook in _hooks)
                NativeMethods.UnhookWinEvent(hook);

            _hooks.Clear();
            _currentDraggedWindow = IntPtr.Zero;
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Only events about a window itself, not its child objects, carets or the cursor
            if (hwnd == IntPtr.Zero || idObject != NativeMethods.OBJID_WINDOW || idChild != 0)
                return;

            try
            {
                Dispatch(eventType, hwnd);
            }
            catch (Exception ex)
            {
                // An exception escaping into the native caller would terminate the process mid-session
                System.Diagnostics.Debug.WriteLine($"WindowTrackingService: handler failed for event 0x{eventType:X}: {ex}");
            }
        }

        private void Dispatch(uint eventType, IntPtr hwnd)
        {
            switch (eventType)
            {
                case NativeMethods.EVENT_SYSTEM_MOVESIZESTART:
                    if (_isTargetWindow(hwnd))
                    {
                        _currentDraggedWindow = hwnd;
                        WindowDragStarted?.Invoke(this, CreateArgs(hwnd));
                    }
                    break;

                case NativeMethods.EVENT_OBJECT_LOCATIONCHANGE:
                    if (hwnd == _currentDraggedWindow)
                        WindowDragMoved?.Invoke(this, CreateArgs(hwnd));
                    if (hwnd == FollowedWindow)
                        FollowedWindowChanged?.Invoke(hwnd);
                    break;

                case NativeMethods.EVENT_SYSTEM_MOVESIZEEND:
                    if (hwnd == _currentDraggedWindow)
                    {
                        _currentDraggedWindow = IntPtr.Zero;
                        WindowDragEnded?.Invoke(this, CreateArgs(hwnd));
                    }
                    break;

                case NativeMethods.EVENT_OBJECT_SHOW:
                    if (NativeMethods.IsTopLevelWindow(hwnd) && _isTargetWindow(hwnd))
                        WindowShown?.Invoke(this, CreateArgs(hwnd));
                    break;

                case NativeMethods.EVENT_OBJECT_NAMECHANGE:
                    WindowTitleChanged?.Invoke(hwnd);
                    break;

                case NativeMethods.EVENT_OBJECT_DESTROY:
                    WindowDestroyed?.Invoke(hwnd);
                    break;

                case NativeMethods.EVENT_SYSTEM_FOREGROUND:
                    ForegroundChanged?.Invoke(hwnd);
                    break;

                case NativeMethods.EVENT_SYSTEM_MINIMIZESTART:
                    if (hwnd == FollowedWindow)
                        FollowedWindowChanged?.Invoke(hwnd);
                    break;

                case NativeMethods.EVENT_SYSTEM_MINIMIZEEND:
                    WindowRestored?.Invoke(hwnd);
                    if (hwnd == FollowedWindow)
                        FollowedWindowChanged?.Invoke(hwnd);
                    break;
            }
        }

        private static WindowDragEventArgs CreateArgs(IntPtr hwnd)
        {
            return new WindowDragEventArgs(hwnd, NativeMethods.GetWindowBounds(hwnd), NativeMethods.GetCursorPosition());
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
