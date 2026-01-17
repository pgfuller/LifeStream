using System.IO;
using Serilog;
using Serilog.Events;

namespace LifeStream.Core.Infrastructure;

/// <summary>
/// Configures Serilog logging for the LifeStream application.
/// Provides structured logging with rolling file output.
/// </summary>
public static class LoggingConfig
{
    /// <summary>
    /// Log categories for different subsystems.
    /// </summary>
    public static class Categories
    {
        public const string Sources = "LifeStream.Sources";
        public const string Refresh = "LifeStream.Refresh";
        public const string Extraction = "LifeStream.Extraction";
        public const string UI = "LifeStream.UI";
        public const string Data = "LifeStream.Data";
        public const string App = "LifeStream.App";
    }

    /// <summary>
    /// Configures and initializes the global Serilog logger.
    /// Call this at application startup before any logging occurs.
    /// </summary>
    /// <param name="minimumLevel">Minimum log level (default: Debug)</param>
    public static void ConfigureLogging(LogEventLevel minimumLevel = LogEventLevel.Debug)
    {
        // Ensure logs directory exists
        Directory.CreateDirectory(AppPaths.LogsPath);

        var logFilePath = Path.Combine(AppPaths.LogsPath, "lifestream-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.WithProperty("Application", "LifeStream")
            .Enrich.FromLogContext()
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.Debug(
                outputTemplate: "[{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Session separator for easy identification in log files
        Log.Information("================================================================================");
        Log.Information("=== NEW SESSION STARTED ===");
        Log.Information("================================================================================");
        Log.Information("LifeStream logging initialized. Log path: {LogPath}", AppPaths.LogsPath);
    }

    /// <summary>
    /// Creates a logger for a specific category/source context.
    /// </summary>
    /// <param name="category">The log category (use Categories constants)</param>
    /// <returns>A contextualized logger</returns>
    public static ILogger ForCategory(string category)
    {
        return Log.ForContext("SourceContext", category);
    }

    /// <summary>
    /// Creates a logger for a specific type.
    /// </summary>
    /// <typeparam name="T">The type to create a logger for</typeparam>
    /// <returns>A contextualized logger</returns>
    public static ILogger ForType<T>()
    {
        return Log.ForContext<T>();
    }

    /// <summary>
    /// Flushes and closes the logger. Call at application shutdown.
    /// </summary>
    public static void CloseAndFlush()
    {
        Log.Information("LifeStream shutting down");
        Log.CloseAndFlush();
    }
}
