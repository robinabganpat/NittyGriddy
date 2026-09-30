using System.Linq;
using System.Windows;
using App.Services;

namespace App.Windows
{
    /// <summary>
    /// Dialog for selecting a window to add to filters
    /// </summary>
    public partial class WindowPickerDialog : Window
    {
        private readonly WindowEnumerationService _windowEnumService;

        public WindowEnumerationService.WindowInfo? SelectedWindow { get; private set; }

        /// <summary>
        /// Whether the new rule should match the selected window's program
        /// </summary>
        public bool MatchProgram => ChkMatchProgram.IsChecked == true;

        /// <summary>
        /// Whether the new rule should match the selected window's class
        /// </summary>
        public bool MatchClass => ChkMatchClass.IsChecked == true;

        /// <summary>
        /// Whether the new rule should match the selected window's title
        /// </summary>
        public bool MatchTitle => ChkMatchTitle.IsChecked == true;

        public WindowPickerDialog()
        {
            InitializeComponent();
            ThemeMode = ThemeMode.Dark;

            _windowEnumService = new WindowEnumerationService();
            LoadWindows();

            WindowsDataGrid.SelectionChanged += WindowsDataGrid_SelectionChanged;
        }

        private void LoadWindows()
        {
            // Windows without a title are overlays and helper windows, never tables
            var processNames = new ProcessNameCache();
            var windows = _windowEnumService.GetAllWindows()
                .Where(w => !string.IsNullOrWhiteSpace(w.Title))
                .OrderBy(w => w.Title)
                .ToList();

            foreach (var window in windows)
                window.ProcessName = processNames.GetName(window.ProcessId);

            WindowsDataGrid.ItemsSource = windows;
        }

        private void WindowsDataGrid_SelectionChanged(object? sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (WindowsDataGrid.SelectedItem is WindowEnumerationService.WindowInfo window)
            {
                TxtSelectedProgram.Text = $"Match the program:  {window.ProcessName}";
                TxtSelectedClass.Text = $"Match the window class:  {window.ClassName}";
                TxtSelectedTitle.Text = $"Match the window title:  {window.Title}";
                BtnOk.IsEnabled = true;
            }
            else
            {
                BtnOk.IsEnabled = false;
            }
        }

        private void WindowsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Double-click to select
            if (WindowsDataGrid.SelectedItem is WindowEnumerationService.WindowInfo)
            {
                BtnOk_Click(sender, e);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadWindows();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            if (WindowsDataGrid.SelectedItem is WindowEnumerationService.WindowInfo window && (MatchProgram || MatchClass || MatchTitle))
            {
                SelectedWindow = window;
                DialogResult = true;
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
