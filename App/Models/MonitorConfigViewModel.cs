using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace App.Models
{
    /// <summary>
    /// ViewModel for monitor grid configuration UI
    /// </summary>
    public class MonitorConfigViewModel : INotifyPropertyChanged
    {
        private readonly MonitorInfo _monitor;
        private readonly GridLayout _layout;
        private readonly Action? _onConfigChanged;

        private bool _isFixedGridMode = true;
        private bool _isNWindowMode = false;
        private bool _isDisabledMode = false;
        private int _rows = 2;
        private int _columns = 3;
        private int _numWindows = 6;
        private double _cellSpacing = 10;
        private double _cellMargins = 5;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MonitorConfigViewModel(MonitorInfo monitor, GridLayout layout, Action? onConfigChanged = null)
        {
            _monitor = monitor;
            _layout = layout;
            _onConfigChanged = onConfigChanged;

            // Initialize from layout - set defaults if not already set
            if (layout.Mode == GridLayoutMode.Disabled)
            {
                _isFixedGridMode = false;
                _isNWindowMode = false;
                _isDisabledMode = true;
            }
            else if (layout.Mode == GridLayoutMode.NWindowOptimized)
            {
                _isFixedGridMode = false;
                _isNWindowMode = true;
                _isDisabledMode = false;
            }
            else if (layout.Mode == GridLayoutMode.FixedGrid || layout.Mode == 0)
            {
                _isFixedGridMode = true;
                _isNWindowMode = false;
                _isDisabledMode = false;
                layout.Mode = GridLayoutMode.FixedGrid;
            }

            // Load values from layout or use defaults if not set
            _rows = layout.Rows > 0 ? layout.Rows : 2;
            _columns = layout.Columns > 0 ? layout.Columns : 3;
            _numWindows = layout.NumberOfWindows > 0 ? layout.NumberOfWindows : 6;

            // For CellSpacing and CellMargins, use the layout values directly (including 0)
            // Only apply defaults if the layout was just created (GridLayout constructor sets CellSpacing=10, CellMargins=5)
            _cellSpacing = layout.CellSpacing;
            _cellMargins = layout.CellMargins.Top;

            // Ensure layout has row/column values
            layout.Rows = _rows;
            layout.Columns = _columns;
            layout.NumberOfWindows = _numWindows;

            System.Diagnostics.Debug.WriteLine($"MonitorConfigViewModel created for {monitor.DeviceName}: Mode={layout.Mode}, Rows={_rows}, Cols={_columns}, Spacing={_cellSpacing}, Margins={_cellMargins}");
        }

        public string Header => $"{_monitor.DeviceName} - {_monitor.Bounds.Width}x{_monitor.Bounds.Height}" +
                                (_monitor.IsPrimary ? " (Primary)" : "");

        public string RadioGroupName => $"LayoutMode_{_monitor.DeviceName}";

        public MonitorInfo Monitor => _monitor;
        public GridLayout Layout => _layout;

        public bool IsFixedGridMode
        {
            get => _isFixedGridMode;
            set
            {
                if (_isFixedGridMode != value)
                {
                    _isFixedGridMode = value;
                    if (value)
                    {
                        _isNWindowMode = false;
                        _isDisabledMode = false;
                        _layout.Mode = GridLayoutMode.FixedGrid;
                        OnPropertyChanged(nameof(IsNWindowMode));
                        OnPropertyChanged(nameof(IsDisabledMode));
                    }
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public bool IsNWindowMode
        {
            get => _isNWindowMode;
            set
            {
                if (_isNWindowMode != value)
                {
                    _isNWindowMode = value;
                    if (value)
                    {
                        _isFixedGridMode = false;
                        _isDisabledMode = false;
                        _layout.Mode = GridLayoutMode.NWindowOptimized;
                        OnPropertyChanged(nameof(IsFixedGridMode));
                        OnPropertyChanged(nameof(IsDisabledMode));
                    }
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public bool IsDisabledMode
        {
            get => _isDisabledMode;
            set
            {
                if (_isDisabledMode != value)
                {
                    _isDisabledMode = value;
                    if (value)
                    {
                        _isFixedGridMode = false;
                        _isNWindowMode = false;
                        _layout.Mode = GridLayoutMode.Disabled;
                        OnPropertyChanged(nameof(IsFixedGridMode));
                        OnPropertyChanged(nameof(IsNWindowMode));
                    }
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public int Rows
        {
            get => _rows;
            set
            {
                if (_rows != value && value > 0 && value <= 10)
                {
                    _rows = value;
                    _layout.Rows = value;
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public int Columns
        {
            get => _columns;
            set
            {
                if (_columns != value && value > 0 && value <= 10)
                {
                    _columns = value;
                    _layout.Columns = value;
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public int NumWindows
        {
            get => _numWindows;
            set
            {
                if (_numWindows != value && value > 0 && value <= 100)
                {
                    _numWindows = value;
                    _layout.NumberOfWindows = value;
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public double CellSpacing
        {
            get => _cellSpacing;
            set
            {
                if (Math.Abs(_cellSpacing - value) > 0.01)
                {
                    _cellSpacing = value;
                    _layout.CellSpacing = value;
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public double CellMargins
        {
            get => _cellMargins;
            set
            {
                if (Math.Abs(_cellMargins - value) > 0.01)
                {
                    _cellMargins = value;
                    _layout.CellMargins = new Thickness(value);
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        private void NotifyConfigChanged()
        {
            _onConfigChanged?.Invoke();
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
