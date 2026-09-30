using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using App.Models;
using Microsoft.Win32;

namespace App.Services
{
    /// <summary>
    /// Owns settings, the grid manager and hotkeys, and is the single place where a change is applied and persisted.
    /// The UI and the tray both act through it.
    /// </summary>
    public sealed class AppController : IDisposable
    {
        private readonly SettingsService _settingsService;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _layoutTimer;
        private readonly Dispatcher _dispatcher;
        private readonly bool _saveAllowed;
        private readonly Dictionary<string, string> _invalidHotkeys = new();

        public AppSettings Settings { get; }
        public GridManagerService Grid { get; }
        public HotkeyService Hotkeys { get; }

        /// <summary>
        /// Set when stored settings could not be read at startup
        /// </summary>
        public string? StartupWarning { get; }

        public string SettingsFilePath => _settingsService.FilePath;

        /// <summary>
        /// Last status message for the user
        /// </summary>
        public string Status { get; private set; } = string.Empty;

        /// <summary>
        /// Grid state, table occupancy or status text changed
        /// </summary>
        public event Action? StateChanged;

        /// <summary>
        /// A different profile became active, or the profile list changed: views must reload from Settings
        /// </summary>
        public event Action? ProfileChanged;

        public AppController(SettingsService settingsService, Dispatcher dispatcher)
        {
            _settingsService = settingsService;

            var loaded = settingsService.Load();
            Settings = loaded.Settings;
            StartupWarning = loaded.Warning;
            _saveAllowed = loaded.SaveAllowed;

            Grid = new GridManagerService(dispatcher);
            Grid.TablesChanged += () => StateChanged?.Invoke();

            Hotkeys = new HotkeyService();
            Hotkeys.HotkeyPressed += Execute;

            _saveTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _saveTimer.Tick += (s, e) => Flush();

            _layoutTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _layoutTimer.Tick += (s, e) => ApplyLayout();

            _dispatcher = dispatcher;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        /// <summary>
        /// A monitor was added, removed or changed resolution: recalculate slots and let views reload their display list
        /// </summary>
        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                // Tables on a display that went away are left where Windows puts them
                Grid.LoadConfiguration(Settings.ActiveProfile, replaceDisplaced: false);
                ProfileChanged?.Invoke();
                StateChanged?.Invoke();
            }));
        }

        /// <summary>
        /// Apply the loaded settings: layout, behaviour, grid state and hotkeys
        /// </summary>
        public void Start()
        {
            Grid.ApplyBehavior(Settings.Behavior);
            Grid.LoadConfiguration(Settings.ActiveProfile);

            if (Settings.GridEnabled)
                Grid.Enable();

            ApplyHotkeys();

            if (StartupWarning != null)
                SetStatus(StartupWarning);
        }

        // ----- Grid -----

        public void SetGridEnabled(bool enabled)
        {
            if (enabled == Grid.IsEnabled)
                return;

            if (enabled)
                Grid.Enable();
            else
                Grid.Disable();

            Settings.GridEnabled = enabled;
            ApplyHotkeys();
            RequestSave();

            if (!enabled)
            {
                SetStatus("Grid off");
                return;
            }

            // Turning the grid on moves nothing by itself; show the slots, and say so if tables are waiting
            Grid.FlashOverlays();
            var waiting = Grid.GetTables().Count(t => t.SlotNumber == null);
            SetStatus(waiting switch
            {
                0 => "Grid on",
                1 => "Grid on. 1 table is not in a slot yet: Arrange tables puts it in one",
                _ => $"Grid on. {waiting} tables are not in a slot yet: Arrange tables puts them in slots"
            });
        }

        /// <summary>
        /// Turn the grid on and put every open table in a slot
        /// </summary>
        public void StartGrid()
        {
            SetGridEnabled(true);
            AutoArrange();
        }

        public void ToggleGrid() => SetGridEnabled(!Grid.IsEnabled);

        public void TogglePreview()
        {
            Grid.TogglePreview();
            StateChanged?.Invoke();
        }

        /// <summary>
        /// True while an edit made through <see cref="ApplyLayoutSoon"/> has not reached the screen yet
        /// </summary>
        public bool IsLayoutApplyPending => _layoutTimer.IsEnabled;

        /// <summary>
        /// Apply the active profile once changes stop arriving. Dragging a slider produces dozens of changes;
        /// applying each one would rebuild the grid and move every table each time.
        /// </summary>
        public void ApplyLayoutSoon()
        {
            _layoutTimer.Stop();
            _layoutTimer.Start();
        }

        /// <summary>
        /// Re-apply the active profile after its layouts or filters were edited
        /// </summary>
        public void ApplyLayout()
        {
            _layoutTimer.Stop();
            Grid.LoadConfiguration(Settings.ActiveProfile);
            RequestSave();
            StateChanged?.Invoke();
        }

        public void ApplyBehavior()
        {
            Grid.ApplyBehavior(Settings.Behavior);
            RequestSave();
        }

        /// <summary>
        /// Raise every table above other windows and give focus to the one used last
        /// </summary>
        public void BringAllTablesToFront()
        {
            var count = Grid.BringAllTablesToFront();
            SetStatus(count switch
            {
                0 => "No tables open",
                1 => "Brought 1 table to the front",
                _ => $"Brought {count} tables to the front"
            });
        }

        public void AutoArrange()
        {
            if (!Grid.IsEnabled)
            {
                SetStatus("Turn the grid on to arrange tables");
                return;
            }

            var arranged = Grid.AutoArrangeWindows();
            SetStatus(arranged == 1 ? "Arranged 1 table" : $"Arranged {arranged} tables");
        }

        // ----- Hotkeys -----

        /// <summary>
        /// Actions whose hotkey is not working, with the reason
        /// </summary>
        public IReadOnlyDictionary<string, string> HotkeyFailures
        {
            get
            {
                var failures = new Dictionary<string, string>(_invalidHotkeys);
                foreach (var (actionId, reason) in Hotkeys.Failures)
                    failures[actionId] = reason;
                return failures;
            }
        }

        /// <summary>
        /// Register the configured hotkeys. Table hotkeys are only held while the grid is on,
        /// so they do not take keys from other programs when not playing.
        /// </summary>
        public void ApplyHotkeys()
        {
            _invalidHotkeys.Clear();
            var bindings = new List<KeyValuePair<string, HotkeyGesture>>();

            foreach (var action in HotkeyActions.All)
            {
                if (!Settings.Hotkeys.TryGetValue(action.Id, out var text) || string.IsNullOrWhiteSpace(text))
                    continue;

                if (!HotkeyGesture.TryParse(text, out var gesture))
                {
                    _invalidHotkeys[action.Id] = "Not a valid key combination";
                    continue;
                }

                if (action.AlwaysActive || Grid.IsEnabled)
                    bindings.Add(new KeyValuePair<string, HotkeyGesture>(action.Id, gesture));
            }

            Hotkeys.Apply(bindings);
            StateChanged?.Invoke();
        }

        public void SetHotkey(string actionId, string gestureText)
        {
            Settings.Hotkeys[actionId] = gestureText;
            ApplyHotkeys();
            RequestSave();
        }

        public void ResetHotkeys()
        {
            Settings.Hotkeys = HotkeyActions.Defaults();
            ApplyHotkeys();
            RequestSave();
        }

        /// <summary>
        /// Run a hotkey action
        /// </summary>
        public void Execute(string actionId)
        {
            switch (actionId)
            {
                case HotkeyActions.ToggleGrid:
                    ToggleGrid();
                    return;
                case HotkeyActions.AutoArrange:
                    AutoArrange();
                    return;
                case HotkeyActions.SnapActive:
                    Grid.SnapActiveTableToNearestCell();
                    return;
                case HotkeyActions.NextTable:
                    NoteTableHotkey(Grid.FocusNext(+1));
                    return;
                case HotkeyActions.PreviousTable:
                    NoteTableHotkey(Grid.FocusNext(-1));
                    return;
                case HotkeyActions.AllTablesToFront:
                    BringAllTablesToFront();
                    return;
                case HotkeyActions.NextProfile:
                    NextProfile();
                    return;
            }

            if (HotkeyActions.TryGetSlot(actionId, out var kind, out var slotNumber))
            {
                if (kind == SlotActionKind.Focus)
                    NoteTableHotkey(Grid.FocusSlot(slotNumber));
                else
                    Grid.MoveActiveToSlot(slotNumber);
            }
        }

        // ----- Getting-started guide -----

        /// <summary>
        /// The state the getting-started guide is derived from
        /// </summary>
        public GuideFacts GetGuideFacts()
        {
            var tables = Grid.GetTables();
            var failures = HotkeyFailures;

            GuideHotkey Key(string actionId)
            {
                var gesture = Settings.Hotkeys.TryGetValue(actionId, out var text) ? text?.Trim() ?? string.Empty : string.Empty;
                return new GuideHotkey(gesture, failures.TryGetValue(actionId, out var problem) ? problem : null);
            }

            return new GuideFacts
            {
                TableCount = tables.Count,
                TablesOutsideGrid = tables.Count(t => t.SlotNumber == null),
                WatchedClients = Settings.ActiveProfile.WindowFilters
                    .Where(r => r.Enabled)
                    .Select(r => r.Name.Length > 0 ? r.Name : r.ProcessName)
                    .Where(name => name.Length > 0)
                    .Distinct()
                    .ToList(),
                // Only worth looking for when no table was found: it explains why
                ClientsWithoutTables = tables.Count > 0
                    ? Array.Empty<string>()
                    : Grid.GetOtherClientWindows().Select(w => w.Client).Distinct().ToList(),
                GridEnabled = Grid.IsEnabled,
                SlotCount = Grid.SlotCount,
                GoToSlot1 = Key(HotkeyActions.FocusSlot(1)),
                GoToSlot2 = Key(HotkeyActions.FocusSlot(2)),
                NextTable = Key(HotkeyActions.NextTable),
                OccupiedSlotHotkeys = tables
                    .Where(t => t.SlotNumber is >= 1 and <= HotkeyActions.SlotHotkeyCount)
                    .Select(t => t.SlotNumber!.Value)
                    .Distinct()
                    .OrderBy(slot => slot)
                    .Select(slot => (slot, Key(HotkeyActions.FocusSlot(slot))))
                    .ToList(),
                HotkeyUsed = Settings.Guide.HotkeyUsed,
                CloseToTray = Settings.Behavior.CloseToTray
            };
        }

        /// <summary>
        /// Show or hide the getting-started guide
        /// </summary>
        public void SetGuideShown(bool shown)
        {
            if (Settings.Guide.Show == shown)
                return;

            Settings.Guide.Show = shown;
            RequestSave();
            SetStatus(shown ? "Guide shown" : "Guide hidden. Behaviour has a button to show it again");
        }

        /// <summary>
        /// Remember that a hotkey brought a table to the front, which is the step of the guide the user cannot tick themselves
        /// </summary>
        private void NoteTableHotkey(bool broughtTableForward)
        {
            if (!broughtTableForward || Settings.Guide.HotkeyUsed)
                return;

            Settings.Guide.HotkeyUsed = true;
            RequestSave();
            StateChanged?.Invoke();
        }

        // ----- Profiles -----

        public IReadOnlyList<string> ProfileNames => Settings.Profiles.Select(p => p.ConfigName).ToList();

        public void SwitchProfile(string name)
        {
            if (name == Settings.ActiveProfileName || Settings.Profiles.All(p => p.ConfigName != name))
                return;

            Settings.ActiveProfileName = name;
            Grid.LoadConfiguration(Settings.ActiveProfile);
            Grid.FlashOverlays();
            RequestSave();
            ProfileChanged?.Invoke();
            SetStatus($"Profile: {name}");
        }

        public void NextProfile()
        {
            var names = ProfileNames;
            if (names.Count < 2)
                return;

            var index = names.ToList().IndexOf(Settings.ActiveProfileName);
            SwitchProfile(names[(index + 1) % names.Count]);
        }

        /// <summary>
        /// Add a profile and switch to it. A duplicate copies everything from the active profile;
        /// a new profile keeps only its window filters and starts with default grids.
        /// </summary>
        public string AddProfile(string name, bool duplicateLayouts)
        {
            var profile = SettingsService.DeserializeProfile(SettingsService.SerializeProfile(Settings.ActiveProfile));
            if (!duplicateLayouts)
                profile.MonitorLayouts.Clear();

            return AddAndSwitch(profile, name);
        }

        public string RenameActiveProfile(string name)
        {
            name = name.Trim();
            if (name.Length == 0 || name == Settings.ActiveProfileName)
                return Settings.ActiveProfileName;

            var taken = ProfileNames.Where(n => n != Settings.ActiveProfileName).ToList();
            var profile = Settings.ActiveProfile;
            profile.ConfigName = AppSettings.UniqueName(name, taken);
            Settings.ActiveProfileName = profile.ConfigName;

            RequestSave();
            ProfileChanged?.Invoke();
            return profile.ConfigName;
        }

        /// <summary>
        /// Delete the active profile. The last remaining profile cannot be deleted.
        /// </summary>
        public bool DeleteActiveProfile()
        {
            if (Settings.Profiles.Count < 2)
                return false;

            var index = Settings.Profiles.IndexOf(Settings.ActiveProfile);
            Settings.Profiles.RemoveAt(index);
            Settings.ActiveProfileName = Settings.Profiles[Math.Min(index, Settings.Profiles.Count - 1)].ConfigName;

            Grid.LoadConfiguration(Settings.ActiveProfile);
            RequestSave();
            ProfileChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Import a profile file, including files written by earlier versions. Throws if the file cannot be read.
        /// </summary>
        public string ImportProfile(string path)
        {
            var profile = SettingsService.DeserializeProfile(File.ReadAllText(path));
            var name = profile.ConfigName == AppSettings.DefaultProfileName || profile.ConfigName == "Imported"
                ? Path.GetFileNameWithoutExtension(path)
                : profile.ConfigName;

            return AddAndSwitch(profile, name);
        }

        public void ExportActiveProfile(string path)
        {
            File.WriteAllText(path, SettingsService.SerializeProfile(Settings.ActiveProfile));
            SetStatus($"Exported profile to {Path.GetFileName(path)}");
        }

        private string AddAndSwitch(MonitorGridConfig profile, string name)
        {
            name = name.Trim();
            profile.ConfigName = AppSettings.UniqueName(name.Length == 0 ? "Profile" : name, ProfileNames.ToList());
            Settings.Profiles.Add(profile);
            SwitchProfile(profile.ConfigName);
            return profile.ConfigName;
        }

        // ----- Persistence -----

        /// <summary>
        /// Save soon. Rapid changes (dragging a slider) collapse into one write.
        /// </summary>
        public void RequestSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>
        /// Write settings now
        /// </summary>
        public void Flush()
        {
            _saveTimer.Stop();

            // The stored file could not be read at startup; writing now would replace it with defaults
            if (!_saveAllowed)
                return;

            try
            {
                _settingsService.Save(Settings);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus($"Settings could not be saved: {ex.Message}");
            }
        }

        public void SetStatus(string message)
        {
            Status = message;
            StateChanged?.Invoke();
        }

        public void Dispose()
        {
            // SystemEvents is static; an un-removed handler would keep this object alive
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            Flush();
            Hotkeys.Dispose();
            Grid.Dispose();
        }
    }
}
