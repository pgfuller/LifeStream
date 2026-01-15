# System Monitor Service - System Performance Metrics

## Overview

The System Monitor service collects high-frequency system performance metrics (CPU, memory, disk, network) and provides historical data for trend visualization. Designed for investigating system issues with historical context.

**Status:** Planned (next implementation)

## Goals

1. **High-frequency collection:** Sample metrics every 1-5 seconds
2. **Historical storage:** Retain data for hours/days of analysis
3. **Chart visualization:** Real-time and historical trend charts
4. **Low overhead:** Minimize impact on system being monitored
5. **Always-on capture:** Collect whenever LifeStream is running

## Metrics to Collect

### CPU
| Metric | Source | Update Rate |
|--------|--------|-------------|
| Total CPU % | PerformanceCounter | 1 sec |
| Per-core % (optional) | PerformanceCounter | 1 sec |
| Process count | Process.GetProcesses | 5 sec |

### Memory
| Metric | Source | Update Rate |
|--------|--------|-------------|
| Used MB | PerformanceCounter | 2 sec |
| Available MB | PerformanceCounter | 2 sec |
| Used % | Computed | 2 sec |
| Page faults/sec | PerformanceCounter | 5 sec |

### Disk
| Metric | Source | Update Rate |
|--------|--------|-------------|
| Read KB/sec | PerformanceCounter | 2 sec |
| Write KB/sec | PerformanceCounter | 2 sec |
| Disk queue length | PerformanceCounter | 2 sec |
| Free space % (per drive) | DriveInfo | 60 sec |

### Network
| Metric | Source | Update Rate |
|--------|--------|-------------|
| Bytes received/sec | PerformanceCounter | 2 sec |
| Bytes sent/sec | PerformanceCounter | 2 sec |
| Packets/sec | PerformanceCounter | 5 sec |

## Data Model

```csharp
public class SystemMetrics
{
    public DateTime Timestamp { get; set; }

    // CPU
    public float CpuPercent { get; set; }
    public float[]? CpuPerCore { get; set; }  // Optional
    public int ProcessCount { get; set; }

    // Memory
    public long MemoryUsedMB { get; set; }
    public long MemoryAvailableMB { get; set; }
    public float MemoryPercent { get; set; }

    // Disk
    public float DiskReadKBps { get; set; }
    public float DiskWriteKBps { get; set; }
    public float DiskQueueLength { get; set; }

    // Network (aggregate all interfaces)
    public float NetworkReceivedKBps { get; set; }
    public float NetworkSentKBps { get; set; }
}

public class DriveMetrics
{
    public string DriveLetter { get; set; }
    public long TotalGB { get; set; }
    public long FreeGB { get; set; }
    public float FreePercent { get; set; }
}
```

## Storage Strategy

### In-Memory Ring Buffer
- Last N samples (e.g., 3600 = 1 hour at 1/sec)
- Used for real-time chart display
- No disk I/O overhead

### Periodic Persistence (Optional)
- Write aggregated data (min/max/avg) every 5 minutes
- SQLite or JSON file per day
- Enables historical analysis across sessions

### Retention Policy
| Resolution | Retention | Purpose |
|------------|-----------|---------|
| 1 sec | 1 hour | Real-time display |
| 1 min avg | 24 hours | Recent history |
| 5 min avg | 7 days | Trend analysis |
| 1 hour avg | 30 days | Long-term patterns |

## UI Panel Design

### Primary Display: Multi-Chart View
```
┌─────────────────────────────────────────────────────────────┐
│ System Monitor                            1h ▼    Refresh   │
├─────────────────────────────────────────────────────────────┤
│ CPU: 23%  │  Memory: 67%  │  Disk R/W  │  Network I/O      │
├─────────────┬─────────────┬─────────────┬───────────────────┤
│   [Chart]   │   [Chart]   │   [Chart]   │     [Chart]       │
│    CPU %    │    Mem %    │   KB/sec    │     KB/sec        │
│    0-100    │    0-100    │   0-auto    │     0-auto        │
└─────────────┴─────────────┴─────────────┴───────────────────┘
│ Updated: 12:30:45 | Samples: 3,600 | C: 2.1GB | D: 156GB    │
└─────────────────────────────────────────────────────────────┘
```

