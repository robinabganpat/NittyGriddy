using System.IO;
using System.Windows;
using System.Windows.Threading;
using App.Models;
using App.Services;
using App.Windows;

namespace NittyGriddy.Tests;

/// <summary>
/// Editing a layout must not rebuild what is on screen for every intermediate value
/// </summary>
[Collection(WpfCollection.Name)]
public class LayoutEditingTests
{
    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
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

    private static MonitorInfo Monitor(string name, Rect bounds) =>
        new() { DeviceName = name, Bounds = bounds, WorkArea = bounds, IsPrimary = true };

    [Fact]
    public void Building_cells_for_a_preview_leaves_the_live_cells_alone()
    {
        var layout = new GridLayout("g") { Mode = GridLayoutMode.FixedGrid, Rows = 2, Columns = 3, DisplayBounds = new Rect(0, 0, 600, 400) };
        layout.CalculateCells();
        var live = layout.Cells;
        live[0].OccupiedWindows.Add(new IntPtr(7));

        layout.Columns = 4;
        var preview = layout.BuildCells();

        Assert.Equal(8, preview.Count);
        Assert.Same(live, layout.Cells);
        Assert.Equal(6, layout.Cells.Count);
        Assert.True(layout.Cells[0].IsOccupied);
    }

    [Fact]
    public void Preview_follows_edits_and_numbers_slots_across_displays()
    {
        var first = new MonitorConfigViewModel(Monitor("A", new Rect(0, 0, 600, 400)), new GridLayout("a"));
        var second = new MonitorConfigViewModel(Monitor("B", new Rect(600, 0, 600, 400)), new GridLayout("b"));
        first.Rows = 1;
        first.Columns = 2;
        second.Rows = 2;
        second.Columns = 2;

        var next = 1;
        next += first.UpdatePreview(next);
        next += second.UpdatePreview(next);

        Assert.Equal(new[] { 1, 2 }, first.PreviewCells.Select(c => c.SlotNumber));
        Assert.Equal(new[] { 3, 4, 5, 6 }, second.PreviewCells.Select(c => c.SlotNumber));
        Assert.Equal("Slots 3–6", second.SlotRange);

        // Nothing has been applied to the real layout yet
        Assert.Empty(first.Layout.Cells);
    }

    [Fact]
    public void Preview_raises_a_redraw_and_a_display_without_a_grid_has_no_slots()
    {
        var viewModel = new MonitorConfigViewModel(Monitor("A", new Rect(0, 0, 600, 400)), new GridLayout("a"));
        var changed = new List<string?>();
        viewModel.PropertyChanged += (s, e) => changed.Add(e.PropertyName);

        viewModel.IsDisabledMode = true;
        var count = viewModel.UpdatePreview(1);

        Assert.Equal(0, count);
        Assert.Equal("No grid on this display", viewModel.SlotRange);
        Assert.Contains(nameof(MonitorConfigViewModel.Revision), changed);
    }

    [Fact]
    public void Applied_grid_replaces_the_preview_with_the_real_cells()
    {
        var viewModel = new MonitorConfigViewModel(Monitor("A", new Rect(0, 0, 600, 400)), new GridLayout("a"));
        viewModel.UpdatePreview(1);
        Assert.NotSame(viewModel.Layout.Cells, viewModel.PreviewCells);

        viewModel.Layout.CalculateCells();
        viewModel.NotifyGridApplied();

        Assert.Same(viewModel.Layout.Cells, viewModel.PreviewCells);
    }

    [Fact]
    public void An_overlay_is_created_transparent_and_showing_it_does_not_make_it_opaque()
    {
        RunOnStaThread(() =>
        {
            var overlay = new GridOverlayWindow(Monitor("A", new Rect(0, 0, 600, 400)));

            Assert.Equal(0, overlay.Opacity);

            // Never shown in this test: SetVisible(false) on a hidden window must leave opacity alone too
            overlay.SetOpacity(0.6);
            overlay.SetVisible(false);
            Assert.Equal(0.6, overlay.Opacity);

            overlay.Close();
        });
    }

    [Fact]
    public void Reapplying_a_configuration_keeps_the_overlay_windows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "NittyGriddyTests", Guid.NewGuid().ToString("N"));

        RunOnStaThread(() =>
        {
            // The grid stays off, so nothing is shown and nothing is moved
            using var controller = new AppController(new SettingsService(Path.Combine(dir, "settings.json")), Dispatcher.CurrentDispatcher);
            var profile = controller.Settings.ActiveProfile;

            controller.Grid.LoadConfiguration(profile);
            var created = controller.Grid.OverlayWindowsCreated;
            Assert.True(created > 0);

            // What a slider drag, a rule change and a pin change all do
            foreach (var layout in profile.MonitorLayouts.Values)
                layout.CellSpacing = 25;
            controller.Grid.LoadConfiguration(profile);
            profile.Pins.Add(new TablePin { TitlePattern = "x", SlotNumber = 1 });
            controller.Grid.LoadConfiguration(profile);

            Assert.Equal(created, controller.Grid.OverlayWindowsCreated);

            // Switching one display to "no grid" and back creates exactly one new overlay
            var one = profile.MonitorLayouts.Values.First();
            one.Mode = GridLayoutMode.Disabled;
            controller.Grid.LoadConfiguration(profile);
            one.Mode = GridLayoutMode.FixedGrid;
            controller.Grid.LoadConfiguration(profile);

            Assert.Equal(created + 1, controller.Grid.OverlayWindowsCreated);
        });

        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Deferred_apply_is_pending_until_applied()
    {
        var dir = Path.Combine(Path.GetTempPath(), "NittyGriddyTests", Guid.NewGuid().ToString("N"));

        RunOnStaThread(() =>
        {
            using var controller = new AppController(new SettingsService(Path.Combine(dir, "settings.json")), Dispatcher.CurrentDispatcher);
            Assert.False(controller.IsLayoutApplyPending);

            controller.ApplyLayoutSoon();
            controller.ApplyLayoutSoon();
            Assert.True(controller.IsLayoutApplyPending);

            controller.ApplyLayout();
            Assert.False(controller.IsLayoutApplyPending);
        });

        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }
}
