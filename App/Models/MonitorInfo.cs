using System;
using System.Windows;

namespace App.Models
{
    /// <summary>
    /// Information about a physical monitor/display
    /// </summary>
    public class MonitorInfo
    {
        public string DeviceName { get; set; }
        public Rect Bounds { get; set; }
        public Rect WorkArea { get; set; }
        public bool IsPrimary { get; set; }
        public IntPtr Handle { get; set; }

        public MonitorInfo()
        {
            DeviceName = string.Empty;
        }

        public override string ToString()
        {
            return $"{DeviceName} ({Bounds.Width}x{Bounds.Height})" + (IsPrimary ? " [Primary]" : "");
        }
    }
}
