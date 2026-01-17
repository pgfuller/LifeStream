using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LifeStream.Core.Infrastructure;
using Serilog;

namespace LifeStream.Desktop.Services.Financial;

/// <summary>
/// Service for fetching and managing financial market data.
/// Provides ASX indices, precious metals prices, and portfolio tracking.
/// </summary>
public class FinancialService : InformationServiceBase
{
    private static readonly ILogger Log = LoggingConfig.ForCategory($"{LoggingConfig.Categories.Sources}.Financial");

    private readonly IMarketDataProvider _marketProvider;
    private readonly IMetalsDataProvider _metalsProvider;
    private readonly HoldingsManager _holdingsManager;

    private FinancialData? _currentData;
    private readonly List<PriceDataPoint> _chartHistory = new();
    private string _chartSymbol = "^AXJO"; // Default to ASX 200

    // Index symbols to track
    private static readonly string[] IndexSymbols = { "^AXJO", "^AORD" };

    // Metal symbols to track
    private static readonly string[] MetalSymbols = { "XAU", "XAG" };

    /// <summary>
    /// Gets the current financial data.
    /// </summary>
    public FinancialData? CurrentData => _currentData;

    /// <summary>
    /// Gets the holdings manager for portfolio access.
    /// </summary>
    public HoldingsManager HoldingsManager => _holdingsManager;

    /// <summary>
    /// Gets the current chart history data.
    /// </summary>
    public IReadOnlyList<PriceDataPoint> ChartHistory => _chartHistory;

    /// <summary>
    /// Gets or sets the symbol for chart data.
    /// </summary>
    public string ChartSymbol
    {
        get => _chartSymbol;
        set
        {
            if (_chartSymbol != value)
            {
                _chartSymbol = value;
                // Chart history will be updated on next refresh
            }
        }
    }

    /// <summary>
    /// Refresh interval: 15 minutes during market hours, 1 hour when closed.
    /// </summary>
    protected override TimeSpan RefreshInterval => IsMarketOpen()
        ? TimeSpan.FromMinutes(15)
        : TimeSpan.FromHours(1);

    public FinancialService(
        IMarketDataProvider marketProvider,
        IMetalsDataProvider metalsProvider)
        : base("financial", "Financial Markets", "Financial")
    {
        _marketProvider = marketProvider ?? throw new ArgumentNullException(nameof(marketProvider));
        _metalsProvider = metalsProvider ?? throw new ArgumentNullException(nameof(metalsProvider));
        _holdingsManager = new HoldingsManager();
    }

    /// <summary>
    /// Creates a FinancialService with mock data providers for development.
    /// </summary>
    public static FinancialService CreateWithMockData()
    {
        return new FinancialService(
            new MockMarketDataProvider(),
            new MockMetalsDataProvider());
    }

    protected override void OnInitialize()
    {
        Log.Information("Initializing Financial Service (Provider: {Market}, {Metals})",
            _marketProvider.ProviderName, _metalsProvider.ProviderName);

        // Load holdings from file
        _holdingsManager.Load();

        // Subscribe to holdings changes
        _holdingsManager.HoldingsChanged += OnHoldingsChanged;
    }

    protected override void OnShutdown()
    {
        _holdingsManager.HoldingsChanged -= OnHoldingsChanged;
        Log.Information("Financial Service shutdown");
    }

