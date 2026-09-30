using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using App.Models;
using App.Services;
using App.Windows;
using Newtonsoft.Json;

namespace App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string ProfileFileFilter = "NittyGriddy profile (*.json)|*.json|All files (*.*)|*.*";

        private readonly AppController _controller;
        private readonly DispatcherTimer _refreshTimer;
        private bool _loadingProfiles;
        private bool _exiting;

        public MainWindow(AppController controller)
        {
            InitializeComponent();
            ThemeMode = ThemeMode.Dark;

            _controller = controller;

            RestorePlacement();

            LayoutPage.Initialize(controller);
            TablesPage.Initialize(controller);
            HotkeysPage.Initialize(controller);
            BehaviorPage.Initialize(controller);

            Guide.Initialize(controller);
            Guide.PageRequested += page => Pages.SelectedIndex = page;
            Guide.BehaviorChanged += BehaviorPage.Reload;

            controller.StateChanged += UpdateHeader;
            controller.ProfileChanged += LoadProfiles;
            LoadProfiles();
            UpdateHeader();

            // Tables open, close and change title outside our control; poll while the window is visible
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += (s, e) => RefreshLiveData();
            _refreshTimer.Start();

            IsVisibleChanged += (s, e) => _refreshTimer.IsEnabled = IsVisible;
        }

        // ----- Header -----

        private void RefreshLiveData()
        {
            UpdateHeader();
            Guide.Refresh();

            if (Pages.SelectedIndex == 1)
                TablesPage.RefreshTables();
        }

        private void UpdateHeader()
        {
            var grid = _controller.Grid;

            SwGrid.IsChecked = grid.IsEnabled;
            TxtGridState.Text = grid.IsEnabled ? "Grid is on" : "Grid is off";
            BtnArrange.IsEnabled = grid.IsEnabled;
            BtnPreview.Content = grid.IsPreviewMode ? "Hide grid" : "Show grid";

            var tables = grid.GetTargetWindowCount();
            TxtGridSummary.Text = $"{Count(tables, "table")} open · {Count(grid.SlotCount, "slot")}";

            TxtStatus.Text = _controller.Status;

            var failures = _controller.HotkeyFailures.Count;
            TxtHotkeyWarning.Visibility = failures > 0 ? Visibility.Visible : Visibility.Collapsed;
            TxtHotkeyWarning.Text = $"{Count(failures, "hotkey")} not working — see Hotkeys";
        }

        private static string Count(int number, string noun) => number == 1 ? $"1 {noun}" : $"{number} {noun}s";

        private void SwGrid_Click(object sender, RoutedEventArgs e)
        {
            _controller.SetGridEnabled(SwGrid.IsChecked == true);
        }

        private void BtnArrange_Click(object sender, RoutedEventArgs e) => _controller.AutoArrange();

        private void BtnPreview_Click(object sender, RoutedEventArgs e) => _controller.TogglePreview();

        private void BtnToFront_Click(object sender, RoutedEventArgs e) => _controller.BringAllTablesToFront();

        private void Pages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source == Pages && Pages.SelectedIndex == 1)
                TablesPage.RefreshTables();
        }

        // ----- Profiles -----

        private void LoadProfiles()
        {
            _loadingProfiles = true;
            CmbProfile.ItemsSource = _controller.ProfileNames;
            CmbProfile.SelectedItem = _controller.Settings.ActiveProfileName;
            MenuDeleteProfile.IsEnabled = _controller.ProfileNames.Count > 1;
            _loadingProfiles = false;
        }

        private void CmbProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_loadingProfiles && CmbProfile.SelectedItem is string name)
                _controller.SwitchProfile(name);
        }

        private void BtnProfileMenu_Click(object sender, RoutedEventArgs e)
        {
            var menu = BtnProfileMenu.ContextMenu!;
            menu.PlacementTarget = BtnProfileMenu;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void MenuNewProfile_Click(object sender, RoutedEventArgs e)
        {
            var name = PromptDialog.AskText(this, "New profile",
                "Name for the new profile. It starts with a default grid and the current table rules.", "New profile", "Create");
            if (name != null)
                _controller.AddProfile(name, duplicateLayouts: false);
        }

        private void MenuDuplicateProfile_Click(object sender, RoutedEventArgs e)
        {
            var name = PromptDialog.AskText(this, "Duplicate profile",
                "Name for the copy.", $"{_controller.Settings.ActiveProfileName} copy", "Duplicate");
            if (name != null)
                _controller.AddProfile(name, duplicateLayouts: true);
        }

        private void MenuRenameProfile_Click(object sender, RoutedEventArgs e)
        {
            var name = PromptDialog.AskText(this, "Rename profile", "New name.", _controller.Settings.ActiveProfileName, "Rename");
            if (name != null)
                _controller.RenameActiveProfile(name);
        }

        private void MenuDeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            var name = _controller.Settings.ActiveProfileName;
            if (PromptDialog.Confirm(this, "Delete profile", $"Delete the profile \"{name}\"? This cannot be undone.", "Delete"))
            {
                _controller.DeleteActiveProfile();
                _controller.SetStatus($"Deleted profile \"{name}\"");
            }
        }

        private void MenuImportProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = ProfileFileFilter, DefaultExt = "json" };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                var name = _controller.ImportProfile(dialog.FileName);
                _controller.SetStatus($"Imported profile \"{name}\"");
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException)
            {
                _controller.SetStatus($"Could not import {Path.GetFileName(dialog.FileName)}: {ex.Message}");
            }
        }

        private void MenuExportProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = ProfileFileFilter,
                DefaultExt = "json",
                FileName = $"{_controller.Settings.ActiveProfileName}.json"
            };
            if (dialog.ShowDialog(this) != true)
                return;

            try
            {
                _controller.ExportActiveProfile(dialog.FileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _controller.SetStatus($"Could not export: {ex.Message}");
            }
        }

        // ----- Window placement, tray behaviour -----

        private void RestorePlacement()
        {
            var placement = _controller.Settings.MainWindow;
            if (placement == null || placement.Width < MinWidth || placement.Height < MinHeight)
                return;

            // Ignore a position that is no longer on any screen (monitor unplugged)
            var visible = placement.Left + 100 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
                          && placement.Left + placement.Width - 100 > SystemParameters.VirtualScreenLeft
                          && placement.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50
                          && placement.Top >= SystemParameters.VirtualScreenTop;
            if (!visible)
                return;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
            Width = placement.Width;
            Height = placement.Height;
            if (placement.Maximized)
                WindowState = WindowState.Maximized;
        }

        private void SavePlacement()
        {
            // RestoreBounds is the normal-state rectangle even while maximised or minimised
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            // A window that was never shown (started in the tray) has no position yet
            if (bounds.IsEmpty || double.IsNaN(bounds.Left) || double.IsNaN(bounds.Top))
                return;

            _controller.Settings.MainWindow = new WindowPlacement
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = WindowState == WindowState.Maximized
            };
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);

            if (WindowState == WindowState.Minimized && _controller.Settings.Behavior.MinimizeToTray)
                Hide();
        }

        /// <summary>
        /// Called when the application is exiting from the tray menu, so closing is not turned into hiding
        /// </summary>
        public void PrepareForExit()
        {
            _exiting = true;
            SavePlacement();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            if (_exiting)
                return;

            SavePlacement();

            if (_controller.Settings.Behavior.CloseToTray)
            {
                e.Cancel = true;
                _controller.RequestSave();
                Hide();
                return;
            }

            _exiting = true;
            Application.Current.Shutdown();
        }

        protected override void OnClosed(EventArgs e)
        {
            _refreshTimer.Stop();
            base.OnClosed(e);
        }
    }
}
