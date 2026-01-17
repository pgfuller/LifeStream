using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LifeStream.Core.Infrastructure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace LifeStream.Desktop.Services.Media;

/// <summary>
/// Service for fetching and managing YouTube videos.
/// </summary>
public class YouTubeService : InformationServiceBase
{
    private static readonly ILogger Log = LoggingConfig.ForCategory($"{LoggingConfig.Categories.Sources}.YouTube");

    private const string YouTubeApiBase = "https://www.googleapis.com/youtube/v3";

    private readonly HttpClient _httpClient;
    private readonly string _configPath;
    private readonly string _statePath;
    private readonly string _thumbnailCachePath;

    private string? _apiKey;
    private List<YouTubeChannel> _channels = new();
    private Dictionary<string, YouTubeVideo> _videos = new();
    private Dictionary<string, VideoState> _savedStates = new();
    private MediaData? _currentData;
    private int _maxVideosPerChannel = 10;
    private int _fetchDaysBack = 7;

    /// <summary>
    /// Gets the current media data.
    /// </summary>
    public MediaData? CurrentData => _currentData;

    /// <summary>
    /// Gets the thumbnail cache path.
    /// </summary>
    public string ThumbnailCachePath => _thumbnailCachePath;

    /// <summary>
    /// Refresh interval: 30 minutes.
    /// </summary>
    protected override TimeSpan RefreshInterval => TimeSpan.FromMinutes(30);

