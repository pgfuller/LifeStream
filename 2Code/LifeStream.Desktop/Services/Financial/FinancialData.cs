using System;
using System.Collections.Generic;

namespace LifeStream.Desktop.Services.Financial;

/// <summary>
/// Type of asset in a portfolio holding.
/// </summary>
public enum AssetType
{
    /// <summary>ASX shares - quantity in shares.</summary>
    Stock,
    /// <summary>Physical gold - quantity in troy ounces.</summary>
    Gold,
    /// <summary>Physical silver - quantity in kilograms.</summary>
    Silver
}

/// <summary>
/// Market quote for a symbol (stock, index, or commodity).
/// </summary>
public class MarketQuote
{
    /// <summary>Symbol (e.g., "BHP.AX", "XAU", "^AXJO").</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Display name (e.g., "BHP Group", "Gold", "ASX 200").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Current/last price.</summary>
    public decimal Price { get; set; }

    /// <summary>Daily change in price.</summary>
    public decimal Change { get; set; }

    /// <summary>Daily change as percentage.</summary>
    public decimal ChangePercent { get; set; }

    /// <summary>Opening price.</summary>
    public decimal Open { get; set; }

    /// <summary>Day's high.</summary>
    public decimal High { get; set; }

    /// <summary>Day's low.</summary>
    public decimal Low { get; set; }

    /// <summary>Previous close price.</summary>
    public decimal PreviousClose { get; set; }

    /// <summary>Trading volume (for stocks).</summary>
    public long Volume { get; set; }

    /// <summary>When this quote was last updated.</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Gets the change formatted for display (e.g., "+1.23 (+0.45%)").
    /// </summary>
    public string ChangeDisplay
    {
        get
        {
            var sign = Change >= 0 ? "+" : "";
            return $"{sign}{Change:N2} ({sign}{ChangePercent:N2}%)";
        }
    }

    /// <summary>
    /// Gets the price formatted for display with currency.
    /// </summary>
    public string PriceDisplay => $"${Price:N2}";
}

/// <summary>
/// Historical price data point.
/// </summary>
public class PriceDataPoint
{
    public DateTime Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
}

/// <summary>
/// A holding in the portfolio (stock or commodity).
/// </summary>
public class HoldingItem
{
    /// <summary>Symbol (e.g., "BHP.AX", "XAU", "XAG").</summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Display name (e.g., "BHP Group", "Gold", "Silver").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Type of asset.</summary>
    public AssetType AssetType { get; set; }

    /// <summary>Quantity held (shares, ounces, or kg depending on AssetType).</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Current market quote (populated by service).</summary>
    public MarketQuote? CurrentQuote { get; set; }

    /// <summary>
    /// Gets the unit label for the quantity.
    /// </summary>
    public string QuantityUnit => AssetType switch
    {
        AssetType.Stock => "shares",
        AssetType.Gold => "oz",
        AssetType.Silver => "kg",
        _ => ""
    };

    /// <summary>
    /// Gets the quantity formatted for display.
    /// </summary>
    public string QuantityDisplay => Quantity.HasValue
        ? AssetType == AssetType.Stock
            ? $"{Quantity:N0} {QuantityUnit}"
            : $"{Quantity:N2} {QuantityUnit}"
        : "-";

    /// <summary>
    /// Gets the holding value in AUD.
    /// Handles unit conversion for silver (kg to oz).
    /// </summary>
    public decimal? HoldingValue
    {
        get
        {
            if (!Quantity.HasValue || CurrentQuote?.Price == null)
                return null;

            return AssetType switch
            {
                AssetType.Stock => Quantity.Value * CurrentQuote.Price,
                AssetType.Gold => Quantity.Value * CurrentQuote.Price,  // oz × $/oz
                AssetType.Silver => Quantity.Value * 32.1507m * CurrentQuote.Price,  // kg × oz/kg × $/oz
                _ => null
            };
        }
    }

    /// <summary>
    /// Gets the holding value formatted for display.
    /// </summary>
    public string HoldingValueDisplay => HoldingValue.HasValue
        ? $"${HoldingValue:N0}"
        : "-";

