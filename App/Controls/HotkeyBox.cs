using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using App.Models;

namespace App.Controls
{
    /// <summary>
    /// Text box that records a key combination: focus it and press the keys.
    /// Backspace or Delete clears the binding; Escape cancels.
    /// </summary>
    public class HotkeyBox : TextBox
    {
        private const string Prompt = "Press keys…";
        private const string Unset = "Not set";

        public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
            nameof(Gesture), typeof(string), typeof(HotkeyBox),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, e) => ((HotkeyBox)d).ShowGesture()));

        /// <summary>
        /// Gesture text such as "Alt+1"; empty when the action is unbound
        /// </summary>
        public string Gesture
        {
            get => (string)GetValue(GestureProperty);
            set => SetValue(GestureProperty, value);
        }

        public HotkeyBox()
        {
            IsReadOnly = true;
            IsReadOnlyCaretVisible = false;
            IsUndoEnabled = false;
            Cursor = Cursors.Hand;
            TextAlignment = TextAlignment.Center;
            ContextMenu = null;

            // Implicit styles apply to the exact type only; take the themed TextBox look
            SetResourceReference(StyleProperty, typeof(TextBox));

            ShowGesture();
        }

        private void ShowGesture()
        {
            if (IsKeyboardFocused)
                return;

            Text = string.IsNullOrEmpty(Gesture) ? Unset : Gesture;
        }

        protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnGotKeyboardFocus(e);
            Text = Prompt;
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            ShowGesture();

            // Focus left the window altogether (another program was activated): do not resume capturing on return
            if (e.NewFocus == null)
            {
                var scope = FocusManager.GetFocusScope(this);
                if (scope != null && ReferenceEquals(FocusManager.GetFocusedElement(scope), this))
                    FocusManager.SetFocusedElement(scope, null);
            }
        }

        /// <summary>
        /// Give up keyboard focus and logical focus. Without the latter, WPF hands keyboard focus back to this box
        /// whenever its window is activated again, and the next key pressed would silently become the binding.
        /// </summary>
        private void EndCapture()
        {
            var scope = FocusManager.GetFocusScope(this);
            if (scope != null && ReferenceEquals(FocusManager.GetFocusedElement(scope), this))
                FocusManager.SetFocusedElement(scope, null);

            Keyboard.ClearFocus();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Alt combinations arrive as Key.System with the real key in SystemKey
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;

            // Tab keeps working for keyboard navigation
            if (key == Key.Tab && (modifiers & ~ModifierKeys.Shift) == ModifierKeys.None)
                return;

            e.Handled = true;

            if (modifiers == ModifierKeys.None && key == Key.Escape)
            {
                EndCapture();
                return;
            }

            if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
            {
                Gesture = string.Empty;
                EndCapture();
                return;
            }

            var gesture = HotkeyGesture.FromKey(key, modifiers);
            if (gesture == null)
                return; // Only modifiers held so far

            Gesture = gesture.Value.ToString();
            EndCapture();
        }
    }
}
