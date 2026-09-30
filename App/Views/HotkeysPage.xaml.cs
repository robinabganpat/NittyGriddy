using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using App.Models;
using App.Services;

namespace App.Views
{
    /// <summary>
    /// One bindable action in the hotkey list
    /// </summary>
    public sealed class HotkeyRow : INotifyPropertyChanged
    {
        private readonly AppController _controller;
        private readonly HotkeyActionInfo _action;

        public event PropertyChangedEventHandler? PropertyChanged;

        public HotkeyRow(AppController controller, HotkeyActionInfo action)
        {
            _controller = controller;
            _action = action;
        }

        public string Label => _action.Label;

        public string Gesture
        {
            get => _controller.Settings.Hotkeys.TryGetValue(_action.Id, out var text) ? text : string.Empty;
            set
            {
                if (value != Gesture)
                    _controller.SetHotkey(_action.Id, value);
            }
        }

        public bool IsSet => Gesture.Length > 0;

        /// <summary>
        /// Why the hotkey is not working, or a caution about what it takes over
        /// </summary>
        public string Note
        {
            get
            {
                if (_controller.HotkeyFailures.TryGetValue(_action.Id, out var failure))
                    return failure;

                if (HotkeyGesture.TryParse(Gesture, out var gesture) && gesture.IsBareKey)
                    return _action.AlwaysActive
                        ? "No Ctrl, Alt or Win: this key stops working in every other program"
                        : "No Ctrl, Alt or Win: this key stops working in other programs while the grid is on";

                return string.Empty;
            }
        }

        public void Refresh()
        {
            foreach (var property in new[] { nameof(Gesture), nameof(IsSet), nameof(Note) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    public sealed record HotkeyGroup(string Name, IReadOnlyList<HotkeyRow> Rows);

    /// <summary>
    /// Global hotkey bindings
    /// </summary>
    public partial class HotkeysPage : UserControl
    {
        private AppController? _controller;
        private List<HotkeyRow> _rows = new();

        public HotkeysPage()
        {
            InitializeComponent();
        }

        public void Initialize(AppController controller)
        {
            _controller = controller;

            _rows = HotkeyActions.All.Select(a => new HotkeyRow(controller, a)).ToList();
            GroupList.ItemsSource = HotkeyActions.All
                .Zip(_rows, (action, row) => (action.Group, row))
                .GroupBy(x => x.Group)
                .Select(g => new HotkeyGroup(g.Key, g.Select(x => x.row).ToList()))
                .ToList();

            // Registration results change when bindings change and when the grid is switched on or off
            controller.StateChanged += () =>
            {
                foreach (var row in _rows)
                    row.Refresh();
            };
        }

        private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            // Release the hotkeys so the combination being typed reaches this window instead of triggering its action
            _controller?.Hotkeys.Suspend();
        }

        private void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _controller?.Hotkeys.Resume();
            _controller?.ApplyHotkeys();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: HotkeyRow row })
                row.Gesture = string.Empty;
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _controller?.ResetHotkeys();
        }
    }
}
