using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace App.Services
{
    /// <summary>
    /// Service for enumerating all windows and getting their properties
    /// </summary>
    public class WindowEnumerationService
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public class WindowInfo
        {
            public IntPtr Handle { get; set; }
            public string Title { get; set; } = string.Empty;
            public string ClassName { get; set; } = string.Empty;
            public int ProcessId { get; set; }

            public override string ToString()
            {
                if (!string.IsNullOrEmpty(Title))
                    return $"{Title} ({ClassName})";
                return $"<No Title> ({ClassName})";
            }
        }

        /// <summary>
        /// Get all visible windows with titles
        /// </summary>
        public List<WindowInfo> GetAllWindows()
        {
            var windows = new List<WindowInfo>();

            EnumWindows((hWnd, lParam) =>
            {
                // Only include visible windows with titles
                if (!IsWindowVisible(hWnd))
                    return true;

                var title = new StringBuilder(256);
                GetWindowText(hWnd, title, title.Capacity);

                var className = new StringBuilder(256);
                GetClassName(hWnd, className, className.Capacity);

                GetWindowThreadProcessId(hWnd, out var processId);

                // Skip windows without titles and class names
                var titleStr = title.ToString();
                var classNameStr = className.ToString();

                if (!string.IsNullOrEmpty(titleStr) || !string.IsNullOrEmpty(classNameStr))
                {
                    windows.Add(new WindowInfo
                    {
                        Handle = hWnd,
                        Title = titleStr,
                        ClassName = classNameStr,
                        ProcessId = processId
                    });
                }

                return true; // Continue enumeration
            }, IntPtr.Zero);

            return windows;
        }

        /// <summary>
        /// Get information about a specific window
        /// </summary>
        public WindowInfo? GetWindowInfo(IntPtr hWnd)
        {
            if (!IsWindowVisible(hWnd))
                return null;

            var title = new StringBuilder(256);
            GetWindowText(hWnd, title, title.Capacity);

            var className = new StringBuilder(256);
            GetClassName(hWnd, className, className.Capacity);

            GetWindowThreadProcessId(hWnd, out var processId);

            return new WindowInfo
            {
                Handle = hWnd,
                Title = title.ToString(),
                ClassName = className.ToString(),
                ProcessId = processId
            };
        }
    }
}
