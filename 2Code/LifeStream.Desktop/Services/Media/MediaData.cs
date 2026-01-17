using System;
using System.Collections.Generic;

namespace LifeStream.Desktop.Services.Media;

/// <summary>
/// State of a media item (video, podcast, etc.).
/// </summary>
public enum MediaItemState
{
    /// <summary>Not yet seen by user.</summary>
    New,
    /// <summary>Marked to watch/listen later.</summary>
    WatchLater,
    /// <summary>Currently watching/in progress.</summary>
    InProgress,
    /// <summary>Watched/listened completely.</summary>
    Watched,
    /// <summary>Not interested, hide from view.</summary>
    NotInterested
}

/// <summary>
/// A YouTube channel configuration.
/// </summary>
public class YouTubeChannel
{
    /// <summary>YouTube channel ID.</summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>Display name for the channel.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Channel description.</summary>
    public string? Description { get; set; }

    /// <summary>Channel thumbnail URL.</summary>
    public string? ThumbnailUrl { get; set; }

    /// <summary>Whether this channel is enabled for fetching.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>User-assigned category/tag.</summary>
    public string? Category { get; set; }

    /// <summary>When this channel was last fetched.</summary>
    public DateTime? LastFetched { get; set; }
}

/// <summary>
/// A YouTube video.
/// </summary>
public class YouTubeVideo
{
    /// <summary>
    /// Global unique ID in format: youtube:{videoId}
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>YouTube video ID (the part after v= in URL).</summary>
    public string VideoId { get; set; } = string.Empty;

    /// <summary>Channel ID this video belongs to.</summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>Channel name.</summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>Video title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Video description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>When the video was published.</summary>
    public DateTime Published { get; set; }

    /// <summary>Video duration.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>View count (if available).</summary>
    public long? ViewCount { get; set; }

    /// <summary>Like count (if available).</summary>
    public long? LikeCount { get; set; }

    /// <summary>Current state of this video.</summary>
    public MediaItemState State { get; set; } = MediaItemState.New;

    /// <summary>User notes if any.</summary>
    public string? UserNotes { get; set; }

    /// <summary>When we fetched this video info.</summary>
    public DateTime FetchedAt { get; set; }

    /// <summary>Local path to cached thumbnail (if downloaded).</summary>
    public string? LocalThumbnailPath { get; set; }

    /// <summary>
    /// Gets the standard quality thumbnail URL.
    /// </summary>
    public string ThumbnailUrl => $"https://img.youtube.com/vi/{VideoId}/mqdefault.jpg";

    /// <summary>
    /// Gets the high quality thumbnail URL.
    /// </summary>
    public string ThumbnailUrlHQ => $"https://img.youtube.com/vi/{VideoId}/hqdefault.jpg";

    /// <summary>
    /// Gets the video watch URL.
    /// </summary>
    public string WatchUrl => $"https://www.youtube.com/watch?v={VideoId}";

    /// <summary>
    /// Gets the age of the video.
    /// </summary>
    public TimeSpan Age => DateTime.Now - Published;

    /// <summary>
    /// Gets a display string for the video age.
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
            if (age.TotalDays < 30)
                return $"{(int)(age.TotalDays / 7)}w ago";
            return Published.ToString("MMM d, yyyy");
        }
    }

    /// <summary>
    /// Gets a display string for the duration.
    /// </summary>
    public string DurationDisplay
    {
        get
        {
            if (Duration.TotalHours >= 1)
                return Duration.ToString(@"h\:mm\:ss");
            return Duration.ToString(@"m\:ss");
        }
    }

    /// <summary>
    /// Gets a display string for the view count.
    /// </summary>
    public string ViewCountDisplay
    {
        get
        {
            if (!ViewCount.HasValue) return "";
            var count = ViewCount.Value;
            if (count >= 1_000_000)
                return $"{count / 1_000_000.0:F1}M views";
            if (count >= 1_000)
                return $"{count / 1_000.0:F1}K views";
            return $"{count} views";
        }
    }

    /// <summary>
    /// Gets the state icon character.
    /// </summary>
    public string StateIcon => State switch
    {
        MediaItemState.New => "●",
        MediaItemState.WatchLater => "⏱",
        MediaItemState.InProgress => "▶",
        MediaItemState.Watched => "✓",
        MediaItemState.NotInterested => "✗",
        _ => ""
    };
}

/// <summary>
/// Container for media service data.
/// </summary>
public class MediaData
{
    /// <summary>All videos from all channels.</summary>
    public List<YouTubeVideo> Videos { get; set; } = new();

    /// <summary>Configured channels.</summary>
    public List<YouTubeChannel> Channels { get; set; } = new();

    /// <summary>When data was last refreshed.</summary>
    public DateTime LastRefresh { get; set; }

    /// <summary>Count of new (unwatched) videos.</summary>
    public int NewCount => Videos.FindAll(v => v.State == MediaItemState.New).Count;

    /// <summary>Count of videos marked watch later.</summary>
    public int WatchLaterCount => Videos.FindAll(v => v.State == MediaItemState.WatchLater).Count;
}

/// <summary>
/// Configuration file structure for YouTube channels.
/// </summary>
public class YouTubeChannelsConfig
{
    /// <summary>YouTube Data API key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>List of configured channels.</summary>
    public List<YouTubeChannel> Channels { get; set; } = new();

    /// <summary>Maximum videos to fetch per channel.</summary>
    public int MaxVideosPerChannel { get; set; } = 10;

    /// <summary>How many days back to fetch videos.</summary>
    public int FetchDaysBack { get; set; } = 7;
}

/// <summary>
/// Compact class for serializing video state.
/// </summary>
public class VideoState
{
    public MediaItemState State { get; set; }
    public string? UserNotes { get; set; }
}
