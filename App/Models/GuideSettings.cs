namespace App.Models
{
    /// <summary>
    /// Where the user is with the getting-started guide
    /// </summary>
    public class GuideSettings
    {
        /// <summary>
        /// Whether the guide is shown in the main window. Off unless this is a first run (no settings file yet),
        /// so someone who already has a working setup never sees it unasked.
        /// </summary>
        public bool Show { get; set; }

        /// <summary>
        /// Set once a table was brought to the front with a hotkey
        /// </summary>
        public bool HotkeyUsed { get; set; }
    }
}
