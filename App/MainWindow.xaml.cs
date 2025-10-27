using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using App.Models;
using App.Services;
using Newtonsoft.Json;

namespace App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly GridManagerService _gridManager;
        private readonly MonitorService _monitorService;
        private MonitorGridConfig _currentConfig;
        private readonly ObservableCollection<MonitorConfigViewModel> _monitorConfigs = new();

        private readonly ObservableCollection<WindowFilter> _windowFilters = new();
        private DispatcherTimer? _windowCountTimer;
        private WindowFilter? _editingFilter;

        public MainWindow()
        {
            InitializeComponent();

            _gridManager = new GridManagerService(Dispatcher);
            _monitorService = new MonitorService();
            _currentConfig = new MonitorGridConfig("Default");

            InitializeUI();
            LoadMonitors();
            SetupKeyboardShortcuts();
            SetupWindowCountMonitoring();
        }

        private void SetupWindowCountMonitoring()
        {
            // Update window count every 2 seconds
            _windowCountTimer = new DispatcherTimer();
            _windowCountTimer.Interval = TimeSpan.FromSeconds(1);
            _windowCountTimer.Tick += (s, e) => UpdateWindowCount();
            _windowCountTimer.Start();
        }

        private void InitializeUI()
        {
            LstWindowFilters.ItemsSource = _windowFilters;

            // Load window filters from config
            _windowFilters.Clear();
            foreach (var filter in _currentConfig.WindowFilters)
            {
                _windowFilters.Add(filter);
            }

            // Load aspect ratio setting
            ChkMaintainAspectRatio.IsChecked = _currentConfig.MaintainAspectRatio;

            // Load monitor configurations
            LoadMonitorConfigs();
        }

        private void LoadMonitors()
        {
            LoadMonitorConfigs();
        }

        private void LoadMonitorConfigs()
        {
            _monitorConfigs.Clear();

            var monitors = _monitorService.GetAllMonitors();

            System.Diagnostics.Debug.WriteLine($"LoadMonitorConfigs: Found {monitors.Count} monitor(s)");

            // Sort monitors: Primary first, then by device name
            var sortedMonitors = monitors.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.DeviceName).ToList();

            foreach (var monitor in sortedMonitors)
            {
                var layout = _currentConfig.GetOrCreateLayoutForMonitor(monitor.DeviceName);
                layout.DisplayBounds = monitor.WorkArea;

                var viewModel = new MonitorConfigViewModel(monitor, layout, OnMonitorConfigChanged);
                _monitorConfigs.Add(viewModel);

                System.Diagnostics.Debug.WriteLine($"  Added monitor: {monitor.DeviceName} - {monitor.Bounds.Width}x{monitor.Bounds.Height}{(monitor.IsPrimary ? " (Primary)" : "")}");
            }

            MonitorConfigsContainer.ItemsSource = _monitorConfigs;

            // Apply the configuration to create overlays
            System.Diagnostics.Debug.WriteLine("LoadMonitorConfigs: Applying initial grid configuration");
            AutoApplyGridConfiguration();

            // Adjust window size to fit all monitors (show all, regardless of enabled/disabled)
            AdjustWindowSizeForMonitors(sortedMonitors.Count);
        }

        private void OnMonitorConfigChanged()
        {
            // Auto-apply when any monitor configuration changes
            AutoApplyGridConfiguration();
        }

        private void AdjustWindowSizeForMonitors(int monitorCount)
        {
            // Base height for fixed elements (header, control panel, tabs, buttons, margins)
            const double baseHeight = 400;

            // Approximate height per monitor configuration section (expanded)
            const double heightPerMonitor = 280;

            // Calculate desired height
            var desiredHeight = baseHeight + (monitorCount * heightPerMonitor);

            // Get the screen where the window will appear (primary screen work area)
            var workingArea = SystemParameters.WorkArea;
            var maxHeight = workingArea.Height - 60; // Leave some margin for taskbar

            // Cap at max screen height
            var finalHeight = Math.Min(desiredHeight, maxHeight);

            // Only adjust if needed and window isn't maximized
            if (this.WindowState != WindowState.Maximized)
            {
                this.Height = finalHeight;
                System.Diagnostics.Debug.WriteLine($"Window height adjusted to {finalHeight}px for {monitorCount} monitor(s)");
            }
        }

        private void SetupKeyboardShortcuts()
        {
            // Ctrl+Shift+G to toggle grid
            var toggleCommand = new RoutedCommand();
            toggleCommand.InputGestures.Add(new KeyGesture(Key.G, ModifierKeys.Control | ModifierKeys.Shift));
            CommandBindings.Add(new CommandBinding(toggleCommand, (s, e) => ToggleGrid()));

            // Ctrl+Shift+S to snap active window
            var snapCommand = new RoutedCommand();
            snapCommand.InputGestures.Add(new KeyGesture(Key.S, ModifierKeys.Control | ModifierKeys.Shift));
            CommandBindings.Add(new CommandBinding(snapCommand, (s, e) => SnapActiveWindow()));
        }

        private void AutoApplyGridConfiguration()
        {
            System.Diagnostics.Debug.WriteLine($"AutoApplyGridConfiguration: Processing {_monitorConfigs.Count} monitor(s)");

            // Apply configuration for all monitors
            foreach (var monitorConfig in _monitorConfigs)
            {
                var layout = monitorConfig.Layout;
                layout.DisplayBounds = monitorConfig.Monitor.WorkArea;
                layout.CalculateCells();

                System.Diagnostics.Debug.WriteLine($"  Monitor {monitorConfig.Monitor.DeviceName}: {layout.Cells.Count} cells calculated");
            }

            // Update window filters
            _currentConfig.WindowFilters.Clear();
            _currentConfig.WindowFilters.AddRange(_windowFilters);

            System.Diagnostics.Debug.WriteLine($"AutoApplyGridConfiguration: Calling GridManager.LoadConfiguration with {_currentConfig.MonitorLayouts.Count} layout(s) and {_currentConfig.WindowFilters.Count} filter(s)");

            // Reload configuration
            _gridManager.LoadConfiguration(_currentConfig);

            UpdateGridStatus();
        }

        private void BtnToggleGrid_Click(object sender, RoutedEventArgs e)
        {
            ToggleGrid();
        }

        private void ToggleGrid()
        {
            _gridManager.Toggle();
            UpdateGridStatus();
        }

        private void UpdateGridStatus()
        {
            if (_gridManager.IsEnabled)
            {
                BtnToggleGrid.Content = "Disable Grid";

                // Show filter status
                if (_windowFilters.Count == 0)
                {
                    TxtGridStatus.Text = "Grid Enabled - No Filters (No Windows Tracked)";
                }
                else
                {
                    TxtGridStatus.Text = $"Grid Enabled - Tracking {_windowFilters.Count} filter(s)";
                }

                // AccentBlue color for enabled state
                TxtGridStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078D4"));
            }
            else
            {
                BtnToggleGrid.Content = "Enable Grid";
                TxtGridStatus.Text = "Grid Disabled";
                // TextSecondary color for disabled state
                TxtGridStatus.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#5E5E5E"));
            }

            // Update preview button
            UpdatePreviewButton();

            // Update window count
            UpdateWindowCount();
        }

        private void BtnPreviewGrid_Click(object sender, RoutedEventArgs e)
        {
            _gridManager.TogglePreview();
            UpdatePreviewButton();
        }

        private void UpdatePreviewButton()
        {
            if (_gridManager.IsPreviewMode)
            {
                BtnPreviewGrid.Content = "Hide Preview";
            }
            else
            {
                BtnPreviewGrid.Content = "Preview Grid";
            }
        }

        private void UpdateWindowCount()
        {
            var windowCount = _gridManager.GetTargetWindowCount();
            TxtWindowCount.Text = $"Target Windows: {windowCount}";
        }

        private void SnapActiveWindow()
        {
            // Get foreground window
            var hWnd = GetForegroundWindow();
            if (hWnd != IntPtr.Zero)
            {
                _gridManager.SnapWindowToNearestCell(hWnd);
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private void BtnAutoArrange_Click(object sender, RoutedEventArgs e)
        {
            if (!_gridManager.IsEnabled)
            {
                MessageBox.Show("Please enable the grid first before auto-arranging windows.", "Grid Not Enabled", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var arranged = _gridManager.AutoArrangeWindows();
            MessageBox.Show($"Successfully arranged {arranged} window(s) across the grid.", "Auto-Arrange Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            UpdateWindowCount();
        }

        private void BtnAddWindowFilter_Click(object sender, RoutedEventArgs e)
        {
            var className = TxtNewWindowClass.Text.Trim();
            var titlePattern = TxtNewWindowTitle.Text.Trim();
            var useRegex = ChkUseRegex.IsChecked == true;

            // At least one must be specified
            if (string.IsNullOrEmpty(className) && string.IsNullOrEmpty(titlePattern))
            {
                MessageBox.Show("Please enter at least a Class Name OR a Title Pattern.", "Empty Filter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate regex patterns if regex mode is enabled
            if (useRegex)
            {
                try
                {
                    if (!string.IsNullOrEmpty(className))
                        System.Text.RegularExpressions.Regex.Match("", className);
                    if (!string.IsNullOrEmpty(titlePattern))
                        System.Text.RegularExpressions.Regex.Match("", titlePattern);
                }
                catch (ArgumentException ex)
                {
                    MessageBox.Show($"Invalid regex pattern:\n\n{ex.Message}", "Regex Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            if (_editingFilter != null)
            {
                // Update existing filter
                _editingFilter.ClassName = className;
                _editingFilter.TitlePattern = titlePattern;
                _editingFilter.UseRegex = useRegex;

                // Refresh the list by removing and re-adding
                var index = _windowFilters.IndexOf(_editingFilter);
                _windowFilters.RemoveAt(index);
                _windowFilters.Insert(index, _editingFilter);

                _editingFilter = null;
                BtnAddFilter.Content = "Add Filter";
                BtnCancelEdit.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Add new filter
                var filter = new WindowFilter(className, titlePattern, useRegex);
                _windowFilters.Add(filter);
            }

            TxtNewWindowClass.Clear();
            TxtNewWindowTitle.Clear();
            ChkUseRegex.IsChecked = false;

            AutoApplyGridConfiguration();
            UpdateWindowCount();
        }

        private void BtnEditWindowFilter_Click(object sender, RoutedEventArgs e)
        {
            if (LstWindowFilters.SelectedItem is WindowFilter selected)
            {
                // Populate fields with selected filter
                TxtNewWindowClass.Text = selected.ClassName;
                TxtNewWindowTitle.Text = selected.TitlePattern;
                ChkUseRegex.IsChecked = selected.UseRegex;

                // Track which filter we're editing
                _editingFilter = selected;
                BtnAddFilter.Content = "Update Filter";
                BtnCancelEdit.Visibility = Visibility.Visible;

                // Scroll to the input area
                TxtNewWindowClass.Focus();
            }
        }

        private void BtnCancelEdit_Click(object sender, RoutedEventArgs e)
        {
            // Clear editing state
            _editingFilter = null;
            BtnAddFilter.Content = "Add Filter";
            BtnCancelEdit.Visibility = Visibility.Collapsed;

            // Clear input fields
            TxtNewWindowClass.Clear();
            TxtNewWindowTitle.Clear();
            ChkUseRegex.IsChecked = false;
        }

        private void BtnRemoveWindowFilter_Click(object sender, RoutedEventArgs e)
        {
            if (LstWindowFilters.SelectedItem is WindowFilter selected)
            {
                // If we're editing this filter, cancel the edit
                if (_editingFilter == selected)
                {
                    BtnCancelEdit_Click(sender, e);
                }

                _windowFilters.Remove(selected);
                AutoApplyGridConfiguration();
                UpdateWindowCount();
            }
        }

        private void BtnPickWindow_Click(object sender, RoutedEventArgs e)
        {
            // Cancel any ongoing edit
            if (_editingFilter != null)
            {
                BtnCancelEdit_Click(sender, e);
            }

            var picker = new Windows.WindowPickerDialog();
            picker.Owner = this;

            if (picker.ShowDialog() == true && picker.SelectedWindow != null)
            {
                var window = picker.SelectedWindow;

                // Ask user what they want to add
                var result = MessageBox.Show(
                    $"Selected Window:\n\n" +
                    $"Title: {window.Title}\n" +
                    $"Class: {window.ClassName}\n\n" +
                    $"How would you like to create the filter?\n\n" +
                    $"Yes = Class ONLY\n" +
                    $"No = Title ONLY\n" +
                    $"Cancel = Class AND Title (both must match)",
                    "Add Filter",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                WindowFilter? newFilter = null;
                var useRegex = ChkUseRegex.IsChecked == true;

                if (result == MessageBoxResult.Yes)
                {
                    // Add class name only
                    if (!string.IsNullOrEmpty(window.ClassName))
                    {
                        newFilter = new WindowFilter(window.ClassName, "", useRegex);
                    }
                }
                else if (result == MessageBoxResult.No)
                {
                    // Add title pattern only
                    if (!string.IsNullOrEmpty(window.Title))
                    {
                        newFilter = new WindowFilter("", window.Title, useRegex);
                    }
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    // Add both (AND logic)
                    if (!string.IsNullOrEmpty(window.ClassName) || !string.IsNullOrEmpty(window.Title))
                    {
                        newFilter = new WindowFilter(window.ClassName ?? "", window.Title ?? "", useRegex);
                    }
                }

                if (newFilter != null)
                {
                    _windowFilters.Add(newFilter);
                    ChkUseRegex.IsChecked = false;
                    AutoApplyGridConfiguration();
                    UpdateWindowCount();
                }
            }
        }


        private void ChkMaintainAspectRatio_Changed(object sender, RoutedEventArgs e)
        {
            // Ignore if called during initialization before _currentConfig is created
            if (_currentConfig == null)
                return;

            _currentConfig.MaintainAspectRatio = ChkMaintainAspectRatio.IsChecked == true;
            System.Diagnostics.Debug.WriteLine($"MaintainAspectRatio setting changed to: {_currentConfig.MaintainAspectRatio}");
        }

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ensure current UI state is synced to config before saving
                SyncUIToConfig();

                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = "json",
                    FileName = "grid_config.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var json = JsonConvert.SerializeObject(_currentConfig, Formatting.Indented);
                    File.WriteAllText(dialog.FileName, json);
                    MessageBox.Show("Configuration saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SyncUIToConfig()
        {
            // Sync window filters from UI to config
            _currentConfig.WindowFilters.Clear();
            _currentConfig.WindowFilters.AddRange(_windowFilters);

            // Sync aspect ratio setting
            _currentConfig.MaintainAspectRatio = ChkMaintainAspectRatio.IsChecked == true;

            // All monitor layouts are already synced through ViewModels with data binding
            // No need to manually sync - ViewModels update layouts in real-time
        }

        private void BtnLoadConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cancel any ongoing edit
                if (_editingFilter != null)
                {
                    BtnCancelEdit_Click(sender, e);
                }

                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = "json"
                };

                if (dialog.ShowDialog() == true)
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var config = JsonConvert.DeserializeObject<MonitorGridConfig>(json);
                    if (config != null)
                    {
                        // Migrate legacy filters if loading old config
                        config.MigrateLegacyFilters();

                        _currentConfig = config;
                        _gridManager.LoadConfiguration(_currentConfig);
                        InitializeUI(); // This will reload monitor configs
                        UpdateWindowCount();
                        MessageBox.Show("Configuration loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _windowCountTimer?.Stop();
            _gridManager.Dispose();
            base.OnClosed(e);
        }
    }
}