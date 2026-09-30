using System.Windows;

namespace App.Windows
{
    /// <summary>
    /// Small modal dialog that asks for a line of text or for confirmation
    /// </summary>
    public partial class PromptDialog : Window
    {
        private PromptDialog(Window owner, string title, string message, string okText)
        {
            InitializeComponent();
            ThemeMode = ThemeMode.Dark;

            Owner = owner;
            Title = title;
            TxtMessage.Text = message;
            BtnOk.Content = okText;
        }

        /// <summary>
        /// Ask for a line of text. Returns null when cancelled or left empty.
        /// </summary>
        public static string? AskText(Window owner, string title, string message, string initialText, string okText = "OK")
        {
            var dialog = new PromptDialog(owner, title, message, okText);
            dialog.TxtInput.Text = initialText;
            dialog.TxtInput.SelectAll();
            dialog.Loaded += (s, e) => dialog.TxtInput.Focus();

            if (dialog.ShowDialog() != true)
                return null;

            var text = dialog.TxtInput.Text.Trim();
            return text.Length > 0 ? text : null;
        }

        /// <summary>
        /// Ask for confirmation of an action
        /// </summary>
        public static bool Confirm(Window owner, string title, string message, string okText)
        {
            var dialog = new PromptDialog(owner, title, message, okText);
            dialog.TxtInput.Visibility = Visibility.Collapsed;

            return dialog.ShowDialog() == true;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
