using System;
using Microsoft.Win32;

namespace App.Services
{
    /// <summary>
    /// Controls whether the application starts with Windows, through the current user's Run registry key
    /// </summary>
    public static class StartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "NittyGriddy";

        public const string MinimizedArgument = "--minimized";

        public static bool IsEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }

        public static void SetEnabled(bool enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                var executable = Environment.ProcessPath
                                 ?? throw new InvalidOperationException("The application path is not available.");
                key.SetValue(ValueName, $"\"{executable}\" {MinimizedArgument}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }
}
