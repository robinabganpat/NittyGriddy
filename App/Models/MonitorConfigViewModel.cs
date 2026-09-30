using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
        private int _revision;
        private IReadOnlyList<GridCell> _previewCells;

        public event PropertyChangedEventHandler? PropertyChanged;

        public MonitorConfigViewModel(MonitorInfo monitor, GridLayout layout, Action? onConfigChanged = null)
        {
            _monitor = monitor;
            _layout = layout;
            _onConfigChanged = onConfigChanged;

            // Custom cells have no editor; treat such a layout as a fixed grid
            if (_layout.Mode == GridLayoutMode.CustomCells)
                _layout.Mode = GridLayoutMode.FixedGrid;

            _previewCells = _layout.Cells;
        }

        /// <summary>
        /// The cells the preview draws. While a setting is being adjusted these are calculated on the spot;
        /// once the change has been applied they are the real cells, which also know which slots hold tables.
        /// </summary>
        public IReadOnlyList<GridCell> PreviewCells => _previewCells;

        /// <summary>
        /// Recalculate the preview from the current settings without applying them to the screen.
        /// Returns the number of slots this display will have.
        /// </summary>
        /// <param name="firstSlotNumber">Number of this display's first slot, counting across displays</param>
        public int UpdatePreview(int firstSlotNumber)
        {
            _layout.DisplayBounds = _monitor.WorkArea;

            var cells = _layout.BuildCells();
            foreach (var cell in cells)
                cell.SlotNumber = firstSlotNumber + cell.Id;

            _previewCells = cells;
            RaisePreviewChanged();
            return cells.Count;
        }

        /// <summary>
        /// "Display 2 · 2560×1440 · Primary"
        /// </summary>
        public string Header
        {
            get
            {
                // Device names look like \\.\DISPLAY2
                var number = new string(_monitor.DeviceName.Where(char.IsDigit).ToArray());
                var name = number.Length > 0 ? $"Display {number}" : _monitor.DeviceName;
                return $"{name} · {_monitor.Bounds.Width:F0}×{_monitor.Bounds.Height:F0}" + (_monitor.IsPrimary ? " · Primary" : "");
            }
        }

        public string RadioGroupName => $"LayoutMode_{_monitor.DeviceName}";

        public MonitorInfo Monitor => _monitor;
        public GridLayout Layout => _layout;

        /// <summary>
        /// Changes whenever the grid or its occupancy changes; views bind to it to redraw
        /// </summary>
        public int Revision => _revision;

        /// <summary>
        /// Which slot numbers this monitor holds, e.g. "Slots 7–12"
        /// </summary>
        public string SlotRange
        {
            get
            {
                if (IsDisabledMode || _previewCells.Count == 0)
                    return "No grid on this display";

                var first = _previewCells[0].SlotNumber;
                var last = _previewCells[^1].SlotNumber;
                return first == last ? $"Slot {first}" : $"Slots {first}–{last}";
            }
        }

        public bool IsFixedGridMode
        {
            get => _layout.Mode == GridLayoutMode.FixedGrid;
            set { if (value) SetMode(GridLayoutMode.FixedGrid); }
        }

        public bool IsNWindowMode
        {
            get => _layout.Mode == GridLayoutMode.NWindowOptimized;
            set { if (value) SetMode(GridLayoutMode.NWindowOptimized); }
        }

        public bool IsAutoFitMode
        {
            get => _layout.Mode == GridLayoutMode.AutoFit;
            set { if (value) SetMode(GridLayoutMode.AutoFit); }
        }

        public int AutoFitMaxTables
        {
            get => _layout.AutoFitMaxTables;
            set
            {
                if (_layout.AutoFitMaxTables != value && value > 0 && value <= 20)
                {
                    _layout.AutoFitMaxTables = value;
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public bool IsDisabledMode
        {
            get => _layout.Mode == GridLayoutMode.Disabled;
            set { if (value) SetMode(GridLayoutMode.Disabled); }
        }

        public bool IsGridActive => !IsDisabledMode;

        private void SetMode(GridLayoutMode mode)
        {
            if (_layout.Mode == mode)
                return;

            _layout.Mode = mode;
            OnPropertyChanged(nameof(IsFixedGridMode));
            OnPropertyChanged(nameof(IsNWindowMode));
            OnPropertyChanged(nameof(IsAutoFitMode));
            OnPropertyChanged(nameof(IsDisabledMode));
            OnPropertyChanged(nameof(IsGridActive));
            NotifyConfigChanged();
        }

        public int Rows
        {
            get => _layout.Rows;
            set
            {
                if (_layout.Rows != value && value > 0 && value <= 10)
                {
                    _layout.Rows = value;
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public int Columns
        {
            get => _layout.Columns;
            set
            {
                if (_layout.Columns != value && value > 0 && value <= 10)
                {
                    _layout.Columns = value;
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public int NumWindows
        {
            get => _layout.NumberOfWindows;
            set
            {
                if (_layout.NumberOfWindows != value && value > 0 && value <= 100)
                {
                    _layout.NumberOfWindows = value;
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public double CellSpacing
        {
            get => _layout.CellSpacing;
            set
            {
                if (Math.Abs(_layout.CellSpacing - value) > 0.01)
                {
                    _layout.CellSpacing = value;
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        public double CellMargins
        {
            get => _layout.CellMargins.Top;
            set
            {
                if (Math.Abs(_layout.CellMargins.Top - value) > 0.01)
                {
                    _layout.CellMargins = new Thickness(value);
                    OnPropertyChanged();
                    NotifyConfigChanged();
                }
            }
        }

        /// <summary>
        /// Call after the grid was recalculated or tables moved, so bound views redraw
        /// </summary>
        public void NotifyGridApplied()
        {
            _previewCells = _layout.Cells;
            RaisePreviewChanged();
        }

        private void RaisePreviewChanged()
        {
            _revision++;
            OnPropertyChanged(nameof(Revision));
            OnPropertyChanged(nameof(SlotRange));
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