### Chart Features
- **Time range selector:** 1 min, 5 min, 15 min, 1 hour, 24 hours
- **Auto-scaling Y axis** for disk/network
- **Fixed 0-100 scale** for percentages
- **Smooth line rendering** with DevExpress SplineAreaSeries
- **Grid lines** for readability
- **Hover tooltip** showing exact values

### Status Bar
- Current values (CPU %, Memory %)
- Sample count in buffer
- Drive free space summary

## Implementation Plan

### Phase 1: Core Service
1. Create `SystemMonitorService` extending `InformationServiceBase`
2. Implement `PerformanceCounter` collection for CPU/Memory
3. Ring buffer for in-memory storage
4. High-frequency timer (1 second)

### Phase 2: UI Panel
1. Create `SystemMonitorPanel` with 4 charts
2. DevExpress `ChartControl` with `SplineAreaSeries`
3. Time range selector (ComboBox)
4. Current value display
5. Bind to service data events

### Phase 3: Data Persistence
1. Aggregation service (downsample for storage)
2. SQLite storage for historical data
3. Automatic retention/cleanup
4. Load historical data on startup

### Phase 4: Enhancements (Future)
1. Per-core CPU view (expandable)
2. Process list view (top N by CPU/memory)
3. Alerts/thresholds (notify when CPU > 90%)
4. Export to CSV

## Technical Considerations

### PerformanceCounter Initialization
```csharp
// Must be initialized before first read
var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
cpuCounter.NextValue(); // First call returns 0, primes the counter
Thread.Sleep(100);      // Small delay before real readings
var cpuPercent = cpuCounter.NextValue();
```

### Network Interface Aggregation
```csharp
// Aggregate all active network interfaces
var category = new PerformanceCounterCategory("Network Interface");
var instances = category.GetInstanceNames();
// Sum values across all instances
```

### Thread Safety
- Collection on background thread
- UI updates via InvokeRequired pattern
- Ring buffer with lock or ConcurrentQueue

### Resource Usage
- PerformanceCounters are lightweight
- Ring buffer: ~50KB for 3600 samples
- Chart rendering is the main CPU cost (limit update frequency)

## File Structure

```
Services/SystemMonitor/
├── SystemMonitorService.cs    (main service)
├── SystemMetrics.cs           (data model)
├── MetricsCollector.cs        (PerformanceCounter wrapper)
├── MetricsRingBuffer.cs       (in-memory storage)
└── MetricsAggregator.cs       (downsampling for storage)

Controls/
└── SystemMonitorPanel.cs      (UI with charts)
```

## Configuration

| Parameter | Default | Description |
|-----------|---------|-------------|
| sampleInterval | 1 sec | Collection frequency |
| bufferSize | 3600 | Samples in ring buffer |
| chartUpdateInterval | 1 sec | UI refresh rate |
| persistInterval | 5 min | Write to disk frequency |
| retentionDays | 7 | Historical data retention |

## Future: Background Service

**Low Priority:** A Windows Service that runs independently to collect metrics even when LifeStream UI is not running.

### Approach
1. Separate Windows Service project
2. Shared data store (SQLite in AppData)
3. LifeStream reads from shared store
4. Service writes continuously

### Benefits
- Complete historical coverage
- Investigate issues that occurred before LifeStream was opened

### Considerations
- Additional deployment complexity
- Service installation/permissions
- Inter-process communication

---

## References

- [PerformanceCounter Class](https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.performancecounter)
- [DevExpress ChartControl](https://docs.devexpress.com/WindowsForms/DevExpress.XtraCharts.ChartControl)
- [Ring Buffer Pattern](https://en.wikipedia.org/wiki/Circular_buffer)
