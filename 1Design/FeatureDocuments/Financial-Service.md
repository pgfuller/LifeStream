# Financial Service - Market & Portfolio Awareness

## Overview

The Financial Service provides background awareness of financial markets, including ASX indices, individual stocks, and precious metals (gold/silver). Designed for passive monitoring rather than active trading, it displays KPIs, historical charts, and optional portfolio value calculations.

**Status:** Planned

## Goals

1. **Market Awareness:** Track ASX indices (ASX 200, All Ordinaries) for overall market sentiment
2. **Commodity Tracking:** Monitor gold and silver spot prices
3. **Watchlist:** Track a small number of individual ASX stocks
4. **Portfolio Value:** Optional calculation based on watchlist quantities
5. **Historical Context:** Multi-resolution data (intraday to multi-year)
6. **Low API Usage:** Respect free tier limits with intelligent caching

## Data Sources

### Primary: Alpha Vantage API

- **Website:** https://www.alphavantage.co/
- **Free Tier:** 25 requests/day (sufficient for background awareness)
- **Coverage:** ASX stocks (.AX suffix), indices, commodities
- **Format:** JSON

#### Endpoints Used

| Data | Endpoint | Parameters |
|------|----------|------------|
| Stock Quote | `GLOBAL_QUOTE` | symbol=BHP.AX |
| Daily History | `TIME_SERIES_DAILY` | symbol, outputsize=full |
| Monthly History | `TIME_SERIES_MONTHLY` | symbol |
| Commodities | `WTI`, `BRENT`, `NATURAL_GAS`, `COPPER`, `ALUMINUM`, `WHEAT`, `CORN`, `COTTON`, `SUGAR`, `COFFEE` | interval=monthly |

**Note:** Alpha Vantage commodity endpoints include energy and agricultural commodities but not precious metals directly. For gold/silver, we use ETF proxies or a secondary API.

### Secondary: Metals API (Gold/Silver)

- **Website:** https://metals-api.com/ or https://gold-api.com/
- **Free Tier:** ~100 requests/month
- **Coverage:** Gold, Silver, Platinum, Palladium
- **Format:** JSON

### Symbol Reference

#### ASX Indices
| Index | Alpha Vantage Symbol | Description |
|-------|---------------------|-------------|
| ASX 200 | ^AXJO | Top 200 companies by market cap |
| All Ordinaries | ^AORD | Broader market index (~500 stocks) |

**Note:** Index data may require Yahoo Finance fallback as Alpha Vantage index support varies.

#### ASX Stocks (Examples)
| Company | Symbol |
|---------|--------|
| BHP Group | BHP.AX |
| Commonwealth Bank | CBA.AX |
| CSL Limited | CSL.AX |
| Telstra | TLS.AX |
| Wesfarmers | WES.AX |

#### Precious Metals
| Metal | Symbol | Source |
|-------|--------|--------|
| Gold (XAU) | XAU | Metals API |
| Silver (XAG) | XAG | Metals API |
| Gold ETF | GOLD.AX | Alpha Vantage (ASX-listed) |

## Features

### Market Overview
- ASX 200 current value and daily change (% and points)
- All Ordinaries current value and daily change
- Market status indicator (Open/Closed based on AEST hours)

### Commodity Prices
- Gold spot price (AUD/oz)
- Silver spot price (AUD/oz)
- Daily change indicators

### Watchlist / Holdings
- User-configurable list of assets (max 10 items)
- Supports multiple asset types:
  - **Stocks:** ASX-listed shares (quantity in shares)
  - **Gold:** Physical gold holdings (quantity in ounces)
  - **Silver:** Physical silver holdings (quantity in kilograms)
- Current price and daily change for each
- Add/remove/edit via settings

### Portfolio Value (Optional)
- Calculated from holdings: quantity × current price (with unit conversion)
- Supports mixed portfolio of stocks and commodities
- Total portfolio value display (AUD)
- Daily change in portfolio value
- Only shown when quantities are configured

