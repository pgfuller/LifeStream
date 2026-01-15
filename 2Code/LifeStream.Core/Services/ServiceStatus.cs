namespace LifeStream.Core.Services;

/// <summary>
/// Status of an information service.
/// </summary>
public enum ServiceStatus
{
    /// <summary>
    /// Service has not been started.
    /// </summary>
    Stopped,

    /// <summary>
    /// Service is starting up (initializing, catching up history).
    /// </summary>
    Starting,

    /// <summary>
    /// Service is running normally.
    /// </summary>
    Running,

    /// <summary>
    /// Service is running but last fetch failed (showing stale data).
    /// </summary>
    Degraded,

    /// <summary>
    /// Service is stopping.
    /// </summary>
    Stopping,

    /// <summary>
    /// Service encountered a fatal error and cannot continue.
    /// </summary>
    Faulted
}
