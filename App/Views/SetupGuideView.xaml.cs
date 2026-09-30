using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using App.Services;

namespace App.Views
{
    /// <summary>
    /// The getting-started guide shown above the pages on a first run. It shows the steps of <see cref="SetupGuide"/>
    /// and follows the application's state, so a step is ticked by doing it, wherever that is done.
    /// </summary>
    public partial class SetupGuideView : UserControl
    {
        /// <summary>
        /// One step in the row of steps. Kept for the life of the view and updated in place,
        /// so a step only animates when it is actually completed.
        /// </summary>
        public sealed class StepRow : INotifyPropertyChanged
        {
            private GuideStep? _step;
            private bool _isSelected;

            public StepRow(int number)
            {
                Number = number;
            }

            public int Number { get; }
            public GuideStepId Id => _step?.Id ?? default;
            public string Title => _step?.Title ?? string.Empty;
            public string Status => _step?.Status ?? string.Empty;
            public bool Done => _step?.Done ?? false;
            public bool IsSelected => _isSelected;

            /// <summary>
            /// What a screen reader says for the step, since its state is otherwise only drawn
            /// </summary>
            public string AccessibleName => $"Step {Number}: {Title}. {(Done ? "Done" : Status)}";

            public event PropertyChangedEventHandler? PropertyChanged;

            public void Update(GuideStep step, bool isSelected)
            {
                if (step == _step && isSelected == _isSelected)
                    return;

                _step = step;
                _isSelected = isSelected;
                // An empty name refreshes every binding of the row
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
            }
        }

        // Tab order of the pages in the main window
        private const int LayoutPageIndex = 0;
        private const int TablesPageIndex = 1;
        private const int HotkeysPageIndex = 2;

        private readonly List<StepRow> _rows = new();
        private AppController? _controller;
        private IReadOnlyList<GuideStep> _steps = Array.Empty<GuideStep>();
        private GuideStepId _current;
        private GuideStepId? _picked;

        /// <summary>
        /// The guide asks for one of the pages to be opened (index of the tab)
        /// </summary>
        public event Action<int>? PageRequested;

        /// <summary>
        /// A behaviour option was changed from the guide; the Behaviour page must reload
        /// </summary>
        public event Action? BehaviorChanged;

        public SetupGuideView()
        {
            InitializeComponent();
        }

        public void Initialize(AppController controller)
        {
            _controller = controller;
            controller.StateChanged += Refresh;
            Refresh();
        }

        /// <summary>
        /// Bring the guide in line with the application; called on every state change and periodically
        /// while the window is visible, because tables open and close outside our control
        /// </summary>
        public void Refresh()
        {
            if (_controller == null) return;

            var shown = _controller.Settings.Guide.Show;
            Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (!shown)
                return;

            _steps = SetupGuide.Build(_controller.GetGuideFacts());

            // The step the user picked stays open until the guide moves on to another step
            var current = SetupGuide.Current(_steps);
            if (current != _current)
            {
                _current = current;
                _picked = null;
            }

            var selected = _picked ?? _current;

            if (_rows.Count == 0)
            {
                _rows.AddRange(_steps.Select((step, index) => new StepRow(index + 1)));
                StepList.ItemsSource = _rows;
            }

            for (var i = 0; i < _steps.Count; i++)
                _rows[i].Update(_steps[i], _steps[i].Id == selected);

            ShowStep(_steps.First(s => s.Id == selected));
        }

        private void ShowStep(GuideStep step)
        {
            TxtDetail.Text = step.Detail;

            TxtNote.Text = step.Note;
            TxtNote.Visibility = step.Note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            TxtNote.SetResourceReference(ForegroundProperty, step.Caution ? "WarningBrush" : "TextFillColorSecondaryBrush");

            var isLastStep = step.Id == GuideStepId.KeepRunning;
            ChkCloseToTray.Visibility = isLastStep ? Visibility.Visible : Visibility.Collapsed;
            if (isLastStep)
                ChkCloseToTray.IsChecked = _controller!.Settings.Behavior.CloseToTray;

            BtnPrimary.Content = step.ActionLabel;
            BtnPrimary.Visibility = step.Action != GuideAction.None ? Visibility.Visible : Visibility.Collapsed;
            BtnSecondary.Content = step.SecondaryLabel;
            BtnSecondary.Visibility = step.SecondaryAction != GuideAction.None ? Visibility.Visible : Visibility.Collapsed;
        }

        private GuideStep? SelectedStep => _steps.FirstOrDefault(s => s.Id == (_picked ?? _current));

        private void Step_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: StepRow row }) return;

            _picked = row.Id;
            Refresh();
        }

        private void BtnPrimary_Click(object sender, RoutedEventArgs e) => Run(SelectedStep?.Action ?? GuideAction.None);

        private void BtnSecondary_Click(object sender, RoutedEventArgs e) => Run(SelectedStep?.SecondaryAction ?? GuideAction.None);

        private void Run(GuideAction action)
        {
            if (_controller == null) return;

            switch (action)
            {
                case GuideAction.OpenLayoutPage:
                    PageRequested?.Invoke(LayoutPageIndex);
                    break;
                case GuideAction.OpenTablesPage:
                    PageRequested?.Invoke(TablesPageIndex);
                    break;
                case GuideAction.OpenHotkeysPage:
                    PageRequested?.Invoke(HotkeysPageIndex);
                    break;
                case GuideAction.TurnGridOn:
                    _controller.StartGrid();
                    break;
                case GuideAction.Arrange:
                    _controller.AutoArrange();
                    break;
                case GuideAction.Finish:
                    _controller.SetGuideShown(false);
                    break;
            }
        }

        private void BtnHide_Click(object sender, RoutedEventArgs e) => _controller?.SetGuideShown(false);

        private void ChkCloseToTray_Click(object sender, RoutedEventArgs e)
        {
            if (_controller == null) return;

            _controller.Settings.Behavior.CloseToTray = ChkCloseToTray.IsChecked == true;
            _controller.ApplyBehavior();
            BehaviorChanged?.Invoke();
            Refresh();
        }
    }
}
