using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LifeStream.Desktop.Services.Financial;

/// <summary>
/// Mock implementation of IMarketDataProvider for development/testing.
/// Generates realistic-looking market data without calling external APIs.
/// </summary>
public class MockMarketDataProvider : IMarketDataProvider
{
    private readonly Random _random = new();
    private readonly Dictionary<string, MockStockConfig> _stockConfigs;

    public string ProviderName => "Mock Market Data";
    public bool IsMock => true;

    public MockMarketDataProvider()
    {
        // Configure realistic base prices for various symbols
        _stockConfigs = new Dictionary<string, MockStockConfig>(StringComparer.OrdinalIgnoreCase)
        {
            // ASX Indices
            ["^AXJO"] = new MockStockConfig("ASX 200", 7850m, 0.015m),
            ["^AORD"] = new MockStockConfig("All Ordinaries", 8100m, 0.014m),

            // ASX Blue Chips
            ["BHP.AX"] = new MockStockConfig("BHP Group", 45.50m, 0.02m),
            ["CBA.AX"] = new MockStockConfig("Commonwealth Bank", 112.80m, 0.015m),
            ["CSL.AX"] = new MockStockConfig("CSL Limited", 285.00m, 0.018m),
            ["NAB.AX"] = new MockStockConfig("National Australia Bank", 32.50m, 0.016m),
            ["WBC.AX"] = new MockStockConfig("Westpac", 25.80m, 0.017m),
            ["ANZ.AX"] = new MockStockConfig("ANZ Bank", 28.40m, 0.016m),
            ["WES.AX"] = new MockStockConfig("Wesfarmers", 65.20m, 0.015m),
            ["WOW.AX"] = new MockStockConfig("Woolworths", 32.10m, 0.012m),
            ["TLS.AX"] = new MockStockConfig("Telstra", 3.85m, 0.018m),
            ["RIO.AX"] = new MockStockConfig("Rio Tinto", 118.50m, 0.022m),
            ["FMG.AX"] = new MockStockConfig("Fortescue Metals", 21.30m, 0.025m),
            ["MQG.AX"] = new MockStockConfig("Macquarie Group", 195.00m, 0.018m),
            ["NCM.AX"] = new MockStockConfig("Newcrest Mining", 24.80m, 0.025m),
            ["STO.AX"] = new MockStockConfig("Santos", 7.25m, 0.022m),
            ["WPL.AX"] = new MockStockConfig("Woodside Energy", 32.50m, 0.020m),

            // Gold ETFs
            ["GOLD.AX"] = new MockStockConfig("ETFS Physical Gold", 28.50m, 0.008m),
            ["PMGOLD.AX"] = new MockStockConfig("Perth Mint Gold", 29.20m, 0.008m)
        };
    }

