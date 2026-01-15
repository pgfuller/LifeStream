# BOM Radar Service - Weather Radar

## Overview

The BOM Radar service fetches weather radar imagery from the Australian Bureau of Meteorology FTP server. Supports multiple radar ranges, frame playback, and layer compositing.

**Status:** Implemented (v0.1.0)

## Data Source

- **Protocol:** FTP (anonymous)
- **Server:** `ftp.bom.gov.au`
- **Path:** `/anon/gen/radar/`
- **Format:** PNG images (transparent radar overlay)

## Features

### Multi-Range Support
- 64km, 128km, 256km, 512km ranges per location
- Range selector dropdown in UI
- Separate frame collections and layers per range
- Default: 128km

### Frame Management
- Maintains rolling window of frames (default 12)
- Checks for new frames every 6 minutes
- Automatic cleanup of old frames
- Frame timestamp displayed in local time

### Playback
- Play/Stop button for animation
- Slider for manual frame selection
- Loops continuously when playing
- 2 fps playback speed (500ms per frame)

### Layer Compositing
Each radar image is composited with static layers:
1. **Background** - Base map/terrain color
2. **Topography** - Elevation/terrain detail
3. **Locations** - City/town labels
4. **Radar** - Weather data (semi-transparent)
5. **Legend** - Color scale for rainfall intensity

## Data Model

```csharp
public class RadarFrame
{
    public DateTime Timestamp { get; set; }    // UTC from filename
    public string FileName { get; set; }
    public string? LocalFilePath { get; set; }
    public bool IsCached { get; set; }
    public string DisplayTime => Timestamp.ToLocalTime().ToString("HH:mm");
}

public class RadarLocation
{
    public string Id { get; set; }              // "sydney"
    public string Name { get; set; }            // "Sydney"
    public string ProductIdPrefix { get; set; } // "IDR71"
    public List<RadarProduct> Products { get; }
    public IReadOnlyList<int> AvailableRanges { get; }
}

public class RadarProduct
{
    public string ProductId { get; set; }       // "IDR713"
    public string Name { get; set; }            // "Sydney 128km"
    public string Location { get; set; }        // "Terrey Hills"
    public int RangeKm { get; set; }            // 128
}
```

## File Structure

```
%APPDATA%\LifeStream\Data\BOMRadar_sydney\
├── 64km\
│   ├── IDR714.T.202601160430.png   (frame)
│   ├── IDR714.background.png       (layer)
│   ├── IDR714.topography.png       (layer)
│   ├── IDR714.locations.png        (layer)
│   └── IDR714.legend.0.png         (layer)
├── 128km\
│   ├── IDR713.T.202601160430.png
│   └── ...
├── 256km\
│   └── ...
└── 512km\
    └── ...
```

## BOM Product IDs (Sydney)

| Range | Product ID | Description |
|-------|------------|-------------|
| 64km | IDR714 | Local detail |
| 128km | IDR713 | Regional view |
| 256km | IDR712 | Wide area |
| 512km | IDR711 | Synoptic |

## Service Configuration

| Parameter | Default | Description |
|-----------|---------|-------------|
| location | Sydney | Radar location |
| defaultRange | 128 | Initial range (km) |
| maxFrames | 12 | Frames to retain |
| checkInterval | 6 min | New frame check |

## UI Panel Layout

```
┌─────────────────────────────────────────────┐
│            [Composite Radar Image]           │
├─────────────────────────────────────────────┤
│ Sydney │ 128km ▼ │ 12:30 16/01/2026 │ Refresh │
├─────────────────────────────────────────────┤
│ Play │ [========slider========] │ 8/12      │
├─────────────────────────────────────────────┤
│ 128km | 12 frames | Next: 12:36:00          │
└─────────────────────────────────────────────┘
```

## Key Files

- `Services/BomRadar/BomRadarService.cs` - Service implementation
- `Services/BomRadar/RadarProduct.cs` - Product and location models
- `Services/BomRadar/RadarFrame.cs` - Frame model
- `Services/BomRadar/RadarFrameCollection.cs` - Frame management
- `Services/BomRadar/RadarLayerManager.cs` - Layer download and compositing
- `Controls/BomRadarPanel.cs` - UI panel

## Future Enhancements

- Additional radar locations (Melbourne, Brisbane, etc.)
- Overlay on map (OpenStreetMap integration)
- Storm tracking/prediction
- Alerts for approaching rain
