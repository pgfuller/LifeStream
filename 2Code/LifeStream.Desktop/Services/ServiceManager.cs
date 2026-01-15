using System;
using System.Collections.Generic;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using Serilog;

namespace LifeStream.Desktop.Services;

/// <summary>
/// Manages the lifecycle of all information services.
/// </summary>
public class ServiceManager : IDisposable
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.App);

    private readonly List<IInformationService> _services = new();
    private bool _disposed;

    /// <summary>
    /// Gets all registered services.
    /// </summary>
    public IReadOnlyList<IInformationService> Services => _services.AsReadOnly();

    /// <summary>
    /// Registers a service with the manager.
    /// </summary>
    public void RegisterService(IInformationService service)
    {
        if (service == null) throw new ArgumentNullException(nameof(service));

        _services.Add(service);
        Log.Information("Registered service: {ServiceName} ({ServiceType})", service.Name, service.SourceType);
    }

    /// <summary>
    /// Gets a service by its ID.
    /// </summary>
    public IInformationService? GetService(string serviceId)
    {
        return _services.Find(s => s.ServiceId == serviceId);
    }

    /// <summary>
    /// Gets a service by type.
    /// </summary>
    public T? GetService<T>() where T : class, IInformationService
    {
        return _services.Find(s => s is T) as T;
    }

    /// <summary>
    /// Starts all registered services.
    /// </summary>
    public void StartAll()
    {
        Log.Information("Starting all services ({Count} registered)", _services.Count);

        foreach (var service in _services)
        {
            try
            {
                service.Start();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to start service: {ServiceName}", service.Name);
                // Continue starting other services
            }
        }

        Log.Information("All services started");
    }

    /// <summary>
    /// Stops all registered services.
    /// </summary>
    public void StopAll()
    {
        Log.Information("Stopping all services");

        foreach (var service in _services)
        {
            try
            {
                service.Stop();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop service: {ServiceName}", service.Name);
                // Continue stopping other services
            }
        }

        Log.Information("All services stopped");
    }

    /// <summary>
    /// Refreshes all running services immediately.
    /// </summary>
    public void RefreshAll()
    {
        Log.Debug("Refreshing all services");

        foreach (var service in _services)
        {
            if (service.IsRunning)
            {
                service.RefreshNow();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopAll();

        foreach (var service in _services)
        {
            service.Dispose();
        }

        _services.Clear();
        _disposed = true;
    }
}
