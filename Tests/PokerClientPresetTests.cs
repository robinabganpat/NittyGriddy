using App.Models;
using App.Services;

namespace NittyGriddy.Tests;

public class PokerClientPresetTests
{
    private static WindowFilter Rule(string id) => PokerClientPresets.Find(id)!.Rule;

    // Window titles read from a live GGPoker client on 2026-09-30
    private const string GgClass = "ApolloRuntimeContentWindow";
    private const string GgTable = "T$ Builder $0.25 : Buy-in $0.25 - Blinds 40 | 80 - Table 33/!)@(#*$&%^|6475666166";

    [Fact]
    public void GGPoker_tournament_table_is_a_table()
    {
        Assert.True(Rule(PokerClientPresets.GGPoker).Matches(new WindowIdentity("GGnet", GgClass, GgTable)));
    }

    [Theory]
    [InlineData("GGPoker")]             // main lobby
    [InlineData("T$ Builder $0.25")]    // tournament lobby
    [InlineData(" ")]                   // hidden helper window
    public void GGPoker_lobbies_are_not_tables(string title)
    {
        Assert.False(Rule(PokerClientPresets.GGPoker).Matches(new WindowIdentity("GGnet", GgClass, title)));
    }

    // Titles below were posted by users on the StackAndTile support forum (2019-2026)
    [Theory]
    [InlineData("NLH Blue 23 - $0.02 / $0.05")]
    [InlineData("NLH White 155 - $0.01 / $0.02")]
    [InlineData("NLHP Green 18 - 100 / 200")]
    [InlineData("SD White 01 - $0.02 (Ante $0.02)")]
    [InlineData("Rush & Cash - $0.01 / $0.02")]
    [InlineData("Spin & Gold$1")]
    [InlineData("Spin & Gold 6-Max $0.25 - Blinds 10/20")]
    [InlineData("Battle Royale $0.25 - Blinds 10/20")]
    [InlineData("Daily Green $25 : Buy-in $25 - Blinds 30/60 - Table 3")]
    [InlineData("Bounty Hunters Mini Main $5.40 : Buy-in $5.40 - Blinds 25,000 | 50,000 - Table 351/!)@(#*$&%*|5758662172")]
    public void GGPoker_table_formats_reported_by_other_users_are_tables(string title)
    {
        Assert.True(Rule(PokerClientPresets.GGPoker).Matches(new WindowIdentity("GGnet", GgClass, title)));
    }

    [Theory]
    [InlineData("GGPCOM")]
    [InlineData("PokerOK")]
    [InlineData("ПОКЕРОК")]
    [InlineData("Daily Main Event $33, $10K GTD")]
    [InlineData("Bounty Hunters Mini Main $5.40")]
    public void GGPoker_skin_lobbies_and_tournament_lobbies_are_not_tables(string title)
    {
        Assert.False(Rule(PokerClientPresets.GGPoker).Matches(new WindowIdentity("GGnet", GgClass, title)));
    }

    [Fact]
    public void GGPoker_rule_ignores_other_programs_and_other_classes()
    {
        Assert.False(Rule(PokerClientPresets.GGPoker).Matches(new WindowIdentity("chrome", GgClass, GgTable)));
        Assert.False(Rule(PokerClientPresets.GGPoker).Matches(new WindowIdentity("GGnet", "Messaging", GgTable)));
    }

    // Read from a live Holland Casino client on 2026-09-30
    private const string HcTable = "Big Deuce Hyper Sat - 2x 2€ 1179671212 | NL Hold'em | Level 5 | 150/300 | Ante 30     v.26.1.1.32";

    [Fact]
    public void Holland_Casino_tournament_table_is_a_table()
    {
        Assert.True(Rule(PokerClientPresets.HollandCasino).Matches(new WindowIdentity("PokerClient", "Qt693QWindowIcon", HcTable)));
    }

    // Read from a live Holland Casino client on 2026-09-30: the tournament name itself contains "|"
    private const string HcTableWithPipes = "IRISH OPEN 2x€150|PCKG|Step3 1179685052 | NL Hold'em | Level 7 | 500/1,000 | Ante 100     v.26.1.1.32";