### Historical Charts
- Interactive line charts using DevExpress ChartControl
- Multiple time ranges: 1D, 1W, 1M, 3M, 1Y, 5Y
- Selectable series: Index, Stock, or Commodity

### Multi-Resolution Data Storage

| Resolution | Retention | Storage | Purpose |
|------------|-----------|---------|---------|
| 15-minute | Current day | Memory | Intraday view |
| Daily OHLC | 3 months | SQLite | Short-term trends |
| Monthly close | 20+ years | SQLite | Long-term context |

## Data Models

### MarketData (Real-time/Daily)

```csharp
public class MarketQuote
{
    public string Symbol { get; set; }          // "BHP.AX"
    public string Name { get; set; }            // "BHP Group"
    public decimal Price { get; set; }          // Current/last price
    public decimal Change { get; set; }         // Daily change ($)
    public decimal ChangePercent { get; set; }  // Daily change (%)
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal PreviousClose { get; set; }
    public long Volume { get; set; }
    public DateTime LastUpdated { get; set; }
}
```

### Historical Data Point

```csharp
public class PriceDataPoint
{
    public DateTime Date { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long Volume { get; set; }
}
```

### Asset Type

```csharp
public enum AssetType
{
    Stock,      // ASX shares - quantity in shares
    Gold,       // Physical gold - quantity in troy ounces
    Silver      // Physical silver - quantity in kilograms
}
```

### Holding Item

```csharp
public class HoldingItem
{
    public string Symbol { get; set; }          // "BHP.AX", "XAU", "XAG"
    public string Name { get; set; }            // "BHP Group", "Gold", "Silver"
    public AssetType AssetType { get; set; }    // Stock, Gold, Silver
    public decimal? Quantity { get; set; }      // Shares, ounces, or kg
    public MarketQuote? CurrentQuote { get; set; }

    // Display helpers
    public string QuantityUnit => AssetType switch
    {
        AssetType.Stock => "shares",
        AssetType.Gold => "oz",
        AssetType.Silver => "kg",
        _ => ""
    };

    public string QuantityDisplay => Quantity.HasValue
        ? $"{Quantity:N2} {QuantityUnit}"
        : "-";

    // Value calculation (handles Silver kg to oz conversion for pricing)
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
}
```

### Portfolio Summary

```csharp
public class PortfolioSummary
{
    public decimal TotalValue { get; set; }
    public decimal DailyChange { get; set; }
    public decimal DailyChangePercent { get; set; }
    public int HoldingsCount { get; set; }
    public DateTime LastUpdated { get; set; }
}
```

### Service Data Container

```csharp
public class FinancialData
{
    // Indices
    public MarketQuote? Asx200 { get; set; }
    public MarketQuote? AllOrdinaries { get; set; }

    // Commodities
    public MarketQuote? Gold { get; set; }
    public MarketQuote? Silver { get; set; }

    // Holdings (stocks + commodities)
    public List<HoldingItem> Holdings { get; set; }

    // Portfolio (computed from watchlist)
    public PortfolioSummary? Portfolio { get; set; }

    // Market status
    public bool IsMarketOpen { get; set; }
    public DateTime? MarketOpenTime { get; set; }
    public DateTime? MarketCloseTime { get; set; }

    // Metadata
    public DateTime LastRefresh { get; set; }
    public int ApiCallsToday { get; set; }
    public int ApiCallsRemaining { get; set; }
}
```

## API Request Strategy

### Daily Budget: 25 requests

Given the 25 requests/day limit, we need to be strategic:

| Data | Requests | Frequency | Priority |
|------|----------|-----------|----------|
| ASX 200 quote | 1 | Every 15 min (market hours) | High |
| All Ords quote | 1 | Every 15 min (market hours) | High |
| Gold price | 1 | Every 30 min | High |
| Silver price | 1 | Every 30 min | Medium |
| Watchlist quotes | N | Every 15 min (market hours) | Medium |
| Historical refresh | 2-3 | Once daily | Low |

### Market Hours Detection

ASX trading hours: 10:00 AM - 4:00 PM AEST (Sydney time)

