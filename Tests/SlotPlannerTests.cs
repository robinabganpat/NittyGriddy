using App.Models;
using App.Services;

namespace NittyGriddy.Tests;

public class SlotPlannerTests
{
    private static IntPtr H(int value) => new(value);
    private static readonly HashSet<int> None = new();

    private static TableRegistry Registry(params (string key, int count)[] groups)
    {
        var registry = new TableRegistry();
        registry.SetGroups(groups.Select(g => new SlotGroup(g.key, g.count)).ToList());
        return registry;
    }

    private static TablePin Pin(string pattern, int slotNumber, bool regex = false) =>
        new() { TitlePattern = pattern, SlotNumber = slotNumber, UseRegex = regex };

    [Fact]
    public void Pin_matches_title_substring_ignoring_case()
    {
        var pins = new[] { Pin("sunday million", 3) };

        Assert.Equal(2, SlotPlanner.PinnedSlot("Sunday Million : Buy-in $109 - Table 4", pins, slotCount: 6));
        Assert.Null(SlotPlanner.PinnedSlot("Bounty Hunters : Buy-in $5 - Table 1", pins, slotCount: 6));
    }

    [Fact]
    public void Pin_survives_the_parts_of_a_title_that_change_during_play()
    {
        // Real GGPoker format: blinds and table number change, the front part does not
        var pins = new[] { Pin("T$ Builder $0.25 : ", 2) };

        Assert.Equal(1, SlotPlanner.PinnedSlot("T$ Builder $0.25 : Buy-in $0.25 - Blinds 40 | 80 - Table 33/!)@(#*$&%^|6475666166", pins, 6));
        Assert.Equal(1, SlotPlanner.PinnedSlot("T$ Builder $0.25 : Buy-in $0.25 - Blinds 300 | 600 - Table 7/!)@(#*$&%^|6475666166", pins, 6));
        Assert.Null(SlotPlanner.PinnedSlot("T$ Builder $0.25 Turbo : Buy-in $0.25 - Blinds 40 | 80 - Table 2", pins, 6));
    }

    [Fact]
    public void First_matching_pin_wins()
    {
        var pins = new[] { Pin("Million", 1), Pin("Sunday", 2) };

        Assert.Equal(0, SlotPlanner.PinnedSlot("Sunday Million", pins, 6));
    }

    [Fact]
    public void Pin_to_a_slot_the_grid_does_not_have_is_ignored()
    {
        var pins = new[] { Pin("Sunday", 9), Pin("Sunday", 2) };

        Assert.Equal(1, SlotPlanner.PinnedSlot("Sunday Million", pins, slotCount: 6));
        Assert.Null(SlotPlanner.PinnedSlot("Sunday Million", new[] { Pin("Sunday", 9) }, slotCount: 6));
        Assert.Equal(new HashSet<int> { 1 }, SlotPlanner.ReservedSlots(pins, slotCount: 6));
    }

    [Fact]
    public void Regex_pin_and_invalid_regex()
    {
        Assert.Equal(0, SlotPlanner.PinnedSlot("Daily $5 Bounty", new[] { Pin(@"^Daily \$\d+", 1, regex: true) }, 4));
        Assert.Null(SlotPlanner.PinnedSlot("anything", new[] { Pin("([broken", 1, regex: true) }, 4));
    }

    [Fact]
    public void Empty_pin_pattern_pins_nothing()
    {
        Assert.Null(SlotPlanner.PinnedSlot("anything", new[] { Pin("", 1) }, 4));
    }

    [Fact]
    public void Unpinned_table_takes_an_unreserved_empty_slot_first()
    {
        var registry = Registry(("A", 3));
        var reserved = new HashSet<int> { 0 };

        Assert.Equal(1, SlotPlanner.PickSlot(registry, "A", reserved));
    }

    [Fact]
    public void Unpinned_table_uses_a_reserved_empty_slot_rather_than_stacking()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 1);
        var reserved = new HashSet<int> { 0 };

        Assert.Equal(0, SlotPlanner.PickSlot(registry, "A", reserved));
    }

    [Fact]
    public void When_everything_is_occupied_the_table_stacks_on_an_unreserved_slot()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 0);
        registry.Place(H(2), 1);
        var reserved = new HashSet<int> { 0 };

        Assert.Equal(1, SlotPlanner.PickSlot(registry, "A", reserved));
    }

    [Fact]
    public void Own_monitor_wins_ties_but_an_empty_slot_elsewhere_beats_stacking()
    {
        var registry = Registry(("A", 1), ("B", 2));

        Assert.Equal(1, SlotPlanner.PickSlot(registry, "B", None));

        registry.Place(H(1), 1);
        registry.Place(H(2), 2);
        Assert.Equal(0, SlotPlanner.PickSlot(registry, "B", None));
    }

    [Fact]
    public void Table_on_a_monitor_without_a_grid_goes_to_any_slot()
    {
        var registry = Registry(("A", 2));

        Assert.Equal(0, SlotPlanner.PickSlot(registry, "NoGridHere", None));
        Assert.Equal(0, SlotPlanner.PickSlot(registry, null, None));
    }

    [Fact]
    public void Excluded_slot_is_never_chosen()
    {
        var registry = Registry(("A", 2));
        registry.Place(H(1), 1);

        // Slot 0 is where a pinned table is arriving; its previous occupant must go elsewhere even if that means stacking
        Assert.Equal(1, SlotPlanner.PickSlot(registry, "A", None, exclude: new HashSet<int> { 0 }));
        Assert.Null(SlotPlanner.PickSlot(Registry(("A", 1)), "A", None, exclude: new HashSet<int> { 0 }));
    }

    [Fact]
    public void No_slots_means_no_placement()
    {
        Assert.Null(SlotPlanner.PickSlot(Registry(), null, None));
    }
}
