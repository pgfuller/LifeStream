using System;
using System.Collections.Generic;
using System.Threading;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using Serilog;

namespace LifeStream.Desktop.Services.SystemMonitor;

/// <summary>
/// Service that collects system performance metrics at high frequency (1 second).
/// Provides real-time and historical data for CPU, memory, disk, and network monitoring.
/// </summary>
public class SystemMonitorService : IInformationService
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.Sources);

    private readonly int _bufferSize;
    private readonly TimeSpan _collectionInterval;
    private readonly RingBuffer<SystemMetrics> _history;
    private readonly SynchronizationContext? _syncContext;
    private MetricsCollector? _collector;
    private System.Threading.Timer? _collectionTimer;
    private SystemMetrics? _currentMetrics;
    private bool _isCollecting;
    private bool _disposed;
    private ServiceStatus _status = ServiceStatus.Stopped;
    private int _consecutiveFailures;
    private string? _lastError;
    private DateTime? _lastRefresh;
    private DateTime? _nextRefresh;

    /// <summary>
    /// Creates a new System Monitor service.
    /// </summary>
    /// <param name="bufferSize">Number of samples to retain in memory (default 3600 = 1 hour at 1/sec).</param>
    /// <param name="collectionIntervalMs">Collection interval in milliseconds (default 1000 = 1 second).</param>
    public SystemMonitorService(int bufferSize = 3600, int collectionIntervalMs = 1000)
    {
        _bufferSize = bufferSize;
        _collectionInterval = TimeSpan.FromMilliseconds(collectionIntervalMs);
        _history = new RingBuffer<SystemMetrics>(bufferSize);
        _syncContext = SynchronizationContext.Current;
    }

    #region IInformationService Implementation

    public string ServiceId => "SystemMonitor";
    public string Name => "System Monitor";
    public string SourceType => "SystemMonitor";

    public ServiceStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                var oldStatus = _status;
                _status = value;
                RaiseStatusChanged(oldStatus, value);
            }
        }
    }

    public bool IsRunning => _status == ServiceStatus.Running || _status == ServiceStatus.Degraded;
    public DateTime? LastRefresh => _lastRefresh;
    public DateTime? NextRefresh => _nextRefresh;
    public string? LastError => _lastError;
    public int ConsecutiveFailures => _consecutiveFailures;

    public event EventHandler<ServiceDataEventArgs>? DataReceived;
    public event EventHandler<ServiceStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<ServiceErrorEventArgs>? ErrorOccurred;

    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SystemMonitorService));

        if (IsRunning)
        {
            Log.Warning("SystemMonitorService.Start called but service is already running");
            return;
        }

        Log.Information("SystemMonitorService starting with {BufferSize} sample buffer, {Interval}ms interval",
            _bufferSize, _collectionInterval.TotalMilliseconds);

        Status = ServiceStatus.Starting;

        try
        {
            // Initialize the metrics collector
            _collector = new MetricsCollector();
            _collector.Initialize();

            // Start the collection timer
            _isCollecting = true;
            _collectionTimer = new System.Threading.Timer(
                CollectionTimerCallback,
                null,
                TimeSpan.FromMilliseconds(100), // Short delay to let UI settle
                _collectionInterval);

            Status = ServiceStatus.Running;
            Log.Information("SystemMonitorService started");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start SystemMonitorService");
            _lastError = ex.Message;
            Status = ServiceStatus.Faulted;
            throw;
        }
    }

    public void Stop()
    {
        if (_status == ServiceStatus.Stopped)
            return;

        Log.Information("SystemMonitorService stopping");
        Status = ServiceStatus.Stopping;

        _isCollecting = false;

        // Stop and dispose timer
        _collectionTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _collectionTimer?.Dispose();
        _collectionTimer = null;

        // Dispose collector
        _collector?.Dispose();
        _collector = null;

        Status = ServiceStatus.Stopped;
        Log.Information("SystemMonitorService stopped. Collected {Count} samples.", _history.Count);
    }

    public void RefreshNow()
    {
        // For a 1-second service, RefreshNow doesn't make much sense,
        // but we can force an immediate collection
        if (IsRunning && _collectionTimer != null)
        {
            _collectionTimer.Change(TimeSpan.Zero, _collectionInterval);
        }
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the current (most recent) metrics snapshot.
    /// </summary>
    public SystemMetrics? CurrentMetrics => _currentMetrics;

    /// <summary>
    /// Gets the number of samples currently stored.
    /// </summary>
    public int SampleCount => _history.Count;

    /// <summary>
    /// Gets the buffer capacity.
    /// </summary>
    public int BufferCapacity => _bufferSize;

    /// <summary>
    /// Gets the collection interval.
    /// </summary>
    public TimeSpan CollectionInterval => _collectionInterval;

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets historical metrics for a specified duration.
    /// </summary>
    /// <param name="duration">Duration of history to retrieve.</param>
    /// <returns>List of metrics samples in chronological order (oldest first).</returns>
    public List<SystemMetrics> GetHistory(TimeSpan duration)
    {
        int samplesNeeded = (int)(duration.TotalSeconds / _collectionInterval.TotalSeconds);
        return _history.GetRecent(samplesNeeded);
    }

    /// <summary>
    /// Gets the most recent N samples.
    /// </summary>
    /// <param name="count">Number of samples to retrieve.</param>
    /// <returns>List of metrics samples in chronological order (oldest first).</returns>
    public List<SystemMetrics> GetRecentSamples(int count)
    {
        return _history.GetRecent(count);
    }

    /// <summary>
    /// Gets all stored samples.
    /// </summary>
    /// <returns>List of all metrics samples in chronological order (oldest first).</returns>
    public List<SystemMetrics> GetAllSamples()
    {
        return _history.ToList();
    }

    #endregion

    #region Private Methods

    private void CollectionTimerCallback(object? state)
    {
        if (!_isCollecting || _collector == null || _disposed)
            return;

        try
        {
            // Collect metrics
            var metrics = _collector.Collect();

            // Store in history
            _history.Add(metrics);
            _currentMetrics = metrics;

            // Update tracking
            _lastRefresh = DateTime.Now;
            _nextRefresh = _lastRefresh.Value + _collectionInterval;
            _consecutiveFailures = 0;
            _lastError = null;

            if (_status == ServiceStatus.Degraded)
            {
                Status = ServiceStatus.Running;
            }

            // Notify listeners on UI thread
            RaiseDataReceived(metrics, isNewData: true);
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _lastError = ex.Message;
            Log.Warning(ex, "Error collecting system metrics (failure {Count})", _consecutiveFailures);

            if (_consecutiveFailures >= 10)
            {
                Status = ServiceStatus.Faulted;
                RaiseError("Collection failed repeatedly", ex, willRetry: false);
            }
            else
            {
                Status = ServiceStatus.Degraded;
                RaiseError("Collection error", ex, willRetry: true);
            }
        }
    }

    private void RaiseDataReceived(SystemMetrics data, bool isNewData)
    {
        var args = new ServiceDataEventArgs(this, data, isNewData, DateTime.Now);
        InvokeOnUIThread(() => DataReceived?.Invoke(this, args));
    }

    private void RaiseStatusChanged(ServiceStatus oldStatus, ServiceStatus newStatus)
    {
        var args = new ServiceStatusChangedEventArgs(this, oldStatus, newStatus);
        InvokeOnUIThread(() => StatusChanged?.Invoke(this, args));
    }

    private void RaiseError(string message, Exception? exception, bool willRetry)
    {
        var args = new ServiceErrorEventArgs(this, message, exception, willRetry, willRetry ? DateTime.Now + _collectionInterval : null);
        InvokeOnUIThread(() => ErrorOccurred?.Invoke(this, args));
    }

    private void InvokeOnUIThread(Action action)
    {
        if (_syncContext != null)
        {
            _syncContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    #endregion
}