    /// <summary>
    /// Gets the price display appropriate for the asset type.
    /// </summary>
    public string PriceDisplay
    {
        get
        {
            if (CurrentQuote == null) return "-";

            return AssetType switch
            {
                AssetType.Stock => $"${CurrentQuote.Price:N2}",
                AssetType.Gold => $"${CurrentQuote.Price:N2}/oz",
                AssetType.Silver => $"${CurrentQuote.Price:N2}/oz",
                _ => $"${CurrentQuote.Price:N2}"
            };
        }
    }

    /// <summary>
    /// Gets the change display from the current quote.
    /// </summary>
    public string ChangeDisplay => CurrentQuote != null
        ? $"{(CurrentQuote.ChangePercent >= 0 ? "+" : "")}{CurrentQuote.ChangePercent:N2}%"
        : "-";

    /// <summary>
    /// Indicates whether the price has increased.
    /// </summary>
    public bool IsUp => CurrentQuote?.Change >= 0;
}

/// <summary>
/// Summary of portfolio value and performance.
/// </summary>
public class PortfolioSummary
{
    /// <summary>Total portfolio value in AUD.</summary>
    public decimal TotalValue { get; set; }

    /// <summary>Daily change in portfolio value.</summary>
    public decimal DailyChange { get; set; }

    /// <summary>Daily change as percentage.</summary>
    public decimal DailyChangePercent { get; set; }

    /// <summary>Number of holdings with quantities.</summary>
    public int HoldingsCount { get; set; }

    /// <summary>When the portfolio was last calculated.</summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Gets the total value formatted for display.
    /// </summary>
    public string TotalValueDisplay => $"${TotalValue:N0}";

    /// <summary>
    /// Gets the daily change formatted for display.
    /// </summary>
    public string DailyChangeDisplay
    {
        get
        {
            var sign = DailyChange >= 0 ? "+" : "";
            return $"{sign}${Math.Abs(DailyChange):N0} ({sign}{DailyChangePercent:N2}%)";
        }
    }
}

/// <summary>
/// Contains all financial data for display.
/// </summary>
public class FinancialData
{
    /// <summary>ASX 200 index quote.</summary>
    public MarketQuote? Asx200 { get; set; }

    /// <summary>All Ordinaries index quote.</summary>
    public MarketQuote? AllOrdinaries { get; set; }

    /// <summary>Gold spot price quote.</summary>
    public MarketQuote? Gold { get; set; }

    /// <summary>Silver spot price quote.</summary>
    public MarketQuote? Silver { get; set; }

    /// <summary>User's holdings (stocks + commodities).</summary>
    public List<HoldingItem> Holdings { get; set; } = new();

    /// <summary>Portfolio summary (computed from holdings).</summary>
    public PortfolioSummary? Portfolio { get; set; }

    /// <summary>Whether the ASX market is currently open.</summary>
    public bool IsMarketOpen { get; set; }

    /// <summary>Next market open time (if closed).</summary>
    public DateTime? MarketOpenTime { get; set; }

    /// <summary>Market close time (if open).</summary>
    public DateTime? MarketCloseTime { get; set; }

    /// <summary>When this data was last refreshed.</summary>
    public DateTime LastRefresh { get; set; }

    /// <summary>API calls made today (for quota tracking).</summary>
    public int ApiCallsToday { get; set; }

    /// <summary>Remaining API calls (for quota tracking).</summary>
    public int ApiCallsRemaining { get; set; }

    /// <summary>
    /// Gets the market status display text.
    /// </summary>
    public string MarketStatusDisplay
    {
        get
        {
            if (IsMarketOpen && MarketCloseTime.HasValue)
                return $"OPEN (closes {MarketCloseTime:h:mm tt})";
            if (!IsMarketOpen && MarketOpenTime.HasValue)
                return $"CLOSED (opens {MarketOpenTime:h:mm tt})";
            return IsMarketOpen ? "OPEN" : "CLOSED";
        }
    }
}

/// <summary>
/// Time range for historical chart data.
/// </summary>
public enum ChartTimeRange
{
    OneDay,
    OneWeek,
    OneMonth,
    ThreeMonths,
    OneYear,
    FiveYears
}