    protected override async Task<object?> FetchDataAsync(CancellationToken cancellationToken)
    {
        Log.Debug("Fetching financial data...");

        var data = new FinancialData
        {
            IsMarketOpen = IsMarketOpen(),
            LastRefresh = DateTime.Now
        };

        // Set market times
        var (openTime, closeTime) = GetNextMarketTimes();
        data.MarketOpenTime = openTime;
        data.MarketCloseTime = closeTime;

        try
        {
            // Fetch all data in parallel
            var indexTask = FetchIndicesAsync(cancellationToken);
            var metalsTask = FetchMetalsAsync(cancellationToken);
            var stocksTask = FetchStockQuotesAsync(cancellationToken);
            var forexTask = FetchExchangeRatesAsync(cancellationToken);

            await Task.WhenAll(indexTask, metalsTask, stocksTask, forexTask);

            // Process indices
            var indices = await indexTask;
            if (indices.TryGetValue("^AXJO", out var asx200))
                data.Asx200 = asx200;
            if (indices.TryGetValue("^AORD", out var allOrds))
                data.AllOrdinaries = allOrds;

            // Process exchange rates
            var forexRates = await forexTask;
            if (forexRates.TryGetValue("AUDUSD", out var audUsd))
                data.AudUsd = audUsd;

            // Process metals (already in AUD)
            var metals = await metalsTask;
            if (metals.TryGetValue("XAU", out var gold))
                data.Gold = gold;
            if (metals.TryGetValue("XAG", out var silver))
            {
                // Convert silver from AUD/oz to AUD/kg for display
                // 1 kg = 32.1507 troy ounces
                const decimal OzPerKg = 32.1507m;
                data.Silver = new MarketQuote
                {
                    Symbol = silver.Symbol,
                    Name = silver.Name,
                    Price = Math.Round(silver.Price * OzPerKg, 0),
                    Change = Math.Round(silver.Change * OzPerKg, 0),
                    ChangePercent = silver.ChangePercent, // Percentage stays the same
                    Open = Math.Round(silver.Open * OzPerKg, 0),
                    High = Math.Round(silver.High * OzPerKg, 0),
                    Low = Math.Round(silver.Low * OzPerKg, 0),
                    PreviousClose = Math.Round(silver.PreviousClose * OzPerKg, 0),
                    Volume = silver.Volume,
                    LastUpdated = silver.LastUpdated
                };
            }

            // Update holdings with stock quotes
            var stockQuotes = await stocksTask;

            // Add metal quotes to the combined dictionary for holdings update
            foreach (var metal in metals)
            {
                stockQuotes[metal.Key] = metal.Value;
            }

            _holdingsManager.UpdateQuotes(stockQuotes);

            // Build holdings list with current quotes
            data.Holdings = _holdingsManager.Holdings.ToList();

            // Calculate portfolio summary
            data.Portfolio = _holdingsManager.CalculatePortfolioSummary();

            Log.Information("Financial data fetched successfully. ASX200: {Asx200}, AUD/USD: {AudUsd}, Gold: {Gold} AUD/oz, Silver: {Silver} AUD/kg",
                data.Asx200?.Price, data.AudUsd?.Price, data.Gold?.Price, data.Silver?.Price);

            return data;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error fetching financial data");
            throw;
        }
    }

    protected override void StoreData(object data)
    {
        if (data is FinancialData financialData)
        {
            _currentData = financialData;
        }
    }

    protected override bool HasDataChanged(object newData, object? previousData)
    {
        // Always treat as changed for financial data (prices fluctuate)
        return true;
    }