```csharp
public bool IsMarketOpen()
{
    var sydneyTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time"));

    if (sydneyTime.DayOfWeek == DayOfWeek.Saturday ||
        sydneyTime.DayOfWeek == DayOfWeek.Sunday)
        return false;

    var time = sydneyTime.TimeOfDay;
    return time >= new TimeSpan(10, 0, 0) && time < new TimeSpan(16, 0, 0);
}
```

### Refresh Strategy

- **Market Open:** Refresh every 15 minutes
- **Market Closed:** Refresh once at startup, then hourly for commodities
- **Historical:** Fetch once daily (after market close or on first startup)

## Storage Structure

```
%APPDATA%\LifeStream\Data\Financial\
├── quotes.json              (current/last quotes cache)
├── holdings.json            (user's holdings config)
├── history.db               (SQLite for historical data)
└── api_usage.json           (track daily API calls)

SQLite Tables:
├── daily_prices (symbol, date, open, high, low, close, volume)
├── monthly_prices (symbol, year_month, close)
└── metadata (symbol, name, last_updated)
```

## UI Panel Design

### Panel Layout

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ Financial Markets                            [Settings]     [Refresh]       │
├───────────────────────────────┬─────────────────────────────────────────────┤
│  MARKET INDICES               │  COMMODITIES                                │
│  ┌─────────────────────────┐  │  ┌─────────────────────────────────────┐   │
│  │ ASX 200     7,842.30    │  │  │ Gold (XAU)        $3,245.60 AUD/oz │   │
│  │             +45.20 (0.58%)│  │  │                  +12.40 (0.38%)   │   │
│  ├─────────────────────────┤  │  ├─────────────────────────────────────┤   │
│  │ All Ords    8,102.15    │  │  │ Silver (XAG)      $38.42 AUD/oz    │   │
│  │             +38.90 (0.48%)│  │  │                  -0.28 (-0.72%)   │   │
│  └─────────────────────────┘  │  └─────────────────────────────────────┘   │
│  Market: OPEN (closes 4:00 PM)│                                             │
├───────────────────────────────┴─────────────────────────────────────────────┤
│  HOLDINGS                                               Portfolio: $95,450  │
│  ┌────────┬──────────┬───────────┬──────────┬───────────┬──────────────┐   │
│  │ Symbol │ Name     │ Price     │ Change   │ Holding   │ Value        │   │
│  ├────────┼──────────┼───────────┼──────────┼───────────┼──────────────┤   │
│  │ XAU    │ Gold     │ $3,245/oz │ +0.38%   │ 10.00 oz  │ $32,456      │   │
│  │ XAG    │ Silver   │ $38.42/oz │ -0.72%   │ 5.00 kg   │ $6,177       │   │
│  │ BHP.AX │ BHP Group│ $45.23    │ +1.00%   │ 500 shares│ $22,615      │   │
│  │ CBA.AX │ Comm Bank│ $112.50   │ -1.07%   │ 100 shares│ $11,250      │   │
│  │ CSL.AX │ CSL Ltd  │ $284.88   │ +1.25%   │ 80 shares │ $22,790      │   │
│  └────────┴──────────┴───────────┴──────────┴───────────┴──────────────┘   │
├─────────────────────────────────────────────────────────────────────────────┤
│  CHART    [ASX 200 ▼]    [1D] [1W] [1M] [3M] [1Y] [5Y]                     │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                                                                      │   │
│  │                         [Line Chart Area]                            │   │
│  │                                                                      │   │
│  │      Price trend visualization with crosshair                        │   │
│  │                                                                      │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
├─────────────────────────────────────────────────────────────────────────────┤
│ Last updated: 2:15 PM | Next refresh: 2:30 PM | API: 12/25 today           │
└─────────────────────────────────────────────────────────────────────────────┘
```

### KPI Cards

| KPI | Display | Color Logic |
|-----|---------|-------------|
| Index Value | Large number with change | Green if +, Red if - |
| Commodity Price | Price with change | Green if +, Red if - |
| Portfolio Value | Total with daily change | Green if +, Red if - |
| Market Status | OPEN/CLOSED badge | Green/Grey |

### Chart Features

- DevExpress `ChartControl` with `LineSeries`
- Time range selector (1D, 1W, 1M, 3M, 1Y, 5Y)
- Symbol selector dropdown (indices, commodities, watchlist items)
- Crosshair for precise value reading
- Zoom and pan support

### Holdings Grid

- DevExpress `GridControl` with row coloring based on change
- Grouped by asset type (Commodities, Stocks)
- Editable Quantity column (respects unit: shares/oz/kg)
- Context menu: Remove from holdings
- Double-click to view chart

## Service Architecture

### FinancialService

```csharp
public class FinancialService : InformationServiceBase<FinancialData>
{
    private readonly AlphaVantageClient _alphaVantage;
    private readonly MetalsApiClient _metalsApi;
    private readonly FinancialDataStore _dataStore;
    private readonly HoldingsManager _holdings;

