# BOM Forecast Service - Weather Forecast

## Overview

The BOM Forecast service fetches weather forecast data from the Australian Bureau of Meteorology FTP server. Provides a 7-day forecast with temperature, conditions, and precipitation data.

**Status:** Implemented (v0.1.0)

## Data Source

- **Protocol:** FTP (anonymous)
- **Server:** `ftp.bom.gov.au`
- **Path:** `/anon/gen/fwo/`
- **Format:** JSON
- **File:** Location-specific (e.g., `IDN10064.json` for Sydney)

## Features

### Forecast Data
- 7-day forecast (today + 6 days)
- Min/max temperatures
- Weather conditions (text and icon code)
- Precipitation chance/range
- Extended text description

### Data Extraction
- Parses BOM JSON structure (nested regional data)
- Extracts location-specific forecast
- Handles missing/null values gracefully
- Falls back to RainfallRange when PrecipitationChance is null

### Caching
- Saves raw JSON response locally
- Timestamps for freshness tracking
- 30-minute refresh interval (data typically updates every 3 hours)

## Data Model

```csharp
public class ForecastData
{
    public string Date { get; set; }            // "2026-01-16"
    public string DayName { get; set; }         // "Thursday"
    public int? MinTemp { get; set; }           // Celsius
    public int? MaxTemp { get; set; }           // Celsius
    public string? Conditions { get; set; }     // "Partly cloudy"
    public string? IconCode { get; set; }       // "3" (BOM icon)
    public int? PrecipitationChance { get; set; } // 0-100%
    public string? RainfallRange { get; set; }  // "4 to 30 mm"
    public string? ExtendedText { get; set; }   // Full description

    // Computed property for display
    public string PrecipitationDisplay { get; }
    public string TemperatureDisplay { get; }
}
```

## BOM Location IDs

| Location | File | Product ID |
|----------|------|------------|
| Sydney | IDN10064.json | IDN10064 |
| Melbourne | IDV10450.json | IDV10450 |
| Brisbane | IDQ10095.json | IDQ10095 |
| Perth | IDW12300.json | IDW12300 |

## File Structure

```
%APPDATA%\LifeStream\Data\BOMForecast_Sydney\
└── forecast.json    (cached BOM response)
```

## Service Configuration

| Parameter | Default | Description |
|-----------|---------|-------------|
| location | Sydney | Forecast location |
| refreshInterval | 30 min | Check for updates |

## UI Panel Layout

```
┌─────────────────────────────────────────────────────────────┐
│ Weather Forecast                              Refresh       │
├──────┬────────┬────────┬──────┬────────────────────────────┤
│ Day  │ Temp   │ Cond.  │ Rain │ Description                 │
├──────┼────────┼────────┼──────┼────────────────────────────┤
│ Thu  │ 22-28  │ Sunny  │ 0%   │ Fine, light winds...        │
│ Fri  │ 20-25  │ Cloudy │ 40%  │ Chance of showers...        │
│ ...  │ ...    │ ...    │ ...  │ ...                         │
└──────┴────────┴────────┴──────┴────────────────────────────┘
│ Updated: 12:30 | Next: 13:00                                │
└─────────────────────────────────────────────────────────────┘
```

## Grid Columns

| Column | Field | Width | Description |
|--------|-------|-------|-------------|
| Day | DayName | 50 | Short day name |
| Temp | TemperatureDisplay | 80 | "Min-Max" format |
| Conditions | Conditions | 120 | Weather text |
| Rain | PrecipitationDisplay | 80 | Percentage or range |
| Description | ExtendedText | Fill | Full forecast text |

## Key Files

- `Services/BomForecast/BomForecastService.cs` - Service implementation
- `Services/BomForecast/ForecastData.cs` - Data model
- `Services/BomForecast/ForecastLocations.cs` - Location definitions
- `Controls/BomForecastPanel.cs` - UI panel

## Future Enhancements

- Weather icons (map BOM icon codes to images)
- Hourly forecast (if available)
- Multiple location comparison
- Weather alerts integration
- Graph view for temperature trends
