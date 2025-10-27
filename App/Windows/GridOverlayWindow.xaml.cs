using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using App.Models;

namespace App.Windows
{
    /// <summary>
    /// Transparent overlay window that displays grid cells on a monitor
    /// </summary>
    public partial class GridOverlayWindow : Window
    {
        private GridLayout? _gridLayout;
        private readonly Dictionary<int, Rectangle> _cellVisuals = new();
        private GridCell? _highlightedCell;

        // Visual styling
        private readonly SolidColorBrush _normalCellBrush = new(Color.FromArgb(60, 100, 149, 237)); // Transparent blue
        private readonly SolidColorBrush _highlightCellBrush = new(Color.FromArgb(120, 50, 205, 50)); // Transparent green
        private readonly SolidColorBrush _occupiedCellBrush = new(Color.FromArgb(80, 220, 20, 60)); // Transparent red
        private readonly Pen _cellBorderPen = new(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 2);

        // Win32 API for making window click-through
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public GridOverlayWindow(MonitorInfo monitor)
        {
            InitializeComponent();

            // Position overlay to cover the entire monitor
            Left = monitor.Bounds.Left;
            Top = monitor.Bounds.Top;
            Width = monitor.Bounds.Width;
            Height = monitor.Bounds.Height;

            _cellBorderPen.Freeze();

            // Make window click-through after it's loaded
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Make the window click-through by setting WS_EX_TRANSPARENT style
            var hwnd = new WindowInteropHelper(this).Handle;
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT);
        }

        /// <summary>
        /// Set the grid layout to display
        /// </summary>
        public void SetGridLayout(GridLayout layout)
        {
            _gridLayout = layout;
            RedrawGrid();
        }

        /// <summary>
        /// Redraw all grid cells
        /// </summary>
        private void RedrawGrid()
        {
            GridCanvas.Children.Clear();
            _cellVisuals.Clear();

            if (_gridLayout == null) return;

            foreach (var cell in _gridLayout.Cells)
            {
                var rect = new Rectangle
                {
                    Width = cell.Bounds.Width,
                    Height = cell.Bounds.Height,
                    Fill = cell.IsOccupied ? _occupiedCellBrush : _normalCellBrush,
                    Stroke = _cellBorderPen.Brush,
                    StrokeThickness = _cellBorderPen.Thickness,
                    RadiusX = 5,
                    RadiusY = 5
                };

                Canvas.SetLeft(rect, cell.Bounds.Left - Left);
                Canvas.SetTop(rect, cell.Bounds.Top - Top);

                GridCanvas.Children.Add(rect);
                _cellVisuals[cell.Id] = rect;

                // Add cell number label
                var label = new TextBlock
                {
                    Text = (cell.Id + 1).ToString(),
                    FontSize = 24,
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

                Canvas.SetLeft(label, cell.Bounds.Left - Left + 10);
                Canvas.SetTop(label, cell.Bounds.Top - Top + 10);

                GridCanvas.Children.Add(label);
            }
        }

        /// <summary>
        /// Highlight a specific cell (e.g., when window hovers over it)
        /// </summary>
        public void HighlightCell(GridCell? cell)
        {
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
                if (_cellVisuals.ContainsKey(cell.Id))
                {
                    var rect = _cellVisuals[cell.Id];
                    if (cell != _highlightedCell)
                    {
                        rect.Fill = cell.IsOccupied ? _occupiedCellBrush : _normalCellBrush;
                    }
                }
            }
        }

        /// <summary>
        /// Show or hide the overlay
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (visible)
            {
                Show();
                Opacity = 1.0;
            }
            else
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
