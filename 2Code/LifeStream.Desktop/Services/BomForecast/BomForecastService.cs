using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DevExpress.Xpo;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using LifeStream.Desktop.Infrastructure;
using LifeStream.Domain.Data;
using LifeStream.Domain.Sources;
using Newtonsoft.Json;
using Serilog;

namespace LifeStream.Desktop.Services.BomForecast;

/// <summary>
/// Service for fetching BOM weather forecasts via FTP.
/// Provides current conditions and 7-day forecast for a location.
/// </summary>
public class BomForecastService : InformationServiceBase
{
    private static readonly ILogger Log = LoggingConfig.ForCategory($"{LoggingConfig.Categories.Sources}.BOMForecast");

    private const string FtpHost = "ftp.bom.gov.au";
    private const string FwoPath = "/anon/gen/fwo";

    private readonly ForecastLocation _location;
    private readonly string _dataPath;
    private readonly AdaptiveRefreshStrategy _refreshStrategy;

    private int _sourceId;
    private ForecastData? _currentForecast;
    private DateTime _lastForecastIssued = DateTime.MinValue;

    /// <summary>
    /// Creates a new BOM forecast service for a location.
    /// </summary>
    public BomForecastService(ForecastLocation location)
        : base($"bom-forecast-{location.State.ToLower()}-{location.Name.ToLower()}",
               $"BOM {location.Name} Forecast", "Forecast")
    {
        _location = location;
        _dataPath = AppPaths.GetServiceDataPath($"BOMForecast_{location.State}");

        // Forecasts typically update 2-4 times per day
        // Use adaptive strategy with 30 minute base interval
        _refreshStrategy = new AdaptiveRefreshStrategy(
            baseInterval: TimeSpan.FromMinutes(30),
            initialSlack: TimeSpan.FromMinutes(5),
            minimumInterval: TimeSpan.FromMinutes(10),
            maximumInterval: TimeSpan.FromHours(2),
            retryInterval: TimeSpan.FromMinutes(5),
            maxRetries: 3,
            maxObservations: 10
        );
    }

    /// <summary>
    /// Gets the current forecast data.
    /// </summary>
    public ForecastData? CurrentForecast => _currentForecast;

    /// <summary>
    /// Gets the configured location.
    /// </summary>
    public ForecastLocation Location => _location;

    /// <summary>
    /// Forecast refresh interval (30 minutes default, adapts based on actual update patterns).
    /// </summary>
    protected override TimeSpan RefreshInterval => TimeSpan.FromMinutes(30);

    #region Initialization

    protected override void OnInitialize()
    {
        Log.Information("Initializing BOM Forecast service for {Location}, data path: {DataPath}",
            _location.Name, _dataPath);

        EnsureSourceRecord();
        LoadCachedForecast();

        if (_currentForecast != null)
        {
            Log.Information("Loaded cached forecast for {Location}, issued: {IssuedAt}",
                _location.Name, _currentForecast.IssuedAt);
            RaiseDataReceived(_currentForecast, false);
        }
    }

    private void EnsureSourceRecord()
    {
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();

        var source = uow.FindObject<InformationSource>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse("Name = ? AND SourceType = ?", Name, SourceType));

        if (source == null)
        {
            Log.Debug("Creating InformationSource record for BOM Forecast {Location}", _location.Name);
            source = new InformationSource(uow)
            {
                Name = Name,
                SourceType = SourceType,
                IsEnabled = true,
                RefreshIntervalSeconds = (int)RefreshInterval.TotalSeconds,
                ConfigJson = JsonConvert.SerializeObject(new
                {
                    _location.Name,
                    _location.State,
                    _location.Aac,
                    _location.PrecisProductId
                })
            };
            uow.CommitChanges();
        }

