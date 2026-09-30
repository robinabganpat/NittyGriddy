using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using App.Models;
using App.Services;

namespace App.Views
{
    /// <summary>
    /// Behaviour options that are not part of a layout profile
    /// </summary>
    public partial class BehaviorPage : UserControl
    {
        /// <summary>
        /// A selectable colour for the active-table border
        /// </summary>
        public sealed class ColorChoice
        {
            public string Name { get; init; } = string.Empty;
            public string Value { get; init; } = string.Empty;
            public bool IsSelected { get; set; }
            public Brush Brush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Value));
        }

        private static readonly (string Name, string Value)[] BorderColors =
        {
            ("Green", "#FF3DDC84"),
            ("Yellow", "#FFFFD23F"),
            ("Orange", "#FFFF8A3D"),
            ("Red", "#FFFF4D4D"),
            ("Magenta", "#FFFF4FD8"),
            ("Blue", "#FF4DA3FF"),
            ("White", "#FFFFFFFF"),
        };

        private AppController? _controller;
        private bool _loading;

        public BehaviorPage()
        {
            InitializeComponent();
        }

        public void Initialize(AppController controller)
        {
            _controller = controller;
            Load();
        }

        /// <summary>
        /// Show the current options again after they were changed elsewhere (the getting-started guide)
        /// </summary>
        public void Reload() => Load();

        private void Load()
        {
            if (_controller == null) return;

            _loading = true;
            var behavior = _controller.Settings.Behavior;

            RadSwap.IsChecked = behavior.DropOnOccupied == DropBehavior.Swap;
            RadStack.IsChecked = behavior.DropOnOccupied == DropBehavior.Stack;
            ChkCompact.IsChecked = behavior.CompactOnClose;
            ChkCascade.IsChecked = behavior.CascadeStacks;
            RadArrangeKeep.IsChecked = behavior.ArrangeKeepsPositions;
            RadArrangePack.IsChecked = !behavior.ArrangeKeepsPositions;

            ChkBorder.IsChecked = behavior.ShowActiveBorder;
            SldThickness.Value = behavior.ActiveBorderThickness;
            TxtThickness.Text = $"{behavior.ActiveBorderThickness} px";
            BorderOptions.IsEnabled = behavior.ShowActiveBorder;

            var colors = BorderColors
                .Select(c => new ColorChoice
                {
                    Name = c.Name,
                    Value = c.Value,
                    IsSelected = string.Equals(c.Value, behavior.ActiveBorderColor, StringComparison.OrdinalIgnoreCase)
                })
                .ToList();
            ColorList.ItemsSource = colors;

            ChkMinimizeToTray.IsChecked = behavior.MinimizeToTray;
            ChkCloseToTray.IsChecked = behavior.CloseToTray;
            ChkStartMinimized.IsChecked = behavior.StartMinimized;
            ChkStartWithWindows.IsChecked = StartupService.IsEnabled();

            TxtSettingsPath.Text = _controller.SettingsFilePath;
            _loading = false;
        }

        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            if (_controller == null || _loading) return;

            var behavior = _controller.Settings.Behavior;
            behavior.DropOnOccupied = RadStack.IsChecked == true ? DropBehavior.Stack : DropBehavior.Swap;
            behavior.CompactOnClose = ChkCompact.IsChecked == true;
            behavior.CascadeStacks = ChkCascade.IsChecked == true;
            behavior.ArrangeKeepsPositions = RadArrangePack.IsChecked != true;
            behavior.ShowActiveBorder = ChkBorder.IsChecked == true;
            behavior.MinimizeToTray = ChkMinimizeToTray.IsChecked == true;
            behavior.CloseToTray = ChkCloseToTray.IsChecked == true;
            behavior.StartMinimized = ChkStartMinimized.IsChecked == true;

            BorderOptions.IsEnabled = behavior.ShowActiveBorder;
            _controller.ApplyBehavior();
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (_controller == null || sender is not FrameworkElement { DataContext: ColorChoice choice }) return;

            _controller.Settings.Behavior.ActiveBorderColor = choice.Value;
            _controller.ApplyBehavior();
        }

        private void SldThickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_controller == null || _loading) return;

            var thickness = (int)Math.Round(e.NewValue);
            _controller.Settings.Behavior.ActiveBorderThickness = thickness;
            TxtThickness.Text = $"{thickness} px";
            _controller.ApplyBehavior();
        }

        private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
        {
            if (_controller == null || _loading) return;

            try
            {
                StartupService.SetEnabled(ChkStartWithWindows.IsChecked == true);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or System.Security.SecurityException)
            {
                _controller.SetStatus($"Could not change the startup setting: {ex.Message}");

                _loading = true;
                ChkStartWithWindows.IsChecked = StartupService.IsEnabled();
                _loading = false;
            }
        }

        private void BtnShowGuide_Click(object sender, RoutedEventArgs e) => _controller?.SetGuideShown(true);

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_controller == null) return;

            var folder = Path.GetDirectoryName(_controller.SettingsFilePath);
            if (folder == null) return;

            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
    }
}
