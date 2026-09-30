using System;
using System.Collections.Generic;
using System.IO;
using App.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace App.Services
{
    /// <summary>
    /// Result of loading settings; Warning is set when the stored file could not be used
    /// </summary>
    /// <param name="SaveAllowed">
    /// False when the stored file exists but could not be read or set aside; saving would then overwrite
    /// settings that may be perfectly good.
    /// </param>
    public sealed record LoadResult(AppSettings Settings, string? Warning, bool SaveAllowed = true);

    /// <summary>
    /// Reads and writes the application settings file
    /// </summary>
    public class SettingsService
    {
        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            // Replace rather than append to collections that constructors pre-populate
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Converters = { new StringEnumConverter() }
        };

        private readonly string _filePath;

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NittyGriddy",
            "settings.json");

        public string FilePath => _filePath;

        public SettingsService(string? filePath = null)
        {
            _filePath = filePath ?? DefaultPath;
        }

        /// <summary>
        /// Load settings. A missing file yields defaults; an unusable file is kept as a backup and defaults are returned with a warning.
        /// </summary>
        public LoadResult Load()
        {
            if (!File.Exists(_filePath))
            {
                // No settings yet means a first run: that is who the getting-started guide is for
                var fresh = Defaults();
                fresh.Guide.Show = true;
                return new LoadResult(fresh, null);
            }

            string json;
            try
            {
                json = File.ReadAllText(_filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The file is there but locked or inaccessible (antivirus, backup, sync). It is not corrupt:
                // leave it alone and do not write over it.
                return new LoadResult(Defaults(),
                    $"The settings file could not be opened ({ex.Message}). Defaults are in use and changes will not be saved this session.",
                    SaveAllowed: false);
            }

            try
            {
                return new LoadResult(Deserialize(json), null);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or FormatException)
            {
                var backup = BackUpUnusableFile();
                if (backup == null)
                {
                    return new LoadResult(Defaults(),
                        "Settings could not be read and the file could not be set aside. Defaults are in use and changes will not be saved this session.",
                        SaveAllowed: false);
                }

                return new LoadResult(Defaults(),
                    $"Settings could not be read and were reset to defaults. The old file was kept as {Path.GetFileName(backup)}.");
            }
        }

        /// <summary>
        /// Write settings atomically: a crash mid-write never leaves a truncated settings file
        /// </summary>
        public void Save(AppSettings settings)
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, Serialize(settings));
            File.Move(tempPath, _filePath, overwrite: true);
        }

        public static string Serialize(AppSettings settings)
        {
            return JsonConvert.SerializeObject(settings, SerializerSettings);
        }

        public static AppSettings Deserialize(string json)
        {
            var settings = JsonConvert.DeserializeObject<AppSettings>(json, SerializerSettings)
                           ?? throw new InvalidDataException("Settings file is empty.");
            settings.Normalize();
            return settings;
        }

        public static string SerializeProfile(MonitorGridConfig profile)
        {
            return JsonConvert.SerializeObject(profile, SerializerSettings);
        }

        /// <summary>
        /// Read a single profile, including files written by the old Save Configuration button
        /// </summary>
        public static MonitorGridConfig DeserializeProfile(string json)
        {
            var profile = JsonConvert.DeserializeObject<MonitorGridConfig>(json, SerializerSettings)
                          ?? throw new InvalidDataException("Profile file is empty.");

            profile.Normalize();
            if (string.IsNullOrWhiteSpace(profile.ConfigName))
                profile.ConfigName = "Imported";

            return profile;
        }

        private static AppSettings Defaults()
        {
            var settings = new AppSettings();
            settings.Normalize();
            return settings;
        }

        private string? BackUpUnusableFile()
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath) ?? "";
                var backup = Path.Combine(directory, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Move(_filePath, backup, overwrite: true);
                return backup;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