    // Adaptive refresh based on market hours
    protected override TimeSpan GetRefreshInterval()
    {
        return IsMarketOpen()
            ? TimeSpan.FromMinutes(15)
            : TimeSpan.FromHours(1);
    }

    protected override async Task<FinancialData> FetchDataAsync(CancellationToken ct)
    {
        // Fetch indices, commodities, watchlist in parallel where possible
        // Respect API rate limits
        // Cache results locally
    }

    // Holdings management
    public void AddHolding(string symbol, string name, AssetType assetType, decimal? quantity = null);
    public void RemoveHolding(string symbol);
    public void UpdateHolding(string symbol, decimal? quantity);

    // Historical data access
    public IReadOnlyList<PriceDataPoint> GetHistory(string symbol, TimeRange range);
}
```

### API Clients

```csharp
public class AlphaVantageClient
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;

    public Task<MarketQuote?> GetQuoteAsync(string symbol);
    public Task<List<PriceDataPoint>> GetDailyHistoryAsync(string symbol, bool full = false);
    public Task<List<PriceDataPoint>> GetMonthlyHistoryAsync(string symbol);
}

public class MetalsApiClient
{
    private readonly string _apiKey;
    private readonly HttpClient _httpClient;

    public Task<MarketQuote?> GetMetalPriceAsync(string metal); // XAU, XAG
}
```

## Configuration

### Service Settings

| Parameter | Default | Description |
|-----------|---------|-------------|
| alphaVantageApiKey | (required) | Free API key from alphavantage.co |
| metalsApiKey | (optional) | API key for precious metals |
| refreshIntervalMarketOpen | 15 min | Refresh during market hours |
| refreshIntervalMarketClosed | 60 min | Refresh outside market hours |
| maxWatchlistItems | 10 | Maximum stocks in watchlist |
| historicalRetentionDays | 90 | Daily data retention |

### User Configuration (holdings.json)

```json
{
  "holdings": [
    { "symbol": "XAU", "name": "Gold", "assetType": "Gold", "quantity": 10.0 },
    { "symbol": "XAG", "name": "Silver", "assetType": "Silver", "quantity": 5.0 },
    { "symbol": "BHP.AX", "name": "BHP Group", "assetType": "Stock", "quantity": 500 },
    { "symbol": "CBA.AX", "name": "Commonwealth Bank", "assetType": "Stock", "quantity": 100 },
    { "symbol": "CSL.AX", "name": "CSL Limited", "assetType": "Stock", "quantity": 80 }
  ],
  "showPortfolioValue": true,
  "defaultChartSymbol": "^AXJO",
  "defaultChartRange": "1M"
}
```

## Development Strategy: Mock Data

To avoid hitting API rate limits (25/day for Alpha Vantage), development uses mock data providers that can be swapped for real APIs when ready.

### Mock Data Approach

```csharp
// Interface for data providers
public interface IMarketDataProvider
{
    Task<MarketQuote?> GetQuoteAsync(string symbol);
    Task<List<PriceDataPoint>> GetDailyHistoryAsync(string symbol);
    Task<List<PriceDataPoint>> GetMonthlyHistoryAsync(string symbol);
}

