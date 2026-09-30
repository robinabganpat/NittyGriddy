using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace App.Models
{
    /// <summary>
    /// Everything the application persists between runs
    /// </summary>
    public class AppSettings
    {
        public const int CurrentVersion = 1;
        public const string DefaultProfileName = "Default";

        public int Version { get; set; } = CurrentVersion;

        /// <summary>
        /// Named layout profiles; there is always at least one
        /// </summary>
        public List<MonitorGridConfig> Profiles { get; set; } = new() { new MonitorGridConfig(DefaultProfileName) };

        public string ActiveProfileName { get; set; } = DefaultProfileName;

        /// <summary>
        /// Whether the grid was enabled when the application last ran
        /// </summary>
        public bool GridEnabled { get; set; }

        /// <summary>
        /// Hotkey action id to gesture text; empty text means the action is unbound
        /// </summary>
        public Dictionary<string, string> Hotkeys { get; set; } = HotkeyActions.Defaults();

        public BehaviorSettings Behavior { get; set; } = new();

        public GuideSettings Guide { get; set; } = new();

        public WindowPlacement? MainWindow { get; set; }

        [JsonIgnore]
        public MonitorGridConfig ActiveProfile
        {
            get => Profiles.FirstOrDefault(p => p.ConfigName == ActiveProfileName) ?? Profiles[0];
        }

        /// <summary>
        /// Repair anything a hand-edited or older file may have left inconsistent
        /// </summary>
        public void Normalize()
        {
            Profiles ??= new List<MonitorGridConfig>();
            Profiles.RemoveAll(p => p == null);

            if (Version > CurrentVersion)
                Version = CurrentVersion;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in Profiles)
            {
                profile.Normalize();
                profile.ConfigName = UniqueName(string.IsNullOrWhiteSpace(profile.ConfigName) ? "Profile" : profile.ConfigName.Trim(), names);
                names.Add(profile.ConfigName);
            }

            if (Profiles.Count == 0)
                Profiles.Add(new MonitorGridConfig(DefaultProfileName));

            if (Profiles.All(p => p.ConfigName != ActiveProfileName))
            {
                // Prefer a name that differs only in case over falling back to the first profile
                ActiveProfileName = Profiles.FirstOrDefault(p => string.Equals(p.ConfigName, ActiveProfileName, StringComparison.OrdinalIgnoreCase))?.ConfigName
                                    ?? Profiles[0].ConfigName;
            }

            Hotkeys ??= new Dictionary<string, string>();
            foreach (var action in HotkeyActions.All)
            {
                // A missing entry gets the default; an empty one was cleared by the user and stays cleared
                if (!Hotkeys.TryGetValue(action.Id, out var gesture) || gesture == null)
                    Hotkeys[action.Id] = action.DefaultGesture;
            }

            Behavior ??= new BehaviorSettings();
            Guide ??= new GuideSettings();
        }

        /// <summary>
        /// Return the name, or the name with a numeric suffix if it is already taken
        /// </summary>
        public static string UniqueName(string name, IEnumerable<string> taken)
        {
            // Names that differ only in case count as the same name
            var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
            if (!used.Contains(name))
                return name;

            for (var i = 2; ; i++)
            {
                var candidate = $"{name} ({i})";
                if (!used.Contains(candidate))
                    return candidate;
            }
        }
    }
}
