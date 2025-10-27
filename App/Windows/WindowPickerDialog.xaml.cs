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

        public WindowPickerDialog()
        {
            InitializeComponent();
            _windowEnumService = new WindowEnumerationService();
            LoadWindows();

            WindowsDataGrid.SelectionChanged += WindowsDataGrid_SelectionChanged;
        }

        private void LoadWindows()
        {
            var windows = _windowEnumService.GetAllWindows()
                .OrderBy(w => w.Title)
                .ToList();

            WindowsDataGrid.ItemsSource = windows;
        }

        private void WindowsDataGrid_SelectionChanged(object? sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (WindowsDataGrid.SelectedItem is WindowEnumerationService.WindowInfo window)
            {
                TxtSelectedTitle.Text = $"Title: {window.Title}";
                TxtSelectedClass.Text = $"Class: {window.ClassName}";
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
            if (WindowsDataGrid.SelectedItem is WindowEnumerationService.WindowInfo window)
            {
                SelectedWindow = window;
                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select a window first.", "No Window Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
