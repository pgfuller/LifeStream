using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LifeStream.Core.Infrastructure;
using LifeStream.Desktop.Infrastructure;
using Newtonsoft.Json.Linq;
using Serilog;

namespace LifeStream.Desktop.Services.Financial;

/// <summary>
/// Shared HTTP client for Alpha Vantage API with caching and rate limiting.
/// Designed to minimize API calls within the free tier quota (25/day).
/// </summary>
public class AlphaVantageClient : IDisposable
{
    private static readonly ILogger Log = LoggingConfig.ForCategory($"{LoggingConfig.Categories.Sources}.Financial.AlphaVantage");

    private const string BaseUrl = "https://www.alphavantage.co/query";
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(500); // Max 2 requests/second

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly TimeSpan _cacheDuration;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _lastRequestTime = DateTime.MinValue;

    private int _apiCallsToday;
    private DateTime _apiCallsResetDate = DateTime.Today;
    private readonly int _dailyLimit;

    /// <summary>
    /// Gets the number of API calls made today.
    /// </summary>
    public int ApiCallsToday
    {
        get
        {
            ResetDailyCounterIfNeeded();
            return _apiCallsToday;
        }
    }

    /// <summary>
    /// Gets the remaining API calls for today.
    /// </summary>
    public int ApiCallsRemaining => Math.Max(0, _dailyLimit - ApiCallsToday);

    /// <summary>
    /// Gets whether the daily API limit has been reached.
    /// </summary>
    public bool IsLimitReached => ApiCallsRemaining <= 0;

    public AlphaVantageClient(string apiKey, int cacheMinutes = 15, int dailyLimit = 25)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentNullException(nameof(apiKey), "Alpha Vantage API key is required");

        _apiKey = apiKey;
        _cacheDuration = TimeSpan.FromMinutes(cacheMinutes);
        _dailyLimit = dailyLimit;

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LifeStream/1.0");

        Log.Information("AlphaVantageClient initialized (cache: {CacheMinutes}min, limit: {DailyLimit}/day)",
            cacheMinutes, dailyLimit);
    }

    /// <summary>
    /// Makes a request to the Alpha Vantage API.
    /// Results are cached to minimize API calls.
    /// </summary>
    public async Task<JObject?> RequestAsync(string function, string parameters, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{function}:{parameters}";

        // Check cache first
        if (_cache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            Log.Debug("Cache hit for {Function} ({Parameters})", function, parameters);
            return cached.Data;
        }

        // Check daily limit
        if (IsLimitReached)
        {
            Log.Warning("Daily API limit reached ({Limit}). Using cached data or returning null.", _dailyLimit);
            return cached?.Data; // Return stale cache if available
        }

        await _rateLimiter.WaitAsync(cancellationToken);
        try
        {
            // Ensure minimum interval between requests
            var timeSinceLastRequest = DateTime.Now - _lastRequestTime;
            if (timeSinceLastRequest < MinRequestInterval)
            {
                var delay = MinRequestInterval - timeSinceLastRequest;
                await Task.Delay(delay, cancellationToken);
            }

            var url = $"{BaseUrl}?function={function}&{parameters}&apikey={_apiKey}";
            Log.Debug("API request: {Function} ({Parameters})", function, parameters);

            var response = await _httpClient.GetStringAsync(url, cancellationToken);
            _lastRequestTime = DateTime.Now;
            IncrementApiCalls();

            var json = JObject.Parse(response);

            // Check for API errors
            if (json.ContainsKey("Note"))
            {
                var note = json["Note"]?.ToString();
                if (note?.Contains("API call frequency") == true)
                {
                    Log.Warning("Alpha Vantage rate limit message: {Note}", note);
                    return cached?.Data; // Return stale cache
                }
            }

            if (json.ContainsKey("Error Message"))
            {
                var error = json["Error Message"]?.ToString();
                Log.Error("Alpha Vantage API error: {Error}", error);
                return null;
            }

            // Cache the result
            _cache[cacheKey] = new CacheEntry(json, _cacheDuration);
            Log.Debug("Cached result for {Function} ({Parameters})", function, parameters);

            return json;
        }
        catch (HttpRequestException ex)
        {
            Log.Error(ex, "HTTP error fetching {Function}: {Message}", function, ex.Message);
            return cached?.Data; // Return stale cache on network error
        }
        catch (TaskCanceledException)
        {
            Log.Debug("Request cancelled for {Function}", function);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching {Function}", function);
            return cached?.Data;
        }
        finally
        {
            _rateLimiter.Release();
        }
    }

    /// <summary>
    /// Clears the cache.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
        Log.Information("Cache cleared");
    }

    private void ResetDailyCounterIfNeeded()
    {
        if (DateTime.Today > _apiCallsResetDate)
        {
            _apiCallsToday = 0;
            _apiCallsResetDate = DateTime.Today;
            Log.Information("Daily API call counter reset");
        }
    }

    private void IncrementApiCalls()
    {
        ResetDailyCounterIfNeeded();
        _apiCallsToday++;
        Log.Debug("API calls today: {Count}/{Limit}", _apiCallsToday, _dailyLimit);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _rateLimiter.Dispose();
    }

    private class CacheEntry
    {
        public JObject Data { get; }
        public DateTime ExpiresAt { get; }
        public bool IsExpired => DateTime.Now > ExpiresAt;

        public CacheEntry(JObject data, TimeSpan duration)
        {
            Data = data;
            ExpiresAt = DateTime.Now + duration;
        }
    }
}