    [Fact]
    public void Holland_Casino_table_whose_tournament_name_contains_a_pipe_is_a_table()
    {
        Assert.True(Rule(PokerClientPresets.HollandCasino).Matches(new WindowIdentity("PokerClient", "Qt693QWindowIcon", HcTableWithPipes)));
    }

    [Fact]
    public void Pin_suggestion_keeps_a_tournament_name_that_contains_a_pipe()
    {
        var suggestion = PokerClientPresets.SuggestPinText(HcTableWithPipes);

        Assert.Equal("IRISH OPEN 2x€150|PCKG|Step3 ", suggestion);
        Assert.Equal(0, SlotPlanner.PinnedSlot(HcTableWithPipes, new[] { new TablePin { TitlePattern = suggestion, SlotNumber = 1 } }, slotCount: 6));
    }

    [Theory]
    [InlineData("Qt693QWindowIcon", "IRISH OPEN 2x€150|PCKG|Step3")]    // tournament lobby, name with pipes
    [InlineData("Qt693QWindowIcon", "Big Deuce Hyper Sat - 2x 2€")]     // tournament lobby
    [InlineData("Qt693QWindowIcon", "Holland Casino Poker")]            // main lobby
    [InlineData("Qt693QWindow", "Holland Casino Poker")]                // popup
    public void Holland_Casino_lobbies_and_popups_are_not_tables(string windowClass, string title)
    {
        Assert.False(Rule(PokerClientPresets.HollandCasino).Matches(new WindowIdentity("PokerClient", windowClass, title)));
    }

    // Read from a live CoinPoker client on 2026-09-30: table and main lobby share both program name and title
    [Theory]
    [InlineData("UnityWndClass", "CoinPoker", true)]                    // table
    [InlineData("Chrome_WidgetWin_1", "CoinPoker", false)]              // main lobby
    [InlineData("Chrome_WidgetWin_1", "Tournament - DAY 1", false)]     // tournament lobby
    public void CoinPoker_windows_as_seen_live(string windowClass, string title, bool isTable)
    {
        Assert.Equal(isTable, Rule(PokerClientPresets.CoinPoker).Matches(new WindowIdentity("CoinPoker", windowClass, title)));
    }

    // Two GGPoker tables seen side by side on 2026-09-30; a pin for one must not catch the other
    [Theory]
    [InlineData("T$ Builder $1 : Buy-in $1 - Blinds 125 | 250 - Table 3/!)@(#*$&%^|6476730008", "T$ Builder $1 : ")]
    [InlineData("T$ Builder $0.50 : Buy-in $0.50 - Blinds 35 | 70 - Table 26/!)@(#*$&%^|6476739928", "T$ Builder $0.50 : ")]
    [InlineData(HcTable, "Big Deuce Hyper Sat - 2x 2€ ")]
    public void Pin_suggested_for_one_live_table_does_not_pin_the_others(string title, string expectedPin)
    {
        var suggestion = PokerClientPresets.SuggestPinText(title);
        Assert.Equal(expectedPin, suggestion);

        var pin = new TablePin { TitlePattern = suggestion, SlotNumber = 1 };
        var others = new[]
        {
            "T$ Builder $1 : Buy-in $1 - Blinds 125 | 250 - Table 3/!)@(#*$&%^|6476730008",
            "T$ Builder $0.50 : Buy-in $0.50 - Blinds 35 | 70 - Table 26/!)@(#*$&%^|6476739928",
            "T$ Builder $10 : Buy-in $10 - Blinds 125 | 250 - Table 3",
            HcTable
        }.Where(t => t != title);

        Assert.True(pin.Matches(title));
        Assert.All(others, other => Assert.False(pin.Matches(other)));
    }

    [Theory]
    [InlineData("Qt693QWindowIcon", "Bruhl 888547180 | NL Hold'em | €0.50/€1    v.26.1.1.34")]
    [InlineData("Qt51518QWindowOwnDCIcon", "Double Or Nothing (€2) 1142312597 | NL Hold'em | Level 4 | 50/100 | Ante 10    v.25.5.1.20")]
    [InlineData("Qt51517QWindowOwnDCIcon", "Progressive Knockout [7-Max] 1121196337 | NL Hold'em | Level 3 | 800/1,600 | Ante 120     v.25.1.1.4")]
    [InlineData("Qt693QWindowIcon", "Twister 0.25€ 1200530962 | NL Hold'em     v.26.1.1.31")]
    [InlineData("Qt5QWindowOwnDCIcon", "Speed Hold'em 2 888539196 | NL Hold'em | €0.05/€0.10     v.20.1.7.2")]
    [InlineData("Qt682QWindowIcon", "UK&Ire 6-max 0.15/0.30 IV 1137277312 | NL Hold'em | £0.15/£0.30     v.25.7.1.34")]
    public void IPoker_tables_are_recognised_across_Qt_versions(string windowClass, string title)
    {
        Assert.True(Rule(PokerClientPresets.HollandCasino).Matches(new WindowIdentity("PokerClient", windowClass, title)));
    }

