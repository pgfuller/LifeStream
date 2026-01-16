using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LifeStream.Desktop.Services.Financial;

/// <summary>
/// Mock implementation of IMetalsDataProvider for development/testing.
/// Generates realistic precious metals prices without calling external APIs.
/// </summary>
public class MockMetalsDataProvider : IMetalsDataProvider
{
    private readonly Random _random = new();

    // Base prices in AUD per troy ounce (approximately current market rates)
    private const decimal GoldBasePrice = 3250m;
    private const decimal SilverBasePrice = 38.50m;
    private const decimal GoldVolatility = 0.008m;   // Gold is less volatile
    private const decimal SilverVolatility = 0.015m; // Silver is more volatile

    public string ProviderName => "Mock Metals Data";
    public bool IsMock => true;

    public async Task<MarketQuote?> GetMetalPriceAsync(string metal, CancellationToken cancellationToken = default)
    {
        // Simulate network latency
        await Task.Delay(_random.Next(200, 500), cancellationToken);

        return metal.ToUpperInvariant() switch
        {
            "XAU" => GenerateMetalQuote("XAU", "Gold", GoldBasePrice, GoldVolatility),
            "XAG" => GenerateMetalQuote("XAG", "Silver", SilverBasePrice, SilverVolatility),
            _ => null
        };
    }

    public async Task<List<PriceDataPoint>> GetMetalHistoryAsync(string metal, int days = 90, CancellationToken cancellationToken = default)
    {
        await Task.Delay(_random.Next(300, 700), cancellationToken);

        return metal.ToUpperInvariant() switch
        {
            "XAU" => GenerateHistory(GoldBasePrice, GoldVolatility, days),
            "XAG" => GenerateHistory(SilverBasePrice, SilverVolatility, days),
            _ => new List<PriceDataPoint>()
        };
    }

    private MarketQuote GenerateMetalQuote(string symbol, string name, decimal basePrice, decimal volatility)
    {
        // Metals trade 24 hours, so generate small intraday movement
        var changePercent = (decimal)((_random.NextDouble() - 0.5) * 2 * (double)volatility * 100);
        var change = basePrice * changePercent / 100;
        var price = basePrice + change;

        // Generate realistic daily range
        var dayRange = basePrice * volatility * 2;
        var open = basePrice + (decimal)(_random.NextDouble() - 0.5) * dayRange * 0.3m;
        var high = Math.Max(price, open) + (decimal)_random.NextDouble() * dayRange * 0.2m;
        var low = Math.Min(price, open) - (decimal)_random.NextDouble() * dayRange * 0.2m;

        return new MarketQuote
        {
            Symbol = symbol,
            Name = name,
            Price = Math.Round(price, 2),
            Change = Math.Round(change, 2),
            ChangePercent = Math.Round(changePercent, 2),
            Open = Math.Round(open, 2),
            High = Math.Round(high, 2),
            Low = Math.Round(low, 2),
            PreviousClose = Math.Round(basePrice, 2),
            Volume = 0, // Spot prices don't have volume
            LastUpdated = DateTime.Now
        };
    }

    private List<PriceDataPoint> GenerateHistory(decimal basePrice, decimal volatility, int days)
    {
        var history = new List<PriceDataPoint>();
        var currentPrice = basePrice * 0.85m; // Start somewhat lower
        var today = DateTime.Today;

        for (int i = days - 1; i >= 0; i--)
        {
            var date = today.AddDays(-i);

            // Metals trade every day (simplified - ignoring market closures)
            // Random walk with slight upward bias (metals tend to preserve value)
            var dailyReturn = (decimal)((_random.NextDouble() - 0.47) * (double)volatility * 2);
            currentPrice *= (1 + dailyReturn);

            // Ensure price stays positive and reasonable
            currentPrice = Math.Max(currentPrice, basePrice * 0.5m);
            currentPrice = Math.Min(currentPrice, basePrice * 1.5m);

            // Converge toward base price in recent days
            if (i < 30)
            {
                currentPrice = currentPrice * 0.95m + basePrice * 0.05m;
            }

            var dayRange = currentPrice * volatility;
            var open = currentPrice + (decimal)(_random.NextDouble() - 0.5) * dayRange;
            var close = currentPrice;
            var high = Math.Max(open, close) + (decimal)_random.NextDouble() * dayRange * 0.3m;
            var low = Math.Min(open, close) - (decimal)_random.NextDouble() * dayRange * 0.3m;

            history.Add(new PriceDataPoint
            {
                Date = date,
                Open = Math.Round(open, 2),
                High = Math.Round(high, 2),
                Low = Math.Round(low, 2),
                Close = Math.Round(close, 2),
                Volume = 0
            });
        }

        // Return most recent first
        history.Reverse();
        return history;
    }
}
