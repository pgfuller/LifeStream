# LifeStream Architecture

## Overview

LifeStream is a personal life dashboard application that aggregates information from multiple sources (weather, space/science, financial, news) and presents it in a customizable high-tech display with historical data capture and trend visualization.

**Current Version:** 0.2.0

## Solution Structure

```
LifeStream.sln
├── Parallon.Core          (reference from QiD3 - reusable framework)
├── Parallon.WinForms      (reference from QiD3 - DevExpress UI base)
├── LifeStream.Core        (netstandard2.1 - domain, interfaces, services)
├── LifeStream.Desktop     (.NET 7-windows - WinForms UI)
└── LifeStream.Tests       (unit tests)
```

### Layer Responsibilities

| Layer | Project | Contents |
|-------|---------|----------|
| Domain | LifeStream.Core | Entities, interfaces, business rules |
| Application | LifeStream.Core | Services, orchestration |
| Infrastructure | LifeStream.Desktop | Data access, API clients, FTP clients |
| Presentation | LifeStream.Desktop | DevExpress WinForms UI |

## Implementation Status

### Completed (v0.2.0)

| Component | Status | Notes |
|-----------|--------|-------|
| Solution Structure | Done | Following Parallon patterns |
| Logging (Serilog) | Done | Rolling file logs, categorized |
| Main Form Shell | Done | DevExpress RibbonForm + DockManager |
| Layout Management | Done | Save/load layouts, Default + Minimal |
| Service Manager | Done | Register, start, stop, refresh services |
| APOD Service | Done | NASA API, catchup, history navigation, gallery |
| BOM Radar Service | Done | Multi-range (64/128/256/512km), playback, layers |
| BOM Forecast Service | Done | 7-day forecast, FTP data extraction |
| Services Panel | Done | Real-time service status display |
| System Monitor | Done | 1-sec sampling, CPU/Memory/Disk/Network, 4 charts, 1-hour buffer |

### Planned (Future)

| Component | Priority | Notes |
|-----------|----------|-------|
| Financial Service | Medium | Stock quotes, price charts |
| News/RSS Service | Medium | Feed aggregation |
| Tasks Panel | Medium | Local task management |
| YouTube Service | Low | Channel feed monitoring |

## Core Architecture

### Service Base Pattern

All information services extend `InformationServiceBase<TData>`:

```csharp
public abstract class InformationServiceBase<TData> : IInformationService
{
    // Events for UI binding
    event EventHandler<ServiceDataEventArgs>? DataReceived;
    event EventHandler<ServiceStatusChangedEventArgs>? StatusChanged;
    event EventHandler<ServiceErrorEventArgs>? ErrorOccurred;

    // Core operations
    void Start();
    void Stop();
    void RefreshNow();

    // Adaptive refresh via RefreshStrategy
    protected abstract Task<TData?> FetchDataAsync(CancellationToken ct);
    protected abstract void OnDataFetched(TData data, bool isNewData);
}
```

### Adaptive Refresh Strategy

Services use `AdaptiveRefreshStrategy` for intelligent polling:

- Configurable base interval, min/max bounds
- Tracks consecutive misses for retry logic
- Calculates next check time based on data patterns
- Supports manual refresh triggering

```csharp
public class AdaptiveRefreshStrategy
{
    public TimeSpan BaseInterval { get; }
    public TimeSpan MinInterval { get; }
    public TimeSpan MaxInterval { get; }
    public int MaxRetries { get; }

    public DateTime GetNextCheckTime();
    public TimeSpan GetDelayUntilNextCheck();
    public void RecordSuccess(bool dataChanged);
    public void RecordFailure();
}
```

### UI Binding Pattern

Panels bind to services via events:

```csharp
public void BindToService(TService service)
{
    service.DataReceived += OnDataReceived;
    service.StatusChanged += OnStatusChanged;
    service.ErrorOccurred += OnErrorOccurred;

    // Display current data if available
    if (service.CurrentData != null)
        DisplayData(service.CurrentData);
}
```

## Folder Structure

### Source Repository
```
LifeStream/
├── 0Plan/                              (project planning)
├── 1Design/
│   ├── Architecture.md                 (this file)
│   ├── FeatureDocuments/               (feature specifications)
│   ├── DesignDocuments/                (detailed designs)
│   └── InformationDocuments/           (external API docs)
├── 2Code/
│   ├── LifeStream.sln
│   ├── LifeStream.Core/
│   │   ├── Infrastructure/             (AppInfo, AppPaths, LoggingConfig)
│   │   └── Services/                   (base classes, interfaces)
│   ├── LifeStream.Desktop/
│   │   ├── Controls/                   (ApodPanel, BomRadarPanel, etc.)
│   │   ├── Forms/                      (MainForm, ImageBrowserForm)
│   │   ├── Infrastructure/             (LayoutManager)
│   │   └── Services/                   (Apod/, BomRadar/, BomForecast/)
│   └── LifeStream.Tests/
└── 3Test/
```

### Runtime Data Location
```
%APPDATA%\LifeStream/
├── Data/
│   ├── APOD/                           (cached APOD images + JSON)
│   ├── BOMRadar_{location}/
│   │   ├── {range}km/                  (radar frames + layers)
│   │   └── ...
│   └── BOMForecast_{location}/         (forecast JSON cache)
├── Logs/
│   └── LifeStream-YYYYMMDD.log         (rolling daily logs)
└── Layouts/
    ├── Default.xml
    └── Minimal.xml
```

## Technology Stack

| Component | Technology | Version |
|-----------|------------|---------|
| Framework | .NET 7 Windows | 7.0 |
| UI | DevExpress WinForms | 22.2.15 |
| Charts | DevExpress ChartControl | 22.2.15 |
| HTTP | HttpClient | Built-in |
| FTP | FtpWebRequest | Built-in |
| JSON | Newtonsoft.Json | 13.0.3 |
| Logging | Serilog | 3.1.1 |
| HTML Parsing | AngleSharp | 1.1.2 |
| Resilience | Polly | 8.3.1 |

## Key Design Decisions

1. **Service-per-source**: Each information source has its own service class with specific data fetching and caching logic.

2. **Event-driven UI**: Services emit events; panels subscribe and update on UI thread via InvokeRequired pattern.

3. **Local caching**: All fetched data is cached locally for offline viewing and history.

4. **Adaptive polling**: Services adjust polling frequency based on data change patterns.

5. **Multi-range support**: Services like BOM Radar support multiple configurations (ranges) with separate data stores.

6. **Image compositing**: Radar display composites multiple layers (background, topography, locations, radar, legend).

## Logging Categories

| Category | Purpose |
|----------|---------|
| LifeStream.Service | Service lifecycle, refresh operations |
| LifeStream.UI | Panel updates, layout changes |
| LifeStream.Data | Cache operations, file I/O |

## System Monitor Implementation

System Monitor is now complete with the following features:
- **1-second sampling** using Windows PerformanceCounters
- **Metrics collected**: System CPU %, App CPU %, Memory Used %, App Working Set, Disk R/W KB/s, Network In/Out KB/s
- **Ring buffer** stores 3600 samples (1 hour of history)
- **4 vertically stacked charts** with DevExpress ChartControl
- **Time range selector**: 1 min, 5 min, 15 min, 1 hour views

See `1Design/FeatureDocuments/SystemMonitor-Service.md` for detailed specification.

## Future Services

Next services to consider:
- **Financial Service** - Stock quotes and price charts via Alpha Vantage or Yahoo Finance
- **News/RSS Service** - RSS feed aggregation with configurable sources
- **Tasks Panel** - Local task management with priorities