    public YouTubeService() : base("youtube", "YouTube", "Media")
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        _configPath = Path.Combine(AppPaths.ConfigPath, "youtube-channels.json");
        _statePath = Path.Combine(AppPaths.GetServiceDataPath("Media"), "video-states.json");
        _thumbnailCachePath = Path.Combine(AppPaths.GetServiceDataPath("Media"), "thumbnails");
    }

    protected override void OnInitialize()
    {
        Log.Information("Initializing YouTube Service");

        // Ensure thumbnail cache directory exists
        Directory.CreateDirectory(_thumbnailCachePath);

        // Load configuration
        LoadConfig();

        // Load saved video states
        LoadVideoStates();

        Log.Information("YouTube Service initialized with {Count} channels", _channels.Count);
    }

    protected override void OnShutdown()
    {
        SaveVideoStates();
        _httpClient.Dispose();
        Log.Information("YouTube Service shutdown");
    }

    protected override async Task<object?> FetchDataAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            Log.Warning("YouTube API key not configured");
            return _currentData;
        }

        var enabledChannels = _channels.Where(c => c.Enabled).ToList();
        Log.Debug("Fetching videos from {Count} channels", enabledChannels.Count);

        var newVideoCount = 0;
        foreach (var channel in enabledChannels)
        {
            try
            {
                var count = await FetchChannelVideosAsync(channel, cancellationToken);
                newVideoCount += count;
                channel.LastFetched = DateTime.Now;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to fetch videos from channel {Channel}", channel.Name);
            }
        }

        // Build the media data
        var data = new MediaData
        {
            Channels = _channels.ToList(),
            Videos = _videos.Values
                .OrderByDescending(v => v.Published)
                .Take(200)
                .ToList(),
            LastRefresh = DateTime.Now
        };

        Log.Information("YouTube fetched: {New} new videos, {Total} total", newVideoCount, data.Videos.Count);

        return data;
    }

    protected override void StoreData(object data)
    {
        if (data is MediaData mediaData)
        {
            _currentData = mediaData;
            SaveVideoStates();
        }
    }

    protected override bool HasDataChanged(object newData, object? previousData)
    {
        if (newData is MediaData media && previousData is MediaData prev)
        {
            return media.Videos.Count != prev.Videos.Count ||
                   media.NewCount != prev.NewCount;
        }
        return true;
    }

    private async Task<int> FetchChannelVideosAsync(YouTubeChannel channel, CancellationToken cancellationToken)
    {
        Log.Debug("Fetching videos from channel: {Channel} ({Id})", channel.Name, channel.ChannelId);

        // First, get the uploads playlist ID for this channel
        var uploadsPlaylistId = await GetUploadsPlaylistIdAsync(channel.ChannelId, cancellationToken);
        if (string.IsNullOrEmpty(uploadsPlaylistId))
        {
            Log.Warning("Could not find uploads playlist for channel {Channel}", channel.Name);
            return 0;
        }

        // Fetch recent videos from the uploads playlist
        var videos = await GetPlaylistVideosAsync(uploadsPlaylistId, channel, cancellationToken);

        var newCount = 0;
        foreach (var video in videos)
        {
            if (!_videos.ContainsKey(video.Id))
            {
                // Apply saved state if we have one
                if (_savedStates.TryGetValue(video.Id, out var savedState))
                {
                    video.State = savedState.State;
                    video.UserNotes = savedState.UserNotes;
                }

                _videos[video.Id] = video;

                if (video.State == MediaItemState.New)
                {
                    newCount++;
                }
            }
        }

        Log.Debug("Fetched {Count} videos from {Channel} ({New} new)", videos.Count, channel.Name, newCount);
        return newCount;
    }

    private async Task<string?> GetUploadsPlaylistIdAsync(string channelId, CancellationToken cancellationToken)
    {
        var url = $"{YouTubeApiBase}/channels?part=contentDetails&id={channelId}&key={_apiKey}";

        var response = await _httpClient.GetStringAsync(url, cancellationToken);
        var json = JObject.Parse(response);

        var items = json["items"] as JArray;
        if (items == null || items.Count == 0)
            return null;

        return items[0]?["contentDetails"]?["relatedPlaylists"]?["uploads"]?.ToString();
    }

    private async Task<List<YouTubeVideo>> GetPlaylistVideosAsync(string playlistId, YouTubeChannel channel, CancellationToken cancellationToken)
    {
        var videos = new List<YouTubeVideo>();
        var cutoffDate = DateTime.Now.AddDays(-_fetchDaysBack);

        var url = $"{YouTubeApiBase}/playlistItems?part=snippet,contentDetails&playlistId={playlistId}&maxResults={_maxVideosPerChannel}&key={_apiKey}";

        var response = await _httpClient.GetStringAsync(url, cancellationToken);
        var json = JObject.Parse(response);

        var items = json["items"] as JArray;
        if (items == null)
            return videos;

        // Collect video IDs to fetch additional details (duration, view count)
        var videoIds = new List<string>();

        foreach (var item in items)
        {
            var snippet = item["snippet"];
            var contentDetails = item["contentDetails"];

            if (snippet == null || contentDetails == null)
                continue;

            var videoId = contentDetails["videoId"]?.ToString();
            if (string.IsNullOrEmpty(videoId))
                continue;

            var publishedStr = snippet["publishedAt"]?.ToString();
            if (!DateTime.TryParse(publishedStr, out var published))
                published = DateTime.Now;

            // Skip videos older than cutoff
            if (published < cutoffDate)
                continue;

            videoIds.Add(videoId);

            var video = new YouTubeVideo
            {
                Id = $"youtube:{videoId}",
                VideoId = videoId,
                ChannelId = channel.ChannelId,
                ChannelName = channel.Name,
                Title = snippet["title"]?.ToString() ?? "",
                Description = snippet["description"]?.ToString() ?? "",
                Published = published,
                FetchedAt = DateTime.Now,
                State = MediaItemState.New
            };

            videos.Add(video);
        }

        // Fetch additional video details (duration, view count)
        if (videoIds.Count > 0)
        {
            await EnrichVideoDetailsAsync(videos, videoIds, cancellationToken);
        }

        return videos;
    }

    private async Task EnrichVideoDetailsAsync(List<YouTubeVideo> videos, List<string> videoIds, CancellationToken cancellationToken)
    {
        var idsParam = string.Join(",", videoIds);
        var url = $"{YouTubeApiBase}/videos?part=contentDetails,statistics&id={idsParam}&key={_apiKey}";

        try
        {
            var response = await _httpClient.GetStringAsync(url, cancellationToken);
            var json = JObject.Parse(response);

            var items = json["items"] as JArray;
            if (items == null)
                return;

            var detailsById = new Dictionary<string, (TimeSpan Duration, long? Views, long? Likes)>();

            foreach (var item in items)
            {
                var id = item["id"]?.ToString();
                if (string.IsNullOrEmpty(id))
                    continue;

                var contentDetails = item["contentDetails"];
                var statistics = item["statistics"];

                var duration = TimeSpan.Zero;
                var durationStr = contentDetails?["duration"]?.ToString();
                if (!string.IsNullOrEmpty(durationStr))
                {
                    duration = ParseISO8601Duration(durationStr);
                }

                long? viewCount = null;
                if (long.TryParse(statistics?["viewCount"]?.ToString(), out var views))
                    viewCount = views;

                long? likeCount = null;
                if (long.TryParse(statistics?["likeCount"]?.ToString(), out var likes))
                    likeCount = likes;

                detailsById[id] = (duration, viewCount, likeCount);
            }

            // Apply details to videos
            foreach (var video in videos)
            {
                if (detailsById.TryGetValue(video.VideoId, out var details))
                {
                    video.Duration = details.Duration;
                    video.ViewCount = details.Views;
                    video.LikeCount = details.Likes;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch video details");
        }
    }

    private static TimeSpan ParseISO8601Duration(string duration)
    {
        // Format: PT#H#M#S (e.g., PT1H30M45S, PT5M30S, PT45S)
        try
        {
            return System.Xml.XmlConvert.ToTimeSpan(duration);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                var config = JsonConvert.DeserializeObject<YouTubeChannelsConfig>(json);
                if (config != null)
                {
                    _apiKey = config.ApiKey;
                    _channels = config.Channels ?? new List<YouTubeChannel>();
                    _maxVideosPerChannel = config.MaxVideosPerChannel > 0 ? config.MaxVideosPerChannel : 10;
                    _fetchDaysBack = config.FetchDaysBack > 0 ? config.FetchDaysBack : 7;
                    Log.Information("Loaded {Count} YouTube channels from config", _channels.Count);
                }
            }
            else
            {
                Log.Information("No YouTube config found, creating default");
                _channels = GetDefaultChannels();
                SaveConfig();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load YouTube config");
            _channels = GetDefaultChannels();
        }
    }

    private void SaveConfig()
    {
        try
        {
            var config = new YouTubeChannelsConfig
            {
                ApiKey = _apiKey,
                Channels = _channels,
                MaxVideosPerChannel = _maxVideosPerChannel,
                FetchDaysBack = _fetchDaysBack
            };
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save YouTube config");
        }
    }

    private static List<YouTubeChannel> GetDefaultChannels()
    {
        // Some popular tech/science channels as examples
        return new List<YouTubeChannel>
        {
            new YouTubeChannel
            {
                ChannelId = "UCBcRF18a7Qf58cCRy5xuWwQ", // MKBHD
                Name = "Marques Brownlee",
                Category = "Tech",
                Enabled = false // Disabled by default until API key is configured
            },
            new YouTubeChannel
            {
                ChannelId = "UCXuqSBlHAE6Xw-yeJA0Tunw", // Linus Tech Tips
                Name = "Linus Tech Tips",
                Category = "Tech",
                Enabled = false
            }
        };
    }

    private void LoadVideoStates()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var json = File.ReadAllText(_statePath);
                var states = JsonConvert.DeserializeObject<Dictionary<string, VideoState>>(json);
                if (states != null)
                {
                    _savedStates = states;

                    foreach (var kvp in states)
                    {
                        if (_videos.TryGetValue(kvp.Key, out var video))
                        {
                            video.State = kvp.Value.State;
                            video.UserNotes = kvp.Value.UserNotes;
                        }
                    }
                    Log.Debug("Loaded {Count} video states", states.Count);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load video states");
        }
    }

    private void SaveVideoStates()
    {
        try
        {
            if (_savedStates.Count > 0)
            {
                var json = JsonConvert.SerializeObject(_savedStates, Formatting.Indented);
                Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
                File.WriteAllText(_statePath, json);
                Log.Debug("Saved {Count} video states", _savedStates.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save video states");
        }
    }

    /// <summary>
    /// Updates the state of a video.
    /// </summary>
    public void SetVideoState(string videoId, MediaItemState state)
    {
        if (_videos.TryGetValue(videoId, out var video))
        {
            video.State = state;

            if (state == MediaItemState.New)
            {
                _savedStates.Remove(videoId);
            }
            else
            {
                _savedStates[videoId] = new VideoState
                {
                    State = state,
                    UserNotes = video.UserNotes
                };
            }

            SaveVideoStates();
            Log.Debug("Video {Id} state changed to {State}", videoId, state);
        }
    }

    /// <summary>
    /// Downloads and caches a video thumbnail.
    /// </summary>
    public async Task<string?> GetThumbnailAsync(YouTubeVideo video, CancellationToken cancellationToken = default)
    {
        // Check if already cached
        var cachedPath = Path.Combine(_thumbnailCachePath, $"{video.VideoId}.jpg");
        if (File.Exists(cachedPath))
        {
            video.LocalThumbnailPath = cachedPath;
            return cachedPath;
        }

        try
        {
            var imageBytes = await _httpClient.GetByteArrayAsync(video.ThumbnailUrl, cancellationToken);
            await File.WriteAllBytesAsync(cachedPath, imageBytes, cancellationToken);
            video.LocalThumbnailPath = cachedPath;
            Log.Debug("Cached thumbnail for video {VideoId}", video.VideoId);
            return cachedPath;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to download thumbnail for {VideoId}", video.VideoId);
            return null;
        }
    }

    /// <summary>
    /// Gets videos filtered by state.
    /// </summary>
    public IEnumerable<YouTubeVideo> GetVideos(MediaItemState? stateFilter = null)
    {
        var query = _videos.Values.AsEnumerable();

        if (stateFilter.HasValue)
        {
            query = query.Where(v => v.State == stateFilter.Value);
        }

        return query.OrderByDescending(v => v.Published);
    }
}
