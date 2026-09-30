using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using App.Models;

namespace App.Controls
{
    /// <summary>
    /// Scaled picture of a monitor showing its numbered slots and which of them hold tables
    /// </summary>
    public class MonitorPreview : FrameworkElement
    {
        public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
            nameof(ViewModel), typeof(MonitorConfigViewModel), typeof(MonitorPreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        // Bound to the view model's revision counter purely to trigger a redraw when the grid changes
        public static readonly DependencyProperty RevisionProperty = DependencyProperty.Register(
            nameof(Revision), typeof(int), typeof(MonitorPreview),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Brush ScreenBrush = Frozen(Color.FromRgb(0x14, 0x16, 0x1A));
        private static readonly Pen ScreenPen = FrozenPen(Color.FromRgb(0x4A, 0x50, 0x5A), 2);
        private static readonly Brush TaskbarBrush = Frozen(Color.FromRgb(0x24, 0x28, 0x2E));
        private static readonly Brush EmptyCellBrush = Frozen(Color.FromArgb(0x38, 0x64, 0x95, 0xED));
        private static readonly Pen EmptyCellPen = FrozenPen(Color.FromArgb(0xB0, 0x64, 0x95, 0xED), 1);
        private static readonly Brush OccupiedCellBrush = Frozen(Color.FromArgb(0x70, 0x3D, 0xDC, 0x84));
        private static readonly Pen OccupiedCellPen = FrozenPen(Color.FromRgb(0x3D, 0xDC, 0x84), 1);
        private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0xF2, 0xF4, 0xF7));
        private static readonly Brush OffLabelBrush = Frozen(Color.FromRgb(0x7A, 0x82, 0x8E));
        private static readonly Typeface LabelTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public MonitorConfigViewModel? ViewModel
        {
            get => (MonitorConfigViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public int Revision
        {
            get => (int)GetValue(RevisionProperty);
            set => SetValue(RevisionProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var bounds = ViewModel?.Monitor.Bounds ?? new Rect(0, 0, 16, 9);
            var width = double.IsInfinity(availableSize.Width) ? 260 : availableSize.Width;

            return new Size(width, width * bounds.Height / Math.Max(1, bounds.Width));
        }

        protected override void OnRender(DrawingContext dc)
        {
            var viewModel = ViewModel;
            if (viewModel == null || ActualWidth <= 0)
                return;

            var monitor = viewModel.Monitor;
            var scale = ActualWidth / Math.Max(1, monitor.Bounds.Width);

            Rect Scaled(Rect screen) => new(
                (screen.Left - monitor.Bounds.Left) * scale,
                (screen.Top - monitor.Bounds.Top) * scale,
                screen.Width * scale,
                screen.Height * scale);

            var screenRect = new Rect(0, 0, ActualWidth, ActualHeight);
            dc.DrawRoundedRectangle(TaskbarBrush, null, screenRect, 5, 5);
            dc.DrawRectangle(ScreenBrush, null, Scaled(monitor.WorkArea));

            var cells = viewModel.PreviewCells;
            if (viewModel.IsDisabledMode || cells.Count == 0)
            {
                DrawCentered(dc, "No grid", screenRect, 13, OffLabelBrush);
            }
            else
            {
                foreach (var cell in cells)
                {
                    var rect = Scaled(cell.Bounds);
                    rect.Inflate(-1, -1);
                    if (rect.Width <= 0 || rect.Height <= 0)
                        continue;

                    dc.DrawRoundedRectangle(
                        cell.IsOccupied ? OccupiedCellBrush : EmptyCellBrush,
                        cell.IsOccupied ? OccupiedCellPen : EmptyCellPen,
                        rect, 2, 2);

                    var fontSize = Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.45, 8, 20);
                    DrawCentered(dc, cell.SlotNumber.ToString(), rect, fontSize, LabelBrush);
                }
            }

            dc.DrawRoundedRectangle(null, ScreenPen, screenRect, 5, 5);
        }

        private void DrawCentered(DrawingContext dc, string text, Rect area, double fontSize, Brush brush)
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                fontSize,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            dc.DrawText(formatted, new Point(
                area.Left + (area.Width - formatted.Width) / 2,
                area.Top + (area.Height - formatted.Height) / 2));
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Pen FrozenPen(Color color, double thickness)
        {
            var pen = new Pen(new SolidColorBrush(color), thickness);
            pen.Freeze();
            return pen;
        }
    }
}