    [Theory]
    [InlineData("Grosvenor Poker")]
    [InlineData("bet365 Poker")]
    [InlineData("[FR] Shasta")]                       // tournament lobby
    [InlineData("Progressive Knockout [7-Max]")]
    public void IPoker_lobbies_are_not_tables(string title)
    {
        Assert.False(Rule(PokerClientPresets.HollandCasino).Matches(new WindowIdentity("PokerClient", "Qt693QWindowIcon", title)));
    }

    [Fact]
    public void IPoker_rule_does_not_claim_other_Qt_programs()
    {
        // Telegram is a Qt program too
        Assert.False(Rule(PokerClientPresets.HollandCasino).Matches(
            new WindowIdentity("Telegram", "Qt51519QWindowIcon", "Group 123456789 | chat")));
    }

    [Fact]
    public void CoinPoker_table_is_told_from_the_lobby_by_window_class_alone()
    {
        var rule = Rule(PokerClientPresets.CoinPoker);

        Assert.True(rule.Matches(new WindowIdentity("CoinPoker", "UnityWndClass", "CoinPoker")));
        Assert.False(rule.Matches(new WindowIdentity("CoinPoker", "Chrome_WidgetWin_1", "CoinPoker")));
        Assert.False(rule.Matches(new WindowIdentity("SomeGame", "UnityWndClass", "CoinPoker")));
    }

    [Fact]
    public void Unibet_rule_ships_switched_off()
    {
        Assert.False(Rule(PokerClientPresets.Unibet).Enabled);
    }

    [Theory]
    [InlineData("€1 - €50 Meteor Shower - 15/30 - Ante 4 (8268672)", true)]
    [InlineData("5€ HexaPro Banzai - 10/20 - Prize pool 10€", true)]
    [InlineData("5€ HexaPro Banzai", true)]
    [InlineData("Unibet Poker v2.16.0", false)]
    [InlineData("€1 - €50 Meteor Shower - 15/30 Replay", false)]
    public void Unibet_rule_once_switched_on(string title, bool isTable)
    {
        var rule = Rule(PokerClientPresets.Unibet).Clone();
        rule.Enabled = true;

        Assert.Equal(isTable, rule.Matches(new WindowIdentity("Unibet Poker", "{4A600085-7E96-432A-8677-61A2F8EE8804}", title)));
    }

