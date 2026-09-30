using App.Models;

namespace NittyGriddy.Tests;

public class WindowFilterTests
{
    [Fact]
    public void Class_matches_by_prefix_and_title_by_substring_ignoring_case()
    {
        var filter = new WindowFilter("apolloruntime", "table");

        Assert.True(filter.Matches("ApolloRuntimeContentWindow", "NL50 - Table 7"));
        Assert.False(filter.Matches("XApolloRuntimeContentWindow", "NL50 - Table 7"));
        Assert.False(filter.Matches("ApolloRuntimeContentWindow", "Lobby"));
    }

    [Fact]
    public void Empty_part_matches_anything()
    {
        Assert.True(new WindowFilter("", "Table").Matches("Whatever", "Table 1"));
        Assert.True(new WindowFilter("Qt5", "").Matches("Qt5Window", ""));
    }

    [Fact]
    public void Regex_mode_applies_patterns_to_both_parts()
    {
        var filter = new WindowFilter("^Qt\\d+", @"\$[\d.]+ / \$[\d.]+", useRegex: true);

        Assert.True(filter.Matches("Qt51518QWindow", "Holdem $0.50 / $1.00"));
        Assert.False(filter.Matches("Qt51518QWindow", "Lobby"));
        Assert.False(filter.Matches("MyQt5", "Holdem $0.50 / $1.00"));
    }

    [Fact]
    public void Invalid_regex_matches_nothing_instead_of_throwing()
    {
        var filter = new WindowFilter("", "([unclosed", useRegex: true);

        Assert.False(filter.Matches("Any", "Any"));
    }

    [Fact]
    public void Process_name_must_match_exactly_ignoring_case_and_exe_suffix()
    {
        var filter = new WindowFilter { ProcessName = "GGnet.exe" };

        Assert.True(filter.Matches(new WindowIdentity("ggnet", "Any", "Any")));
        Assert.True(filter.Matches(new WindowIdentity("GGnet.exe", "Any", "Any")));
        Assert.False(filter.Matches(new WindowIdentity("GGnetHelper", "Any", "Any")));
        Assert.False(filter.Matches(new WindowIdentity("", "Any", "Any")));
    }

    [Fact]
    public void A_rule_without_process_ignores_the_process()
    {
        var filter = new WindowFilter("Cls", "");

        Assert.True(filter.Matches(new WindowIdentity("whatever", "Cls", "t")));
    }

    [Fact]
    public void Excluded_title_wins_over_a_matching_title()
    {
        var filter = new WindowFilter("", "Poker") { ExcludeTitlePattern = "Lobby" };

        Assert.True(filter.Matches("c", "Poker table 5"));
        Assert.False(filter.Matches("c", "Poker Lobby"));
    }

    [Fact]
    public void Exclude_pattern_is_a_regex_in_regex_mode()
    {
        var filter = new WindowFilter("c", "", useRegex: true) { ExcludeTitlePattern = "^(Lobby|Cashier)$" };

        Assert.False(filter.Matches("c", "Lobby"));
        Assert.True(filter.Matches("c", "Lobby table 3"));
    }

    [Fact]
    public void A_disabled_rule_matches_nothing()
    {
        var filter = new WindowFilter("", "Table") { Enabled = false };

        Assert.False(filter.Matches("c", "Table 1"));
    }

    [Fact]
    public void A_rule_that_restricts_nothing_matches_nothing()
    {
        // Guards against an empty rule silently claiming every window on the desktop
        Assert.False(new WindowFilter().Matches(new WindowIdentity("explorer", "CabinetWClass", "Documents")));
        Assert.False(new WindowFilter { ExcludeTitlePattern = "Lobby" }.Matches("c", "anything"));
    }

    [Fact]
    public void Process_is_only_looked_up_when_class_and_title_already_match()
    {
        var filter = new WindowFilter("Cls", "Table") { ProcessName = "client" };
        var lookups = 0;
        string Lookup() { lookups++; return "client"; }

        Assert.False(filter.Matches("Other", "Table 1", Lookup));
        Assert.False(filter.Matches("Cls", "Lobby", Lookup));
        Assert.Equal(0, lookups);

        Assert.True(filter.Matches("Cls", "Table 1", Lookup));
        Assert.Equal(1, lookups);
    }

    [Fact]
    public void Description_names_the_parts_that_are_set()
    {
        var filter = new WindowFilter("Cls", "Table", useRegex: true) { ProcessName = "client", ExcludeTitlePattern = "Lobby" };

        var text = filter.Describe();

        Assert.Contains("client", text);
        Assert.Contains("Cls", text);
        Assert.Contains("Table", text);
        Assert.Contains("Lobby", text);
    }
}
