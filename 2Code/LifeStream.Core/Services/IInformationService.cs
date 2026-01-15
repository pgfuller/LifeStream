using System;

namespace LifeStream.Core.Services;

/// <summary>
/// Interface for all information services that fetch data from external sources.
/// </summary>
public interface IInformationService : IDisposable
{
    /// <summary>
    /// Unique identifier for this service instance.
    /// </summary>
    string ServiceId { get; }

    /// <summary>
    /// Display name for the service.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Type of information source (e.g., "APOD", "Weather", "RSS").
    /// </summary>
    string SourceType { get; }

    /// <summary>
    /// Current status of the service.
    /// </summary>
    ServiceStatus Status { get; }

    /// <summary>
    /// Whether the service is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// When the last successful data fetch occurred.
    /// </summary>
    DateTime? LastRefresh { get; }

    /// <summary>
    /// When the next scheduled refresh will occur.
    /// </summary>
    DateTime? NextRefresh { get; }

    /// <summary>
    /// Last error message if the service is degraded or faulted.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Number of consecutive failures.
    /// </summary>
    int ConsecutiveFailures { get; }

    /// <summary>
    /// Starts the service. This is synchronous and should complete initialization
    /// before returning. Background polling starts after this returns.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the service. This is synchronous and should complete cleanup
    /// before returning.
    /// </summary>
    void Stop();

    /// <summary>
    /// Triggers an immediate refresh, bypassing the normal schedule.
    /// </summary>
    void RefreshNow();

    /// <summary>
    /// Raised when new data is received from the source.
    /// This event is raised on the UI thread for safe binding.
    /// </summary>
    event EventHandler<ServiceDataEventArgs>? DataReceived;

    /// <summary>
    /// Raised when the service status changes.
    /// This event is raised on the UI thread for safe binding.
    /// </summary>
    event EventHandler<ServiceStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Raised when an error occurs during data fetching.
    /// This event is raised on the UI thread for safe binding.
    /// </summary>
    event EventHandler<ServiceErrorEventArgs>? ErrorOccurred;
}

/// <summary>
/// Event arguments for data received events.
/// </summary>
public class ServiceDataEventArgs : EventArgs
{
    /// <summary>
    /// The service that received the data.
    /// </summary>
    public IInformationService Service { get; }

    /// <summary>
    /// The data that was received. Type depends on the service.
    /// </summary>
    public object Data { get; }

    /// <summary>
    /// Whether this is new data (changed from previous) or a refresh of existing data.
    /// </summary>
    public bool IsNewData { get; }

    /// <summary>
    /// Timestamp when the data was fetched.
    /// </summary>
    public DateTime Timestamp { get; }

    public ServiceDataEventArgs(IInformationService service, object data, bool isNewData, DateTime timestamp)
    {
        Service = service;
        Data = data;
        IsNewData = isNewData;
        Timestamp = timestamp;
    }
}

/// <summary>
/// Event arguments for status changed events.
/// </summary>
public class ServiceStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// The service whose status changed.
    /// </summary>
    public IInformationService Service { get; }

    /// <summary>
    /// The previous status.
    /// </summary>
    public ServiceStatus OldStatus { get; }

    /// <summary>
    /// The new status.
    /// </summary>
    public ServiceStatus NewStatus { get; }

    public ServiceStatusChangedEventArgs(IInformationService service, ServiceStatus oldStatus, ServiceStatus newStatus)
    {
        Service = service;
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }
}

/// <summary>
/// Event arguments for error events.
/// </summary>
public class ServiceErrorEventArgs : EventArgs
{
    /// <summary>
    /// The service that encountered the error.
    /// </summary>
    public IInformationService Service { get; }

    /// <summary>
    /// The error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// The exception that caused the error, if any.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Whether the service will retry.
    /// </summary>
    public bool WillRetry { get; }

    /// <summary>
    /// When the next retry will occur, if applicable.
    /// </summary>
    public DateTime? NextRetry { get; }

    public ServiceErrorEventArgs(IInformationService service, string message, Exception? exception, bool willRetry, DateTime? nextRetry)
    {
        Service = service;
        Message = message;
        Exception = exception;
        WillRetry = willRetry;
        NextRetry = nextRetry;
    }
}
