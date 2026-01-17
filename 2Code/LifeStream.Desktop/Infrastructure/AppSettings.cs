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

    /// <summary>
    /// Financial service settings.
    /// </summary>
    public FinancialSettings Financial { get; set; } = new();
}

/// <summary>
/// Settings for the Financial service.
/// </summary>
public class FinancialSettings
{
    /// <summary>
    /// Whether to use real API data instead of mock data.
    /// </summary>
    public bool UseRealData { get; set; } = false;

    /// <summary>
    /// Alpha Vantage API key. Get a free key at https://www.alphavantage.co/support/#api-key
    /// </summary>
    public string? AlphaVantageApiKey { get; set; }

    /// <summary>
    /// Cache duration for API responses in minutes.
    /// Helps reduce API calls within the daily quota.
    /// </summary>
    public int CacheMinutes { get; set; } = 15;

    /// <summary>
    /// Daily API call limit (Alpha Vantage free tier is 25/day).
    /// </summary>
    public int DailyApiLimit { get; set; } = 25;
}
