using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using App.Models;
using App.Services;
using App.Windows;

namespace App.Views
{
    /// <summary>
    /// Which windows are tables (client rules), which tables are pinned to a slot, and what is currently recognised
    /// </summary>
    public partial class TablesPage : UserControl
    {
        /// <summary>
        /// One rule in the client list
        /// </summary>
        public sealed class RuleRow
        {
            private readonly Action _changed;

            public RuleRow(WindowFilter rule, Action changed)
            {
                Rule = rule;
                _changed = changed;
            }

            public WindowFilter Rule { get; }

            public string Title => string.IsNullOrEmpty(Rule.Name) ? "Your rule" : Rule.Name;
            /// <summary>
            /// A built-in rule is summarised by its program; the patterns are behind Edit. Other rules are shown in full.
            /// </summary>
            public string Description => PokerClientPresets.Find(Rule.PresetId) != null && !Rule.Customized
                ? $"Built-in rule for {Rule.ProcessName}.exe"
                : Rule.Describe();

            /// <summary>
            /// For a built-in rule, how well established it is; for an edited one, that it no longer updates
            /// </summary>
            public string Status
            {
                get
                {
                    var preset = PokerClientPresets.Find(Rule.PresetId);
                    if (preset == null)
                        return string.Empty;

                    return Rule.Customized ? "Edited by you; no longer follows the built-in rule." : preset.Status;
                }
            }

            public bool HasStatus => Status.Length > 0;

            public bool IsCaution => !Rule.Customized && PokerClientPresets.Find(Rule.PresetId)?.Caution == true;

            public bool Enabled
            {
                get => Rule.Enabled;
                set
                {
                    if (Rule.Enabled == value) return;
                    Rule.Enabled = value;
                    _changed();
                }
            }
        }

        /// <summary>
        /// One pin in the pin list
        /// </summary>
        public sealed record PinRow(TablePin Pin, string SlotText, string Pattern, string Warning)
        {
            public bool HasWarning => Warning.Length > 0;
        }

        /// <summary>
        /// One row of the open-tables list
        /// </summary>
        public sealed record TableRow(string SlotText, string Title, int? SlotNumber, bool IsPinned)
        {
            public string PinButtonText => IsPinned ? "Pinned" : "Pin…";
            public bool CanPin => !IsPinned;
        }

        private AppController? _controller;
        private WindowFilter? _editingRule;
        private List<TableRow> _shownTables = new();
        private List<OtherClientWindow> _shownOthers = new();
        private int _shownSlotCount = -1;

        public TablesPage()
        {
            InitializeComponent();
        }

        public void Initialize(AppController controller)
        {
            _controller = controller;
            controller.ProfileChanged += LoadProfile;
            LoadProfile();
        }

        private MonitorGridConfig Profile => _controller!.Settings.ActiveProfile;

        private void LoadProfile()
        {
            if (_controller == null) return;

            CloseRuleForm();
            ShowRules();
            ShowPins();
            RefreshTables();
        }

        // ----- Rules -----

        private void ShowRules()
        {
            RuleList.ItemsSource = Profile.WindowFilters.Select(r => new RuleRow(r, Commit)).ToList();
            TxtNoFilters.Visibility = Profile.WindowFilters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Clients that are not in the list yet can be added back
            ClientMenu.Items.Clear();
            foreach (var preset in PokerClientPresets.All.Where(p => Profile.WindowFilters.All(r => r.PresetId != p.Id)))
            {
                var item = new MenuItem { Header = preset.Name, Tag = preset };
                item.Click += MenuAddClient_Click;
                ClientMenu.Items.Add(item);
            }
            BtnAddClient.IsEnabled = ClientMenu.Items.Count > 0;
        }

        /// <summary>
        /// Apply and save the profile after a change to rules or pins
        /// </summary>
        private void Commit()
        {
            if (_controller == null) return;

            _controller.ApplyLayout();
            ShowRules();
            ShowPins();
            RefreshTables();
        }

