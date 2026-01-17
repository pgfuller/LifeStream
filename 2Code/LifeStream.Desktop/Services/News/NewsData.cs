using System;
using System.Collections.Generic;

namespace LifeStream.Desktop.Services.News;

/// <summary>
/// State of a news article or media item.
/// </summary>
public enum ItemState
{
    /// <summary>Not yet seen by user.</summary>
    New,
    /// <summary>Skimmed but no action taken.</summary>
    Seen,
    /// <summary>Want to review/read later.</summary>
    Hold,
    /// <summary>Read/processed, no longer relevant.</summary>
    Done,
    /// <summary>Not interested, don't show again.</summary>
    Rejected
}

/// <summary>
/// Trust level for a news source.
/// </summary>
public enum TrustLevel
{
    Trusted,
    Neutral,
    Biased,
    Blocked
}

/// <summary>
/// Political/editorial bias direction.
/// </summary>
public enum BiasDirection
{
    None,
    Left,
    Right,
    Sensational
}

/// <summary>
/// Category of news source.
/// </summary>
public enum NewsCategory
{
    News,
    Tech,
    Finance,
    Opinion,
    Science
}

/// <summary>
/// Configuration for a news RSS feed source.
/// </summary>
public class NewsSource
{
    /// <summary>Unique identifier (slug).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>RSS/Atom feed URL.</summary>
    public string FeedUrl { get; set; } = string.Empty;

    /// <summary>Category of content.</summary>
    public NewsCategory Category { get; set; } = NewsCategory.News;

    /// <summary>Trust level for this source.</summary>
    public TrustLevel TrustLevel { get; set; } = TrustLevel.Neutral;

    /// <summary>Bias direction if applicable.</summary>
    public BiasDirection BiasDirection { get; set; } = BiasDirection.None;

    /// <summary>Whether this source is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Refresh interval in minutes.</summary>
    public int RefreshIntervalMinutes { get; set; } = 30;

    /// <summary>When this source was last fetched.</summary>
    public DateTime? LastFetched { get; set; }

    /// <summary>Icon/logo URL for display.</summary>
    public string? IconUrl { get; set; }
}

/// <summary>
/// A news article from an RSS feed.
/// </summary>
public class NewsArticle
{
    /// <summary>
    /// Global unique ID in format: news:{source}:{hash}
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Source ID this article came from.</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Article title/headline.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Summary/description text.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Link to full article.</summary>
    public string Link { get; set; } = string.Empty;

    /// <summary>Author name if available.</summary>
    public string? Author { get; set; }

    /// <summary>Publication date.</summary>
    public DateTime Published { get; set; }

    /// <summary>When we fetched this article.</summary>
    public DateTime FetchedAt { get; set; }

    /// <summary>Extracted topic keywords.</summary>
    public List<string> Topics { get; set; } = new();

    /// <summary>Current state of the article.</summary>
    public ItemState State { get; set; } = ItemState.New;

    /// <summary>Calculated interest score (0-1).</summary>
    public float InterestScore { get; set; } = 0.5f;

    /// <summary>User notes if any.</summary>
    public string? UserNotes { get; set; }

    /// <summary>
    /// Gets the age of the article.
    /// </summary>
    public TimeSpan Age => DateTime.Now - Published;

    /// <summary>
    /// Gets a display string for the article age.
    /// </summary>
    public string AgeDisplay
    {
        get
        {
            var age = Age;
            if (age.TotalMinutes < 60)
                return $"{(int)age.TotalMinutes}m ago";
            if (age.TotalHours < 24)
                return $"{(int)age.TotalHours}h ago";
            if (age.TotalDays < 7)
                return $"{(int)age.TotalDays}d ago";
            return Published.ToString("MMM d");
        }
    }

    /// <summary>
    /// Gets the state icon character.
    /// </summary>
    public string StateIcon => State switch
    {
        ItemState.New => "●",
        ItemState.Seen => "○",
        ItemState.Hold => "⏸",
        ItemState.Done => "✓",
        ItemState.Rejected => "✗",
        _ => ""
    };
}

/// <summary>
/// Container for news service data.
/// </summary>
public class NewsData
{
    /// <summary>All articles from all sources.</summary>
    public List<NewsArticle> Articles { get; set; } = new();

    /// <summary>Configured sources.</summary>
    public List<NewsSource> Sources { get; set; } = new();

    /// <summary>When data was last refreshed.</summary>
    public DateTime LastRefresh { get; set; }

    /// <summary>Count of new (unread) articles.</summary>
    public int NewCount => Articles.FindAll(a => a.State == ItemState.New).Count;

    /// <summary>Count of articles on hold.</summary>
    public int HoldCount => Articles.FindAll(a => a.State == ItemState.Hold).Count;
}

/// <summary>
/// Configuration file structure for news sources.
/// </summary>
public class NewsSourcesConfig
{
    /// <summary>List of configured news sources.</summary>
    public List<NewsSource> Sources { get; set; } = new();
}
