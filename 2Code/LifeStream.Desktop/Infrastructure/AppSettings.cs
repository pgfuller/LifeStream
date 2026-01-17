using Newtonsoft.Json;

namespace LifeStream.Desktop.Infrastructure;

/// <summary>
/// Application settings stored as JSON.
/// Simple POCO class for user preferences.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Name of the selected DevExpress skin/theme.
    /// </summary>
    public string SkinName { get; set; } = "The Bezier";

    /// <summary>
    /// Default location for weather data (e.g., "Sydney").
    /// </summary>
    public string? DefaultLocation { get; set; }

    /// <summary>
    /// Whether to start LifeStream when Windows starts.
    /// </summary>
    public bool StartWithWindows { get; set; } = false;

    /// <summary>
    /// Whether to minimize to system tray instead of taskbar.
    /// </summary>
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// Name of the default dashboard layout.
    /// </summary>
    public string DefaultLayoutName { get; set; } = "Default";
}
