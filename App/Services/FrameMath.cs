using System;
using System.Windows;

namespace App.Services
{
    /// <summary>
    /// Geometry for fitting a window into a slot
    /// </summary>
    public static class FrameMath
    {
        // Invisible borders are a few pixels; anything larger means the reported frame cannot be trusted
        private const double MaxInvisibleBorder = 32;

        /// <summary>
        /// The rectangle to give a window so that its visible frame covers <paramref name="visibleTarget"/>.
        /// Windows surrounds many windows with an invisible resize border that is part of the window rectangle
        /// but not of what is seen on screen.
        /// </summary>
        /// <param name="windowRect">The window's current rectangle</param>
        /// <param name="visibleFrame">The window's current visible frame</param>
        public static Rect WindowRectForVisibleTarget(Rect visibleTarget, Rect windowRect, Rect visibleFrame)
        {
            if (windowRect.IsEmpty || visibleFrame.IsEmpty)
                return visibleTarget;

            var left = visibleFrame.Left - windowRect.Left;
            var top = visibleFrame.Top - windowRect.Top;
            var right = windowRect.Right - visibleFrame.Right;
            var bottom = windowRect.Bottom - visibleFrame.Bottom;

            foreach (var border in new[] { left, top, right, bottom })
            {
                if (border < 0 || border > MaxInvisibleBorder)
                    return visibleTarget;
            }

            return new Rect(
                visibleTarget.Left - left,
                visibleTarget.Top - top,
                visibleTarget.Width + left + right,
                visibleTarget.Height + top + bottom);
        }

        /// <summary>
        /// The area for table <paramref name="index"/> (0 = bottom) of a stack of <paramref name="count"/> cascaded
        /// inside <paramref name="area"/>: each table is offset diagonally by the step, and all have the same size so
        /// the whole cascade fits. For deep stacks the step shrinks so each table keeps three quarters of the area.
        /// </summary>
        public static Rect CascadeArea(Rect area, int index, int count, double step)
        {
            if (count <= 1)
                return area;

            index = Math.Clamp(index, 0, count - 1);

            var maxStep = Math.Min(area.Width, area.Height) * 0.25 / (count - 1);
            step = Math.Max(0, Math.Min(step, maxStep));

            var shrink = step * (count - 1);
            return new Rect(
                area.Left + step * index,
                area.Top + step * index,
                area.Width - shrink,
                area.Height - shrink);
        }

        /// <summary>
        /// The largest rectangle with the content's aspect ratio that fits in the area, centred
        /// </summary>
        public static Rect FitPreservingAspect(Size content, Rect area)
        {
            if (content.IsEmpty || content.Width <= 0 || content.Height <= 0 || area.Width <= 0 || area.Height <= 0)
                return area;

            var scale = Math.Min(area.Width / content.Width, area.Height / content.Height);
            var width = content.Width * scale;
            var height = content.Height * scale;

            return new Rect(
                area.Left + (area.Width - width) / 2,
                area.Top + (area.Height - height) / 2,
                width,
                height);
        }
    }
}
