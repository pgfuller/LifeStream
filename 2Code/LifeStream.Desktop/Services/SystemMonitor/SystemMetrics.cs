using System;

namespace LifeStream.Desktop.Services.SystemMonitor;

/// <summary>
/// Represents a single snapshot of system performance metrics.
/// All metrics are captured together at 1-second intervals.
/// </summary>
public class SystemMetrics
{
    /// <summary>
    /// Timestamp when this sample was collected.
    /// </summary>
    public DateTime Timestamp { get; set; }

    // CPU Metrics

    /// <summary>
    /// Total system CPU usage (0-100%).
    /// </summary>
    public float SystemCpuPercent { get; set; }

    /// <summary>
    /// LifeStream application CPU usage (0-100%).
    /// </summary>
    public float AppCpuPercent { get; set; }

    // System Memory Metrics

    /// <summary>
    /// Total physical memory in MB (static value).
    /// </summary>
    public long MemoryTotalMB { get; set; }

    /// <summary>
    /// Available (free) physical memory in MB.
    /// </summary>
    public long MemoryAvailableMB { get; set; }

    /// <summary>
    /// System memory usage percentage (0-100%).
    /// Computed as: (Total - Available) / Total * 100
    /// </summary>
    public float MemoryUsedPercent { get; set; }

    /// <summary>
    /// Memory pages read/written per second.
    /// High values indicate memory pressure/thrashing.
    /// </summary>
    public float MemoryPagesPerSec { get; set; }

    // App Memory Metrics

    /// <summary>
    /// LifeStream working set (physical RAM) in MB.
    /// </summary>
    public long AppWorkingSetMB { get; set; }

    /// <summary>
    /// LifeStream private bytes (committed memory) in MB.
    /// Growing over time indicates a memory leak.
    /// </summary>
    public long AppPrivateBytesMB { get; set; }

    // Disk Metrics

    /// <summary>
    /// Disk read throughput in KB/sec (all physical disks).
    /// </summary>
    public float DiskReadKBps { get; set; }

    /// <summary>
    /// Disk write throughput in KB/sec (all physical disks).
    /// </summary>
    public float DiskWriteKBps { get; set; }

    // Network Metrics

    /// <summary>
    /// Network bytes received per second in KB/sec (all interfaces).
    /// </summary>
    public float NetworkReceivedKBps { get; set; }

    /// <summary>
    /// Network bytes sent per second in KB/sec (all interfaces).
    /// </summary>
    public float NetworkSentKBps { get; set; }

    /// <summary>
    /// Creates a copy of this metrics snapshot.
    /// </summary>
    public SystemMetrics Clone()
    {
        return new SystemMetrics
        {
            Timestamp = Timestamp,
            SystemCpuPercent = SystemCpuPercent,
            AppCpuPercent = AppCpuPercent,
            MemoryTotalMB = MemoryTotalMB,
            MemoryAvailableMB = MemoryAvailableMB,
            MemoryUsedPercent = MemoryUsedPercent,
            MemoryPagesPerSec = MemoryPagesPerSec,
            AppWorkingSetMB = AppWorkingSetMB,
            AppPrivateBytesMB = AppPrivateBytesMB,
            DiskReadKBps = DiskReadKBps,
            DiskWriteKBps = DiskWriteKBps,
            NetworkReceivedKBps = NetworkReceivedKBps,
            NetworkSentKBps = NetworkSentKBps
        };
    }
}
