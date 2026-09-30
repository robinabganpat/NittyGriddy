using System.IO;
using System.Windows.Threading;
using App.Models;
using App.Services;

namespace NittyGriddy.Tests;

/// <summary>
/// Profile management through the controller, against a temporary settings file.
/// The grid stays off and no hotkeys are applied, so nothing appears on screen and nothing is registered system-wide.
/// </summary>
[Collection(WpfCollection.Name)]
public class AppControllerProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "NittyGriddyTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private void WithController(Action<AppController> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var controller = new AppController(new SettingsService(FilePath), Dispatcher.CurrentDispatcher);
                test(controller);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private AppSettings Reload() => new SettingsService(FilePath).Load().Settings;

    [Fact]
    public void New_profile_keeps_table_rules_but_not_layouts_and_becomes_active()
    {
        WithController(controller =>
        {
            var active = controller.Settings.ActiveProfile;
            active.WindowFilters.Clear();
            active.WindowFilters.Add(new WindowFilter("MyClient", ""));
            active.GetOrCreateLayoutForMonitor("M1").Rows = 5;

            var changed = 0;
            controller.ProfileChanged += () => changed++;

            var name = controller.AddProfile("Nine tables", duplicateLayouts: false);

            Assert.Equal("Nine tables", name);
            Assert.Equal("Nine tables", controller.Settings.ActiveProfileName);
            Assert.Equal(1, changed);
            Assert.Equal("MyClient", Assert.Single(controller.Settings.ActiveProfile.WindowFilters).ClassName);
            Assert.False(controller.Settings.ActiveProfile.MonitorLayouts.ContainsKey("M1"));
        });

        // Disposing the controller writes the settings
        var saved = Reload();
        Assert.Equal(2, saved.Profiles.Count);
        Assert.Equal("Nine tables", saved.ActiveProfileName);
    }

    [Fact]
    public void Duplicate_copies_layouts_independently()
    {
        WithController(controller =>
        {
            controller.Settings.ActiveProfile.GetOrCreateLayoutForMonitor("M1").Rows = 5;

            controller.AddProfile("Copy", duplicateLayouts: true);
            controller.Settings.ActiveProfile.MonitorLayouts["M1"].Rows = 9;

            Assert.Equal(5, controller.Settings.Profiles[0].MonitorLayouts["M1"].Rows);
            Assert.Equal(9, controller.Settings.Profiles[1].MonitorLayouts["M1"].Rows);
        });
    }

    [Fact]
    public void Names_are_made_unique()
    {
        WithController(controller =>
        {
            Assert.Equal("Default (2)", controller.AddProfile("Default", duplicateLayouts: true));
            Assert.Equal("Default (3)", controller.AddProfile("Default", duplicateLayouts: true));

            // Renaming to its own name is a no-op
            Assert.Equal("Default (3)", controller.RenameActiveProfile("Default (3)"));

            // Renaming onto another profile's name takes the first free suffix
            controller.RenameActiveProfile("Tournaments");
            Assert.Equal("Default (3)", controller.RenameActiveProfile("Default"));
            Assert.Equal("Default (3)", controller.Settings.ActiveProfileName);
            Assert.Equal(new[] { "Default", "Default (2)", "Default (3)" }, controller.ProfileNames);
        });
    }

    [Fact]
    public void The_last_profile_cannot_be_deleted()
    {
        WithController(controller =>
        {
            Assert.False(controller.DeleteActiveProfile());
            Assert.Single(controller.Settings.Profiles);

            controller.AddProfile("Second", duplicateLayouts: true);
            Assert.True(controller.DeleteActiveProfile());

            Assert.Equal("Default", controller.Settings.ActiveProfileName);
            Assert.Single(controller.Settings.Profiles);
        });
    }

    [Fact]
    public void Next_profile_cycles_and_is_a_no_op_with_one_profile()
    {
        WithController(controller =>
        {
            controller.NextProfile();
            Assert.Equal("Default", controller.Settings.ActiveProfileName);

            controller.AddProfile("B", duplicateLayouts: true);
            controller.NextProfile();
            Assert.Equal("Default", controller.Settings.ActiveProfileName);
            controller.NextProfile();
            Assert.Equal("B", controller.Settings.ActiveProfileName);
        });
    }

    [Fact]
    public void Import_reads_a_version_1_file_and_names_it_after_the_file()
    {
        Directory.CreateDirectory(_dir);
        var legacyPath = Path.Combine(_dir, "six-max.json");
        File.WriteAllText(legacyPath, """
        {
          "ConfigName": "Default",
          "MonitorLayouts": { "M1": { "Name": "Grid", "Mode": 1, "Rows": 2, "Columns": 3, "Cells": [], "NumberOfWindows": 6, "DisplayBounds": "0,0,100,100", "CellMargins": "0,0,0,0", "CellSpacing": 4.0 } },
          "IsEnabled": true,
          "WindowFilters": [ { "ClassName": "Qt5", "TitlePattern": "", "UseRegex": false } ],
          "MaintainAspectRatio": false
        }
        """);

        WithController(controller =>
        {
            var name = controller.ImportProfile(legacyPath);

            Assert.Equal("six-max", name);
            var profile = controller.Settings.ActiveProfile;
            Assert.Equal(GridLayoutMode.NWindowOptimized, profile.MonitorLayouts["M1"].Mode);
            Assert.Equal(4.0, profile.MonitorLayouts["M1"].CellSpacing);
            Assert.False(profile.MaintainAspectRatio);
            Assert.Equal("Qt5", Assert.Single(profile.WindowFilters).ClassName);
        });
    }

    [Fact]
    public void Export_then_import_round_trips_a_profile()
    {
        Directory.CreateDirectory(_dir);
        var exportPath = Path.Combine(_dir, "exported.json");

        WithController(controller =>
        {
            controller.RenameActiveProfile("Cash");
            controller.Settings.ActiveProfile.GetOrCreateLayoutForMonitor("M1").Columns = 7;
            controller.ExportActiveProfile(exportPath);

            var name = controller.ImportProfile(exportPath);

            Assert.Equal("Cash (2)", name);
            Assert.Equal(7, controller.Settings.ActiveProfile.MonitorLayouts["M1"].Columns);
        });
    }
}
