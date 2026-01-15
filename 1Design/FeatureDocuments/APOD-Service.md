# APOD Service - Astronomy Picture of the Day

## Overview

The APOD service fetches NASA's Astronomy Picture of the Day, including the image, title, explanation, and metadata. Supports historical catchup and navigation through past images.

**Status:** Implemented (v0.1.0)

## Data Source

- **API:** NASA APOD API
- **Endpoint:** `https://api.nasa.gov/planetary/apod`
- **API Key:** Required (free registration at api.nasa.gov)
- **Rate Limits:** 1000 requests/hour (default key)

## Features

### Core Functionality
- Fetches today's APOD on startup and daily refresh
- Displays image, title, date, explanation, and copyright
- Local caching of images and metadata as JSON

### Historical Catchup
- On startup, fetches last N days (configurable, default 7)
- Fills gaps in local cache
- Runs asynchronously to not block UI

### Navigation
- **Back/Forward buttons:** Navigate through history (newest to oldest)
- **Browse button:** Opens thumbnail gallery of all cached images
- **History position indicator:** Shows "X/Y" in status

### Image Browser
- Split view: thumbnails on left, full preview on right
- Sorted by date (newest first)
- Double-click or Select button to navigate to image
- Generic component usable for other image galleries

## Data Model

```csharp
public class ApodData
{
    public string Date { get; set; }           // "YYYY-MM-DD"
    public string Title { get; set; }
    public string Explanation { get; set; }
    public string? Copyright { get; set; }
    public string Url { get; set; }            // Standard resolution
    public string? HdUrl { get; set; }         // High resolution
    public string MediaType { get; set; }      // "image" or "video"
    public string? LocalImagePath { get; set; } // Cached file path
}
```

## File Structure

```
%APPDATA%\LifeStream\Data\APOD\
├── APOD_2026-01-15.json    (metadata)
├── APOD_2026-01-15.jpg     (cached image)
├── APOD_2026-01-14.json
├── APOD_2026-01-14.jpg
└── ...
```

## Service Configuration

| Parameter | Default | Description |
|-----------|---------|-------------|
| apiKey | Required | NASA API key |
| catchupDays | 7 | Days to fetch on startup |
| refreshInterval | 4 hours | Check for new APOD |

## UI Panel Layout

```
┌─────────────────────────────────────────────┐
│                   [Image]                    │
├─────────────────────────────────────────────┤
│ 2026-01-15 │ Title of the Day     │ Refresh │
├─────────────────────────────────────────────┤
│                   Status                     │
├─────────────────────────────────────────────┤
│ [Explanation text area with scrollbar]       │
├─────────────────────────────────────────────┤
│ < Back │ Forward > │            │ Browse... │
└─────────────────────────────────────────────┘
```

## Key Files

- `Services/Apod/ApodService.cs` - Service implementation
- `Services/Apod/ApodData.cs` - Data model
- `Controls/ApodPanel.cs` - UI panel
- `Forms/ImageBrowserForm.cs` - Thumbnail gallery

## Future Enhancements

- Video support (currently shows placeholder for video APODs)
- Wallpaper integration (set APOD as desktop background)
- Favorites/bookmarks
- Share to social media
