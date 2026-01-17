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
/// Alpha Vantage implementation of IMetalsDataProvider for precious metals and forex.
/// Uses CURRENCY_EXCHANGE_RATE for real-time prices and FX_DAILY for historical data.
/// </summary>
public class AlphaVantageMetalsDataProvider : IMetalsDataProvider
{
    private static readonly ILogger Log = LoggingConfig.ForCategory($"{LoggingConfig.Categories.Sources}.Financial.AlphaVantage");

    private readonly AlphaVantageClient _client;

    // Metal display names
    private static readonly Dictionary<string, string> MetalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        { "XAU", "Gold" },
        { "XAG", "Silver" }
    };

    public string ProviderName => "Alpha Vantage";
    public bool IsMock => false;

    public AlphaVantageMetalsDataProvider(AlphaVantageClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<MarketQuote?> GetMetalPriceAsync(string metal, CancellationToken cancellationToken = default)
    {
        // First get USD price, then convert to AUD
        var usdQuote = await GetCurrencyRateAsync(metal, "USD", cancellationToken);
        if (usdQuote == null)
            return null;

        // Get AUD/USD rate to convert to AUD
        var audUsdRate = await GetAudUsdRateAsync(cancellationToken);
        if (audUsdRate == 0)
        {
            Log.Warning("Could not get AUD/USD rate, using fallback of 0.65");
            audUsdRate = 0.65m; // Fallback rate
        }

        // Convert USD price to AUD (divide by AUD/USD rate)
        var audPrice = usdQuote.Price / audUsdRate;
        var audChange = usdQuote.Change / audUsdRate;
        var audOpen = usdQuote.Open / audUsdRate;
        var audHigh = usdQuote.High / audUsdRate;
        var audLow = usdQuote.Low / audUsdRate;
        var audPreviousClose = usdQuote.PreviousClose / audUsdRate;

        return new MarketQuote
        {
            Symbol = metal,
            Name = GetMetalName(metal),
            Price = Math.Round(audPrice, 2),
            Change = Math.Round(audChange, 2),
            ChangePercent = usdQuote.ChangePercent, // Percentage stays same
            Open = Math.Round(audOpen, 2),
            High = Math.Round(audHigh, 2),
            Low = Math.Round(audLow, 2),
            PreviousClose = Math.Round(audPreviousClose, 2),
            Volume = 0, // No volume for spot prices
            LastUpdated = DateTime.Now
        };
    }

    public async Task<MarketQuote?> GetExchangeRateAsync(string pair, CancellationToken cancellationToken = default)
    {
        // Parse pair (e.g., "AUDUSD" -> from=AUD, to=USD)
        if (pair.Length != 6)
        {
            Log.Warning("Invalid currency pair format: {Pair}", pair);
            return null;
        }

        var fromCurrency = pair[..3];
        var toCurrency = pair[3..];

        var quote = await GetCurrencyRateAsync(fromCurrency, toCurrency, cancellationToken);
        if (quote == null)
            return null;

        quote.Symbol = pair;
        quote.Name = $"{fromCurrency}/{toCurrency}";
        return quote;
    }

    public async Task<List<PriceDataPoint>> GetMetalHistoryAsync(string metal, int days = 90, CancellationToken cancellationToken = default)
    {
        // Get historical data in USD
        var outputSize = days > 100 ? "full" : "compact";

        var json = await _client.RequestAsync(
            "FX_DAILY",
            $"from_symbol={metal}&to_symbol=USD&outputsize={outputSize}",
            cancellationToken);

        if (json == null)
            return new List<PriceDataPoint>();

        // Get current AUD/USD rate for conversion
        var audUsdRate = await GetAudUsdRateAsync(cancellationToken);
        if (audUsdRate == 0)
            audUsdRate = 0.65m;

        return ParseFxTimeSeries(json, days, audUsdRate);
    }

    private async Task<MarketQuote?> GetCurrencyRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken)
    {
        var json = await _client.RequestAsync(
            "CURRENCY_EXCHANGE_RATE",
            $"from_currency={fromCurrency}&to_currency={toCurrency}",
            cancellationToken);

        if (json == null)
            return null;

        return ParseExchangeRate(json, fromCurrency, toCurrency);
    }

    private async Task<decimal> GetAudUsdRateAsync(CancellationToken cancellationToken)
    {
        var quote = await GetCurrencyRateAsync("AUD", "USD", cancellationToken);
        return quote?.Price ?? 0;
    }

    private MarketQuote? ParseExchangeRate(JObject json, string fromCurrency, string toCurrency)
    {
        var rateData = json["Realtime Currency Exchange Rate"];
        if (rateData == null || !rateData.HasValues)
        {
            Log.Warning("No exchange rate data in response for {From}/{To}", fromCurrency, toCurrency);
            return null;
        }

        try
        {
            var price = ParseDecimal(rateData["5. Exchange Rate"]);
            var bidPrice = ParseDecimal(rateData["8. Bid Price"]);
            var askPrice = ParseDecimal(rateData["9. Ask Price"]);

            // Calculate change from bid/ask spread (approximation)
            // Note: Alpha Vantage doesn't provide previous close in this endpoint
            var spread = askPrice - bidPrice;
            var changePercent = spread > 0 ? (spread / price) * 100 : 0;

            return new MarketQuote
            {
                Symbol = $"{fromCurrency}{toCurrency}",
                Name = $"{fromCurrency}/{toCurrency}",
                Price = Math.Round(price, 4),
                Change = 0, // Will be calculated separately if needed
                ChangePercent = 0,
                Open = price,
                High = askPrice > 0 ? askPrice : price,
                Low = bidPrice > 0 ? bidPrice : price,
                PreviousClose = price, // Not available from this endpoint
                Volume = 0,
                LastUpdated = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error parsing exchange rate for {From}/{To}", fromCurrency, toCurrency);
            return null;
        }
    }

    private List<PriceDataPoint> ParseFxTimeSeries(JObject json, int maxEntries, decimal audUsdRate)
    {
        var result = new List<PriceDataPoint>();
        var series = json["Time Series FX (Daily)"] as JObject;

        if (series == null)
        {
            Log.Warning("No FX time series data found in response");
            return result;
        }

        foreach (var entry in series.Properties().Take(maxEntries))
        {
            try
            {
                if (!DateTime.TryParse(entry.Name, out var date))
                    continue;

                var data = entry.Value;

                // USD values
                var usdOpen = ParseDecimal(data["1. open"]);
                var usdHigh = ParseDecimal(data["2. high"]);
                var usdLow = ParseDecimal(data["3. low"]);
                var usdClose = ParseDecimal(data["4. close"]);

                // Convert to AUD
                result.Add(new PriceDataPoint
                {
                    Date = date,
                    Open = Math.Round(usdOpen / audUsdRate, 2),
                    High = Math.Round(usdHigh / audUsdRate, 2),
                    Low = Math.Round(usdLow / audUsdRate, 2),
                    Close = Math.Round(usdClose / audUsdRate, 2),
                    Volume = 0
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error parsing FX time series entry for {Date}", entry.Name);
            }
        }

        // Sort by date descending (most recent first)
        result.Sort((a, b) => b.Date.CompareTo(a.Date));
        return result;
    }

    private static string GetMetalName(string symbol)
    {
        return MetalNames.TryGetValue(symbol, out var name) ? name : symbol;
    }

    private static decimal ParseDecimal(JToken? token)
    {
        if (token == null)
            return 0;

        var str = token.ToString().Trim();
        if (string.IsNullOrEmpty(str))
            return 0;

        return decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0;
    }
}
