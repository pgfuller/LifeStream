using System;
using System.IO;
using DevExpress.Xpo;
using DevExpress.Xpo.DB;
using DevExpress.Xpo.Metadata;
using LifeStream.Core.Infrastructure;
using LifeStream.Domain.Configuration;
using LifeStream.Domain.Dashboard;
using LifeStream.Domain.Data;
using LifeStream.Domain.Sources;
using Microsoft.Data.Sqlite;
using Serilog;

namespace LifeStream.Desktop.Infrastructure;

/// <summary>
/// Helper class for creating and managing XPO data layers.
/// Provides SQLite-based data layer for LifeStream persistence.
/// </summary>
public static class XpoDataLayerHelper
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.Data);
    private static IDataLayer? _dataLayer;

    /// <summary>
    /// Gets or creates the singleton data layer for the application.
    /// </summary>
    public static IDataLayer DataLayer => _dataLayer ?? throw new InvalidOperationException(
        "Data layer not initialized. Call Initialize() first.");

    /// <summary>
    /// Initializes the XPO data layer using the default database path.
    /// </summary>
    public static void Initialize()
    {
        Initialize(AppPaths.DatabasePath);
    }

    /// <summary>
    /// Initializes the XPO data layer with the specified database path.
    /// </summary>
    /// <param name="databasePath">Full path to the SQLite database file.</param>
    public static void Initialize(string databasePath)
    {
        if (_dataLayer != null)
        {
            Log.Warning("XPO data layer already initialized");
            return;
        }

        Log.Information("Initializing XPO data layer at {DatabasePath}", databasePath);
        _dataLayer = CreateSqliteDataLayer(databasePath);
        XpoDefault.DataLayer = _dataLayer;
        Log.Information("XPO data layer initialized successfully");
    }

    /// <summary>
    /// Creates an XPO data layer for the specified SQLite database file.
    /// </summary>
    private static IDataLayer CreateSqliteDataLayer(string databasePath, bool updateSchema = true)
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            Log.Debug("Created database directory: {Directory}", directory);
        }

        // Build SQLite connection string
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        // Create XPO connection string for SQLite
        var xpoConnectionString = $"XpoProvider=SQLite;{connectionString}";

        // Get the XPO dictionary with registered types
        var dictionary = GetXpoDictionary();

        // Create the data store
        var dataStore = XpoDefault.GetConnectionProvider(xpoConnectionString, AutoCreateOption.DatabaseAndSchema);

        // Create thread-safe data layer
        var dataLayer = new ThreadSafeDataLayer(dictionary, dataStore);

        if (updateSchema)
        {
            UpdateSchema(dataLayer);
        }

        return dataLayer;
    }

    /// <summary>
    /// Gets the XPO dictionary with all registered persistent types.
    /// </summary>
    private static XPDictionary GetXpoDictionary()
    {
        var dictionary = new ReflectionDictionary();

        // Register domain entities
        // Add new entity types here as they are created
        dictionary.GetDataStoreSchema(
            // Configuration
            typeof(UserSettings),
            // Dashboard
            typeof(DashboardLayout),
            // Sources
            typeof(InformationSource),
            // Data
            typeof(DataPoint),
            typeof(Alert)
        );

        return dictionary;
    }

    /// <summary>
    /// Updates the database schema to match the current entity definitions.
    /// </summary>
    private static void UpdateSchema(IDataLayer dataLayer)
    {
        Log.Debug("Updating database schema");
        using var session = new Session(dataLayer);

        // Use explicit types - do NOT call parameterless UpdateSchema()
        session.UpdateSchema(
            // Configuration
            typeof(UserSettings),
            // Dashboard
            typeof(DashboardLayout),
            // Sources
            typeof(InformationSource),
            // Data
            typeof(DataPoint),
            typeof(Alert)
        );
        session.CreateObjectTypeRecords(
            // Configuration
            typeof(UserSettings),
            // Dashboard
            typeof(DashboardLayout),
            // Sources
            typeof(InformationSource),
            // Data
            typeof(DataPoint),
            typeof(Alert)
        );

        Log.Debug("Database schema updated");
    }

    /// <summary>
    /// Creates a new UnitOfWork for database operations.
    /// </summary>
    public static UnitOfWork CreateUnitOfWork()
    {
        return new UnitOfWork(DataLayer);
    }

    /// <summary>
    /// Creates a new Session for database operations.
    /// </summary>
    public static Session CreateSession()
    {
        return new Session(DataLayer);
    }
}
