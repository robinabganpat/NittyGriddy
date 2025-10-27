using System.Collections.Generic;

namespace App.Models
{
    /// <summary>
    /// Configuration for grid layouts across all monitors
    /// </summary>
    public class MonitorGridConfig
    {
        public string ConfigName { get; set; }
        public Dictionary<string, GridLayout> MonitorLayouts { get; set; }
        public bool IsEnabled { get; set; }

        // Window filter settings
        public List<WindowFilter> WindowFilters { get; set; }

        // Snapping behavior settings
        public bool MaintainAspectRatio { get; set; }

        // Legacy properties for backward compatibility (will be migrated on load)
        public List<string>? TargetWindowClasses { get; set; }
        public List<string>? TargetWindowTitles { get; set; }

        public MonitorGridConfig(string name)
        {
            ConfigName = name;
            MonitorLayouts = new Dictionary<string, GridLayout>();
            WindowFilters = new List<WindowFilter>
            {
                // Default filter for poker windows (common poker client)
                new WindowFilter("ApolloRuntimeContentWindow", "")
            };
            IsEnabled = true;
            MaintainAspectRatio = true; // Default to maintaining aspect ratio
        }

        /// <summary>
        /// Migrate legacy class/title lists to new WindowFilter format
        /// </summary>
        public void MigrateLegacyFilters()
        {
            if (WindowFilters == null || WindowFilters.Count == 0)
            {
                WindowFilters = new List<WindowFilter>();

                // If we have legacy data, migrate it
                if (TargetWindowClasses != null && TargetWindowClasses.Count > 0)
                {
                    foreach (var className in TargetWindowClasses)
                    {
                        WindowFilters.Add(new WindowFilter(className, ""));
                    }
                }

                if (TargetWindowTitles != null && TargetWindowTitles.Count > 0)
                {
                    foreach (var title in TargetWindowTitles)
                    {
                        WindowFilters.Add(new WindowFilter("", title));
                    }
                }
            }

            // Clear legacy properties after migration
            TargetWindowClasses = null;
            TargetWindowTitles = null;
        }

        /// <summary>
        /// Get or create a grid layout for a specific monitor
        /// </summary>
        public GridLayout GetOrCreateLayoutForMonitor(string deviceName)
        {
            if (!MonitorLayouts.ContainsKey(deviceName))
            {
                MonitorLayouts[deviceName] = new GridLayout($"Grid for {deviceName}");
            }
            return MonitorLayouts[deviceName];
        }
    }
}
