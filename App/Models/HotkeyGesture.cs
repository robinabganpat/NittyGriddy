using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace App.Models
{
    /// <summary>
    /// Modifier flags; values match the Win32 MOD_* constants used by RegisterHotKey
    /// </summary>
    [Flags]
    public enum HotkeyModifiers
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Win = 8
    }

    /// <summary>
    /// A global hotkey: modifiers plus a virtual key code
    /// </summary>
    public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
    {
        private static readonly Dictionary<string, Key> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PgDn"] = Key.PageDown,
            ["PgUp"] = Key.PageUp,
            ["Enter"] = Key.Enter,
            ["Backspace"] = Key.Back,
            ["CapsLock"] = Key.CapsLock,
            ["PrintScreen"] = Key.PrintScreen,
            ["Esc"] = Key.Escape,
            ["Del"] = Key.Delete,
            ["Ins"] = Key.Insert,
            ["`"] = Key.OemTilde,
        };

        private static readonly Dictionary<string, HotkeyModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ctrl"] = HotkeyModifiers.Control,
            ["Control"] = HotkeyModifiers.Control,
            ["Alt"] = HotkeyModifiers.Alt,
            ["Shift"] = HotkeyModifiers.Shift,
            ["Win"] = HotkeyModifiers.Win,
            ["Windows"] = HotkeyModifiers.Win,
        };

        /// <summary>
        /// Parse text such as "Ctrl+Alt+G" or "F5"
        /// </summary>
        public static bool TryParse(string? text, out HotkeyGesture gesture)
        {
            gesture = default;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            var parts = text.Split('+', StringSplitOptions.TrimEntries);
            var modifiers = HotkeyModifiers.None;

            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (!ModifierNames.TryGetValue(parts[i], out var modifier))
                    return false;
                modifiers |= modifier;
            }

            if (!TryParseKey(parts[^1], out var key) || IsModifierKey(key))
                return false;

            var virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey == 0)
                return false;

            gesture = new HotkeyGesture(modifiers, virtualKey);
            return true;
        }

        /// <summary>
        /// Build a gesture from a WPF key event. Returns null while only modifier keys are held.
        /// </summary>
        public static HotkeyGesture? FromKey(Key key, ModifierKeys modifiers)
        {
            if (key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed || IsModifierKey(key))
                return null;

            var virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey == 0)
                return null;

            var result = HotkeyModifiers.None;
            if (modifiers.HasFlag(ModifierKeys.Control)) result |= HotkeyModifiers.Control;
            if (modifiers.HasFlag(ModifierKeys.Alt)) result |= HotkeyModifiers.Alt;
            if (modifiers.HasFlag(ModifierKeys.Shift)) result |= HotkeyModifiers.Shift;
            if (modifiers.HasFlag(ModifierKeys.Windows)) result |= HotkeyModifiers.Win;

            return new HotkeyGesture(result, virtualKey);
        }

        /// <summary>
        /// True when the gesture has no Ctrl, Alt or Win modifier, so it takes a plain key away from other programs
        /// </summary>
        public bool IsBareKey => (Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0;

        public override string ToString()
        {
            if (VirtualKey == 0)
                return string.Empty;

            var parts = new List<string>(5);
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
            parts.Add(KeyName(KeyInterop.KeyFromVirtualKey(VirtualKey)));

            return string.Join("+", parts);
        }

        private static bool TryParseKey(string text, out Key key)
        {
            key = Key.None;

            if (text.Length == 0)
                return false;

            if (text.Length == 1 && text[0] >= '0' && text[0] <= '9')
            {
                key = Key.D0 + (text[0] - '0');
                return true;
            }

            if (KeyAliases.TryGetValue(text, out key))
                return true;

            // Enum.TryParse also accepts raw numbers; those are not key names
            if (char.IsDigit(text[0]) || text[0] == '-')
                return false;

            return Enum.TryParse(text, ignoreCase: true, out key) && Enum.IsDefined(key) && key != Key.None;
        }

        private static string KeyName(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return ((int)(key - Key.D0)).ToString();

            return key switch
            {
                Key.PageDown => "PgDn",
                Key.PageUp => "PgUp",
                Key.Enter => "Enter",
                Key.Back => "Backspace",
                Key.CapsLock => "CapsLock",
                Key.PrintScreen => "PrintScreen",
                Key.OemTilde => "`",
                _ => key.ToString()
            };
        }

        private static bool IsModifierKey(Key key)
        {
            return key is Key.LeftCtrl or Key.RightCtrl
                or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift
                or Key.LWin or Key.RWin;
        }
    }
}
