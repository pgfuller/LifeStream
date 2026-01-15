# System Monitor Service - System Performance Metrics

## Overview

The System Monitor service collects high-frequency system performance metrics (CPU, memory, disk, network) with distinction between system-wide and application-specific usage. Provides historical data for trend visualization and issue investigation.

**Status:** In Development

## Goals

1. **High-frequency collection:** Sample all metrics every 1 second (uniform)
2. **System vs Application:** Distinguish between total system load and LifeStream's own usage
3. **Historical storage:** Retain data for hours/days of analysis
4. **Chart visualization:** Real-time and historical trend charts
5. **Low overhead:** Minimize impact on system being monitored
6. **Always-on capture:** Collect whenever LifeStream is running

## Metrics

All metrics collected at **1-second intervals** (uniform sampling for simplicity and correlation).

### CPU Metrics
| Metric | PerformanceCounter | Description |
|--------|-------------------|-------------|
| System CPU % | `Processor` / `% Processor Time` / `_Total` | Total system CPU load |
| App CPU % | `Process` / `% Processor Time` / `LifeStream.Desktop` | LifeStream's CPU usage |

### Memory Metrics
| Metric | Source | Description |
|--------|--------|-------------|
| Available MB | `Memory` / `Available MBytes` | Free physical memory |
| Used % | Computed: `(Total - Available) / Total` | System memory pressure |
| Pages/sec | `Memory` / `Pages/sec` | Paging activity (high = thrashing) |
| App Working Set MB | `Process` / `Working Set` / `LifeStream.Desktop` | LifeStream physical RAM |
| App Private Bytes MB | `Process` / `Private Bytes` / `LifeStream.Desktop` | LifeStream committed memory (leak detection) |

### Disk Metrics
| Metric | PerformanceCounter | Description |
|--------|-------------------|-------------|
| Read KB/sec | `PhysicalDisk` / `Disk Read Bytes/sec` / `_Total` | Disk read throughput |
| Write KB/sec | `PhysicalDisk` / `Disk Write Bytes/sec` / `_Total` | Disk write throughput |

### Network Metrics
| Metric | PerformanceCounter | Description |
|--------|-------------------|-------------|
| Received KB/sec | `Network Interface` / `Bytes Received/sec` | Aggregate all interfaces |
| Sent KB/sec | `Network Interface` / `Bytes Sent/sec` | Aggregate all interfaces |

## Data Model

```csharp
public class SystemMetrics
{
    public DateTime Timestamp { get; set; }

    // CPU
    public float SystemCpuPercent { get; set; }    // Total system (0-100)
    public float AppCpuPercent { get; set; }       // LifeStream only (0-100)

    // System Memory
    public long MemoryTotalMB { get; set; }        // Total physical (static)
    public long MemoryAvailableMB { get; set; }    // Free physical
    public float MemoryUsedPercent { get; set; }   // System-wide pressure
    public float MemoryPagesPerSec { get; set; }   // Paging activity

    // App Memory
    public long AppWorkingSetMB { get; set; }      // LifeStream physical RAM
    public long AppPrivateBytesMB { get; set; }    // LifeStream committed

    // Disk
    public float DiskReadKBps { get; set; }
    public float DiskWriteKBps { get; set; }

    // Network
    public float NetworkReceivedKBps { get; set; }
    public float NetworkSentKBps { get; set; }
}
```

## Storage Strategy

### In-Memory Ring Buffer
- **Size:** 3,600 samples (1 hour at 1/sec)
- **Purpose:** Real-time chart display
- **Structure:** Circular buffer, overwrites oldest when full
- **Memory:** ~150KB for 3,600 samples

### Periodic Persistence (Phase 2)
- Write aggregated data (min/max/avg) every 5 minutes
- SQLite storage for historical analysis
- Automatic retention/cleanup

### Retention Policy
| Resolution | Retention | Purpose |
|------------|-----------|---------|
| 1 sec | 1 hour | Real-time display (in-memory) |
| 1 min avg | 24 hours | Recent history |
| 5 min avg | 7 days | Trend analysis |

## Service Architecture

### MetricsCollector
Wraps PerformanceCounter initialization and reading:

```csharp
public class MetricsCollector : IDisposable
{
    // Initializes all counters, handles first-read priming
    public MetricsCollector();

    // Collects all metrics in one call
    public SystemMetrics Collect();
}
```

### SystemMonitorService
Extends `InformationServiceBase<SystemMetrics>`:

```csharp
public class SystemMonitorService : InformationServiceBase<SystemMetrics>
{
    // Ring buffer for history
    private readonly RingBuffer<SystemMetrics> _history;

    // High-frequency timer (1 sec)
    private readonly Timer _collectionTimer;

    // Access to historical data
    public IReadOnlyList<SystemMetrics> GetHistory(TimeSpan duration);
}
```