    public async Task<MarketQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default)
    {
        // Simulate network latency
        await Task.Delay(_random.Next(200, 600), cancellationToken);

        if (!_stockConfigs.TryGetValue(symbol, out var config))
        {
            // Return a generic quote for unknown symbols
            config = new MockStockConfig(symbol, 50m, 0.02m);
        }

        return GenerateQuote(symbol, config);
    }

    public async Task<Dictionary<string, MarketQuote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        // Simulate network latency (batch call is faster per-symbol)
        await Task.Delay(_random.Next(300, 800), cancellationToken);

        var results = new Dictionary<string, MarketQuote>();
        foreach (var symbol in symbols)
        {
            if (_stockConfigs.TryGetValue(symbol, out var config))
            {
                results[symbol] = GenerateQuote(symbol, config);
            }
            else
            {
                var genericConfig = new MockStockConfig(symbol, 50m, 0.02m);
                results[symbol] = GenerateQuote(symbol, genericConfig);
            }
        }
        return results;
    }

    public async Task<List<PriceDataPoint>> GetDailyHistoryAsync(string symbol, int days = 90, CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(400, 1000), cancellationToken);

        if (!_stockConfigs.TryGetValue(symbol, out var config))
        {
            config = new MockStockConfig(symbol, 50m, 0.02m);
        }

        return GenerateDailyHistory(config.BasePrice, config.Volatility, days);
    }

    public async Task<List<PriceDataPoint>> GetMonthlyHistoryAsync(string symbol, int months = 240, CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(500, 1200), cancellationToken);

        if (!_stockConfigs.TryGetValue(symbol, out var config))
        {
            config = new MockStockConfig(symbol, 50m, 0.02m);
        }

        return GenerateMonthlyHistory(config.BasePrice, config.Volatility, months);
    }

    private MarketQuote GenerateQuote(string symbol, MockStockConfig config)
    {
        // Generate a small random daily change
        var changePercent = (decimal)((_random.NextDouble() - 0.5) * 2 * (double)config.Volatility * 100);
        var change = config.BasePrice * changePercent / 100;
        var price = config.BasePrice + change;

        // Generate realistic OHLC based on the day's movement
        var dayRange = Math.Abs(change) + config.BasePrice * config.Volatility * 0.5m;
        var open = config.BasePrice + (decimal)(_random.NextDouble() - 0.5) * dayRange * 0.3m;
        var high = Math.Max(price, open) + (decimal)_random.NextDouble() * dayRange * 0.3m;
        var low = Math.Min(price, open) - (decimal)_random.NextDouble() * dayRange * 0.3m;

        return new MarketQuote
        {
            Symbol = symbol,
            Name = config.Name,
            Price = Math.Round(price, 2),
            Change = Math.Round(change, 2),
            ChangePercent = Math.Round(changePercent, 2),
            Open = Math.Round(open, 2),
            High = Math.Round(high, 2),
            Low = Math.Round(low, 2),
            PreviousClose = Math.Round(config.BasePrice, 2),
            Volume = symbol.StartsWith("^") ? 0 : _random.Next(500000, 10000000),
            LastUpdated = DateTime.Now
        };
    }

    private List<PriceDataPoint> GenerateDailyHistory(decimal basePrice, decimal volatility, int days)
    {
        var history = new List<PriceDataPoint>();
        var currentPrice = basePrice;
        var today = DateTime.Today;

        for (int i = days - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);

            // Skip weekends
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                continue;

            // Random walk with slight upward bias
            var dailyReturn = (decimal)((_random.NextDouble() - 0.48) * (double)volatility * 2);
            currentPrice *= (1 + dailyReturn);

            // Ensure price stays positive
            currentPrice = Math.Max(currentPrice, basePrice * 0.3m);

            var dayRange = currentPrice * volatility;
            var open = currentPrice + (decimal)(_random.NextDouble() - 0.5) * dayRange;
            var close = currentPrice;
            var high = Math.Max(open, close) + (decimal)_random.NextDouble() * dayRange * 0.5m;
            var low = Math.Min(open, close) - (decimal)_random.NextDouble() * dayRange * 0.5m;

            history.Add(new PriceDataPoint
            {
                Date = date,
                Open = Math.Round(open, 2),
                High = Math.Round(high, 2),
                Low = Math.Round(low, 2),
                Close = Math.Round(close, 2),
                Volume = _random.Next(500000, 10000000)
            });
        }

        // Return most recent first
        history.Reverse();
        return history;
    }

    private List<PriceDataPoint> GenerateMonthlyHistory(decimal basePrice, decimal volatility, int months)
    {
        var history = new List<PriceDataPoint>();
        var currentPrice = basePrice * 0.3m; // Start lower for long-term history
        var today = DateTime.Today;

        for (int i = months - 1; i >= 0; i--)
        {
            var date = new DateTime(today.Year, today.Month, 1).AddMonths(-i);

            // Random walk with upward bias (markets tend to rise over time)
            var monthlyReturn = (decimal)((_random.NextDouble() - 0.45) * (double)volatility * 4);
            currentPrice *= (1 + monthlyReturn);

            // Ensure price stays positive and trends toward base price
            currentPrice = Math.Max(currentPrice, basePrice * 0.1m);
            if (i < 12) // Last year, converge toward current price
            {
                currentPrice = currentPrice * 0.9m + basePrice * 0.1m;
            }

            var monthRange = currentPrice * volatility * 2;
            var open = currentPrice + (decimal)(_random.NextDouble() - 0.5) * monthRange;
            var close = currentPrice;
            var high = Math.Max(open, close) + (decimal)_random.NextDouble() * monthRange;
            var low = Math.Min(open, close) - (decimal)_random.NextDouble() * monthRange;

            history.Add(new PriceDataPoint
            {
                Date = date,
                Open = Math.Round(open, 2),
                High = Math.Round(high, 2),
                Low = Math.Round(low, 2),
                Close = Math.Round(close, 2),
                Volume = _random.Next(10000000, 100000000)
            });
        }

        // Return most recent first
        history.Reverse();
        return history;
    }

    private class MockStockConfig
    {
        public string Name { get; }
        public decimal BasePrice { get; }
        public decimal Volatility { get; }

        public MockStockConfig(string name, decimal basePrice, decimal volatility)
        {
            Name = name;
            BasePrice = basePrice;
            Volatility = volatility;
        }
    }
}
