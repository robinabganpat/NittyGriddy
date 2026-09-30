using System;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace App.Services
{
    /// <summary>
    /// Notification-area icon with a menu for the most common actions
    /// </summary>
    public sealed class TrayService : IDisposable
    {
        private readonly AppController _controller;
        private readonly Forms.NotifyIcon _icon;
        private readonly Action _openMainWindow;
        private readonly Action _exit;

        public TrayService(AppController controller, Action openMainWindow, Action exit)
        {
            _controller = controller;
            _openMainWindow = openMainWindow;
            _exit = exit;

            _icon = new Forms.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "NittyGriddy",
                Visible = true,
                ContextMenuStrip = new Forms.ContextMenuStrip()
            };

            _icon.DoubleClick += (s, e) => _openMainWindow();
            _icon.ContextMenuStrip.Opening += (s, e) =>
            {
                BuildMenu();

                // Windows Forms cancels the opening of a menu that was empty when the click arrived;
                // it has items now
                e.Cancel = false;
            };

            _controller.StateChanged += UpdateTooltip;
            UpdateTooltip();
        }

        private static Icon LoadIcon()
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico"));
            if (resource == null)
                return SystemIcons.Application;

            using var stream = resource.Stream;
            return new Icon(stream, Forms.SystemInformation.SmallIconSize);
        }

        private void UpdateTooltip()
        {
            _icon.Text = _controller.Grid.IsEnabled ? "NittyGriddy - grid on" : "NittyGriddy - grid off";
        }

        /// <summary>
        /// The menu is rebuilt each time it opens so it reflects the current grid state and profiles
        /// </summary>
        private void BuildMenu()
        {
            var menu = _icon.ContextMenuStrip!;
            menu.Items.Clear();

            menu.Items.Add("Open NittyGriddy", null, (s, e) => _openMainWindow());
            menu.Items.Add(new Forms.ToolStripSeparator());

            var gridItem = new Forms.ToolStripMenuItem("Grid on") { Checked = _controller.Grid.IsEnabled };
            gridItem.Click += (s, e) => _controller.ToggleGrid();
            menu.Items.Add(gridItem);

            var frontItem = new Forms.ToolStripMenuItem("Tables to front");
            frontItem.Click += (s, e) => _controller.BringAllTablesToFront();
            menu.Items.Add(frontItem);

            var arrangeItem = new Forms.ToolStripMenuItem("Arrange tables") { Enabled = _controller.Grid.IsEnabled };
            arrangeItem.Click += (s, e) => _controller.AutoArrange();
            menu.Items.Add(arrangeItem);

            var profilesItem = new Forms.ToolStripMenuItem("Profile");
            foreach (var name in _controller.ProfileNames)
            {
                var profileName = name;
                var item = new Forms.ToolStripMenuItem(profileName) { Checked = profileName == _controller.Settings.ActiveProfileName };
                item.Click += (s, e) => _controller.SwitchProfile(profileName);
                profilesItem.DropDownItems.Add(item);
            }
            menu.Items.Add(profilesItem);

            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => _exit());
        }

        public void Dispose()
        {
            _controller.StateChanged -= UpdateTooltip;
            _icon.Visible = false;
            _icon.ContextMenuStrip?.Dispose();
            _icon.Dispose();
        }
    }
}
