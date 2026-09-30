using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace App.Controls
{
    /// <summary>
    /// Whole-number input with step buttons, limited to a range
    /// </summary>
    public partial class NumberBox : UserControl
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(int), typeof(NumberBox),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(int), typeof(NumberBox), new PropertyMetadata(1));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(int), typeof(NumberBox), new PropertyMetadata(100));

        public int Value
        {
            get => (int)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public int Minimum
        {
            get => (int)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        public int Maximum
        {
            get => (int)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public NumberBox()
        {
            InitializeComponent();
            Input.Text = Value.ToString();
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = (NumberBox)d;
            box.Input.Text = box.Value.ToString();
        }

        private void Step(int delta)
        {
            Value = Math.Clamp(Value + delta, Minimum, Maximum);
        }

        /// <summary>
        /// Take what was typed if it is a number, clamped to the range; otherwise put the current value back
        /// </summary>
        private void Commit()
        {
            if (int.TryParse(Input.Text, out var typed))
                Value = Math.Clamp(typed, Minimum, Maximum);

            Input.Text = Value.ToString();
        }

        private void BtnDown_Click(object sender, RoutedEventArgs e) => Step(-1);

        private void BtnUp_Click(object sender, RoutedEventArgs e) => Step(+1);

        private void Input_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Up:
                    Commit();
                    Step(+1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    Commit();
                    Step(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Commit();
                    Input.SelectAll();
                    e.Handled = true;
                    break;
            }
        }

        private void Input_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

        private void Input_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Input.IsKeyboardFocusWithin)
                return;

            Step(e.Delta > 0 ? +1 : -1);
            e.Handled = true;
        }
    }
}
