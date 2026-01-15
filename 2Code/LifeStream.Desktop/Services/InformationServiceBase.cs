using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using Serilog;

namespace LifeStream.Desktop.Services;

/// <summary>
/// Base class for polling-based information services.
/// Provides common functionality for scheduling, error handling, and UI thread marshaling.
/// </summary>
public abstract class InformationServiceBase : IInformationService
{
    private readonly ILogger _log;
    private readonly SynchronizationContext? _syncContext;
    private System.Threading.Timer? _timer;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private bool _isFetching;
    private readonly object _lock = new();

    // Retry backoff configuration
    private static readonly TimeSpan[] BackoffIntervals = new[]
    {
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30)
    };

    protected InformationServiceBase(string serviceId, string name, string sourceType)
    {
        ServiceId = serviceId;
        Name = name;
        SourceType = sourceType;
        _log = LoggingConfig.ForCategory($"{LoggingConfig.Categories.Sources}.{sourceType}");
        _syncContext = SynchronizationContext.Current;
    }

    #region IInformationService Properties

    public string ServiceId { get; }
    public string Name { get; }
    public string SourceType { get; }

    private ServiceStatus _status = ServiceStatus.Stopped;
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

    public bool IsRunning => Status == ServiceStatus.Running || Status == ServiceStatus.Degraded;
    public DateTime? LastRefresh { get; protected set; }
    public DateTime? NextRefresh { get; protected set; }
    public string? LastError { get; private set; }
    public int ConsecutiveFailures { get; private set; }

    #endregion

    #region Configuration Properties

    /// <summary>
    /// Normal polling interval when service is running.
    /// </summary>
    protected abstract TimeSpan RefreshInterval { get; }

    /// <summary>
    /// Maximum number of retries before marking as faulted.
    /// </summary>
    protected virtual int MaxRetries => 10;

    #endregion

    #region IInformationService Methods

    public void Start()
    {
        lock (_lock)
        {
            if (Status != ServiceStatus.Stopped && Status != ServiceStatus.Faulted)
            {
                _log.Warning("{Service} Start called but status is {Status}", Name, Status);
                return;
            }

            _log.Information("Starting service: {Service}", Name);
            Status = ServiceStatus.Starting;

            try
            {
                _cts = new CancellationTokenSource();

                // Perform synchronous initialization (including catchup)
                OnInitialize();

                // Start the polling timer
                _timer = new System.Threading.Timer(
                    OnTimerCallback,
                    null,
                    TimeSpan.Zero, // Fire immediately
                    Timeout.InfiniteTimeSpan); // Manual reschedule

                Status = ServiceStatus.Running;
                _log.Information("Service started: {Service}", Name);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to start service: {Service}", Name);
                LastError = ex.Message;
                Status = ServiceStatus.Faulted;
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (Status == ServiceStatus.Stopped)
            {
                return;
            }

            _log.Information("Stopping service: {Service}", Name);
            Status = ServiceStatus.Stopping;

            try
            {
                _cts?.Cancel();
                _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                _timer?.Dispose();
                _timer = null;

                // Perform synchronous cleanup
                OnShutdown();

                _cts?.Dispose();
                _cts = null;

                Status = ServiceStatus.Stopped;
                _log.Information("Service stopped: {Service}", Name);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Error stopping service: {Service}", Name);
                Status = ServiceStatus.Stopped;
            }
        }
    }

    public void RefreshNow()
    {
        if (!IsRunning)
        {
            _log.Warning("{Service} RefreshNow called but service is not running", Name);
            return;
        }

        _log.Debug("{Service} Manual refresh requested", Name);

        // Reset the timer to fire immediately
        _timer?.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    #endregion

    #region Events

    public event EventHandler<ServiceDataEventArgs>? DataReceived;
    public event EventHandler<ServiceStatusChangedEventArgs>? StatusChanged;
    public event EventHandler<ServiceErrorEventArgs>? ErrorOccurred;

    protected void RaiseDataReceived(object data, bool isNewData)
    {
        var args = new ServiceDataEventArgs(this, data, isNewData, DateTime.Now);
        InvokeOnUIThread(() => DataReceived?.Invoke(this, args));
    }

    private void RaiseStatusChanged(ServiceStatus oldStatus, ServiceStatus newStatus)
    {
        var args = new ServiceStatusChangedEventArgs(this, oldStatus, newStatus);
        InvokeOnUIThread(() => StatusChanged?.Invoke(this, args));
    }

    protected void RaiseError(string message, Exception? exception, bool willRetry, DateTime? nextRetry)
    {
        var args = new ServiceErrorEventArgs(this, message, exception, willRetry, nextRetry);
        InvokeOnUIThread(() => ErrorOccurred?.Invoke(this, args));
    }

    #endregion

    #region Abstract Methods

    /// <summary>
    /// Called during Start() for synchronous initialization.
    /// Use this for database setup, catchup queries, etc.
    /// </summary>
    protected virtual void OnInitialize() { }

    /// <summary>
    /// Called during Stop() for synchronous cleanup.
    /// </summary>
    protected virtual void OnShutdown() { }

    /// <summary>
    /// Fetches data from the source. Called on a background thread.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The fetched data, or null if no new data.</returns>
    protected abstract Task<object?> FetchDataAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores fetched data to the database. Called on the UI thread.
    /// </summary>
    /// <param name="data">The data to store.</param>
    protected abstract void StoreData(object data);

    /// <summary>
    /// Compares new data with previous data to detect changes.
    /// </summary>
    /// <param name="newData">The newly fetched data.</param>
    /// <param name="previousData">The previous data (may be null).</param>
    /// <returns>True if the data has changed.</returns>
    protected virtual bool HasDataChanged(object newData, object? previousData) => true;

    #endregion

    #region Timer and Fetch Logic

    private object? _lastData;

    private async void OnTimerCallback(object? state)
    {
        if (_disposed || _cts?.IsCancellationRequested == true)
        {
            return;
        }

        // Prevent concurrent fetches
        lock (_lock)
        {
            if (_isFetching)
            {
                return;
            }
            _isFetching = true;
        }

        try
        {
            await PerformFetchAsync();
        }
        finally
        {
            lock (_lock)
            {
                _isFetching = false;
            }

            // Schedule next refresh
            ScheduleNextRefresh();
        }
    }

    private async Task PerformFetchAsync()
    {
        _log.Debug("{Service} Starting fetch", Name);

        try
        {
            var data = await FetchDataAsync(_cts?.Token ?? CancellationToken.None);

            if (data != null)
            {
                var isNewData = HasDataChanged(data, _lastData);
                _lastData = data;

                // Store and raise event on UI thread
                InvokeOnUIThread(() =>
                {
                    try
                    {
                        StoreData(data);
                        RaiseDataReceived(data, isNewData);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "{Service} Error storing data", Name);
                    }
                });

                LastRefresh = DateTime.Now;
                ConsecutiveFailures = 0;
                LastError = null;

                if (Status == ServiceStatus.Degraded)
                {
                    Status = ServiceStatus.Running;
                }

                _log.Debug("{Service} Fetch completed successfully, IsNew={IsNew}", Name, isNewData);
            }
            else
            {
                _log.Debug("{Service} Fetch returned no data", Name);
            }
        }
        catch (OperationCanceledException)
        {
            _log.Debug("{Service} Fetch cancelled", Name);
        }
        catch (Exception ex)
        {
            HandleFetchError(ex);
        }
    }

    private void HandleFetchError(Exception ex)
    {
        ConsecutiveFailures++;
        LastError = ex.Message;

        _log.Error(ex, "{Service} Fetch failed (attempt {Attempt})", Name, ConsecutiveFailures);

        if (ConsecutiveFailures >= MaxRetries)
        {
            Status = ServiceStatus.Faulted;
            RaiseError($"Service faulted after {MaxRetries} consecutive failures: {ex.Message}", ex, false, null);
        }
        else
        {
            Status = ServiceStatus.Degraded;
            var nextRetry = DateTime.Now + GetBackoffInterval();
            RaiseError($"Fetch failed: {ex.Message}", ex, true, nextRetry);
        }
    }

    private void ScheduleNextRefresh()
    {
        if (_disposed || _cts?.IsCancellationRequested == true || _timer == null)
        {
            return;
        }

        TimeSpan interval;
        if (ConsecutiveFailures > 0)
        {
            interval = GetBackoffInterval();
        }
        else
        {
            interval = RefreshInterval;
        }

        NextRefresh = DateTime.Now + interval;
        _timer.Change(interval, Timeout.InfiniteTimeSpan);

        _log.Debug("{Service} Next refresh scheduled for {Time}", Name, NextRefresh);
    }

    private TimeSpan GetBackoffInterval()
    {
        var index = Math.Min(ConsecutiveFailures - 1, BackoffIntervals.Length - 1);
        return BackoffIntervals[Math.Max(0, index)];
    }

    #endregion

    #region UI Thread Marshaling

    protected void InvokeOnUIThread(Action action)
    {
        if (_syncContext != null)
        {
            _syncContext.Post(_ => action(), null);
        }
        else
        {
            // Fallback if no sync context (shouldn't happen in WinForms)
            action();
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Stop();
        }

        _disposed = true;
    }

    #endregion
}
