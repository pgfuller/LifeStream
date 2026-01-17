using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LifeStream.Core.Infrastructure;
using Newtonsoft.Json.Linq;
using Serilog;

namespace LifeStream.Desktop.Services.Financial;

/// <summary>
/// Alpha Vantage implementation of IMarketDataProvider for stocks and indices.
/// Supports ASX stocks (with .AX suffix) and global indices.
/// </summary>
public class AlphaVantageMarketDataProvider : IMarketDataProvider
{
    private static readonly ILogger Log = LoggingConfig.ForCategory($"{LoggingConfig.Categories.Sources}.Financial.AlphaVantage");

    private readonly AlphaVantageClient _client;

    // Map of symbols to friendly names
    private static readonly Dictionary<string, string> SymbolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        { "^AXJO", "S&P/ASX 200" },
        { "^AORD", "All Ordinaries" },
        { "BHP.AX", "BHP Group" },
        { "CBA.AX", "Commonwealth Bank" },
        { "CSL.AX", "CSL Limited" },
        { "NAB.AX", "National Australia Bank" },
        { "WBC.AX", "Westpac" },
        { "ANZ.AX", "ANZ Banking Group" },
        { "WES.AX", "Wesfarmers" },
        { "WOW.AX", "Woolworths" },
        { "MQG.AX", "Macquarie Group" },
        { "FMG.AX", "Fortescue Metals" },
        { "RIO.AX", "Rio Tinto" },
        { "TLS.AX", "Telstra" },
        { "NCM.AX", "Newcrest Mining" },
        { "STO.AX", "Santos" },
        { "WDS.AX", "Woodside Energy" },
        { "QAN.AX", "Qantas" },
        { "ALL.AX", "Aristocrat Leisure" }
    };

    public string ProviderName => "Alpha Vantage";
    public bool IsMock => false;

    public AlphaVantageMarketDataProvider(AlphaVantageClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<MarketQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        // Alpha Vantage uses different symbol format for ASX indices
        var apiSymbol = ConvertSymbol(symbol);

        var json = await _client.RequestAsync(
            "GLOBAL_QUOTE",
            $"symbol={apiSymbol}",
            cancellationToken);

        if (json == null)
            return null;

        return ParseGlobalQuote(json, symbol);
    }

    public async Task<Dictionary<string, MarketQuote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, MarketQuote>();

        // Alpha Vantage doesn't support batch quotes on free tier, so we fetch one by one
        // To minimize API calls, we prioritize indices over individual stocks
        var symbolList = symbols.ToList();
        var prioritized = symbolList
            .OrderBy(s => s.StartsWith("^") ? 0 : 1) // Indices first
            .ToList();

        foreach (var symbol in prioritized)
        {
            // Check if we're over the limit
            if (_client.IsLimitReached)
            {
                Log.Warning("API limit reached, skipping remaining symbols");
                break;
            }

            var quote = await GetQuoteAsync(symbol, cancellationToken);
            if (quote != null)
            {
                results[symbol] = quote;
            }

            // Small delay between requests to be nice to the API
            if (prioritized.IndexOf(symbol) < prioritized.Count - 1)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return results;
    }

    public async Task<List<PriceDataPoint>> GetDailyHistoryAsync(string symbol, int days = 90, CancellationToken cancellationToken = default)
    {
        var apiSymbol = ConvertSymbol(symbol);
        var outputSize = days > 100 ? "full" : "compact"; // compact = 100 days, full = 20 years

        var json = await _client.RequestAsync(
            "TIME_SERIES_DAILY",
            $"symbol={apiSymbol}&outputsize={outputSize}",
            cancellationToken);

        if (json == null)
            return new List<PriceDataPoint>();

        return ParseTimeSeries(json, "Time Series (Daily)", days);
    }

    public async Task<List<PriceDataPoint>> GetMonthlyHistoryAsync(string symbol, int months = 240, CancellationToken cancellationToken = default)
    {
        var apiSymbol = ConvertSymbol(symbol);

        var json = await _client.RequestAsync(
            "TIME_SERIES_MONTHLY",
            $"symbol={apiSymbol}",
            cancellationToken);

        if (json == null)
            return new List<PriceDataPoint>();

        return ParseTimeSeries(json, "Monthly Time Series", months);
    }

    /// <summary>
    /// Converts our internal symbol format to Alpha Vantage format.
    /// </summary>
    private static string ConvertSymbol(string symbol)
    {
        // Alpha Vantage uses different format for some indices
        // ^AXJO → XJO.AX (ASX 200 index)
        // ^AORD → XAO.AX (All Ordinaries)
        return symbol switch
        {
            "^AXJO" => "XJO.AX",
            "^AORD" => "XAO.AX",
            _ => symbol
        };
    }

    private MarketQuote? ParseGlobalQuote(JObject json, string originalSymbol)
    {
        var quoteData = json["Global Quote"];
        if (quoteData == null || !quoteData.HasValues)
        {
            Log.Warning("No quote data in response for {Symbol}", originalSymbol);
            return null;
        }

        try
        {
            var price = ParseDecimal(quoteData["05. price"]);
            var previousClose = ParseDecimal(quoteData["08. previous close"]);
            var change = ParseDecimal(quoteData["09. change"]);
            var changePercent = ParseDecimal(quoteData["10. change percent"]?.ToString()?.TrimEnd('%'));

            return new MarketQuote
            {
                Symbol = originalSymbol,
                Name = GetSymbolName(originalSymbol),
                Price = price,
                Change = change,
                ChangePercent = changePercent,
                Open = ParseDecimal(quoteData["02. open"]),
                High = ParseDecimal(quoteData["03. high"]),
                Low = ParseDecimal(quoteData["04. low"]),
                PreviousClose = previousClose,
                Volume = ParseLong(quoteData["06. volume"]),
                LastUpdated = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error parsing quote for {Symbol}", originalSymbol);
            return null;
        }
    }

    private List<PriceDataPoint> ParseTimeSeries(JObject json, string seriesKey, int maxEntries)
    {
        var result = new List<PriceDataPoint>();
        var series = json[seriesKey] as JObject;

        if (series == null)
        {
            Log.Warning("No time series data found in response (key: {Key})", seriesKey);
            return result;
        }

        foreach (var entry in series.Properties().Take(maxEntries))
        {
            try
            {
                if (!DateTime.TryParse(entry.Name, out var date))
                    continue;

                var data = entry.Value;
                result.Add(new PriceDataPoint
                {
                    Date = date,
                    Open = ParseDecimal(data["1. open"]),
                    High = ParseDecimal(data["2. high"]),
                    Low = ParseDecimal(data["3. low"]),
                    Close = ParseDecimal(data["4. close"]),
                    Volume = ParseLong(data["5. volume"])
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error parsing time series entry for {Date}", entry.Name);
            }
        }

        // Sort by date descending (most recent first)
        result.Sort((a, b) => b.Date.CompareTo(a.Date));
        return result;
    }

    private static string GetSymbolName(string symbol)
    {
        if (SymbolNames.TryGetValue(symbol, out var name))
            return name;

        // Strip .AX suffix for display
        return symbol.EndsWith(".AX", StringComparison.OrdinalIgnoreCase)
            ? symbol[..^3]
            : symbol;
    }

    private static decimal ParseDecimal(JToken? token)
    {
        if (token == null)
            return 0;

        var str = token.ToString().Trim();
        if (string.IsNullOrEmpty(str))
            return 0;

        // Remove any percentage sign
        str = str.TrimEnd('%');

        return decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }

    private static long ParseLong(JToken? token)
    {
        if (token == null)
            return 0;

        return long.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }
}
