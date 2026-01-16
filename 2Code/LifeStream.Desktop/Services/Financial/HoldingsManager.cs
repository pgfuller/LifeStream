using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LifeStream.Core.Infrastructure;
using Newtonsoft.Json;
using Serilog;

namespace LifeStream.Desktop.Services.Financial;

/// <summary>
/// Manages the user's portfolio holdings.
/// Handles loading/saving to JSON file and CRUD operations.
/// </summary>
public class HoldingsManager
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.Data);

    private readonly string _holdingsFilePath;
    private readonly List<HoldingItem> _holdings = new();
    private readonly object _lock = new();

    /// <summary>
    /// Gets the current holdings (read-only snapshot).
    /// </summary>
    public IReadOnlyList<HoldingItem> Holdings
    {
        get
        {
            lock (_lock)
            {
                return _holdings.ToList();
            }
        }
    }

    /// <summary>
    /// Event raised when holdings are modified.
    /// </summary>
    public event EventHandler? HoldingsChanged;

    public HoldingsManager()
    {
        var dataPath = AppPaths.GetServiceDataPath("Financial");
        _holdingsFilePath = Path.Combine(dataPath, "holdings.json");
    }

    /// <summary>
    /// Loads holdings from the JSON file.
    /// If the file doesn't exist, creates default demo holdings.
    /// </summary>
    public void Load()
    {
        lock (_lock)
        {
            _holdings.Clear();

            if (File.Exists(_holdingsFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_holdingsFilePath);
                    var config = JsonConvert.DeserializeObject<HoldingsConfig>(json);
                    if (config?.Holdings != null)
                    {
                        _holdings.AddRange(config.Holdings);
                    }
                    Log.Information("Loaded {Count} holdings from {Path}", _holdings.Count, _holdingsFilePath);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to load holdings from {Path}", _holdingsFilePath);
                    CreateDefaultHoldings();
                }
            }
            else
            {
                Log.Information("Holdings file not found, creating defaults");
                CreateDefaultHoldings();
                Save();
            }
        }
    }

    /// <summary>
    /// Saves holdings to the JSON file.
    /// </summary>
    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var config = new HoldingsConfig { Holdings = _holdings };
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_holdingsFilePath, json);
                Log.Debug("Saved {Count} holdings to {Path}", _holdings.Count, _holdingsFilePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save holdings to {Path}", _holdingsFilePath);
            }
        }
    }

    /// <summary>
    /// Adds a new holding to the portfolio.
    /// </summary>
    public void AddHolding(string symbol, string name, AssetType assetType, decimal? quantity = null)
    {
        lock (_lock)
        {
            // Check if already exists
            if (_holdings.Any(h => h.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Warning("Holding {Symbol} already exists, use UpdateHolding instead", symbol);
                return;
            }

            var holding = new HoldingItem
            {
                Symbol = symbol.ToUpperInvariant(),
                Name = name,
                AssetType = assetType,
                Quantity = quantity
            };

            _holdings.Add(holding);
            Log.Information("Added holding: {Symbol} ({Name})", symbol, name);
        }

        Save();
        HoldingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes a holding from the portfolio.
    /// </summary>
    public void RemoveHolding(string symbol)
    {
        lock (_lock)
        {
            var holding = _holdings.FirstOrDefault(h => h.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            if (holding != null)
            {
                _holdings.Remove(holding);
                Log.Information("Removed holding: {Symbol}", symbol);
            }
        }

        Save();
        HoldingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the quantity for an existing holding.
    /// </summary>
    public void UpdateHolding(string symbol, decimal? quantity)
    {
        lock (_lock)
        {
            var holding = _holdings.FirstOrDefault(h => h.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            if (holding != null)
            {
                holding.Quantity = quantity;
                Log.Information("Updated holding {Symbol} quantity to {Quantity}", symbol, quantity);
            }
        }

        Save();
        HoldingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets all symbols that need price updates (holdings + commodities).
    /// </summary>
    public List<string> GetAllSymbols()
    {
        lock (_lock)
        {
            return _holdings
                .Where(h => h.AssetType == AssetType.Stock)
                .Select(h => h.Symbol)
                .ToList();
        }
    }

    /// <summary>
    /// Gets the metal symbols from holdings.
    /// </summary>
    public List<string> GetMetalSymbols()
    {
        lock (_lock)
        {
            return _holdings
                .Where(h => h.AssetType == AssetType.Gold || h.AssetType == AssetType.Silver)
                .Select(h => h.Symbol)
                .ToList();
        }
    }

    /// <summary>
    /// Updates the quotes for holdings (called by service after fetching data).
    /// </summary>
    public void UpdateQuotes(Dictionary<string, MarketQuote> quotes)
    {
        lock (_lock)
        {
            foreach (var holding in _holdings)
            {
                if (quotes.TryGetValue(holding.Symbol, out var quote))
                {
                    holding.CurrentQuote = quote;
                }
            }
        }
    }

    /// <summary>
    /// Calculates the portfolio summary from current holdings.
    /// </summary>
    public PortfolioSummary? CalculatePortfolioSummary()
    {
        lock (_lock)
        {
            var holdingsWithValue = _holdings
                .Where(h => h.Quantity.HasValue && h.HoldingValue.HasValue)
                .ToList();

            if (holdingsWithValue.Count == 0)
                return null;

            var totalValue = holdingsWithValue.Sum(h => h.HoldingValue!.Value);

            // Calculate daily change based on previous close values
            decimal previousTotalValue = 0;
            foreach (var holding in holdingsWithValue)
            {
                if (holding.CurrentQuote != null && holding.Quantity.HasValue)
                {
                    var previousPrice = holding.CurrentQuote.PreviousClose;
                    var previousValue = holding.AssetType switch
                    {
                        AssetType.Stock => holding.Quantity.Value * previousPrice,
                        AssetType.Gold => holding.Quantity.Value * previousPrice,
                        AssetType.Silver => holding.Quantity.Value * 32.1507m * previousPrice,
                        _ => 0
                    };
                    previousTotalValue += previousValue;
                }
            }

            var dailyChange = totalValue - previousTotalValue;
            var dailyChangePercent = previousTotalValue > 0
                ? (dailyChange / previousTotalValue) * 100
                : 0;

            return new PortfolioSummary
            {
                TotalValue = totalValue,
                DailyChange = dailyChange,
                DailyChangePercent = dailyChangePercent,
                HoldingsCount = holdingsWithValue.Count,
                LastUpdated = DateTime.Now
            };
        }
    }

    private void CreateDefaultHoldings()
    {
        // Create some demo holdings for development/testing
        _holdings.AddRange(new[]
        {
            new HoldingItem { Symbol = "XAU", Name = "Gold", AssetType = AssetType.Gold, Quantity = 5.0m },
            new HoldingItem { Symbol = "XAG", Name = "Silver", AssetType = AssetType.Silver, Quantity = 2.0m },
            new HoldingItem { Symbol = "BHP.AX", Name = "BHP Group", AssetType = AssetType.Stock, Quantity = 200 },
            new HoldingItem { Symbol = "CBA.AX", Name = "Commonwealth Bank", AssetType = AssetType.Stock, Quantity = 50 },
            new HoldingItem { Symbol = "CSL.AX", Name = "CSL Limited", AssetType = AssetType.Stock, Quantity = 20 }
        });
    }

    /// <summary>
    /// Internal class for JSON serialization.
    /// </summary>
    private class HoldingsConfig
    {
        public List<HoldingItem> Holdings { get; set; } = new();
    }
}