// Mock implementation for development
public class MockMarketDataProvider : IMarketDataProvider
{
    // Returns realistic but static/generated data
    // Simulates API latency with small delays
    // Generates historical data with realistic patterns
}

// Real implementation for production
public class AlphaVantageProvider : IMarketDataProvider
{
    // Calls actual Alpha Vantage API
}
```

### Configuration Toggle

```csharp
// In FinancialService constructor or config
public FinancialService(IMarketDataProvider marketProvider, IMetalsDataProvider metalsProvider)
{
    // Dependency injection allows swapping mock/real providers
}

// Usage in app startup
#if DEBUG
    var marketProvider = new MockMarketDataProvider();
    var metalsProvider = new MockMetalsDataProvider();
#else
    var marketProvider = new AlphaVantageProvider(apiKey);
    var metalsProvider = new MetalsApiProvider(apiKey);
#endif
```

### Mock Data Characteristics

| Data | Mock Behavior |
|------|---------------|
| Quotes | Realistic base prices with small random daily changes |
| Daily History | Generated OHLC with realistic volatility patterns |
| Monthly History | Long-term trend data (20 years) with market cycles |
| Refresh | Simulated delays (500-1500ms) to mimic network latency |

### Testing Phases

1. **Phase A (Development):** 100% mock data - build and test all UI/logic
2. **Phase B (Integration):** Single API call to verify real data parsing
3. **Phase C (Production):** Full API integration with rate limiting

## Implementation Phases

### Phase 1: Core Service & Basic UI
1. `AlphaVantageClient` with quote and history endpoints
2. `FinancialService` extending `InformationServiceBase<FinancialData>`
3. Basic `FinancialPanel` with index KPIs
4. Local JSON caching for quotes
5. Integration into MainForm

### Phase 2: Holdings & Portfolio
1. `HoldingsManager` with add/remove/update for stocks and commodities
2. Holdings grid in panel (stocks, gold, silver)
3. Portfolio value calculation with unit conversion (oz, kg, shares)
4. Quantity editing in grid
5. Settings dialog for holdings management

### Phase 3: Historical Charts
1. SQLite storage for historical data
2. Multi-resolution data fetching
3. Chart component with time range selector
4. Symbol selector for chart
5. Background historical data sync

### Phase 4: Commodities & Polish
1. `MetalsApiClient` for gold/silver
2. Commodity KPI cards
3. Market hours indicator
4. API usage tracking/display
5. Error handling and offline mode

## Key Files

```
Services/Financial/
├── FinancialService.cs           (main service)
├── FinancialData.cs              (data models)
├── AlphaVantageClient.cs         (Alpha Vantage API client)
├── MetalsApiClient.cs            (Metals API client)
├── HoldingsManager.cs            (holdings CRUD for stocks + commodities)
├── FinancialDataStore.cs         (SQLite + JSON storage)
└── MarketHours.cs                (ASX trading hours)

Controls/
└── FinancialPanel.cs             (UI panel with charts)
```

## API Key Setup

Users need to obtain free API keys:

1. **Alpha Vantage:** https://www.alphavantage.co/support/#api-key
   - Free tier: 25 requests/day
   - No credit card required

2. **Metals API (optional):** https://metals-api.com/ or https://gold-api.com/
   - Free tier available
   - Required for live gold/silver prices

Keys stored in user settings (not in code repository).

## Future Enhancements

- Currency conversion (USD to AUD for commodities)
- Price alerts/notifications
- Dividend tracking
- News feed integration for watchlist stocks
- Export portfolio history to CSV
- Multiple portfolio support
- Integration with broker APIs (read-only)

## References

- [Alpha Vantage Documentation](https://www.alphavantage.co/documentation/)
- [ASX Market Information](https://www.asx.com.au/)
- [Metals-API Documentation](https://metals-api.com/documentation)
