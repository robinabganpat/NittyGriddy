using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace App.Models
{
    /// <summary>
    /// A built-in recognition rule for one poker client
    /// </summary>
    /// <param name="Rule">The rule as added to a profile</param>
    /// <param name="TournamentPrefixPattern">
    /// Regex whose match at the start of a table title is the part that identifies the tournament and does not
    /// change during play. Used to suggest the text for a pin. Null when the title carries no such information.
    /// </param>
    /// <param name="Status">How the rule was established, shown to the user</param>
    /// <param name="Caution">True when the status is a warning the user should read before switching the rule on</param>
    public sealed record PokerClientPreset(WindowFilter Rule, string? TournamentPrefixPattern, string Status, bool Caution = false)
    {
        public string Id => Rule.PresetId!;
        public string Name => Rule.Name;
    }

    /// <summary>
    /// Built-in rules that recognise the table windows of supported poker clients and leave out their lobbies.
    /// A client is identified by its program name; tables are told apart from other windows by class and title.
    /// </summary>
    public static class PokerClientPresets
    {
        public const string GGPoker = "ggpoker";
        public const string HollandCasino = "hollandcasino";
        public const string CoinPoker = "coinpoker";
        public const string Unibet = "unibet";

        public static IReadOnlyList<PokerClientPreset> All { get; } = new List<PokerClientPreset>
        {
            // GGPoker (GGnet.exe, Adobe AIR). Lobby, tournament lobby and tables share one window class and style,
            // so only the title separates them. The lobby title is the skin name and is localised, so tables are
            // matched positively by the shape of their title rather than by excluding the lobby.
            //   Tournament (read from a live client, 2026-09-30):
            //     "T$ Builder $0.25 : Buy-in $0.25 - Blinds 40 | 80 - Table 33/!)@(#*$&%^|6475666166"
            //     tournament lobby: "T$ Builder $0.25"; main lobby: "GGPoker"
            //   Other formats (from window diagnostics users posted on the StackAndTile forum, 2019-2026):
            //     cash "NLH Blue 23 - $0.02 / $0.05", play money "NLHP Green 18 - 100 / 200",
            //     short deck "SD White 01 - $0.02 (Ante $0.02)", "Rush & Cash - $0.01 / $0.02",
            //     "Spin & Gold 6-Max $0.25 - Blinds 10/20", "Battle Royale $0.25 - Blinds 10/20"
            //   "Table" and "Ante" are translated in some client languages.
            new(new WindowFilter
                {
                    PresetId = GGPoker,
                    Name = "GGPoker",
                    ProcessName = "GGnet",
                    ClassName = "^ApolloRuntimeContentWindow$",
                    TitlePattern = @" - (Table|Tisch) \d+| - Blinds [\d.,]+|[\d.,]+ / \D?[\d.,]+|\((Ante|Анте) |^(Spin & Gold|All-In or Fold|Rush & Cash|Battle Royale)",
                    UseRegex = true
                },
                TournamentPrefixPattern: @"^.+? : (?=Buy-in)",
                Status: "Tournament tables checked against the live client. Cash, Rush & Cash and Spin tables follow titles reported by other users."),

            // iPoker network (PokerClient.exe, Qt). The window class embeds the Qt version and has changed ten times
            // since 2017 ("Qt5QWindowOwnDCIcon" ... "Qt693QWindowIcon"), so it is matched loosely. Lobby and tables
            // share the class. A table title is "<name> <9-10 digit table id> | <game> | ...":
            //     "Bruhl 888547180 | NL Hold'em | €0.50/€1    v.26.1.1.34"
            //     "Progressive Knockout [7-Max] 1121196337 | NL Hold'em | Level 3 | 800/1,600 | Ante 120     v.25.1.1.4"
            // while the lobby is the skin name and a tournament lobby is the tournament name alone.
            //   Holland Casino (read from a live client, 2026-09-30), all from PokerClient.exe:
            //     table            "Big Deuce Hyper Sat - 2x 2€ 1179671212 | NL Hold'em | Level 5 | 150/300 | Ante 30     v.26.1.1.32"
            //     tournament lobby "Big Deuce Hyper Sat - 2x 2€"         (class Qt693QWindowIcon, like the table)
            //     main lobby       "Holland Casino Poker"                (class Qt693QWindowIcon)
            //     popup            "Holland Casino Poker"                (class Qt693QWindow)
            //   Tournament names can contain "|" themselves (live, 2026-09-30):
            //     table            "IRISH OPEN 2x€150|PCKG|Step3 1179685052 | NL Hold'em | Level 7 | 500/1,000 | Ante 100     v.26.1.1.32"
            //     tournament lobby "IRISH OPEN 2x€150|PCKG|Step3"
            //   so a table is recognised by a 6+ digit table id followed by "|" anywhere in the title, not only
            //   before the first "|".
            //   Cash and Twister titles are from other iPoker skins (StackAndTile forum, 2019-2026).
            new(new WindowFilter
                {
                    PresetId = HollandCasino,
                    Name = "HC Online (iPoker)",
                    ProcessName = "PokerClient",
                    ClassName = @"^Qt\d+QWindow",
                    TitlePattern = @"(?<!\d)\d{6,}\s*\|",
                    UseRegex = true
                },
                TournamentPrefixPattern: @"^.+? (?=\d{6,}\s*\|)",
                Status: "Tournament tables checked against the live Holland Casino client. Cash and Twister tables follow titles reported for other iPoker clients."),

            // CoinPoker (client rebuilt in March 2026): the lobby is a Chromium window and each table is a Unity
            // window, both titled just "CoinPoker" and both from a program named CoinPoker.exe. Only the window
            // class tells a table from the lobby. Nothing at window level identifies the table or its tournament,
            // so CoinPoker tables cannot be pinned by title.
            //   Read from a live client, 2026-09-30, all from programs named CoinPoker:
            //     table            class UnityWndClass,      title "CoinPoker"   (each table is its own process)
            //     main lobby       class Chrome_WidgetWin_1, title "CoinPoker"
            //     tournament lobby class Chrome_WidgetWin_1, title "Tournament - DAY 1"
            new(new WindowFilter
                {
                    PresetId = CoinPoker,
                    Name = "CoinPoker",
                    ProcessName = "CoinPoker",
                    ClassName = "^UnityWndClass$",
                    UseRegex = true
                },
                TournamentPrefixPattern: null,
                Status: "All CoinPoker tables are titled \"CoinPoker\", so they can be managed but not pinned by tournament. Checked against the live client."),

            // Unibet Poker (Relax Gaming, "Unibet Poker.exe"). The window class is a random GUID that changes at every
            // start of the client. Lobby: "Unibet Poker v...". Tables, from a StackAndTile user configuration of 2024:
            // titles starting "Texas Hold'em" or containing "/" (blinds), e.g.
            //     "€1 - €50 Meteor Shower - 15/30 - Ante 4 (8268672)", "x€ HexaPro Banzai - 10/20 - Prize pool x€"
            // Replayer windows end in "Replay" or "(rp...)".
            // Shipped switched off: Unibet's terms prohibit helper software, and StackAndTile dropped its built-in
            // Unibet support for that reason. Whether to use it is the player's decision.
            new(new WindowFilter
                {
                    PresetId = Unibet,
                    Name = "Unibet Poker",
                    ProcessName = "Unibet Poker",
                    ClassName = @"^\{[0-9A-F-]{36}\}$",
                    TitlePattern = @"^Texas Hold'em|/|HexaPro",
                    ExcludeTitlePattern = @"^Unibet Poker|Replay$|\(rp.*\)$",
                    Enabled = false,
                    UseRegex = true
                },
                TournamentPrefixPattern: @"^.+? - (?=[\d.,]+/[\d.,]+)",
                Status: "Off by default: Unibet's terms prohibit helper software, and other table managers dropped Unibet for that reason. Rule based on a 2024 report; not checked against the current client.",
                Caution: true),
        };

        public static PokerClientPreset? Find(string? id) => All.FirstOrDefault(p => p.Id == id);

        /// <summary>
        /// Fresh copies of every preset rule, for a new profile
        /// </summary>
        public static List<WindowFilter> CreateAll() => All.Select(p => p.Rule.Clone()).ToList();

        /// <summary>
        /// Bring preset rules the user has not edited up to date with the built-in definitions,
        /// keeping whether each is switched on
        /// </summary>
        public static void Refresh(List<WindowFilter> rules)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                var preset = Find(rules[i].PresetId);
                if (preset == null || rules[i].Customized)
                    continue;

                var updated = preset.Rule.Clone();
                updated.Enabled = rules[i].Enabled;
                rules[i] = updated;
            }
        }

        /// <summary>
        /// The part of a table title that identifies its tournament, if a preset knows the client's title format;
        /// otherwise the whole title
        /// </summary>
        public static string SuggestPinText(string title)
        {
            foreach (var preset in All.Where(p => p.TournamentPrefixPattern != null))
            {
                var match = Regex.Match(title, preset.TournamentPrefixPattern!);
                if (match.Success && match.Length > 0)
                    return match.Value;
            }

            return title;
        }
    }
}
