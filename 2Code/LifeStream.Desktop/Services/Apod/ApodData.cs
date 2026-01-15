using System;
using Newtonsoft.Json;

namespace LifeStream.Desktop.Services.Apod;

/// <summary>
/// Data transfer object for NASA APOD (Astronomy Picture of the Day) API response.
/// </summary>
public class ApodData
{
    /// <summary>
    /// Date of the APOD (format: YYYY-MM-DD).
    /// </summary>
    [JsonProperty("date")]
    public string Date { get; set; } = string.Empty;

    /// <summary>
    /// Title of the image/video.
    /// </summary>
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Explanation/description of the image.
    /// </summary>
    [JsonProperty("explanation")]
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// URL to the image or video.
    /// </summary>
    [JsonProperty("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// URL to the high-definition version (may be null for videos).
    /// </summary>
    [JsonProperty("hdurl")]
    public string? HdUrl { get; set; }

    /// <summary>
    /// Media type: "image" or "video".
    /// </summary>
    [JsonProperty("media_type")]
    public string MediaType { get; set; } = "image";

    /// <summary>
    /// Copyright information (may be null for public domain).
    /// </summary>
    [JsonProperty("copyright")]
    public string? Copyright { get; set; }

    /// <summary>
    /// Thumbnail URL (only present when requested and media_type is video).
    /// </summary>
    [JsonProperty("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// API version (usually "v1").
    /// </summary>
    [JsonProperty("service_version")]
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Local file path where the image is cached (not from API, set by service).
    /// </summary>
    [JsonIgnore]
    public string? LocalImagePath { get; set; }

    /// <summary>
    /// Parses the Date string to a DateTime.
    /// </summary>
    public DateTime GetDate()
    {
        return DateTime.TryParse(Date, out var date) ? date : DateTime.MinValue;
    }

    /// <summary>
    /// Whether this is an image (vs video).
    /// </summary>
    public bool IsImage => MediaType?.Equals("image", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Gets the best available image URL (HD if available, otherwise regular).
    /// </summary>
    public string GetBestImageUrl()
    {
        if (IsImage && !string.IsNullOrEmpty(HdUrl))
        {
            return HdUrl;
        }
        return Url;
    }

    /// <summary>
    /// Gets a display-friendly URL (thumbnail for video, regular for image).
    /// </summary>
    public string GetDisplayUrl()
    {
        if (!IsImage && !string.IsNullOrEmpty(ThumbnailUrl))
        {
            return ThumbnailUrl;
        }
        return Url;
    }
}