## UI Panel Design

### Initial Layout
System Monitor panel fills the main display area (using `DockingStyle.Fill`).

**Note:** Panel creation order is critical for Fill to work correctly. The Fill panel must be created last after all edge-docked panels.

### Panel Layout
```
┌─────────────────────────────────────────────────────────────────────────┐
│ System Monitor                                    1h ▼       Refresh    │
├─────────────────────────────────────────────────────────────────────────┤
│ CPU: Sys 45% / App 2%  │  Mem: 67% (App 180MB)  │  Disk  │  Network    │
├─────────────────────────┬─────────────────────────┬────────┬────────────┤
│                         │                         │        │            │
│      [CPU Chart]        │    [Memory Chart]       │ [Disk] │ [Network]  │
│                         │                         │        │            │
│  ── System (blue)       │  ── Used % (blue)       │  Read  │  Received  │
│  ── App (green)         │  ── App MB (green)      │  Write │  Sent      │
│                         │                         │        │            │
└─────────────────────────┴─────────────────────────┴────────┴────────────┘
│ Samples: 3,600 | Collecting every 1s | Pages/sec: 12                    │
└─────────────────────────────────────────────────────────────────────────┘
```

### Chart Specifications

| Chart | Series | Y-Axis | Color |
|-------|--------|--------|-------|
| CPU | System CPU % | 0-100 fixed | Blue |
| CPU | App CPU % | 0-100 fixed | Green |
| Memory | Used % | 0-100 fixed | Blue |
| Memory | App Working Set | Auto-scale MB | Green |
| Disk | Read KB/s | Auto-scale | Blue |
| Disk | Write KB/s | Auto-scale | Orange |
| Network | Received KB/s | Auto-scale | Blue |
| Network | Sent KB/s | Auto-scale | Orange |

### Time Range Selector
- 1 minute (60 samples)
- 5 minutes (300 samples)
- 15 minutes (900 samples)
- 1 hour (3,600 samples)

### Chart Features
- DevExpress `ChartControl` with `SplineAreaSeries` for smooth visualization
- Semi-transparent fill under lines
- Grid lines for readability
- Legend showing series names
- Crosshair for hover values

## Implementation Phases

### Phase 1: Core Implementation
1. `SystemMetrics` data model
2. `MetricsCollector` with PerformanceCounter wrappers
3. `RingBuffer<T>` for in-memory history
4. `SystemMonitorService` with 1-second collection
5. `SystemMonitorPanel` with 4 charts
6. Integration into MainForm (Fill panel)

### Phase 2: Persistence (Future)
1. SQLite storage schema
2. Aggregation service (downsample)
3. Load historical data on startup
4. Retention/cleanup job

### Phase 3: Enhancements (Future)
1. Per-core CPU expansion
2. Alerts/thresholds
3. Export to CSV
4. Background Windows Service for continuous collection

## Technical Notes

### PerformanceCounter Initialization
```csharp
// Counters must be "primed" - first read returns 0
var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
cpuCounter.NextValue(); // Prime the counter
Thread.Sleep(100);      // Brief delay
var actualValue = cpuCounter.NextValue(); // Now returns real value
```

### Network Interface Aggregation
```csharp
// Sum across all active network interfaces
var category = new PerformanceCounterCategory("Network Interface");
foreach (var instance in category.GetInstanceNames())
{
    // Aggregate bytes received/sent from each interface
}
```

### Process Name for App Metrics
```csharp
// Get current process name (without .exe)
var processName = Process.GetCurrentProcess().ProcessName;
// Use this for Process category instance name
```

### Thread Safety
- Collection runs on background timer thread
- Ring buffer uses lock for thread-safe access
- UI updates via `BeginInvoke` for thread marshaling

## File Structure

```
Services/SystemMonitor/
├── SystemMonitorService.cs    (main service)
├── SystemMetrics.cs           (data model)
├── MetricsCollector.cs        (PerformanceCounter wrapper)
└── RingBuffer.cs              (circular buffer)

Controls/
└── SystemMonitorPanel.cs      (UI with charts)
```

## Configuration

| Parameter | Default | Description |
|-----------|---------|-------------|
| collectionInterval | 1 sec | Sampling frequency |
| bufferSize | 3600 | Ring buffer capacity (1 hour) |
| chartUpdateInterval | 1 sec | UI refresh rate |

## Future: Background Service (Low Priority)

A Windows Service that runs independently to collect metrics even when LifeStream UI is not running.

### Benefits
- Complete historical coverage
- Investigate issues that occurred before LifeStream was opened

### Approach
- Separate Windows Service project
- Shared SQLite database in AppData
- LifeStream reads from shared store on startup
