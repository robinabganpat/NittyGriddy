using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace App.Models
{
    /// <summary>
    /// A layout profile: grid layouts across all monitors plus the windows they apply to
    /// </summary>
    public class MonitorGridConfig
    {
        public string ConfigName { get; set; }
        public Dictionary<string, GridLayout> MonitorLayouts { get; set; }

        // Window filter settings
        public List<WindowFilter> WindowFilters { get; set; }

        /// <summary>
        /// Tables that always go to a fixed slot
        /// </summary>
        public List<TablePin> Pins { get; set; } = new();

        // Snapping behavior settings
        public bool MaintainAspectRatio { get; set; }

        // Legacy properties for backward compatibility (will be migrated on load)
        public List<string>? TargetWindowClasses { get; set; }
        public List<string>? TargetWindowTitles { get; set; }

        public MonitorGridConfig(string name)
        {
            ConfigName = name;
            MonitorLayouts = new Dictionary<string, GridLayout>();
            // A new profile recognises the supported poker clients out of the box
            WindowFilters = PokerClientPresets.CreateAll();
            MaintainAspectRatio = true; // Default to maintaining aspect ratio
        }

        /// <summary>
        /// Used when reading a stored profile: starts empty so a filter the user removed is not re-added
        /// </summary>
        [JsonConstructor]
        private MonitorGridConfig()
        {
            ConfigName = string.Empty;
            MonitorLayouts = new Dictionary<string, GridLayout>();
            WindowFilters = new List<WindowFilter>();
            MaintainAspectRatio = true;
        }

        /// <summary>
        /// Repair what a hand-edited or older file may contain: missing collections, null entries, legacy filter lists.
        /// Preset rules the user has not edited are refreshed from the built-in definitions.
        /// </summary>
        public void Normalize()
        {
            MonitorLayouts ??= new Dictionary<string, GridLayout>();
            foreach (var key in MonitorLayouts.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList())
                MonitorLayouts.Remove(key);
            foreach (var layout in MonitorLayouts.Values.Where(l => l.AutoFitMaxTables < 1))
                layout.AutoFitMaxTables = 9;

            MigrateLegacyFilters();
            WindowFilters.RemoveAll(f => f == null);
            foreach (var filter in WindowFilters)
            {
                filter.Name ??= string.Empty;
                filter.ProcessName ??= string.Empty;
                filter.ClassName ??= string.Empty;
                filter.TitlePattern ??= string.Empty;
                filter.ExcludeTitlePattern ??= string.Empty;
            }
            PokerClientPresets.Refresh(WindowFilters);

            Pins ??= new List<TablePin>();
            Pins.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.TitlePattern) || p.SlotNumber < 1);
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
