using System;
using System.IO;

namespace LifeStream.Core.Infrastructure;

/// <summary>
/// Provides standardized paths for application data, logs, and configuration.
/// Runtime data is stored in %APPDATA%\LifeStream.
/// </summary>
public static class AppPaths
{
    private const string AppName = "LifeStream";

    /// <summary>
    /// Root folder for all LifeStream application data.
    /// %APPDATA%\LifeStream (e.g., C:\Users\{user}\AppData\Roaming\LifeStream)
    /// </summary>
    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    /// <summary>
    /// Folder for SQLite database files.
    /// </summary>
    public static string DataPath => Path.Combine(AppDataRoot, "Data");

    /// <summary>
    /// Full path to the main SQLite database file.
    /// </summary>
    public static string DatabasePath => Path.Combine(DataPath, "lifestream.db");

    /// <summary>
    /// Folder for Serilog rolling log files.
    /// </summary>
    public static string LogsPath => Path.Combine(AppDataRoot, "Logs");

    /// <summary>
    /// Folder for saved dashboard layouts.
    /// </summary>
    public static string LayoutsPath => Path.Combine(AppDataRoot, "Layouts");

    /// <summary>
    /// Folder for cached API responses and images.
    /// </summary>
    public static string CachePath => Path.Combine(AppDataRoot, "Cache");

    /// <summary>
    /// Gets the cache folder for a specific service (temporary/disposable data).
    /// Creates the folder if it doesn't exist.
    /// </summary>
    /// <param name="serviceName">Name of the service (e.g., "Weather").</param>
    /// <returns>Full path to the service's cache folder.</returns>
    public static string GetServiceCachePath(string serviceName)
    {
        var path = Path.Combine(CachePath, serviceName);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Gets the permanent data folder for a specific service (valuable/historical data).
    /// Creates the folder if it doesn't exist.
    /// Use this for data that should be preserved (APOD images, radar history, etc.)
    /// </summary>
    /// <param name="serviceName">Name of the service (e.g., "APOD", "BOMRadar").</param>
    /// <returns>Full path to the service's data folder.</returns>
    public static string GetServiceDataPath(string serviceName)
    {
        var path = Path.Combine(DataPath, serviceName);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Folder for configuration files (settings, sources, API keys).
    /// </summary>
    public static string ConfigPath => Path.Combine(AppDataRoot, "Config");

    /// <summary>
    /// Path to user settings JSON file.
    /// </summary>
    public static string SettingsFilePath => Path.Combine(ConfigPath, "settings.json");

    /// <summary>
    /// Path to information sources configuration JSON file.
    /// </summary>
    public static string SourcesFilePath => Path.Combine(ConfigPath, "sources.json");

    /// <summary>
    /// Path to API keys configuration JSON file.
    /// </summary>
    public static string ApiKeysFilePath => Path.Combine(ConfigPath, "apikeys.json");

    /// <summary>
    /// Ensures all required application directories exist.
    /// Call this at application startup.
    /// </summary>
    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(LayoutsPath);
        Directory.CreateDirectory(CachePath);
        Directory.CreateDirectory(ConfigPath);
    }
}
