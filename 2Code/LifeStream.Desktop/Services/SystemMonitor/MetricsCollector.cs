using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using LifeStream.Core.Infrastructure;
using Serilog;

namespace LifeStream.Desktop.Services.SystemMonitor;

/// <summary>
/// Collects system performance metrics using PerformanceCounters.
/// Handles initialization, priming, and aggregation of multiple counters.
/// </summary>
public class MetricsCollector : IDisposable
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.Sources);

    // CPU counters
    private PerformanceCounter? _systemCpuCounter;
    private PerformanceCounter? _appCpuCounter;

    // Memory counters
    private PerformanceCounter? _availableMemoryCounter;
    private PerformanceCounter? _pagesPerSecCounter;
    private PerformanceCounter? _appWorkingSetCounter;
    private PerformanceCounter? _appPrivateBytesCounter;

    // Disk counters
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;

    // Network counters (one per interface)
    private List<PerformanceCounter>? _networkReceivedCounters;
    private List<PerformanceCounter>? _networkSentCounters;

    // Static system info
    private long _totalMemoryMB;
    private string _processName;
    private bool _isInitialized;
    private bool _disposed;

    public MetricsCollector()
    {
        _processName = Process.GetCurrentProcess().ProcessName;
        _totalMemoryMB = GetTotalPhysicalMemory();
    }

    /// <summary>
    /// Initializes all performance counters. Must be called before Collect().
    /// Counters are "primed" with an initial read.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized)
            return;

        Log.Information("Initializing MetricsCollector for process: {ProcessName}", _processName);

        try
        {
            // CPU counters
            _systemCpuCounter = CreateCounter("Processor", "% Processor Time", "_Total");
            _appCpuCounter = CreateCounter("Process", "% Processor Time", _processName);

            // Memory counters
            _availableMemoryCounter = CreateCounter("Memory", "Available MBytes", null);
            _pagesPerSecCounter = CreateCounter("Memory", "Pages/sec", null);
            _appWorkingSetCounter = CreateCounter("Process", "Working Set", _processName);
            _appPrivateBytesCounter = CreateCounter("Process", "Private Bytes", _processName);

            // Disk counters
            _diskReadCounter = CreateCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            _diskWriteCounter = CreateCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

            // Network counters - aggregate all interfaces
            InitializeNetworkCounters();

            // Prime all counters (first read returns 0 for rate-based counters)
            PrimeCounters();

            _isInitialized = true;
            Log.Information("MetricsCollector initialized successfully. Total memory: {TotalMB} MB", _totalMemoryMB);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize MetricsCollector");
            throw;
        }
    }

    /// <summary>
    /// Collects all metrics and returns a snapshot.
    /// </summary>
    public SystemMetrics Collect()
    {
        if (!_isInitialized)
            throw new InvalidOperationException("MetricsCollector must be initialized before collecting.");

        var metrics = new SystemMetrics
        {
            Timestamp = DateTime.Now,
            MemoryTotalMB = _totalMemoryMB
        };

        // CPU
        metrics.SystemCpuPercent = SafeNextValue(_systemCpuCounter);
        metrics.AppCpuPercent = SafeNextValue(_appCpuCounter) / Environment.ProcessorCount; // Normalize to 0-100

        // Memory
        metrics.MemoryAvailableMB = (long)SafeNextValue(_availableMemoryCounter);
        metrics.MemoryUsedPercent = _totalMemoryMB > 0
            ? (float)(_totalMemoryMB - metrics.MemoryAvailableMB) / _totalMemoryMB * 100f
            : 0;
        metrics.MemoryPagesPerSec = SafeNextValue(_pagesPerSecCounter);

        // App memory (convert bytes to MB)
        metrics.AppWorkingSetMB = (long)(SafeNextValue(_appWorkingSetCounter) / (1024 * 1024));
        metrics.AppPrivateBytesMB = (long)(SafeNextValue(_appPrivateBytesCounter) / (1024 * 1024));

        // Disk (convert bytes to KB)
        metrics.DiskReadKBps = SafeNextValue(_diskReadCounter) / 1024f;
        metrics.DiskWriteKBps = SafeNextValue(_diskWriteCounter) / 1024f;

        // Network (aggregate all interfaces, convert bytes to KB)
        metrics.NetworkReceivedKBps = AggregateCounters(_networkReceivedCounters) / 1024f;
        metrics.NetworkSentKBps = AggregateCounters(_networkSentCounters) / 1024f;

        return metrics;
    }

    private PerformanceCounter? CreateCounter(string category, string counter, string? instance)
    {
        try
        {
            var pc = instance != null
                ? new PerformanceCounter(category, counter, instance, readOnly: true)
                : new PerformanceCounter(category, counter, readOnly: true);

            return pc;
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to create counter {Category}/{Counter}/{Instance}: {Error}",
                category, counter, instance ?? "(none)", ex.Message);
            return null;
        }
    }

    private void InitializeNetworkCounters()
    {
        _networkReceivedCounters = new List<PerformanceCounter>();
        _networkSentCounters = new List<PerformanceCounter>();

        try
        {
            var category = new PerformanceCounterCategory("Network Interface");
            var instances = category.GetInstanceNames();

            foreach (var instance in instances)
            {
                // Skip loopback and virtual adapters if desired
                // For now, include all interfaces

                var receivedCounter = CreateCounter("Network Interface", "Bytes Received/sec", instance);
                var sentCounter = CreateCounter("Network Interface", "Bytes Sent/sec", instance);

                if (receivedCounter != null)
                    _networkReceivedCounters.Add(receivedCounter);
                if (sentCounter != null)
                    _networkSentCounters.Add(sentCounter);
            }

            Log.Debug("Initialized {Count} network interface counters", _networkReceivedCounters.Count);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to initialize network counters: {Error}", ex.Message);
        }
    }

    private void PrimeCounters()
    {
        // Rate-based counters need two reads to calculate a rate
        // First read initializes them, second read (after delay) gives real value

        SafeNextValue(_systemCpuCounter);
        SafeNextValue(_appCpuCounter);
        SafeNextValue(_diskReadCounter);
        SafeNextValue(_diskWriteCounter);
        SafeNextValue(_pagesPerSecCounter);

        foreach (var counter in _networkReceivedCounters ?? Enumerable.Empty<PerformanceCounter>())
            SafeNextValue(counter);
        foreach (var counter in _networkSentCounters ?? Enumerable.Empty<PerformanceCounter>())
            SafeNextValue(counter);

        // Brief delay to allow counters to accumulate data
        System.Threading.Thread.Sleep(100);
    }

    private float SafeNextValue(PerformanceCounter? counter)
    {
        if (counter == null)
            return 0f;

        try
        {
            return counter.NextValue();
        }
        catch (Exception ex)
        {
            Log.Debug("Error reading counter {Counter}: {Error}", counter.CounterName, ex.Message);
            return 0f;
        }
    }

    private float AggregateCounters(List<PerformanceCounter>? counters)
    {
        if (counters == null || counters.Count == 0)
            return 0f;

        float total = 0f;
        foreach (var counter in counters)
        {
            total += SafeNextValue(counter);
        }
        return total;
    }

    private static long GetTotalPhysicalMemory()
    {
        try
        {
            // Use GC to get approximate total memory
            // For more accurate value, could use WMI or Microsoft.VisualBasic.Devices.ComputerInfo
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            return gcMemoryInfo.TotalAvailableMemoryBytes / (1024 * 1024);
        }
        catch
        {
            // Fallback: estimate from available memory (not ideal)
            return 16384; // Default 16GB assumption
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _systemCpuCounter?.Dispose();
        _appCpuCounter?.Dispose();
        _availableMemoryCounter?.Dispose();
        _pagesPerSecCounter?.Dispose();
        _appWorkingSetCounter?.Dispose();
        _appPrivateBytesCounter?.Dispose();
        _diskReadCounter?.Dispose();
        _diskWriteCounter?.Dispose();

        if (_networkReceivedCounters != null)
        {
            foreach (var counter in _networkReceivedCounters)
                counter.Dispose();
        }

        if (_networkSentCounters != null)
        {
            foreach (var counter in _networkSentCounters)
                counter.Dispose();
        }

        Log.Debug("MetricsCollector disposed");
    }
}
