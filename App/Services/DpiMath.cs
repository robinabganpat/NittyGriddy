using System.Windows;

namespace App.Services
{
    /// <summary>
    /// Conversions between physical pixels (Win32) and device-independent units (WPF)
    /// </summary>
    public static class DpiMath
    {
        /// <summary>
        /// Convert a screen rectangle in physical pixels to WPF units relative to a window whose top-left is at originPx
        /// </summary>
        public static Rect PixelsToDips(Rect px, Point originPx, double scale)
        {
            if (scale <= 0)
                scale = 1.0;

            return new Rect(
                (px.Left - originPx.X) / scale,
                (px.Top - originPx.Y) / scale,
                px.Width / scale,
                px.Height / scale);
        }
    }
}
