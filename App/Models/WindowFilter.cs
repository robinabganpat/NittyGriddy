using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace App.Models
{
    /// <summary>
    /// What is known about a window for matching: owning process name (without .exe), class and title
    /// </summary>
    public readonly record struct WindowIdentity(string ProcessName, string ClassName, string Title);

    /// <summary>
    /// A rule that decides whether a window is a table, by owning process, class name and title
    /// </summary>
    public class WindowFilter
    {
        /// <summary>
        /// Display name, e.g. the poker client the rule is for
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// A disabled rule matches nothing; lets a client be switched off without deleting its rule
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Name of the owning program, with or without ".exe" (e.g. "GGnet"). Exact match, case-insensitive.
        /// If empty, any process matches.
        /// </summary>
        public string ProcessName { get; set; } = string.Empty;

        /// <summary>
        /// Window class name to match (e.g., "ApolloRuntimeContentWindow")
        /// If empty/null, any class matches
        /// </summary>
        public string ClassName { get; set; }

        /// <summary>
        /// Title pattern to match (substring match or regex, case-insensitive)
        /// If empty/null, any title matches
        /// </summary>
        public string TitlePattern { get; set; }

        /// <summary>
        /// Titles that are never tables even when everything else matches (lobby, cashier, ...).
        /// Substring or regex like TitlePattern. If empty, nothing is excluded.
        /// </summary>
        public string ExcludeTitlePattern { get; set; } = string.Empty;

        /// <summary>
        /// If true, TitlePattern and ClassName are treated as regex patterns
        /// If false, uses simple string matching (StartsWith for class, Contains for title)
        /// </summary>
        public bool UseRegex { get; set; }

        /// <summary>
        /// Id of the built-in client preset this rule came from, if any
        /// </summary>
        public string? PresetId { get; set; }

        /// <summary>
        /// True once the user edited a preset rule; such a rule is no longer refreshed from the built-in definition
        /// </summary>
        public bool Customized { get; set; }

        public WindowFilter()
        {
            ClassName = string.Empty;
            TitlePattern = string.Empty;
            UseRegex = false;
        }

        public WindowFilter(string className, string titlePattern, bool useRegex = false)
        {
            ClassName = className ?? string.Empty;
            TitlePattern = titlePattern ?? string.Empty;
            UseRegex = useRegex;
        }

        /// <summary>
        /// True when the rule names a process or a class. A rule that only has a title can match any program's
        /// window (a browser tab, a document) and is treated more cautiously.
        /// </summary>
        [JsonIgnore]
        public bool IsSpecific => !string.IsNullOrEmpty(ProcessName) || !string.IsNullOrEmpty(ClassName);

        /// <summary>
        /// Check class and title only, for a rule without a process
        /// </summary>
        public bool Matches(string windowClass, string windowTitle)
        {
            return Matches(windowClass, windowTitle, () => string.Empty);
        }

        public bool Matches(WindowIdentity window)
        {
            return Matches(window.ClassName, window.Title, () => window.ProcessName);
        }

        /// <summary>
        /// Check if a window matches this rule. The process name is only requested when class and title already match.
        /// </summary>
        public bool Matches(string windowClass, string windowTitle, Func<string> processName)
        {
            if (!Enabled)
                return false;

            // A rule that restricts nothing would claim every window on the desktop
            if (string.IsNullOrEmpty(ProcessName) && string.IsNullOrEmpty(ClassName) && string.IsNullOrEmpty(TitlePattern))
                return false;

            try
            {
                if (!PartMatches(ClassName, windowClass, prefix: true))
                    return false;

                if (!PartMatches(TitlePattern, windowTitle, prefix: false))
                    return false;

                if (!string.IsNullOrEmpty(ExcludeTitlePattern) && PartMatches(ExcludeTitlePattern, windowTitle, prefix: false))
                    return false;
            }
            catch (ArgumentException ex)
            {
                // Invalid regex pattern - treat as non-match
                System.Diagnostics.Debug.WriteLine($"WindowFilter: Invalid regex pattern - {ex.Message}");
                return false;
            }

            return string.IsNullOrEmpty(ProcessName) || SameProcess(ProcessName, processName());
        }

        /// <summary>
        /// Whether a window of this process could be matched by this rule at all, whatever its title
        /// </summary>
        public bool AppliesToProcess(string processName)
        {
            return Enabled && !string.IsNullOrEmpty(ProcessName) && SameProcess(ProcessName, processName);
        }

        private bool PartMatches(string pattern, string value, bool prefix)
        {
            if (string.IsNullOrEmpty(pattern))
                return true;

            if (UseRegex)
                return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);

            return prefix
                ? value.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)
                : value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameProcess(string expected, string actual)
        {
            return string.Equals(StripExe(expected), StripExe(actual), StringComparison.OrdinalIgnoreCase);
        }

        private static string StripExe(string name)
        {
            name = name.Trim();
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }

        /// <summary>
        /// One-line summary of what the rule matches
        /// </summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(ProcessName)) parts.Add($"program {StripExe(ProcessName)}");
            if (!string.IsNullOrEmpty(ClassName)) parts.Add($"class {ClassName}");
            if (!string.IsNullOrEmpty(TitlePattern)) parts.Add($"title {TitlePattern}");
            if (!string.IsNullOrEmpty(ExcludeTitlePattern)) parts.Add($"not {ExcludeTitlePattern}");

            if (parts.Count == 0)
                return "Empty rule (matches nothing)";

            return string.Join(" · ", parts) + (UseRegex ? " · regex" : "");
        }

        /// <summary>
        /// Display string for UI
        /// </summary>
        public override string ToString()
        {
            return string.IsNullOrEmpty(Name) ? Describe() : $"{Name}: {Describe()}";
        }

        public WindowFilter Clone() => (WindowFilter)MemberwiseClone();
    }
}
