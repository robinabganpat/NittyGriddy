using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using App.Services;

namespace App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class PokerApp : Application
    {
        private const string InstanceMutexName = "NittyGriddy.SingleInstance";
        private const string ShowEventName = "NittyGriddy.ShowMainWindow";

        private Mutex? _instanceMutex;
        private EventWaitHandle? _showEvent;
        private RegisteredWaitHandle? _showEventRegistration;
        private AppController? _controller;
        private TrayService? _tray;
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // A second launch only brings the running instance to the front
            _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
            if (!isFirstInstance)
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }

                _instanceMutex.Dispose();
                _instanceMutex = null;
                Shutdown();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _showEventRegistration = ThreadPool.RegisterWaitForSingleObject(
                _showEvent,
                (state, timedOut) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);

            DispatcherUnhandledException += OnUnhandledException;

            // --settings <path> uses a different settings file (portable use, testing)
            var settingsPath = ArgumentValue(e.Args, "--settings");
            _controller = new AppController(new SettingsService(settingsPath), Dispatcher);

            _mainWindow = new MainWindow(_controller);
            MainWindow = _mainWindow;

            _tray = new TrayService(_controller, ShowMainWindow, ExitApplication);
            _controller.Start();

            var startMinimized = _controller.Settings.Behavior.StartMinimized
                                 || e.Args.Contains(StartupService.MinimizedArgument, StringComparer.OrdinalIgnoreCase);

            if (!startMinimized)
            {
                _mainWindow.Show();
            }
            else if (!_controller.Settings.Behavior.MinimizeToTray)
            {
                _mainWindow.WindowState = WindowState.Minimized;
                _mainWindow.Show();
            }
        }

        private static string? ArgumentValue(string[] args, string name)
        {
            var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private void ShowMainWindow()
        {
            if (_mainWindow == null)
                return;

            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        public void ExitApplication()
        {
            _mainWindow?.PrepareForExit();
            Shutdown();
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsService.DefaultPath)!;
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path.Combine(directory, "error.log"), $"{DateTime.Now:u}\n{e.Exception}\n\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Nothing more can be done if the log cannot be written
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _showEventRegistration?.Unregister(null);
            _showEvent?.Dispose();
            _tray?.Dispose();
            _controller?.Dispose();
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();

            base.OnExit(e);
        }
    }
}
