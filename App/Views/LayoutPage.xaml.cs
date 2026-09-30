using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using App.Models;
using App.Services;

namespace App.Views
{
    /// <summary>
    /// Grid settings per display for the active profile
    /// </summary>
    public partial class LayoutPage : UserControl
    {
        private readonly ObservableCollection<MonitorConfigViewModel> _monitorConfigs = new();
        private readonly MonitorService _monitorService = new();
        private AppController? _controller;
        private bool _loading;

        public LayoutPage()
        {
            InitializeComponent();
            MonitorList.ItemsSource = _monitorConfigs;
        }

        public void Initialize(AppController controller)
        {
            _controller = controller;
            controller.ProfileChanged += LoadProfile;
            controller.StateChanged += OnStateChanged;
            LoadProfile();
        }

        /// <summary>
        /// Rebuild the display cards from the active profile
        /// </summary>
        private void LoadProfile()
        {
            if (_controller == null) return;

            _loading = true;

            var profile = _controller.Settings.ActiveProfile;
            _monitorConfigs.Clear();

            // Same order as the slot numbering: primary display first, then by device name
            var monitors = _monitorService.GetAllMonitors()
                .OrderByDescending(m => m.IsPrimary)
                .ThenBy(m => m.DeviceName, System.StringComparer.Ordinal);

            foreach (var monitor in monitors)
            {
                var layout = profile.GetOrCreateLayoutForMonitor(monitor.DeviceName);
                _monitorConfigs.Add(new MonitorConfigViewModel(monitor, layout, OnMonitorConfigChanged));
            }

            ChkMaintainAspectRatio.IsChecked = profile.MaintainAspectRatio;

            _loading = false;
            OnStateChanged();
        }

        private void OnMonitorConfigChanged()
        {
            if (_controller == null || _loading) return;

            // The picture in this window follows every step of a slider...
            var nextSlotNumber = 1;
            foreach (var monitorConfig in _monitorConfigs)
                nextSlotNumber += monitorConfig.UpdatePreview(nextSlotNumber);

            // ...while the screen, and the tables on it, are rearranged once, when the value has settled
            _controller.ApplyLayoutSoon();
        }

        private void OnStateChanged()
        {
            // Keep showing the edited preview until the edit has been applied
            if (_controller == null || _controller.IsLayoutApplyPending)
                return;

            foreach (var monitorConfig in _monitorConfigs)
                monitorConfig.NotifyGridApplied();
        }

        private void ChkMaintainAspectRatio_Changed(object sender, RoutedEventArgs e)
        {
            if (_controller == null || _loading) return;

            _controller.Settings.ActiveProfile.MaintainAspectRatio = ChkMaintainAspectRatio.IsChecked == true;
            _controller.ApplyLayout();
        }
    }
}
