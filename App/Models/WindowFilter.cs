using System;
using System.Text.RegularExpressions;

namespace App.Models
{
    /// <summary>
    /// Represents a filter for matching windows by class name and/or title pattern
    /// </summary>
    public class WindowFilter
    {
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
        /// If true, TitlePattern and ClassName are treated as regex patterns
        /// If false, uses simple string matching (StartsWith for class, Contains for title)
        /// </summary>
        public bool UseRegex { get; set; }

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
        /// Check if a window matches this filter
        /// </summary>
        public bool Matches(string windowClass, string windowTitle)
        {
            bool classMatches;
            bool titleMatches;

            if (UseRegex)
            {
                // Regex matching
                try
                {
                    classMatches = string.IsNullOrEmpty(ClassName) ||
                                   Regex.IsMatch(windowClass, ClassName, RegexOptions.IgnoreCase);

                    titleMatches = string.IsNullOrEmpty(TitlePattern) ||
                                   Regex.IsMatch(windowTitle, TitlePattern, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException ex)
                {
                    // Invalid regex pattern - treat as non-match
                    System.Diagnostics.Debug.WriteLine($"WindowFilter: Invalid regex pattern - {ex.Message}");
                    return false;
                }
            }
            else
            {
                // Simple string matching
                classMatches = string.IsNullOrEmpty(ClassName) ||
                               windowClass.StartsWith(ClassName, StringComparison.OrdinalIgnoreCase);

                titleMatches = string.IsNullOrEmpty(TitlePattern) ||
                               windowTitle.Contains(TitlePattern, StringComparison.OrdinalIgnoreCase);
            }

            var result = classMatches && titleMatches;

            // Debug logging
            System.Diagnostics.Debug.WriteLine($"WindowFilter.Matches: '{windowTitle}' (Class: {windowClass})");
            System.Diagnostics.Debug.WriteLine($"  Filter: Class='{ClassName}', Title='{TitlePattern}', Regex={UseRegex}");
            System.Diagnostics.Debug.WriteLine($"  ClassMatch={classMatches}, TitleMatch={titleMatches}, Result={result}");

            // Both must match (AND logic)
            return result;
        }

        /// <summary>
        /// Display string for UI
        /// </summary>
        public override string ToString()
        {
            var mode = UseRegex ? " [REGEX]" : "";

            if (!string.IsNullOrEmpty(ClassName) && !string.IsNullOrEmpty(TitlePattern))
            {
                return $"Class: {ClassName} AND Title: {TitlePattern}{mode}";
            }
            else if (!string.IsNullOrEmpty(ClassName))
            {
                return $"Class: {ClassName}{mode}";
            }
            else if (!string.IsNullOrEmpty(TitlePattern))
            {
                return $"Title: {TitlePattern}{mode}";
            }
            else
            {
                return "(Empty filter - matches all windows)";
            }
        }
    }
}
