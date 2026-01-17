using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using LifeStream.Core.Infrastructure;
using Newtonsoft.Json;
using Serilog;

namespace LifeStream.Desktop.Services.News;

/// <summary>
/// Service for fetching and managing news from RSS feeds.
/// </summary>
public class NewsService : InformationServiceBase
{
    private static readonly ILogger Log = LoggingConfig.ForCategory($"{LoggingConfig.Categories.Sources}.News");

    private readonly HttpClient _httpClient;
    private readonly string _configPath;
    private readonly string _statePath;

    private List<NewsSource> _sources = new();
    private Dictionary<string, NewsArticle> _articles = new();
    private Dictionary<string, ArticleState> _savedStates = new();
    private NewsData? _currentData;

    /// <summary>
    /// Gets the current news data.
    /// </summary>
    public NewsData? CurrentData => _currentData;

    /// <summary>
    /// Refresh interval: 15 minutes.
    /// </summary>
    protected override TimeSpan RefreshInterval => TimeSpan.FromMinutes(15);

    public NewsService() : base("news", "News", "News")
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "LifeStream/1.0 (News Reader)");

        _configPath = Path.Combine(AppPaths.ConfigPath, "news-sources.json");
        _statePath = Path.Combine(AppPaths.GetServiceDataPath("News"), "article-states.json");
    }

    protected override void OnInitialize()
    {
        Log.Information("Initializing News Service");

        // Load sources configuration
        LoadSources();

        // Load saved article states
        LoadArticleStates();

        Log.Information("News Service initialized with {Count} sources", _sources.Count);
    }

    protected override void OnShutdown()
    {
        // Save article states
        SaveArticleStates();
        _httpClient.Dispose();
        Log.Information("News Service shutdown");
    }

    protected override async Task<object?> FetchDataAsync(CancellationToken cancellationToken)
    {
        Log.Debug("Fetching news from {Count} sources", _sources.Count(s => s.Enabled));

        var enabledSources = _sources.Where(s => s.Enabled).ToList();
        var fetchTasks = enabledSources.Select(source => FetchSourceAsync(source, cancellationToken));

        var results = await Task.WhenAll(fetchTasks);
        var newArticleCount = results.Sum(r => r);

        // Build the news data
        var data = new NewsData
        {
            Sources = _sources.ToList(),
            Articles = _articles.Values
                .OrderByDescending(a => a.Published)
                .Take(200) // Limit to most recent 200
                .ToList(),
            LastRefresh = DateTime.Now
        };

        Log.Information("News fetched: {New} new articles, {Total} total", newArticleCount, data.Articles.Count);

        return data;
    }

    protected override void StoreData(object data)
    {
        if (data is NewsData newsData)
        {
            _currentData = newsData;
            // Periodically save article states
            SaveArticleStates();
        }
    }

    protected override bool HasDataChanged(object newData, object? previousData)
    {
        if (newData is NewsData news && previousData is NewsData prev)
        {
            return news.Articles.Count != prev.Articles.Count ||
                   news.NewCount != prev.NewCount;
        }
        return true;
    }

    private async Task<int> FetchSourceAsync(NewsSource source, CancellationToken cancellationToken)
    {
        try
        {
            Log.Debug("Fetching RSS from {Source}: {Url}", source.Name, source.FeedUrl);

            var response = await _httpClient.GetStringAsync(source.FeedUrl, cancellationToken);
            var articles = ParseRssFeed(response, source);

            var newCount = 0;
            foreach (var article in articles)
            {
                if (!_articles.ContainsKey(article.Id))
                {
                    // Apply saved state if we have one for this article
                    if (_savedStates.TryGetValue(article.Id, out var savedState))
                    {
                        article.State = savedState.State;
                        article.UserNotes = savedState.UserNotes;
                    }

                    _articles[article.Id] = article;

                    // Only count as "new" if it's actually in New state
                    if (article.State == ItemState.New)
                    {
                        newCount++;
                    }
                }
            }

            source.LastFetched = DateTime.Now;
            Log.Debug("Fetched {Count} articles from {Source} ({New} new)", articles.Count, source.Name, newCount);

            return newCount;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch RSS from {Source}", source.Name);
            return 0;
        }
    }

    private List<NewsArticle> ParseRssFeed(string xml, NewsSource source)
    {
        var articles = new List<NewsArticle>();

        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null) return articles;

            // Handle RSS 2.0, Atom, and RDF/RSS 1.0 feeds
            var rootName = root.Name.LocalName;

            if (rootName == "feed")
            {
                // Atom feed
                articles = ParseAtomFeed(root, source);
            }
            else if (rootName == "RDF")
            {
                // RDF/RSS 1.0 feed (used by Slashdot)
                articles = ParseRdfFeed(root, source);
            }
            else
            {
                // RSS 2.0 feed
                articles = ParseRss2Feed(root, source);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse feed from {Source}", source.Name);
        }

        return articles;
    }

    private List<NewsArticle> ParseRss2Feed(XElement root, NewsSource source)
    {
        var articles = new List<NewsArticle>();
        var channel = root.Element("channel");
        if (channel == null) return articles;

        foreach (var item in channel.Elements("item"))
        {
            try
            {
                var guid = item.Element("guid")?.Value;
                var link = item.Element("link")?.Value ?? "";
                var title = item.Element("title")?.Value ?? "";
                var description = item.Element("description")?.Value ?? "";
                var author = item.Element("author")?.Value ?? item.Element("{http://purl.org/dc/elements/1.1/}creator")?.Value;
                var pubDateStr = item.Element("pubDate")?.Value;

                var pubDate = DateTime.Now;
                if (!string.IsNullOrEmpty(pubDateStr))
                {
                    DateTime.TryParse(pubDateStr, out pubDate);
                }

                var articleId = GenerateArticleId(source.Id, guid, link);

                articles.Add(new NewsArticle
                {
                    Id = articleId,
                    SourceId = source.Id,
                    Title = CleanText(title),
                    Summary = CleanText(StripHtml(description)),
                    Link = link,
                    Author = author,
                    Published = pubDate,
                    FetchedAt = DateTime.Now,
                    State = ItemState.New
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to parse RSS item");
            }
        }

        return articles;
    }

    private List<NewsArticle> ParseRdfFeed(XElement root, NewsSource source)
    {
        var articles = new List<NewsArticle>();
        XNamespace rss = "http://purl.org/rss/1.0/";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

        // RDF feeds have items as direct children or under the default namespace
        var items = root.Elements(rss + "item").ToList();
        if (items.Count == 0)
        {
            // Try without namespace
            items = root.Elements("item").ToList();
        }

        foreach (var item in items)
        {
            try
            {
                var link = item.Element(rss + "link")?.Value ?? item.Element("link")?.Value ?? "";
                var title = item.Element(rss + "title")?.Value ?? item.Element("title")?.Value ?? "";
                var description = item.Element(rss + "description")?.Value ?? item.Element("description")?.Value ?? "";
                var author = item.Element(dc + "creator")?.Value;
                var dateStr = item.Element(dc + "date")?.Value;

                // Use rdf:about as GUID if available
                var guid = item.Attribute(rdf + "about")?.Value ?? link;

                var pubDate = DateTime.Now;
                if (!string.IsNullOrEmpty(dateStr))
                {
                    DateTime.TryParse(dateStr, out pubDate);
                }

                var articleId = GenerateArticleId(source.Id, guid, link);

                articles.Add(new NewsArticle
                {
                    Id = articleId,
                    SourceId = source.Id,
                    Title = CleanText(title),
                    Summary = CleanText(StripHtml(description)),
                    Link = link,
                    Author = author,
                    Published = pubDate,
                    FetchedAt = DateTime.Now,
                    State = ItemState.New
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to parse RDF item");
            }
        }

        return articles;
    }

    private List<NewsArticle> ParseAtomFeed(XElement root, NewsSource source)
    {
        var articles = new List<NewsArticle>();
        XNamespace atom = "http://www.w3.org/2005/Atom";

        foreach (var entry in root.Elements(atom + "entry"))
        {
            try
            {
                var id = entry.Element(atom + "id")?.Value;
                var link = entry.Elements(atom + "link")
                    .FirstOrDefault(l => l.Attribute("rel")?.Value == "alternate" || l.Attribute("rel") == null)
                    ?.Attribute("href")?.Value ?? "";
                var title = entry.Element(atom + "title")?.Value ?? "";
                var summary = entry.Element(atom + "summary")?.Value ??
                              entry.Element(atom + "content")?.Value ?? "";
                var author = entry.Element(atom + "author")?.Element(atom + "name")?.Value;
                var updatedStr = entry.Element(atom + "updated")?.Value ??
                                 entry.Element(atom + "published")?.Value;

                var pubDate = DateTime.Now;
                if (!string.IsNullOrEmpty(updatedStr))
                {
                    DateTime.TryParse(updatedStr, out pubDate);
                }

                var articleId = GenerateArticleId(source.Id, id, link);

                articles.Add(new NewsArticle
                {
                    Id = articleId,
                    SourceId = source.Id,
                    Title = CleanText(title),
                    Summary = CleanText(StripHtml(summary)),
                    Link = link,
                    Author = author,
                    Published = pubDate,
                    FetchedAt = DateTime.Now,
                    State = ItemState.New
                });
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to parse Atom entry");
            }
        }

        return articles;
    }

    /// <summary>
    /// Generates a unique article ID in format: news:{source}:{hash}
    /// </summary>
    private static string GenerateArticleId(string sourceId, string? guid, string link)
    {
        // Use GUID if available and valid, otherwise hash the link
        var hashInput = !string.IsNullOrEmpty(guid) ? guid : link;
        var hash = ComputeShortHash(hashInput);
        return $"news:{sourceId}:{hash}";
    }

    private static string ComputeShortHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        // Take first 8 bytes and convert to hex (16 chars)
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private static string CleanText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        // Decode HTML entities and normalize whitespace
        text = System.Net.WebUtility.HtmlDecode(text);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return text.Trim();
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        // Simple HTML tag removal
        return System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", "");
    }

    private void LoadSources()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonConvert.DeserializeObject<NewsSourcesConfig>(json);
                _sources = config?.Sources ?? new List<NewsSource>();
                Log.Information("Loaded {Count} news sources from config", _sources.Count);
            }
            else
            {
                Log.Information("No news sources config found, using defaults");
                _sources = GetDefaultSources();
                SaveSources();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load news sources");
            _sources = GetDefaultSources();
        }
    }

    private void SaveSources()
    {
        try
        {
            var config = new NewsSourcesConfig { Sources = _sources };
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save news sources");
        }
    }

    private static List<NewsSource> GetDefaultSources()
    {
        return new List<NewsSource>
        {
            new NewsSource
            {
                Id = "abc-au",
                Name = "ABC News Australia",
                FeedUrl = "https://www.abc.net.au/news/feed/2942460/rss.xml",
                Category = NewsCategory.News,
                TrustLevel = TrustLevel.Trusted,
                Enabled = true
            },
            new NewsSource
            {
                Id = "bbc",
                Name = "BBC News",
                FeedUrl = "https://feeds.bbci.co.uk/news/rss.xml",
                Category = NewsCategory.News,
                TrustLevel = TrustLevel.Trusted,
                Enabled = true
            },
            new NewsSource
            {
                Id = "slashdot",
                Name = "Slashdot",
                FeedUrl = "https://rss.slashdot.org/Slashdot/slashdotMain",
                Category = NewsCategory.Tech,
                TrustLevel = TrustLevel.Neutral,
                Enabled = true
            }
        };
    }

    private void LoadArticleStates()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var json = File.ReadAllText(_statePath);
                var states = JsonConvert.DeserializeObject<Dictionary<string, ArticleState>>(json);
                if (states != null)
                {
                    // Store saved states for applying to articles when they're fetched
                    _savedStates = states;

                    // Also apply to any existing articles (in case they were already loaded)
                    foreach (var kvp in states)
                    {
                        if (_articles.TryGetValue(kvp.Key, out var article))
                        {
                            article.State = kvp.Value.State;
                            article.UserNotes = kvp.Value.UserNotes;
                        }
                    }
                    Log.Debug("Loaded {Count} article states", states.Count);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load article states");
        }
    }

    private void SaveArticleStates()
    {
        try
        {
            // Save the states dictionary directly - it contains all non-New states
            // This preserves states for articles that may no longer be in the current feed
            if (_savedStates.Count > 0)
            {
                var json = JsonConvert.SerializeObject(_savedStates, Formatting.Indented);
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                File.WriteAllText(_statePath, json);
                Log.Debug("Saved {Count} article states", _savedStates.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save article states");
        }
    }

    /// <summary>
    /// Updates the state of an article.
    /// </summary>
    public void SetArticleState(string articleId, ItemState state)
    {
        if (_articles.TryGetValue(articleId, out var article))
        {
            article.State = state;

            // Update saved states dictionary
            if (state == ItemState.New)
            {
                // Remove from saved states if marked as New (default state)
                _savedStates.Remove(articleId);
            }
            else
            {
                _savedStates[articleId] = new ArticleState
                {
                    State = state,
                    UserNotes = article.UserNotes
                };
            }

            SaveArticleStates();
            Log.Debug("Article {Id} state changed to {State}", articleId, state);
        }
    }

    /// <summary>
    /// Marks all visible articles as seen.
    /// </summary>
    public void MarkAllSeen()
    {
        foreach (var article in _articles.Values.Where(a => a.State == ItemState.New))
        {
            article.State = ItemState.Seen;
            _savedStates[article.Id] = new ArticleState
            {
                State = ItemState.Seen,
                UserNotes = article.UserNotes
            };
        }
        SaveArticleStates();
    }

    /// <summary>
    /// Gets articles filtered by state.
    /// </summary>
    public IEnumerable<NewsArticle> GetArticles(ItemState? stateFilter = null)
    {
        var query = _articles.Values.AsEnumerable();

        if (stateFilter.HasValue)
        {
            query = query.Where(a => a.State == stateFilter.Value);
        }

        return query.OrderByDescending(a => a.Published);
    }

    /// <summary>
    /// Gets the source by ID.
    /// </summary>
    public NewsSource? GetSource(string sourceId)
    {
        return _sources.FirstOrDefault(s => s.Id == sourceId);
    }

    /// <summary>
    /// Compact class for serializing article state.
    /// </summary>
    private class ArticleState
    {
        public ItemState State { get; set; }
        public string? UserNotes { get; set; }
    }
}
