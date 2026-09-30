using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using App.Models;
using App.Native;
using App.Services;

namespace App.Windows
{
    /// <summary>
    /// Transparent overlay window that displays grid cells on a monitor
    /// </summary>
    public partial class GridOverlayWindow : Window
    {
        private readonly MonitorInfo _monitor;
        private GridLayout? _gridLayout;
        private readonly Dictionary<int, Rectangle> _cellVisuals = new();
        private readonly Dictionary<int, TextBlock> _countLabels = new();
        private GridCell? _highlightedCell;

        // Visual styling
        private readonly SolidColorBrush _normalCellBrush = new(Color.FromArgb(60, 100, 149, 237)); // Transparent blue
        private readonly SolidColorBrush _highlightCellBrush = new(Color.FromArgb(120, 50, 205, 50)); // Transparent green
        private readonly SolidColorBrush _occupiedCellBrush = new(Color.FromArgb(80, 220, 20, 60)); // Transparent red
        private readonly SolidColorBrush _cellBorderBrush = new(Color.FromArgb(200, 255, 255, 255));

        public GridOverlayWindow(MonitorInfo monitor)
        {
            InitializeComponent();

            _monitor = monitor;

            // Invisible until asked otherwise: a window that becomes visible at full opacity, even for one frame,
            // is seen as a flash across the whole display
            Opacity = 0;

            // Provisional placement so the window is created on the right monitor; exact bounds are set in pixels below
            Left = monitor.Bounds.Left;
            Top = monitor.Bounds.Top;
            Width = monitor.Bounds.Width;
            Height = monitor.Bounds.Height;

            SourceInitialized += OnSourceInitialized;
            DpiChanged += (s, e) => Dispatcher.BeginInvoke(new Action(ApplyMonitorBounds));
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            // Click-through, never activated, not in Alt+Tab
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.AddExtendedStyle(hwnd,
                NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT |
                NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);

            ApplyMonitorBounds();
        }

        /// <summary>
        /// Cover the monitor exactly, in physical pixels, regardless of display scaling
        /// </summary>
        private void ApplyMonitorBounds()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            NativeMethods.SetWindowPos(
                hwnd,
                IntPtr.Zero,
                (int)_monitor.Bounds.Left,
                (int)_monitor.Bounds.Top,
                (int)_monitor.Bounds.Width,
                (int)_monitor.Bounds.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

            RedrawGrid();
        }

        /// <summary>
        /// The display area this overlay was created for
        /// </summary>
        public Rect MonitorBounds => _monitor.Bounds;

        /// <summary>
        /// Set the grid layout to display
        /// </summary>
        public void SetGridLayout(GridLayout layout)
        {
            _gridLayout = layout;
            _highlightedCell = null;
            RedrawGrid();
        }

        /// <summary>
        /// Redraw all grid cells
        /// </summary>
        private void RedrawGrid()
        {
            GridCanvas.Children.Clear();
            _cellVisuals.Clear();
            _countLabels.Clear();

            if (_gridLayout == null) return;

            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;

            foreach (var cell in _gridLayout.Cells)
            {
                // Cell bounds are screen pixels; the canvas works in WPF units relative to the monitor's corner
                var bounds = DpiMath.PixelsToDips(cell.Bounds, _monitor.Bounds.TopLeft, scale);

                var rect = new Rectangle
                {
                    Width = bounds.Width,
                    Height = bounds.Height,
                    Fill = cell.IsOccupied ? _occupiedCellBrush : _normalCellBrush,
                    Stroke = _cellBorderBrush,
                    StrokeThickness = 2,
                    RadiusX = 5,
                    RadiusY = 5
                };

                Canvas.SetLeft(rect, bounds.Left);
                Canvas.SetTop(rect, bounds.Top);

                GridCanvas.Children.Add(rect);
                _cellVisuals[cell.Id] = rect;

                // Slot number: the same number the slot hotkeys use
                var label = CreateLabel(cell.SlotNumber.ToString(), 24);
                Canvas.SetLeft(label, bounds.Left + 10);
                Canvas.SetTop(label, bounds.Top + 10);
                GridCanvas.Children.Add(label);

                // Number of tables stacked in the slot, shown only when more than one
                var countLabel = CreateLabel(string.Empty, 14);
                Canvas.SetLeft(countLabel, bounds.Left + 12);
                Canvas.SetTop(countLabel, bounds.Top + 44);
                GridCanvas.Children.Add(countLabel);
                _countLabels[cell.Id] = countLabel;
            }

            UpdateOccupiedCells();
        }

        private static TextBlock CreateLabel(string text, double fontSize)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 3,
                    ShadowDepth = 2,
                    Opacity = 0.8
                }
            };
        }

        /// <summary>
        /// Highlight a specific cell (e.g., when window hovers over it)
        /// </summary>
        public void HighlightCell(GridCell? cell)
        {
            // Called for every mouse movement during a drag; nothing to do while the cursor stays in the same slot
            if (ReferenceEquals(cell, _highlightedCell))
                return;

            // Unhighlight previous cell
            if (_highlightedCell != null && _cellVisuals.ContainsKey(_highlightedCell.Id))
            {
                var prevRect = _cellVisuals[_highlightedCell.Id];
                prevRect.Fill = _highlightedCell.IsOccupied ? _occupiedCellBrush : _normalCellBrush;
                prevRect.StrokeThickness = 2;
            }

            _highlightedCell = cell;

            // Highlight new cell
            if (_highlightedCell != null && _cellVisuals.ContainsKey(_highlightedCell.Id))
            {
                var rect = _cellVisuals[_highlightedCell.Id];
                rect.Fill = _highlightCellBrush;
                rect.StrokeThickness = 4;
            }
        }

        /// <summary>
        /// Update occupied state of cells
        /// </summary>
        public void UpdateOccupiedCells()
        {
            if (_gridLayout == null) return;

            foreach (var cell in _gridLayout.Cells)
            {
                if (_cellVisuals.TryGetValue(cell.Id, out var rect) && cell != _highlightedCell)
                {
                    rect.Fill = cell.IsOccupied ? _occupiedCellBrush : _normalCellBrush;
                }

                if (_countLabels.TryGetValue(cell.Id, out var countLabel))
                {
                    countLabel.Text = cell.WindowCount > 1 ? $"{cell.WindowCount} tables" : string.Empty;
                }
            }
        }

        /// <summary>
        /// Show or hide the overlay
        /// </summary>
        public void SetVisible(bool visible)
        {
            // Opacity is left alone here; callers set it before showing
            if (visible)
            {
                if (!IsVisible)
                    Show();
            }
            else if (IsVisible)
            {
                Hide();
            }
        }

        /// <summary>
        /// Set the opacity of the overlay
        /// </summary>
        public void SetOpacity(double opacity)
        {
            Opacity = Math.Clamp(opacity, 0.0, 1.0);
        }
    }
}
