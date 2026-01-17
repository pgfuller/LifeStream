using System;
using System.IO;
using LifeStream.Core.Infrastructure;
using Newtonsoft.Json;
using Serilog;

namespace LifeStream.Desktop.Infrastructure;

/// <summary>
/// Service for loading and saving application settings to JSON.
/// Settings are stored at %APPDATA%\LifeStream[-Debug]\Config\settings.json
/// </summary>
public static class SettingsService
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.App);
    private static AppSettings? _current;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the current settings instance.
    /// Loads from file on first access.
    /// </summary>
    public static AppSettings Current
    {
        get
        {
            if (_current == null)
            {
                lock (_lock)
                {
                    _current ??= Load();
                }
            }
            return _current;
        }
    }

    /// <summary>
    /// Loads settings from the JSON file.
    /// Returns default settings if file doesn't exist or is invalid.
    /// </summary>
    public static AppSettings Load()
    {
        var filePath = AppPaths.SettingsFilePath;

        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                if (settings != null)
                {
                    Log.Debug("Settings loaded from {Path}", filePath);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load settings from {Path}, using defaults", filePath);
        }

        Log.Debug("Using default settings");
        return new AppSettings();
    }

    /// <summary>
    /// Saves the current settings to the JSON file.
    /// </summary>
    public static void Save()
    {
        if (_current == null) return;

        Save(_current);
    }

    /// <summary>
    /// Saves the specified settings to the JSON file.
    /// </summary>
    public static void Save(AppSettings settings)
    {
        var filePath = AppPaths.SettingsFilePath;

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(filePath, json);
            Log.Debug("Settings saved to {Path}", filePath);

            // Update current instance
            lock (_lock)
            {
                _current = settings;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings to {Path}", filePath);
        }
    }

    /// <summary>
    /// Updates a single setting and saves.
    /// </summary>
    public static void Update(Action<AppSettings> updateAction)
    {
        lock (_lock)
        {
            updateAction(Current);
            Save();
        }
    }
}
