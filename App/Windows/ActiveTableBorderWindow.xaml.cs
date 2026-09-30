using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using App.Native;

namespace App.Windows
{
    /// <summary>
    /// Click-through frame drawn around the table that currently has focus.
    /// It is a window of this application laid over the table; the table itself is not touched.
    /// </summary>
    public partial class ActiveTableBorderWindow : Window
    {
        private readonly IntPtr _hwnd;
        private Rect _bounds = Rect.Empty;

        public ActiveTableBorderWindow()
        {
            InitializeComponent();

            _hwnd = new WindowInteropHelper(this).EnsureHandle();

            // Click-through, never activated, not in Alt+Tab
            NativeMethods.AddExtendedStyle(_hwnd,
                NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
                NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);

            // On a change of display scaling WPF resizes the window; put it back on the table's frame
            DpiChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsVisible && !_bounds.IsEmpty)
                    ShowAround(_bounds);
            }));
        }

        public void SetStyle(Color color, double thickness)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();

            Frame.BorderBrush = brush;
            Frame.BorderThickness = new Thickness(Math.Clamp(thickness, 1, 20));
        }

        /// <summary>
        /// Show the frame over the given screen rectangle (physical pixels)
        /// </summary>
        public void ShowAround(Rect bounds)
        {
            if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            {
                HideBorder();
                return;
            }

            _bounds = bounds;

            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HWND_TOPMOST,
                (int)bounds.Left,
                (int)bounds.Top,
                (int)bounds.Width,
                (int)bounds.Height,
                NativeMethods.SWP_NOACTIVATE);

            if (!IsVisible)
                Show();
        }

        public void HideBorder()
        {
            if (IsVisible)
                Hide();
        }
    }
}
