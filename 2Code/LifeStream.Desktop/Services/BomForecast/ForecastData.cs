using System;
using System.Collections.Generic;

namespace LifeStream.Desktop.Services.BomForecast;

/// <summary>
/// Contains weather forecast data for a location.
/// </summary>
public class ForecastData
{
    /// <summary>
    /// Location/area name (e.g., "Sydney").
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// AAC identifier for the location.
    /// </summary>
    public string Aac { get; set; } = string.Empty;

    /// <summary>
    /// When the forecast was issued.
    /// </summary>
    public DateTime IssuedAt { get; set; }

    /// <summary>
    /// When the data was fetched.
    /// </summary>
    public DateTime FetchedAt { get; set; }

    /// <summary>
    /// Product identifier from BOM.
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Daily forecast entries (typically 7 days).
    /// </summary>
    public List<DayForecast> Days { get; set; } = new List<DayForecast>();

    /// <summary>
    /// Gets today's forecast.
    /// </summary>
    public DayForecast? Today => Days.Count > 0 ? Days[0] : null;
}

/// <summary>
/// Forecast for a single day.
/// </summary>
public class DayForecast
{
    /// <summary>
    /// Date of the forecast.
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Day name (e.g., "Monday").
    /// </summary>
    public string DayName { get; set; } = string.Empty;

    /// <summary>
    /// Short forecast summary (précis, max ~30 chars).
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Longer forecast description.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Minimum temperature in Celsius.
    /// </summary>
    public double? MinTemp { get; set; }

    /// <summary>
    /// Maximum temperature in Celsius.
    /// </summary>
    public double? MaxTemp { get; set; }

    /// <summary>
    /// Probability of precipitation (0-100%).
    /// </summary>
    public int? PrecipitationChance { get; set; }

    /// <summary>
    /// Expected rainfall amount range (e.g., "0 to 2 mm").
    /// </summary>
    public string? RainfallRange { get; set; }

    /// <summary>
    /// BOM icon code for the forecast.
    /// </summary>
    public int? IconCode { get; set; }

    /// <summary>
    /// UV Alert message if applicable.
    /// </summary>
    public string? UvAlert { get; set; }

    /// <summary>
    /// Fire danger rating if applicable.
    /// </summary>
    public string? FireDanger { get; set; }

    /// <summary>
    /// Formats temperature range for display.
    /// </summary>
    public string TemperatureRange
    {
        get
        {
            if (MinTemp.HasValue && MaxTemp.HasValue)
                return $"{MinTemp:F0}° - {MaxTemp:F0}°";
            if (MaxTemp.HasValue)
                return $"Max {MaxTemp:F0}°";
            if (MinTemp.HasValue)
                return $"Min {MinTemp:F0}°";
            return "";
        }
    }

    /// <summary>
    /// Formats precipitation for display - shows percentage if available, otherwise rainfall range.
    /// </summary>
    public string PrecipitationDisplay
    {
        get
        {
            if (PrecipitationChance.HasValue)
                return $"{PrecipitationChance}%";
            if (!string.IsNullOrEmpty(RainfallRange))
                return RainfallRange;
            return "";
        }
    }
}

/// <summary>
/// Configuration for forecast locations.
/// </summary>
public static class ForecastLocations
{
    public static class NSW
    {
        /// <summary>
        /// Sydney metropolitan area.
        /// </summary>
        public static ForecastLocation Sydney => new ForecastLocation
        {
            Name = "Sydney",
            State = "NSW",
            Aac = "NSW_PT131",
            PrecisProductId = "IDN11060",
            CityProductId = "IDN11050",
            TownProductId = "IDN11100"
        };
    }
}

/// <summary>
/// Defines a forecast location and its product IDs.
/// </summary>
public class ForecastLocation
{
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Aac { get; set; } = string.Empty;
    public string PrecisProductId { get; set; } = string.Empty;
    public string CityProductId { get; set; } = string.Empty;
    public string TownProductId { get; set; } = string.Empty;
}