        private void BtnAddClient_Click(object sender, RoutedEventArgs e)
        {
            ClientMenu.PlacementTarget = BtnAddClient;
            ClientMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            ClientMenu.IsOpen = true;
        }

        private void MenuAddClient_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: PokerClientPreset preset })
            {
                Profile.WindowFilters.Add(preset.Rule.Clone());
                Commit();
            }
        }

        private void BtnNewRule_Click(object sender, RoutedEventArgs e) => OpenRuleForm(null);

        private void BtnEditRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: RuleRow row })
                OpenRuleForm(row.Rule);
        }

        private void BtnRemoveRule_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: RuleRow row }) return;

            if (_editingRule == row.Rule)
                CloseRuleForm();

            Profile.WindowFilters.Remove(row.Rule);
            Commit();
        }

        private void OpenRuleForm(WindowFilter? rule)
        {
            _editingRule = rule;

            TxtName.Text = rule?.Name ?? string.Empty;
            TxtProcess.Text = rule?.ProcessName ?? string.Empty;
            TxtClass.Text = rule?.ClassName ?? string.Empty;
            TxtTitle.Text = rule?.TitlePattern ?? string.Empty;
            TxtExclude.Text = rule?.ExcludeTitlePattern ?? string.Empty;
            ChkRegex.IsChecked = rule?.UseRegex ?? false;

            TxtFormTitle.Text = rule == null ? "New rule" : $"Edit rule: {(string.IsNullOrEmpty(rule.Name) ? "your rule" : rule.Name)}";
            BtnSaveRule.Content = rule == null ? "Add rule" : "Save rule";
            TxtFormError.Visibility = Visibility.Collapsed;
            RuleForm.Visibility = Visibility.Visible;
            RuleForm.BringIntoView();
            TxtName.Focus();
        }

        private void CloseRuleForm()
        {
            _editingRule = null;
            RuleForm.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelEdit_Click(object sender, RoutedEventArgs e) => CloseRuleForm();

        private void BtnSaveRule_Click(object sender, RoutedEventArgs e)
        {
            var process = TxtProcess.Text.Trim();
            var className = TxtClass.Text.Trim();
            var title = TxtTitle.Text.Trim();
            var exclude = TxtExclude.Text.Trim();
            var useRegex = ChkRegex.IsChecked == true;

            var error = Validate(process, className, title, exclude, useRegex);
            if (error != null)
            {
                TxtFormError.Text = error;
                TxtFormError.Visibility = Visibility.Visible;
                return;
            }

            var rule = _editingRule ?? new WindowFilter();
            rule.Name = TxtName.Text.Trim();
            rule.ProcessName = process;
            rule.ClassName = className;
            rule.TitlePattern = title;
            rule.ExcludeTitlePattern = exclude;
            rule.UseRegex = useRegex;

            // An edited built-in rule is the user's from now on
            if (rule.PresetId != null)
                rule.Customized = true;

            if (_editingRule == null)
                Profile.WindowFilters.Add(rule);

            CloseRuleForm();
            Commit();
        }

        private static string? Validate(string process, string className, string title, string exclude, bool useRegex)
        {
            if (process.Length == 0 && className.Length == 0 && title.Length == 0)
                return "Enter a program, a window class or a title. A rule with none of these would match nothing.";

            return useRegex ? FirstRegexError(className, title, exclude) : null;
        }

        private static string? FirstRegexError(params string[] patterns)
        {
            foreach (var pattern in patterns.Where(p => p.Length > 0))
            {
                try
                {
                    _ = new Regex(pattern);
                }
                catch (ArgumentException ex)
                {
                    return $"Not a valid regular expression: {ex.Message}";
                }
            }

            return null;
        }

        private void BtnPick_Click(object sender, RoutedEventArgs e)
        {
            var picker = new WindowPickerDialog { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() != true || picker.SelectedWindow == null)
                return;

            var window = picker.SelectedWindow;
            var rule = new WindowFilter
            {
                Name = picker.MatchProgram ? window.ProcessName : string.Empty,
                ProcessName = picker.MatchProgram ? window.ProcessName : string.Empty,
                ClassName = picker.MatchClass ? window.ClassName : string.Empty,
                TitlePattern = picker.MatchTitle ? window.Title : string.Empty
            };

            if (rule.ProcessName.Length == 0 && rule.ClassName.Length == 0 && rule.TitlePattern.Length == 0)
                return;

            Profile.WindowFilters.Add(rule);
            Commit();
        }

        // ----- Pins -----

        private void ShowPins()
        {
            if (_controller == null) return;

            var slotCount = _controller.Grid.SlotCount;
            _shownSlotCount = slotCount;
            PinList.ItemsSource = Profile.Pins
                .Select(p => new PinRow(
                    p,
                    $"Slot {p.SlotNumber}",
                    p.UseRegex ? $"{p.TitlePattern}   (regex)" : p.TitlePattern,
                    p.SlotNumber > slotCount ? $"This layout has only {slotCount} slots, so this pin does nothing here." : string.Empty))
                .ToList();

            NumPinSlot.Maximum = Math.Max(1, slotCount);
        }

        private void BtnAddPin_Click(object sender, RoutedEventArgs e)
        {
            // Leading and trailing spaces can be meaningful ("Name : "), so only reject a blank pattern
            var pattern = TxtPinPattern.Text;
            var useRegex = ChkPinRegex.IsChecked == true;

            var error = string.IsNullOrWhiteSpace(pattern)
                ? "Enter the text the table title must contain."
                : useRegex ? FirstRegexError(pattern) : null;

            if (error != null)
            {
                TxtPinError.Text = error;
                TxtPinError.Visibility = Visibility.Visible;
                return;
            }

            TxtPinError.Visibility = Visibility.Collapsed;
            Profile.Pins.Add(new TablePin { TitlePattern = pattern, UseRegex = useRegex, SlotNumber = NumPinSlot.Value });

            TxtPinPattern.Clear();
            ChkPinRegex.IsChecked = false;
            Commit();
        }

        private void BtnRemovePin_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: PinRow row })
            {
                Profile.Pins.Remove(row.Pin);
                Commit();
            }
        }

        /// <summary>
        /// Prepare a pin for an open table: the stable part of its title and the slot it is in now
        /// </summary>
        private void BtnPinTable_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: TableRow row }) return;

            TxtPinPattern.Text = PokerClientPresets.SuggestPinText(row.Title);
            ChkPinRegex.IsChecked = false;
            NumPinSlot.Value = Math.Clamp(row.SlotNumber ?? 1, NumPinSlot.Minimum, NumPinSlot.Maximum);
            TxtPinError.Visibility = Visibility.Collapsed;

            PinList.BringIntoView();
            TxtPinPattern.Focus();
            TxtPinPattern.CaretIndex = TxtPinPattern.Text.Length;
        }

        // ----- Recognised windows -----

        /// <summary>
        /// Update the lists of open tables and other client windows; called periodically while the page is visible
        /// </summary>
        public void RefreshTables()
        {
            if (_controller == null) return;

            // Pin warnings depend on how many slots the layout has, which changes with the grid settings
            if (_controller.Grid.SlotCount != _shownSlotCount)
                ShowPins();

            var rows = _controller.Grid.GetTables()
                .Select(t => new TableRow(
                    t.SlotNumber != null ? $"Slot {t.SlotNumber}" : "—",
                    t.Title.Length > 0 ? t.Title : "(untitled)",
                    t.SlotNumber,
                    t.IsPinned))
                .ToList();

            // Avoid rebuilding the lists when nothing changed
            if (!rows.SequenceEqual(_shownTables))
            {
                _shownTables = rows;
                TableList.ItemsSource = rows;
                TxtNoTables.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                TxtTablesTitle.Text = rows.Count == 0 ? "Open tables" : $"Open tables ({rows.Count})";
            }

            var others = _controller.Grid.GetOtherClientWindows().ToList();
            if (!others.SequenceEqual(_shownOthers))
            {
                _shownOthers = others;
                OtherList.ItemsSource = others;
                OtherWindowsPanel.Visibility = others.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }
}
