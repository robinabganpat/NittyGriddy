using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using App.Models;

namespace App.Services
{
    /// <summary>
    /// Service for detecting and managing multiple monitors
    /// </summary>
    public class MonitorService
    {
        private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int Size;
            public RECT Monitor;
            public RECT WorkArea;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        private const int MONITORINFOF_PRIMARY = 1;

        private readonly List<MonitorInfo> _monitors = new();

        /// <summary>
        /// Get all available monitors
        /// </summary>
        public List<MonitorInfo> GetAllMonitors()
        {
            _monitors.Clear();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumProc, IntPtr.Zero);
            return new List<MonitorInfo>(_monitors);
        }

        private bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
        {
            var mi = new MONITORINFOEX();
            mi.Size = Marshal.SizeOf(mi);

            if (GetMonitorInfo(hMonitor, ref mi))
            {
                var monitorInfo = new MonitorInfo
                {
                    Handle = hMonitor,
                    DeviceName = mi.DeviceName,
                    Bounds = new Rect(
                        mi.Monitor.Left,
                        mi.Monitor.Top,
                        mi.Monitor.Right - mi.Monitor.Left,
                        mi.Monitor.Bottom - mi.Monitor.Top),
                    WorkArea = new Rect(
                        mi.WorkArea.Left,
                        mi.WorkArea.Top,
                        mi.WorkArea.Right - mi.WorkArea.Left,
                        mi.WorkArea.Bottom - mi.WorkArea.Top),
                    IsPrimary = (mi.Flags & MONITORINFOF_PRIMARY) != 0
                };

                _monitors.Add(monitorInfo);
            }

            return true; // Continue enumeration
        }

        /// <summary>
        /// Get the monitor that contains the specified point
        /// </summary>
        public MonitorInfo? GetMonitorFromPoint(Point point)
        {
            var monitors = GetAllMonitors();
            foreach (var monitor in monitors)
            {
                if (monitor.Bounds.Contains(point))
                    return monitor;
            }
            return null;
        }

        /// <summary>
        /// Get the monitor that contains the specified window
        /// </summary>
        public MonitorInfo? GetMonitorFromWindow(IntPtr hWnd)
        {
            RECT rect;
            if (GetWindowRect(hWnd, out rect))
            {
                var center = new Point(
                    (rect.Left + rect.Right) / 2.0,
                    (rect.Top + rect.Bottom) / 2.0);
                return GetMonitorFromPoint(center);
            }
            return null;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    }
}