        _sourceId = source.Oid;
    }

    private void LoadCachedForecast()
    {
        var cachePath = GetCacheFilePath();
        if (!File.Exists(cachePath))
            return;

        try
        {
            var json = File.ReadAllText(cachePath);
            _currentForecast = JsonConvert.DeserializeObject<ForecastData>(json);

            if (_currentForecast != null)
            {
                _lastForecastIssued = _currentForecast.IssuedAt;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load cached forecast for {Location}", _location.Name);
        }
    }

    private string GetCacheFilePath()
    {
        return Path.Combine(_dataPath, $"forecast_{_location.Name.ToLower()}.json");
    }

    #endregion

    #region Data Fetching

    protected override async Task<object?> FetchDataAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() => FetchForecastSync(), cancellationToken);
    }

    private ForecastData? FetchForecastSync()
    {
        Log.Debug("Fetching BOM forecast for {Location}", _location.Name);

        try
        {
            // Fetch the précis (short) forecast XML
            var xml = DownloadForecastXml(_location.PrecisProductId);
            if (xml == null)
            {
                Log.Warning("Failed to download forecast XML for {Location}", _location.Name);
                return null;
            }

            var forecast = ParseForecastXml(xml);
            if (forecast == null)
            {
                Log.Warning("Failed to parse forecast XML for {Location}", _location.Name);
                return null;
            }

            forecast.FetchedAt = DateTime.Now;
            forecast.ProductId = _location.PrecisProductId;

            // Check if this is a new forecast
            if (forecast.IssuedAt > _lastForecastIssued)
            {
                Log.Information("New forecast for {Location}, issued: {IssuedAt} (was {OldIssued})",
                    _location.Name, forecast.IssuedAt, _lastForecastIssued);
                _lastForecastIssued = forecast.IssuedAt;
                _refreshStrategy.RecordSuccess(forecast.IssuedAt);
            }
            else
            {
                Log.Debug("Forecast for {Location} unchanged, issued: {IssuedAt}",
                    _location.Name, forecast.IssuedAt);
            }

            return forecast;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching forecast for {Location}", _location.Name);
            _refreshStrategy.RecordMiss();
            throw;
        }
    }

    private string? DownloadForecastXml(string productId)
    {
        try
        {
            var url = $"ftp://{FtpHost}{FwoPath}/{productId}.xml";
            Log.Debug("Downloading forecast XML: {Url}", url);

            var ftpRequest = (FtpWebRequest)WebRequest.Create(url);
            ftpRequest.Method = WebRequestMethods.Ftp.DownloadFile;
            ftpRequest.Credentials = new NetworkCredential("anonymous", "lifestream@local");
            ftpRequest.Timeout = 30000;

            using var response = (FtpWebResponse)ftpRequest.GetResponse();
            using var stream = response.GetResponseStream();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to download forecast XML: {ProductId}", productId);
            return null;
        }
    }

    private ForecastData? ParseForecastXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null)
            {
                Log.Warning("BOM forecast XML has no root element");
                return null;
            }

            Log.Debug("BOM forecast XML root element: {Name}, children: {Children}",
                root.Name.LocalName,
                string.Join(", ", root.Elements().Select(e => e.Name.LocalName).Distinct().Take(10)));

            var forecast = new ForecastData();

            // Get issue time - try multiple possible locations
            // Try amoc/issue-time-utc first
            var amoc = root.Element("amoc");
            if (amoc != null)
            {
                var issueTimeStr = amoc.Element("issue-time-utc")?.Value;
                if (!string.IsNullOrEmpty(issueTimeStr) &&
                    DateTime.TryParse(issueTimeStr, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var issuedAt))
                {
                    forecast.IssuedAt = issuedAt.ToLocalTime();
                    Log.Debug("Found issue time from amoc: {IssuedAt}", forecast.IssuedAt);
                }
            }

            // Also try amoc/issue-time-local as fallback
            if (forecast.IssuedAt == default && amoc != null)
            {
                var issueTimeLocal = amoc.Element("issue-time-local")?.Value;
                if (!string.IsNullOrEmpty(issueTimeLocal) &&
                    DateTime.TryParse(issueTimeLocal, CultureInfo.InvariantCulture, DateTimeStyles.None, out var issuedLocal))
                {
                    forecast.IssuedAt = issuedLocal;
                    Log.Debug("Found issue time from amoc local: {IssuedAt}", forecast.IssuedAt);
                }
            }

            // Log all areas for debugging
            var allAreas = root.Descendants("area").ToList();
            Log.Debug("BOM forecast has {Count} area elements. Types: {Types}",
                allAreas.Count,
                string.Join(", ", allAreas.Select(a => a.Attribute("type")?.Value ?? "null").Distinct()));

            // Find the location's forecast area - try multiple matching strategies
            var areas = allAreas
                .Where(a => a.Attribute("aac")?.Value == _location.Aac)
                .ToList();

            if (!areas.Any())
            {
                // Try description match
                areas = allAreas
                    .Where(a => a.Attribute("description")?.Value?.Equals(_location.Name, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
            }

            if (!areas.Any())
            {
                // Try partial description match
                areas = allAreas
                    .Where(a => a.Attribute("description")?.Value?.Contains(_location.Name, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
            }

            if (!areas.Any())
            {
                // Fall back to first metropolitan or location type area that has forecast-period children
                areas = allAreas
                    .Where(a => (a.Attribute("type")?.Value == "metropolitan" ||
                                a.Attribute("type")?.Value == "location" ||
                                a.Attribute("type")?.Value == "public-district") &&
                               a.Elements("forecast-period").Any())
                    .Take(1)
                    .ToList();
            }

            if (!areas.Any())
            {
                // Last resort: any area with forecast-period children
                areas = allAreas
                    .Where(a => a.Elements("forecast-period").Any())
                    .Take(1)
                    .ToList();

                if (areas.Any())
                {
                    Log.Debug("Using fallback area (first with forecast-periods): {Aac} ({Description})",
                        areas.First().Attribute("aac")?.Value,
                        areas.First().Attribute("description")?.Value);
                }
            }

            if (!areas.Any())
            {
                Log.Warning("No matching forecast area found for {Location} ({Aac}). Available AACs: {Aacs}",
                    _location.Name, _location.Aac,
                    string.Join(", ", allAreas.Select(a => a.Attribute("aac")?.Value).Where(a => a != null).Take(20)));
                return null;
            }

            var area = areas.First();
            forecast.Location = area.Attribute("description")?.Value ?? _location.Name;
            forecast.Aac = area.Attribute("aac")?.Value ?? _location.Aac;

            Log.Debug("Using forecast area: {Aac} - {Description} (type: {Type})",
                forecast.Aac, forecast.Location, area.Attribute("type")?.Value);

            // Parse forecast periods (days)
            var forecastPeriods = area.Elements("forecast-period")
                .OrderBy(fp => fp.Attribute("start-time-utc")?.Value ?? fp.Attribute("start-time-local")?.Value)
                .ToList();

            Log.Debug("Found {Count} forecast periods in area", forecastPeriods.Count);

            foreach (var period in forecastPeriods)
            {
                var day = ParseDayForecast(period);
                if (day != null)
                {
                    forecast.Days.Add(day);
                }
            }

            Log.Information("Parsed BOM forecast for {Location}: {DayCount} days, issued: {IssuedAt}",
                forecast.Location, forecast.Days.Count, forecast.IssuedAt);
            return forecast;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error parsing BOM forecast XML");
            return null;
        }
    }

    private DayForecast? ParseDayForecast(XElement period)
    {
        try
        {
            var day = new DayForecast();

            // Parse date
            var startTimeStr = period.Attribute("start-time-utc")?.Value;
            if (!string.IsNullOrEmpty(startTimeStr) &&
                DateTime.TryParse(startTimeStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            {
                day.Date = date.ToLocalTime().Date;
                day.DayName = day.Date.ToString("dddd");
            }

            // Parse elements
            foreach (var element in period.Elements("element"))
            {
                var type = element.Attribute("type")?.Value;
                var value = element.Value;

                switch (type)
                {
                    case "forecast_icon_code":
                        if (int.TryParse(value, out var iconCode))
                            day.IconCode = iconCode;
                        break;

                    case "air_temperature_minimum":
                        if (double.TryParse(value, out var minTemp))
                            day.MinTemp = minTemp;
                        break;

                    case "air_temperature_maximum":
                        if (double.TryParse(value, out var maxTemp))
                            day.MaxTemp = maxTemp;
                        break;

                    case "precipitation_range":
                        day.RainfallRange = value;
                        break;

                    case "probability_of_precipitation":
                        if (int.TryParse(value?.TrimEnd('%'), out var pop))
                            day.PrecipitationChance = pop;
                        break;

                    case "uv_alert":
                        day.UvAlert = value;
                        break;

                    case "fire_danger":
                        day.FireDanger = value;
                        break;
                }
            }

            // Parse text elements
            foreach (var text in period.Elements("text"))
            {
                var type = text.Attribute("type")?.Value;
                var value = text.Value;

                switch (type)
                {
                    case "precis":
                        day.Summary = value;
                        break;

                    case "forecast":
                        day.Description = value;
                        break;
                }
            }

            return day;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error parsing day forecast");
            return null;
        }
    }

    #endregion

    #region Data Storage

    protected override void StoreData(object data)
    {
        if (data is ForecastData forecast)
        {
            _currentForecast = forecast;
            SaveForecastToCache(forecast);
            StoreForecastRecord(forecast);
        }
    }

    private void SaveForecastToCache(ForecastData forecast)
    {
        try
        {
            var cachePath = GetCacheFilePath();
            var json = JsonConvert.SerializeObject(forecast, Formatting.Indented);
            File.WriteAllText(cachePath, json);
            Log.Debug("Saved forecast to cache: {Path}", cachePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save forecast to cache");
        }
    }

    private void StoreForecastRecord(ForecastData forecast)
    {
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();

        // Store as a daily record
        var today = DateTime.Today;

        var existing = uow.FindObject<DataPoint>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse(
                "SourceId = ? AND SeriesName = ? AND Timestamp >= ? AND Timestamp < ?",
                _sourceId, "DailyForecast", today, today.AddDays(1)));

        if (existing != null)
        {
            existing.MetadataJson = JsonConvert.SerializeObject(forecast);
            Log.Debug("Updated existing forecast record for {Date}", today.ToString("yyyy-MM-dd"));
        }
        else
        {
            var dataPoint = new DataPoint(uow)
            {
                SourceId = _sourceId,
                SeriesName = "DailyForecast",
                Timestamp = DateTime.Now,
                StringValue = forecast.Location,
                MetadataJson = JsonConvert.SerializeObject(forecast)
            };
            Log.Debug("Created new forecast record for {Date}", today.ToString("yyyy-MM-dd"));
        }

        var source = uow.GetObjectByKey<InformationSource>(_sourceId);
        if (source != null)
        {
            source.LastRefreshAt = DateTime.Now;
            source.LastErrorMessage = null;
        }

        uow.CommitChanges();
    }

    protected override bool HasDataChanged(object newData, object? previousData)
    {
        if (newData is ForecastData newForecast && previousData is ForecastData oldForecast)
        {
            return newForecast.IssuedAt > oldForecast.IssuedAt;
        }
        return true;
    }

    #endregion
}
