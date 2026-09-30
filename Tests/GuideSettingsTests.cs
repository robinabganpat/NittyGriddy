using System.IO;
using System.Windows.Threading;
using App.Models;
using App.Services;

namespace NittyGriddy.Tests;

/// <summary>
/// Who gets to see the getting-started guide, and that hiding it sticks
/// </summary>
[Collection(WpfCollection.Name)]
public class GuideSettingsTests : IDisposable
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

    [Fact]
    public void A_first_run_shows_the_guide()
    {
        Assert.True(new SettingsService(FilePath).Load().Settings.Guide.Show);
    }

    [Fact]
    public void Settings_saved_before_the_guide_existed_do_not_show_it()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """{ "Version": 1, "GridEnabled": true }""");

        var guide = new SettingsService(FilePath).Load().Settings.Guide;

        Assert.False(guide.Show);
        Assert.False(guide.HotkeyUsed);
    }

    [Fact]
    public void Settings_that_were_reset_because_the_file_was_unreadable_do_not_show_it()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");

        var result = new SettingsService(FilePath).Load();

        Assert.NotNull(result.Warning);
        Assert.False(result.Settings.Guide.Show);
    }

    [Fact]
    public void A_null_guide_in_the_file_is_repaired()
    {
        var settings = SettingsService.Deserialize("""{ "Guide": null }""");

        Assert.NotNull(settings.Guide);
    }

    [Fact]
    public void Hiding_the_guide_is_remembered_and_it_can_be_shown_again()
    {
        WithController(controller =>
        {
            Assert.True(controller.Settings.Guide.Show);

            controller.SetGuideShown(false);

            Assert.False(controller.Settings.Guide.Show);
            Assert.Contains("Behaviour", controller.Status);
        });

        // Disposing the controller writes the settings
        Assert.False(new SettingsService(FilePath).Load().Settings.Guide.Show);

        WithController(controller => controller.SetGuideShown(true));

        Assert.True(new SettingsService(FilePath).Load().Settings.Guide.Show);
    }

    [Fact]
    public void Guide_facts_reflect_the_settings()
    {
        WithController(controller =>
        {
            controller.Settings.Behavior.CloseToTray = true;
            controller.Settings.Hotkeys[HotkeyActions.FocusSlot(2)] = "";

            var facts = controller.GetGuideFacts();

            Assert.False(facts.GridEnabled);
            Assert.True(facts.CloseToTray);
            Assert.False(facts.HotkeyUsed);
            Assert.Equal("Alt+1", facts.GoToSlot1.Gesture);
            Assert.False(facts.GoToSlot2.Works);
            // Unibet ships switched off, so it is not among the clients the guide names
            Assert.Equal(new[] { "GGPoker", "HC Online (iPoker)", "CoinPoker" }, facts.WatchedClients);
        });
    }
}
