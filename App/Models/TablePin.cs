using System;
using System.Text.RegularExpressions;

namespace App.Models
{
    /// <summary>
    /// Keeps tables whose title matches in a fixed slot, e.g. a particular tournament always in slot 1
    /// </summary>
    public class TablePin
    {
        /// <summary>
        /// Text the table title must contain (or a regular expression). Case-insensitive.
        /// Use the part of the title that does not change during play, such as the tournament name.
        /// </summary>
        public string TitlePattern { get; set; } = string.Empty;

        public bool UseRegex { get; set; }

        /// <summary>
        /// 1-based slot number as shown on the grid
        /// </summary>
        public int SlotNumber { get; set; } = 1;

        public bool Matches(string title)
        {
            if (string.IsNullOrEmpty(TitlePattern))
                return false;

            if (!UseRegex)
                return title.Contains(TitlePattern, StringComparison.OrdinalIgnoreCase);

            try
            {
                return Regex.IsMatch(title, TitlePattern, RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                // An invalid pattern pins nothing
                return false;
            }
        }

        public override string ToString()
        {
            return $"{TitlePattern}{(UseRegex ? " (regex)" : "")} → slot {SlotNumber}";
        }
    }
}
