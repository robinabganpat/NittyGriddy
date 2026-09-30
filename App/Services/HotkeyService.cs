using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using App.Models;
using App.Native;

namespace App.Services
{
    /// <summary>
    /// Registers system-wide hotkeys (RegisterHotKey) and reports when one is pressed
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        public const string MessageWindowName = "NittyGriddyHotkeys";

        private readonly HwndSource _source;
        private readonly Dictionary<int, string> _registered = new();
        private List<KeyValuePair<string, HotkeyGesture>> _bindings = new();
        private int _nextId = 1;
        private bool _suspended;

        /// <summary>
        /// Raised with the action id of the hotkey that was pressed
        /// </summary>
        public event Action<string>? HotkeyPressed;

        public HotkeyService()
        {
            // Message-only window: receives WM_HOTKEY without ever being visible
            var parameters = new HwndSourceParameters(MessageWindowName)
            {
                ParentWindow = NativeMethods.HWND_MESSAGE,
                Width = 0,
                Height = 0,
                WindowStyle = 0
            };

            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);
        }

        /// <summary>
        /// Replace all registered hotkeys. Returns the actions that could not be registered, with the reason.
        /// </summary>
        public IReadOnlyDictionary<string, string> Apply(IEnumerable<KeyValuePair<string, HotkeyGesture>> bindings)
        {
            _bindings = new List<KeyValuePair<string, HotkeyGesture>>(bindings);
            if (!_suspended)
                RegisterAll();
            return Failures;
        }

        /// <summary>
        /// Actions that could not be registered the last time hotkeys were applied, with the reason
        /// </summary>
        public IReadOnlyDictionary<string, string> Failures { get; private set; } = new Dictionary<string, string>();

        /// <summary>
        /// Temporarily release every hotkey, e.g. while the user is typing a new binding
        /// </summary>
        public void Suspend()
        {
            if (_suspended) return;

            _suspended = true;
            UnregisterAll();
        }

        public void Resume()
        {
            if (!_suspended) return;

            _suspended = false;
            RegisterAll();
        }

        private void RegisterAll()
        {
            UnregisterAll();

            var failures = new Dictionary<string, string>();
            var seen = new Dictionary<HotkeyGesture, string>();

            foreach (var (actionId, gesture) in _bindings)
            {
                if (seen.TryGetValue(gesture, out var other))
                {
                    var label = HotkeyActions.Find(other)?.Label ?? other;
                    failures[actionId] = $"Already used for \"{label}\"";
                    continue;
                }

                var id = _nextId++;
                if (NativeMethods.RegisterHotKey(_source.Handle, id, (uint)gesture.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)gesture.VirtualKey))
                {
                    _registered[id] = actionId;
                    seen[gesture] = actionId;
                }
                else
                {
                    const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
                    var error = Marshal.GetLastWin32Error();
                    failures[actionId] = error == ERROR_HOTKEY_ALREADY_REGISTERED
                        ? "In use by another program"
                        : new Win32Exception(error).Message;
                }
            }

            Failures = failures;
        }

        private void UnregisterAll()
        {
            foreach (var id in _registered.Keys)
                NativeMethods.UnregisterHotKey(_source.Handle, id);

            _registered.Clear();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && _registered.TryGetValue(wParam.ToInt32(), out var actionId))
            {
                handled = true;
                HotkeyPressed?.Invoke(actionId);
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterAll();
            _source.RemoveHook(WndProc);
            _source.Dispose();
        }
    }
}
