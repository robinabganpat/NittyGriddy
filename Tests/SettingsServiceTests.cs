using System.IO;
using App.Models;
using App.Services;

namespace NittyGriddy.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NittyGriddyTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_yields_defaults_without_warning()
    {
        var result = new SettingsService(FilePath).Load();

        Assert.Null(result.Warning);
        var profile = Assert.Single(result.Settings.Profiles);
        Assert.Equal(result.Settings.ActiveProfileName, profile.ConfigName);
        Assert.Same(profile, result.Settings.ActiveProfile);
        Assert.NotEmpty(result.Settings.Hotkeys);
    }

    [Fact]
    public void Round_trip_preserves_layouts_filters_hotkeys_and_behaviour()
    {
        var service = new SettingsService(FilePath);
        var settings = service.Load().Settings;
        var layout = settings.ActiveProfile.GetOrCreateLayoutForMonitor(@"\\.\DISPLAY1");
        layout.Mode = GridLayoutMode.NWindowOptimized;
        layout.NumberOfWindows = 7;
        layout.CellSpacing = 0;
        settings.ActiveProfile.WindowFilters.Add(new WindowFilter("Qt5", @"\| Ante", useRegex: true));
        settings.ActiveProfile.MaintainAspectRatio = false;
        settings.Hotkeys[HotkeyActions.NextTable] = "F8";
        settings.Hotkeys[HotkeyActions.PreviousTable] = "";
        settings.GridEnabled = true;
        settings.Behavior.CompactOnClose = true;
        settings.Behavior.DropOnOccupied = DropBehavior.Stack;

        service.Save(settings);
        var loaded = new SettingsService(FilePath).Load().Settings;

        var loadedLayout = loaded.ActiveProfile.MonitorLayouts[@"\\.\DISPLAY1"];
        Assert.Equal(GridLayoutMode.NWindowOptimized, loadedLayout.Mode);
        Assert.Equal(7, loadedLayout.NumberOfWindows);
        Assert.Equal(0, loadedLayout.CellSpacing);
        Assert.Contains(loaded.ActiveProfile.WindowFilters, f => f.ClassName == "Qt5" && f.UseRegex);
        Assert.False(loaded.ActiveProfile.MaintainAspectRatio);
        Assert.Equal("F8", loaded.Hotkeys[HotkeyActions.NextTable]);
        Assert.Equal("", loaded.Hotkeys[HotkeyActions.PreviousTable]);
        Assert.True(loaded.GridEnabled);
        Assert.True(loaded.Behavior.CompactOnClose);
        Assert.Equal(DropBehavior.Stack, loaded.Behavior.DropOnOccupied);
    }

    [Fact]
    public void Removed_default_filter_stays_removed_after_reload()
    {
        var service = new SettingsService(FilePath);
        var settings = service.Load().Settings;
        settings.ActiveProfile.WindowFilters.Clear();
        settings.ActiveProfile.WindowFilters.Add(new WindowFilter("OnlyThis", ""));

        service.Save(settings);
        service.Save(new SettingsService(FilePath).Load().Settings);
        var loaded = new SettingsService(FilePath).Load().Settings;

        var filter = Assert.Single(loaded.ActiveProfile.WindowFilters);
        Assert.Equal("OnlyThis", filter.ClassName);
    }

    [Fact]
    public void Runtime_state_is_not_written()
    {
        var settings = new SettingsService(FilePath).Load().Settings;
        var layout = settings.ActiveProfile.GetOrCreateLayoutForMonitor("M");
        layout.Mode = GridLayoutMode.FixedGrid;
        layout.Rows = 2;
        layout.Columns = 2;
        layout.DisplayBounds = new System.Windows.Rect(0, 0, 800, 600);
        layout.CalculateCells();
        layout.Cells[0].OccupiedWindows.Add(new IntPtr(1234));

        var json = SettingsService.Serialize(settings);

        Assert.DoesNotContain("OccupiedWindows", json);
        Assert.DoesNotContain("DisplayBounds", json);
        Assert.DoesNotContain("\"Cells\"", json);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_keeps_a_backup()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ \"Profiles\": [ { \"ConfigName\": ");

        var result = new SettingsService(FilePath).Load();

        Assert.NotNull(result.Warning);
        Assert.Single(result.Settings.Profiles);
        var backup = Assert.Single(Directory.GetFiles(_dir, "settings.corrupt-*.json"));
        Assert.Contains("ConfigName", File.ReadAllText(backup));
    }

    [Fact]
    public void Empty_file_is_treated_as_corrupt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "");

        var result = new SettingsService(FilePath).Load();

        Assert.NotNull(result.Warning);
        Assert.Single(result.Settings.Profiles);
    }

    [Fact]
    public void Legacy_config_file_imports_as_profile()
    {
        const string legacy = """
        {
          "ConfigName": "Default",
          "MonitorLayouts": {
            "\\\\.\\DISPLAY2": {
              "Name": "Grid", "Mode": 0, "Rows": 3, "Columns": 4,
              "Cells": [ { "Id": 0, "Bounds": "0,0,10,10", "Row": 0, "Column": 0, "OccupiedWindows": [] } ],
              "NumberOfWindows": 6, "DisplayBounds": "0,0,2560,1392",
              "CellMargins": "5,5,5,5", "CellSpacing": 10.0
            }
          },
          "IsEnabled": true,
          "WindowFilters": [ { "ClassName": "ApolloRuntimeContentWindow", "TitlePattern": "- Table", "UseRegex": true } ],
          "MaintainAspectRatio": true,
          "TargetWindowClasses": null,
          "TargetWindowTitles": null
        }
        """;

        var profile = SettingsService.DeserializeProfile(legacy);

        Assert.Equal("Default", profile.ConfigName);
        var layout = profile.MonitorLayouts[@"\\.\DISPLAY2"];
        Assert.Equal(3, layout.Rows);
        Assert.Equal(4, layout.Columns);
        var filter = Assert.Single(profile.WindowFilters);
        Assert.Equal("- Table", filter.TitlePattern);
    }

    [Fact]
    public void Legacy_class_and_title_lists_are_migrated_to_filters()
    {
        const string legacy = """
        { "ConfigName": "Old", "MonitorLayouts": {}, "TargetWindowClasses": ["ClassA"], "TargetWindowTitles": ["Poker"] }
        """;

        var profile = SettingsService.DeserializeProfile(legacy);

        Assert.Equal(2, profile.WindowFilters.Count);
        Assert.Contains(profile.WindowFilters, f => f.ClassName == "ClassA");
        Assert.Contains(profile.WindowFilters, f => f.TitlePattern == "Poker");
    }

    [Fact]
    public void Normalize_repairs_empty_profiles_and_dangling_active_name()
    {
        var settings = new AppSettings { ActiveProfileName = "Gone" };
        settings.Profiles.Clear();

        settings.Normalize();

        var profile = Assert.Single(settings.Profiles);
        Assert.Equal(profile.ConfigName, settings.ActiveProfileName);
    }

    [Fact]
    public void Normalize_adds_defaults_for_unknown_actions_but_keeps_cleared_ones()
    {
        var settings = new AppSettings();
        settings.Hotkeys.Clear();
        settings.Hotkeys[HotkeyActions.ToggleGrid] = "";

        settings.Normalize();

        Assert.Equal("", settings.Hotkeys[HotkeyActions.ToggleGrid]);
        Assert.Equal("Alt+1", settings.Hotkeys[HotkeyActions.FocusSlot(1)]);
    }

    [Fact]
    public void A_locked_settings_file_is_left_untouched_and_saving_is_disallowed()
    {
        var service = new SettingsService(FilePath);
        var settings = service.Load().Settings;
        settings.GridEnabled = true;
        service.Save(settings);
        var original = File.ReadAllText(FilePath);

        LoadResult result;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = new SettingsService(FilePath).Load();
        }

        Assert.NotNull(result.Warning);
        Assert.False(result.SaveAllowed);
        Assert.Equal(original, File.ReadAllText(FilePath));
        Assert.Empty(Directory.GetFiles(_dir, "settings.corrupt-*.json"));
    }

    [Fact]
    public void Corrupt_file_that_was_set_aside_allows_saving()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "not json");

        Assert.True(new SettingsService(FilePath).Load().SaveAllowed);
    }

    [Fact]
    public void Null_entries_in_a_hand_edited_file_are_dropped()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
        {
          "Profiles": [
            null,
            { "ConfigName": "P", "MonitorLayouts": { "M1": null, "M2": { "Name": "g", "Rows": 1, "Columns": 1 } },
              "WindowFilters": [ null, { "ClassName": null, "TitlePattern": "Table" } ],
              "Pins": [ null, { "TitlePattern": "", "SlotNumber": 1 }, { "TitlePattern": "Sunday", "SlotNumber": 0 }, { "TitlePattern": "Sunday", "SlotNumber": 2 } ] }
          ],
          "ActiveProfileName": "P",
          "Hotkeys": { "ToggleGrid": null },
          "Behavior": null
        }
        """);

        var result = new SettingsService(FilePath).Load();

        Assert.Null(result.Warning);
        var profile = Assert.Single(result.Settings.Profiles);
        Assert.Equal(new[] { "M2" }, profile.MonitorLayouts.Keys);
        var filter = Assert.Single(profile.WindowFilters);
        Assert.Equal("", filter.ClassName);
        Assert.True(filter.Matches("anything", "Table 1"));
        Assert.Equal(2, Assert.Single(profile.Pins).SlotNumber);
        Assert.NotNull(result.Settings.Behavior);
        Assert.Equal("Ctrl+Alt+G", result.Settings.Hotkeys[HotkeyActions.ToggleGrid]);
    }

    [Fact]
    public void Profile_names_differing_only_in_case_are_told_apart_and_the_active_one_is_kept()
    {
        var settings = new AppSettings();
        settings.Profiles.Add(new MonitorGridConfig("default"));
        settings.ActiveProfileName = "default";

        settings.Normalize();

        Assert.Equal(new[] { "Default", "default (2)" }, settings.Profiles.Select(p => p.ConfigName));
        Assert.Equal("Default", settings.ActiveProfileName);
        Assert.Equal("cash (2)", AppSettings.UniqueName("cash", new[] { "Cash" }));
    }

    [Fact]
    public void Pins_rule_details_and_remaining_options_round_trip()
    {
        var service = new SettingsService(FilePath);
        var settings = service.Load().Settings;
        var profile = settings.ActiveProfile;
        profile.Pins.Add(new TablePin { TitlePattern = "Sunday Million : ", SlotNumber = 3 });
        profile.Pins.Add(new TablePin { TitlePattern = "^Daily", SlotNumber = 1, UseRegex = true });
        profile.WindowFilters.Clear();
        profile.WindowFilters.Add(new WindowFilter("Cls", "Table", useRegex: true)
        {
            Name = "My client", ProcessName = "client", ExcludeTitlePattern = "Lobby", Enabled = false
        });
        var layout = profile.GetOrCreateLayoutForMonitor("M");
        layout.Rows = 4;
        layout.Columns = 5;
        layout.CellMargins = new System.Windows.Thickness(7);
        settings.Behavior.ActiveBorderColor = "#FFFF4D4D";
        settings.Behavior.ActiveBorderThickness = 9;
        settings.MainWindow = new WindowPlacement { Left = -100, Top = 20, Width = 1000, Height = 700, Maximized = true };

        service.Save(settings);
        var loaded = new SettingsService(FilePath).Load().Settings;

        Assert.Equal(new[] { ("Sunday Million : ", 3, false), ("^Daily", 1, true) },
            loaded.ActiveProfile.Pins.Select(p => (p.TitlePattern, p.SlotNumber, p.UseRegex)));
        var rule = Assert.Single(loaded.ActiveProfile.WindowFilters);
        Assert.Equal(("My client", "client", "Cls", "Table", "Lobby", true, false),
            (rule.Name, rule.ProcessName, rule.ClassName, rule.TitlePattern, rule.ExcludeTitlePattern, rule.UseRegex, rule.Enabled));
        var loadedLayout = loaded.ActiveProfile.MonitorLayouts["M"];
        Assert.Equal((4, 5, 7.0), (loadedLayout.Rows, loadedLayout.Columns, loadedLayout.CellMargins.Left));
        Assert.Equal(("#FFFF4D4D", 9), (loaded.Behavior.ActiveBorderColor, loaded.Behavior.ActiveBorderThickness));
        Assert.Equal((-100.0, 20.0, 1000.0, 700.0, true),
            (loaded.MainWindow!.Left, loaded.MainWindow.Top, loaded.MainWindow.Width, loaded.MainWindow.Height, loaded.MainWindow.Maximized));
    }

    [Fact]
    public void A_preset_rule_switched_off_stays_off_and_an_edited_one_keeps_the_edit()
    {
        var service = new SettingsService(FilePath);
        var settings = service.Load().Settings;
        var rules = settings.ActiveProfile.WindowFilters;
        var gg = rules.Single(r => r.PresetId == PokerClientPresets.GGPoker);
        gg.Enabled = false;
        var coin = rules.Single(r => r.PresetId == PokerClientPresets.CoinPoker);
        coin.TitlePattern = "my own pattern";
        coin.Customized = true;

        service.Save(settings);
        var loaded = new SettingsService(FilePath).Load().Settings.ActiveProfile.WindowFilters;

        Assert.Equal(PokerClientPresets.All.Count, loaded.Count);
        Assert.False(loaded.Single(r => r.PresetId == PokerClientPresets.GGPoker).Enabled);
        Assert.Equal("my own pattern", loaded.Single(r => r.PresetId == PokerClientPresets.CoinPoker).TitlePattern);
    }

    [Fact]
    public void Settings_from_before_the_arrange_and_cascade_options_get_their_defaults()
    {
        var settings = SettingsService.Deserialize("""{ "Behavior": { "CompactOnClose": true } }""");

        Assert.True(settings.Behavior.ArrangeKeepsPositions);
        Assert.False(settings.Behavior.CascadeStacks);
        Assert.True(settings.Behavior.CompactOnClose);
    }

    [Fact]
    public void Save_leaves_no_temp_file_and_overwrites()
    {
        var service = new SettingsService(FilePath);
        var settings = service.Load().Settings;

        service.Save(settings);
        settings.GridEnabled = true;
        service.Save(settings);

        Assert.Equal(new[] { FilePath }, Directory.GetFiles(_dir));
        Assert.True(new SettingsService(FilePath).Load().Settings.GridEnabled);
    }
}