    private async Task<Dictionary<string, MarketQuote>> FetchIndicesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _marketProvider.GetQuotesAsync(IndexSymbols, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch index data");
            return new Dictionary<string, MarketQuote>();
        }
    }

    private async Task<Dictionary<string, MarketQuote>> FetchMetalsAsync(CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, MarketQuote>();

        foreach (var metal in MetalSymbols)
        {
            try
            {
                var quote = await _metalsProvider.GetMetalPriceAsync(metal, cancellationToken);
                if (quote != null)
                {
                    results[metal] = quote;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to fetch {Metal} price", metal);
            }
        }

        return results;
    }

    private async Task<Dictionary<string, MarketQuote>> FetchExchangeRatesAsync(CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, MarketQuote>();

        try
        {
            var quote = await _metalsProvider.GetExchangeRateAsync("AUDUSD", cancellationToken);
            if (quote != null)
            {
                results["AUDUSD"] = quote;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch AUD/USD exchange rate");
        }

        return results;
    }

    private async Task<Dictionary<string, MarketQuote>> FetchStockQuotesAsync(CancellationToken cancellationToken)
    {
        var stockSymbols = _holdingsManager.GetAllSymbols();
        if (stockSymbols.Count == 0)
        {
            return new Dictionary<string, MarketQuote>();
        }

        try
        {
            return await _marketProvider.GetQuotesAsync(stockSymbols, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch stock quotes");
            return new Dictionary<string, MarketQuote>();
        }
    }

    /// <summary>
    /// Loads historical chart data for the current chart symbol.
    /// </summary>
    public async Task LoadChartHistoryAsync(ChartTimeRange range, CancellationToken cancellationToken = default)
    {
        Log.Debug("Loading chart history for {Symbol}, range: {Range}", _chartSymbol, range);

        try
        {
            var days = range switch
            {
                ChartTimeRange.OneDay => 1,
                ChartTimeRange.OneWeek => 7,
                ChartTimeRange.OneMonth => 30,
                ChartTimeRange.ThreeMonths => 90,
                ChartTimeRange.OneYear => 365,
                ChartTimeRange.FiveYears => 1825,
                _ => 30
            };

            List<PriceDataPoint> history;

            // Use appropriate provider based on symbol
            if (_chartSymbol == "XAU" || _chartSymbol == "XAG")
            {
                history = await _metalsProvider.GetMetalHistoryAsync(_chartSymbol, days, cancellationToken);
            }
            else if (days > 365)
            {
                // Use monthly data for long time ranges
                var months = days / 30;
                history = await _marketProvider.GetMonthlyHistoryAsync(_chartSymbol, months, cancellationToken);
            }
            else
            {
                history = await _marketProvider.GetDailyHistoryAsync(_chartSymbol, days, cancellationToken);
            }

            lock (_chartHistory)
            {
                _chartHistory.Clear();
                _chartHistory.AddRange(history);
            }

            Log.Information("Loaded {Count} history points for {Symbol}", history.Count, _chartSymbol);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load chart history for {Symbol}", _chartSymbol);
        }
    }

    private void OnHoldingsChanged(object? sender, EventArgs e)
    {
        // Trigger a refresh when holdings change
        if (IsRunning)
        {
            RefreshNow();
        }
    }

    /// <summary>
    /// Checks if the ASX market is currently open.
    /// ASX trading hours: 10:00 AM - 4:00 PM AEST (Sydney time).
    /// </summary>
    public static bool IsMarketOpen()
    {
        try
        {
            var sydneyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
            var sydneyTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, sydneyTimeZone);

            // Weekend check
            if (sydneyTime.DayOfWeek == DayOfWeek.Saturday || sydneyTime.DayOfWeek == DayOfWeek.Sunday)
                return false;

            // Market hours: 10:00 AM - 4:00 PM
            var time = sydneyTime.TimeOfDay;
            return time >= new TimeSpan(10, 0, 0) && time < new TimeSpan(16, 0, 0);
        }
        catch
        {
            // If timezone conversion fails, assume market is open
            return true;
        }
    }

    /// <summary>
    /// Gets the next market open and close times.
    /// </summary>
    public static (DateTime? OpenTime, DateTime? CloseTime) GetNextMarketTimes()
    {
        try
        {
            var sydneyTimeZone = TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
            var sydneyTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, sydneyTimeZone);
            var today = sydneyTime.Date;

            // Find next trading day
            var nextTradingDay = today;
            while (nextTradingDay.DayOfWeek == DayOfWeek.Saturday || nextTradingDay.DayOfWeek == DayOfWeek.Sunday)
            {
                nextTradingDay = nextTradingDay.AddDays(1);
            }

            var openTime = nextTradingDay.AddHours(10);
            var closeTime = nextTradingDay.AddHours(16);

            // Convert back to local time
            var localOpenTime = TimeZoneInfo.ConvertTime(openTime, sydneyTimeZone, TimeZoneInfo.Local);
            var localCloseTime = TimeZoneInfo.ConvertTime(closeTime, sydneyTimeZone, TimeZoneInfo.Local);

            if (IsMarketOpen())
            {
                return (null, localCloseTime);
            }
            else
            {
                return (localOpenTime, null);
            }
        }
        catch
        {
            return (null, null);
        }
    }
}
