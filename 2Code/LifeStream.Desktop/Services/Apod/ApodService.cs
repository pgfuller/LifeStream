using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevExpress.Xpo;
using LifeStream.Core.Infrastructure;
using LifeStream.Desktop.Infrastructure;
using LifeStream.Domain.Data;
using LifeStream.Domain.Sources;
using Newtonsoft.Json;
using Serilog;

namespace LifeStream.Desktop.Services.Apod;

/// <summary>
/// Service for fetching NASA Astronomy Picture of the Day.
/// Supports historical catchup to fill gaps when application was not running.
/// </summary>
public class ApodService : InformationServiceBase
{
    private static readonly ILogger Log = LoggingConfig.ForCategory($"{LoggingConfig.Categories.Sources}.APOD");

    private const string ApiBaseUrl = "https://api.nasa.gov/planetary/apod";
    private const string SeriesName = "APOD";
    private const int DefaultCatchupDays = 7; // How many days back to catch up

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly int _catchupDays;
    private readonly string _cachePath;
    private int _sourceId;
    private ApodData? _currentApod;

    /// <summary>
    /// Creates a new APOD service.
    /// </summary>
    /// <param name="apiKey">NASA API key. Use "DEMO_KEY" for testing (rate limited).</param>
    /// <param name="catchupDays">Number of days to catch up on startup.</param>
    public ApodService(string apiKey = "DEMO_KEY", int catchupDays = DefaultCatchupDays)
        : base("apod-nasa", "NASA APOD", "APOD")
    {
        _apiKey = apiKey;
        _catchupDays = catchupDays;
        _cachePath = AppPaths.GetServiceDataPath("APOD"); // Permanent data storage
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60) // Increased for image downloads
        };
    }

    /// <summary>
    /// Gets the local cache path for this service.
    /// </summary>
    public string CachePath => _cachePath;

    /// <summary>
    /// APOD updates once daily, so we check every 4 hours to be safe.
    /// The actual update time varies based on when NASA posts.
    /// </summary>
    protected override TimeSpan RefreshInterval => TimeSpan.FromHours(4);

    /// <summary>
    /// Gets the current/latest APOD data.
    /// </summary>
    public ApodData? CurrentApod => _currentApod;

    #region Initialization

    protected override void OnInitialize()
    {
        Log.Information("Initializing APOD service, data path: {DataPath}", _cachePath);

        // Ensure InformationSource record exists
        EnsureSourceRecord();

        // Try to load today's APOD from cache first
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        _currentApod = LoadFromCache(today);

        if (_currentApod != null)
        {
            Log.Information("Loaded today's APOD from cache: {Title}", _currentApod.Title);
        }
        else
        {
            // Load the most recent APOD from database or cache
            LoadLatestApod();
        }

        // Notify UI of loaded data
        if (_currentApod != null)
        {
            RaiseDataReceived(_currentApod, false);
        }

        // Start async catchup in the background (non-blocking)
        // This fills in any gaps in history without blocking UI startup
        Task.Run(() => PerformCatchupAsync());

        Log.Information("APOD service initialized, current date: {Date}", _currentApod?.Date ?? "(none)");
    }

    private void EnsureSourceRecord()
    {
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();

        var source = uow.FindObject<InformationSource>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse("Name = ? AND SourceType = ?", Name, SourceType));

        if (source == null)
        {
            Log.Debug("Creating InformationSource record for APOD");
            source = new InformationSource(uow)
            {
                Name = Name,
                SourceType = SourceType,
                IsEnabled = true,
                RefreshIntervalSeconds = (int)RefreshInterval.TotalSeconds,
                ConfigJson = JsonConvert.SerializeObject(new { ApiKey = _apiKey, CatchupDays = _catchupDays })
            };
            uow.CommitChanges();
        }

        _sourceId = source.Oid;
    }

    private async Task PerformCatchupAsync()
    {
        try
        {
            Log.Information("Starting APOD background catchup for last {Days} days", _catchupDays);

            var missingDates = GetMissingDates(_catchupDays);

            if (missingDates.Count == 0)
            {
                Log.Information("No missing dates to catch up");
                return;
            }

            Log.Information("Found {Count} missing dates to fetch: {Dates}",
                missingDates.Count,
                string.Join(", ", missingDates.Select(d => d.ToString("yyyy-MM-dd"))));

            var fetchedCount = 0;
            var failedCount = 0;

            foreach (var date in missingDates)
            {
                // Check if service is still running
                if (!IsRunning)
                {
                    Log.Information("APOD catchup aborted - service stopping");
                    break;
                }

                try
                {
                    var apod = await FetchApodForDateAsync(date);
                    if (apod != null)
                    {
                        StoreApodData(apod);
                        fetchedCount++;
                        Log.Information("Caught up APOD for {Date}: {Title}", date.ToString("yyyy-MM-dd"), apod.Title);

                        // If this is more recent than current, notify UI
                        if (_currentApod == null || string.CompareOrdinal(apod.Date, _currentApod.Date) > 0)
                        {
                            _currentApod = apod;
                            RaiseDataReceived(apod, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    Log.Warning(ex, "Failed to catch up APOD for {Date}", date.ToString("yyyy-MM-dd"));

                    // If rate limited, wait longer before next request
                    if (ex.Message.Contains("429") || ex.Message.Contains("Rate limited"))
                    {
                        Log.Information("Rate limited, waiting 5 seconds before next request");
                        await Task.Delay(5000);
                    }
                }

                // Delay between requests to avoid rate limiting (NASA API: 1000/hour with key)
                await Task.Delay(1000);
            }

            Log.Information("APOD background catchup completed: {Fetched} fetched, {Failed} failed",
                fetchedCount, failedCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "APOD background catchup failed unexpectedly");
        }
    }

    private async Task<ApodData?> FetchApodForDateAsync(DateTime date)
    {
        var dateStr = date.ToString("yyyy-MM-dd");
        var url = $"{ApiBaseUrl}?api_key={_apiKey}&date={dateStr}&thumbs=true";

        Log.Debug("Fetching APOD for {Date}", dateStr);
        var response = await _httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            if (statusCode == 404)
            {
                Log.Debug("No APOD available for {Date} (404)", dateStr);
                return null;
            }
            else if (statusCode == 429)
            {
                Log.Warning("APOD API rate limited (429) for {Date}", dateStr);
                throw new HttpRequestException($"Rate limited: {statusCode}");
            }
            else
            {
                Log.Warning("APOD API returned {StatusCode} for {Date}", statusCode, dateStr);
                throw new HttpRequestException($"HTTP {statusCode}");
            }
        }

        var json = await response.Content.ReadAsStringAsync();
        var apod = JsonConvert.DeserializeObject<ApodData>(json);

        if (apod != null)
        {
            Log.Debug("Fetched APOD for {Date}: {Title}", dateStr, apod.Title);
        }

        return apod;
    }

    private List<DateTime> GetMissingDates(int days)
    {
        var missingDates = new List<DateTime>();
        var today = DateTime.Today;

        for (int i = 0; i < days; i++)
        {
            var date = today.AddDays(-i);
            var dateStr = date.ToString("yyyy-MM-dd");

            // Only check file cache - don't rely on database records
            // This ensures we actually have the files locally, not just DB entries
            // from when files may have been in a different location
            if (!HasCachedApod(dateStr))
            {
                missingDates.Add(date);
            }
        }

        // Return in chronological order (oldest first)
        missingDates.Reverse();
        return missingDates;
    }

    private void LoadLatestApod()
    {
        // First try to find the most recent cached file
        var cachedFiles = Directory.GetFiles(_cachePath, "APOD_*.json")
            .OrderByDescending(f => f)
            .ToList();

        foreach (var file in cachedFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            var date = fileName.Replace("APOD_", "");
            var apod = LoadFromCache(date);
            if (apod != null)
            {
                _currentApod = apod;
                Log.Debug("Loaded latest APOD from cache: {Date}", date);
                return;
            }
        }

        // Fallback to database
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();

        var latest = uow.Query<DataPoint>()
            .Where(dp => dp.SourceId == _sourceId && dp.SeriesName == SeriesName)
            .OrderByDescending(dp => dp.Timestamp)
            .FirstOrDefault();

        if (latest != null && !string.IsNullOrEmpty(latest.MetadataJson))
        {
            try
            {
                _currentApod = JsonConvert.DeserializeObject<ApodData>(latest.MetadataJson);
                Log.Debug("Loaded latest APOD from database: {Date}", _currentApod?.Date);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to deserialize stored APOD data");
            }
        }
    }

    #endregion

    #region File Cache

    private string GetJsonFileName(string date) => Path.Combine(_cachePath, $"APOD_{date}.json");
    private string GetImageFileName(string date, string extension) => Path.Combine(_cachePath, $"APOD_{date}{extension}");

    /// <summary>
    /// Checks if we have a cached APOD for the given date.
    /// </summary>
    private bool HasCachedApod(string date)
    {
        var jsonPath = GetJsonFileName(date);
        return File.Exists(jsonPath);
    }

    /// <summary>
    /// Loads APOD data from local cache.
    /// </summary>
    private ApodData? LoadFromCache(string date)
    {
        var jsonPath = GetJsonFileName(date);
        if (!File.Exists(jsonPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(jsonPath);
            var apod = JsonConvert.DeserializeObject<ApodData>(json);

            if (apod != null)
            {
                // Find the cached image file
                var imagePattern = $"APOD_{date}.*";
                var imageFiles = Directory.GetFiles(_cachePath, imagePattern)
                    .Where(f => !f.EndsWith(".json"))
                    .ToList();

                if (imageFiles.Count > 0)
                {
                    apod.LocalImagePath = imageFiles[0];
                }

                Log.Debug("Loaded APOD from cache: {Date}", date);
            }

            return apod;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load APOD from cache for {Date}", date);
            return null;
        }
    }

    /// <summary>
    /// Saves APOD data and image to local cache.
    /// </summary>
    private void SaveToCache(ApodData apod)
    {
        try
        {
            var date = apod.Date;

            // Save JSON metadata
            var jsonPath = GetJsonFileName(date);
            var json = JsonConvert.SerializeObject(apod, Formatting.Indented);
            File.WriteAllText(jsonPath, json);

            // Download and save image (if it's an image, not video)
            if (apod.IsImage && !string.IsNullOrEmpty(apod.Url))
            {
                DownloadAndCacheImage(apod);
            }

            Log.Debug("Saved APOD to cache: {Date}", date);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save APOD to cache for {Date}", apod.Date);
        }
    }

    /// <summary>
    /// Downloads the APOD image and saves it to cache.
    /// </summary>
    private void DownloadAndCacheImage(ApodData apod)
    {
        try
        {
            // Determine image URL (prefer HD if available)
            var imageUrl = apod.GetBestImageUrl();
            if (string.IsNullOrEmpty(imageUrl))
            {
                return;
            }

            // Get file extension from URL
            var extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
            if (string.IsNullOrEmpty(extension))
            {
                extension = ".jpg"; // Default
            }

            var imagePath = GetImageFileName(apod.Date, extension);

            // Skip if already cached
            if (File.Exists(imagePath))
            {
                apod.LocalImagePath = imagePath;
                return;
            }

            // Download image
            Log.Debug("Downloading APOD image for {Date} from {Url}", apod.Date, imageUrl);
            var imageData = _httpClient.GetByteArrayAsync(imageUrl).GetAwaiter().GetResult();

            // Save to disk
            File.WriteAllBytes(imagePath, imageData);
            apod.LocalImagePath = imagePath;

            Log.Debug("Cached APOD image: {Path} ({Size} bytes)", imagePath, imageData.Length);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to download/cache APOD image for {Date}", apod.Date);
        }
    }

    /// <summary>
    /// Checks if today's APOD is already cached and current.
    /// </summary>
    private bool IsTodayCached()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        return HasCachedApod(today);
    }

    #endregion

    #region Data Fetching

    protected override async Task<object?> FetchDataAsync(CancellationToken cancellationToken)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");

        // Check if today's APOD is already cached - skip API call if so
        if (IsTodayCached())
        {
            var cached = LoadFromCache(today);
            if (cached != null)
            {
                Log.Debug("Today's APOD already cached, skipping API fetch: {Title}", cached.Title);
                return cached;
            }
        }

        Log.Debug("Fetching today's APOD from API");

        var url = $"{ApiBaseUrl}?api_key={_apiKey}&thumbs=true";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var apod = JsonConvert.DeserializeObject<ApodData>(json);

        if (apod == null)
        {
            Log.Warning("APOD API returned null data");
            return null;
        }

        Log.Debug("Fetched APOD from API: {Title} ({Date})", apod.Title, apod.Date);
        return apod;
    }

    private ApodData? FetchApodForDateSync(DateTime date)
    {
        var dateStr = date.ToString("yyyy-MM-dd");
        var url = $"{ApiBaseUrl}?api_key={_apiKey}&date={dateStr}&thumbs=true";

        try
        {
            Log.Debug("Fetching APOD for {Date}", dateStr);
            var response = _httpClient.GetAsync(url).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                if (statusCode == 404)
                {
                    // No APOD for this date (e.g., government shutdown)
                    Log.Debug("No APOD available for {Date} (404)", dateStr);
                    return null;
                }
                else if (statusCode == 429)
                {
                    // Rate limited - log and throw to trigger retry delay
                    Log.Warning("APOD API rate limited (429) for {Date}", dateStr);
                    throw new HttpRequestException($"Rate limited: {statusCode}");
                }
                else
                {
                    Log.Warning("APOD API returned {StatusCode} for {Date}", statusCode, dateStr);
                    throw new HttpRequestException($"HTTP {statusCode}");
                }
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var apod = JsonConvert.DeserializeObject<ApodData>(json);

            if (apod != null)
            {
                Log.Debug("Fetched APOD for {Date}: {Title}", dateStr, apod.Title);
            }

            return apod;
        }
        catch (HttpRequestException)
        {
            throw; // Re-throw to be handled by PerformCatchup
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Unexpected error fetching APOD for {Date}", dateStr);
            throw;
        }
    }

    protected override void StoreData(object data)
    {
        if (data is ApodData apod)
        {
            StoreApodData(apod);
            _currentApod = apod;
        }
    }

    private void StoreApodData(ApodData apod)
    {
        // Save to file cache first
        SaveToCache(apod);

        // Then save to database
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();

        var apodDate = apod.GetDate();

        // Check if we already have this date
        var existing = uow.FindObject<DataPoint>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse(
                "SourceId = ? AND SeriesName = ? AND Timestamp >= ? AND Timestamp < ?",
                _sourceId, SeriesName, apodDate.Date, apodDate.Date.AddDays(1)));

        if (existing != null)
        {
            // Update existing record
            existing.StringValue = apod.LocalImagePath ?? apod.Url;
            existing.MetadataJson = JsonConvert.SerializeObject(apod);
            Log.Debug("Updated existing APOD DataPoint for {Date}", apod.Date);
        }
        else
        {
            // Create new record
            var dataPoint = new DataPoint(uow)
            {
                SourceId = _sourceId,
                SeriesName = SeriesName,
                Timestamp = apodDate,
                StringValue = apod.LocalImagePath ?? apod.Url,
                MetadataJson = JsonConvert.SerializeObject(apod)
            };
            Log.Debug("Created new APOD DataPoint for {Date}", apod.Date);
        }

        // Update source last refresh time
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
        if (newData is ApodData newApod && previousData is ApodData oldApod)
        {
            return newApod.Date != oldApod.Date;
        }
        return true;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets historical APOD entries from the database.
    /// </summary>
    /// <param name="days">Number of days to retrieve.</param>
    /// <returns>List of APOD data ordered by date descending.</returns>
    public List<ApodData> GetHistory(int days = 7)
    {
        var result = new List<ApodData>();
        var cutoff = DateTime.Today.AddDays(-days);

        using var uow = XpoDataLayerHelper.CreateUnitOfWork();

        var dataPoints = uow.Query<DataPoint>()
            .Where(dp => dp.SourceId == _sourceId && dp.SeriesName == SeriesName && dp.Timestamp >= cutoff)
            .OrderByDescending(dp => dp.Timestamp)
            .ToList();

        foreach (var dp in dataPoints)
        {
            if (!string.IsNullOrEmpty(dp.MetadataJson))
            {
                try
                {
                    var apod = JsonConvert.DeserializeObject<ApodData>(dp.MetadataJson);
                    if (apod != null)
                    {
                        result.Add(apod);
                    }
                }
                catch
                {
                    // Skip malformed entries
                }
            }
        }

        return result;
    }

    #endregion

    #region Cleanup

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient.Dispose();
        }
        base.Dispose(disposing);
    }

    #endregion
}