    [Fact]
    public void Every_preset_names_a_program_and_has_valid_patterns()
    {
        foreach (var preset in PokerClientPresets.All)
        {
            Assert.False(string.IsNullOrEmpty(preset.Rule.ProcessName), preset.Id);
            Assert.False(string.IsNullOrEmpty(preset.Rule.Name), preset.Id);

            // Invalid regular expressions throw here
            foreach (var pattern in new[] { preset.Rule.ClassName, preset.Rule.TitlePattern, preset.Rule.ExcludeTitlePattern, preset.TournamentPrefixPattern })
            {
                if (!string.IsNullOrEmpty(pattern))
                    _ = new System.Text.RegularExpressions.Regex(pattern);
            }
        }

        Assert.Equal(PokerClientPresets.All.Count, PokerClientPresets.All.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void New_profile_contains_every_preset_as_an_independent_copy()
    {
        var first = new MonitorGridConfig("A");
        var second = new MonitorGridConfig("B");

        Assert.Equal(PokerClientPresets.All.Count, first.WindowFilters.Count);

        first.WindowFilters[0].TitlePattern = "changed";
        Assert.NotEqual("changed", second.WindowFilters[0].TitlePattern);
        Assert.NotEqual("changed", PokerClientPresets.All[0].Rule.TitlePattern);
    }

    [Fact]
    public void Untouched_preset_rules_are_refreshed_but_keep_their_on_off_state()
    {
        var rules = new List<WindowFilter>
        {
            new() { PresetId = PokerClientPresets.GGPoker, Name = "GGPoker", ProcessName = "GGnet", TitlePattern = "outdated", Enabled = false }
        };

        PokerClientPresets.Refresh(rules);

        Assert.Equal(Rule(PokerClientPresets.GGPoker).TitlePattern, rules[0].TitlePattern);
        Assert.False(rules[0].Enabled);
    }

    [Fact]
    public void Customized_preset_rules_and_plain_rules_are_left_alone()
    {
        var rules = new List<WindowFilter>
        {
            new() { PresetId = PokerClientPresets.GGPoker, Customized = true, TitlePattern = "mine" },
            new("SomeClass", "plain"),
            new() { PresetId = "no-longer-exists", TitlePattern = "orphan" }
        };

        PokerClientPresets.Refresh(rules);

        Assert.Equal(new[] { "mine", "plain", "orphan" }, rules.Select(r => r.TitlePattern));
    }

    [Theory]
    [InlineData(GgTable, "T$ Builder $0.25 : ")]
    [InlineData("Bounty Hunters Daily Main $52.50 : Buy-in $52.50 - Blinds 10,000/20,000 - Table 137/!)@(#*$&%^|247389738", "Bounty Hunters Daily Main $52.50 : ")]
    [InlineData("Progressive Knockout [7-Max] 1121196337 | NL Hold'em | Level 3 | 800/1,600 | Ante 120     v.25.1.1.4", "Progressive Knockout [7-Max] ")]
    [InlineData("€1 - €50 Meteor Shower - 15/30 - Ante 4 (8268672)", "€1 - €50 Meteor Shower - ")]
    [InlineData("CoinPoker", "CoinPoker")]
    [InlineData("Some other table", "Some other table")]
    public void Pin_suggestion_is_the_stable_front_part_of_the_title_where_the_format_is_known(string title, string expected)
    {
        Assert.Equal(expected, PokerClientPresets.SuggestPinText(title));
    }

    [Fact]
    public void Suggested_pin_text_keeps_pinning_the_table_as_blinds_and_table_number_change()
    {
        var pin = new TablePin { TitlePattern = PokerClientPresets.SuggestPinText(GgTable), SlotNumber = 1 };
        const string later = "T$ Builder $0.25 : Buy-in $0.25 - Blinds 300 | 600 - Table 7/!)@(#*$&%^|6475666166";

        Assert.Equal(0, SlotPlanner.PinnedSlot(GgTable, new[] { pin }, slotCount: 6));
        Assert.Equal(0, SlotPlanner.PinnedSlot(later, new[] { pin }, slotCount: 6));
    }
}

public class ProcessNameCacheTests
{
    [Fact]
    public void Looks_up_names_and_rereads_the_list_for_an_unknown_id_only_after_a_pause()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var reads = 0;
        var processes = new Dictionary<int, string> { [10] = "GGnet" };
        var cache = new ProcessNameCache(() => { reads++; return new Dictionary<int, string>(processes); }, () => now);

        Assert.Equal("GGnet", cache.GetName(10));
        Assert.Equal("", cache.GetName(99));
        Assert.Equal(1, reads);

        // A program that started later is found once the list may be read again
        processes[99] = "PokerClient";
        Assert.Equal("", cache.GetName(99));
        now = now.AddSeconds(2);
        Assert.Equal("PokerClient", cache.GetName(99));
        Assert.Equal(2, reads);
    }

    [Fact]
    public void A_reused_process_id_is_not_remembered_for_long()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var processes = new Dictionary<int, string> { [10] = "GGnet" };
        var cache = new ProcessNameCache(() => new Dictionary<int, string>(processes), () => now);

        Assert.Equal("GGnet", cache.GetName(10));

        processes[10] = "notepad";
        now = now.AddSeconds(6);

        Assert.Equal("notepad", cache.GetName(10));
    }

    [Fact]
    public void Reads_the_real_process_list()
    {
        var cache = new ProcessNameCache();

        Assert.Equal(System.Diagnostics.Process.GetCurrentProcess().ProcessName, cache.GetName(Environment.ProcessId));
    }
}
