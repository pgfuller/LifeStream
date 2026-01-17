using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LifeStream.Desktop.Services.Financial;

/// <summary>
/// Interface for market data providers (stocks, indices).
/// Implementations can be mock (for development) or real API clients.
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// Gets a quote for a single symbol.
    /// </summary>
    /// <param name="symbol">The symbol (e.g., "BHP.AX", "^AXJO").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The quote, or null if not available.</returns>
    Task<MarketQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets quotes for multiple symbols in a single call.
    /// </summary>
    /// <param name="symbols">The symbols to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of symbol to quote.</returns>
    Task<Dictionary<string, MarketQuote>> GetQuotesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets daily historical price data.
    /// </summary>
    /// <param name="symbol">The symbol.</param>
    /// <param name="days">Number of days of history.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of price data points, most recent first.</returns>
    Task<List<PriceDataPoint>> GetDailyHistoryAsync(string symbol, int days = 90, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets monthly historical price data.
    /// </summary>
    /// <param name="symbol">The symbol.</param>
    /// <param name="months">Number of months of history.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of price data points, most recent first.</returns>
    Task<List<PriceDataPoint>> GetMonthlyHistoryAsync(string symbol, int months = 240, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the provider name for logging/display.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets whether this provider is using mock data.
    /// </summary>
    bool IsMock { get; }
}

/// <summary>
/// Interface for precious metals data providers (gold, silver) and currency rates.
/// </summary>
public interface IMetalsDataProvider
{
    /// <summary>
    /// Gets the current spot price for a metal.
    /// </summary>
    /// <param name="metal">Metal symbol (e.g., "XAU" for gold, "XAG" for silver).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The quote with price in AUD per troy ounce.</returns>
    Task<MarketQuote?> GetMetalPriceAsync(string metal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current exchange rate for a currency pair.
    /// </summary>
    /// <param name="pair">Currency pair (e.g., "AUDUSD").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The quote with exchange rate.</returns>
    Task<MarketQuote?> GetExchangeRateAsync(string pair, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets historical prices for a metal.
    /// </summary>
    /// <param name="metal">Metal symbol.</param>
    /// <param name="days">Number of days of history.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of price data points.</returns>
    Task<List<PriceDataPoint>> GetMetalHistoryAsync(string metal, int days = 90, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the provider name for logging/display.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Gets whether this provider is using mock data.
    /// </summary>
    bool IsMock { get; }
}
