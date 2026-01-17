using System;
using System.Collections.Generic;
using System.Linq;

namespace LifeStream.Desktop.Services.BomRadar;

/// <summary>
/// Defines a BOM radar location with multiple range products.
/// </summary>
public class RadarLocation
{
    /// <summary>
    /// Location identifier (used for folder naming).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable location name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Location description (e.g., "Terrey Hills").
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Product ID prefix (e.g., "IDR71" for Sydney).
    /// </summary>
    public string ProductIdPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Available radar ranges for this location.
    /// </summary>
    public List<RadarProduct> Products { get; set; } = new List<RadarProduct>();

    /// <summary>
    /// Gets a product by range in km.
    /// </summary>
    public RadarProduct? GetProduct(int rangeKm) =>
        Products.FirstOrDefault(p => p.RangeKm == rangeKm);

    /// <summary>
    /// Gets the default product (128km).
    /// </summary>
    public RadarProduct? DefaultProduct => GetProduct(128) ?? Products.FirstOrDefault();

    /// <summary>
    /// Available ranges in km.
    /// </summary>
    public IReadOnlyList<int> AvailableRanges =>
        Products.Select(p => p.RangeKm).OrderBy(r => r).ToList();

    /// <summary>
    /// Sydney (Terrey Hills) radar location with Australia-wide option.
    /// </summary>
    public static RadarLocation Sydney => new RadarLocation
    {
        Id = "sydney",
        Name = "Sydney",
        Description = "Terrey Hills",
        ProductIdPrefix = "IDR71",
        Products = new List<RadarProduct>
        {
            new RadarProduct { ProductId = "IDR714", Name = "Sydney 64km", Location = "Terrey Hills", RangeKm = 64 },
            new RadarProduct { ProductId = "IDR713", Name = "Sydney 128km", Location = "Terrey Hills", RangeKm = 128 },
            new RadarProduct { ProductId = "IDR712", Name = "Sydney 256km", Location = "Terrey Hills", RangeKm = 256 },
            new RadarProduct { ProductId = "IDR711", Name = "Sydney 512km", Location = "Terrey Hills", RangeKm = 512 },
            new RadarProduct
            {
                ProductId = "IDR00004",
                Name = "Australia",
                Location = "National Composite",
                RangeKm = 9999,  // Special value for Australia-wide
                UpdateInterval = TimeSpan.FromMinutes(10),
                IsComposite = true
            }
        }
    };

    /// <summary>
    /// Australia-wide national composite radar.
    /// </summary>
    public static RadarLocation Australia => new RadarLocation
    {
        Id = "australia",
        Name = "Australia",
        Description = "National Composite",
        ProductIdPrefix = "IDR00",
        Products = new List<RadarProduct>
        {
            new RadarProduct
            {
                ProductId = "IDR00004",
                Name = "Australia",
                Location = "National Composite",
                RangeKm = 0,  // National - no specific range
                UpdateInterval = TimeSpan.FromMinutes(10),
                IsComposite = true
            }
        }
    };
}

/// <summary>
/// Defines a BOM radar product configuration.
/// </summary>
public class RadarProduct
{
    /// <summary>
    /// Product ID (e.g., "IDR713" for Sydney 128km).
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Radar location name.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Range in kilometers.
    /// </summary>
    public int RangeKm { get; set; }

    /// <summary>
    /// Expected update interval (typically 6 or 10 minutes).
    /// </summary>
    public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromMinutes(6);

    /// <summary>
    /// Whether this is a composite/mosaic product.
    /// </summary>
    public bool IsComposite { get; set; }

    /// <summary>
    /// Standard Sydney radar products (legacy - use RadarLocation.Sydney instead).
    /// </summary>
    public static class Sydney
    {
        public static RadarProduct Range512Km => new RadarProduct
        {
            ProductId = "IDR711",
            Name = "Sydney 512km",
            Location = "Terrey Hills",
            RangeKm = 512,
            UpdateInterval = TimeSpan.FromMinutes(6)
        };

        public static RadarProduct Range256Km => new RadarProduct
        {
            ProductId = "IDR712",
            Name = "Sydney 256km",
            Location = "Terrey Hills",
            RangeKm = 256,
            UpdateInterval = TimeSpan.FromMinutes(6)
        };

        public static RadarProduct Range128Km => new RadarProduct
        {
            ProductId = "IDR713",
            Name = "Sydney 128km",
            Location = "Terrey Hills",
            RangeKm = 128,
            UpdateInterval = TimeSpan.FromMinutes(6)
        };

        public static RadarProduct Range64Km => new RadarProduct
        {
            ProductId = "IDR714",
            Name = "Sydney 64km",
            Location = "Terrey Hills",
            RangeKm = 64,
            UpdateInterval = TimeSpan.FromMinutes(6)
        };
    }

    /// <summary>
    /// National composite radar.
    /// </summary>
    public static RadarProduct NationalComposite => new RadarProduct
    {
        ProductId = "IDR00004",
        Name = "National Composite",
        Location = "Australia",
        RangeKm = 0,
        UpdateInterval = TimeSpan.FromMinutes(10),
        IsComposite = true
    };
}

/// <summary>
/// Represents a single radar frame (image with timestamp).
/// </summary>
public class RadarFrame
{
    /// <summary>
    /// Product ID this frame belongs to.
    /// </summary>
    public string ProductId { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp extracted from the filename (UTC).
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Remote filename on FTP server.
    /// </summary>
    public string RemoteFileName { get; set; } = string.Empty;

    /// <summary>
    /// Local cached file path.
    /// </summary>
    public string? LocalFilePath { get; set; }

    /// <summary>
    /// File size in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Whether this frame has been downloaded locally.
    /// </summary>
    public bool IsCached => !string.IsNullOrEmpty(LocalFilePath) && System.IO.File.Exists(LocalFilePath);

    /// <summary>
    /// Formats timestamp for display.
    /// </summary>
    public string DisplayTime => Timestamp.ToLocalTime().ToString("HH:mm");

    /// <summary>
    /// Formats timestamp for filename.
    /// </summary>
    public string FileTimestamp => Timestamp.ToString("yyyyMMddHHmm");
}

/// <summary>
/// Collection of radar frames for a product, supporting playback.
/// </summary>
public class RadarFrameCollection
{
    private readonly List<RadarFrame> _frames = new List<RadarFrame>();
    private readonly object _lock = new object();

    /// <summary>
    /// Product this collection belongs to.
    /// </summary>
    public RadarProduct Product { get; }

    /// <summary>
    /// Maximum number of frames to retain.
    /// </summary>
    public int MaxFrames { get; set; } = 144; // 24 hours at 10-min intervals

    public RadarFrameCollection(RadarProduct product)
    {
        Product = product;
    }

    /// <summary>
    /// All frames in chronological order.
    /// </summary>
    public IReadOnlyList<RadarFrame> Frames
    {
        get
        {
            lock (_lock)
                return _frames.OrderBy(f => f.Timestamp).ToList();
        }
    }

    /// <summary>
    /// Most recent frame.
    /// </summary>
    public RadarFrame? LatestFrame
    {
        get
        {
            lock (_lock)
                return _frames.OrderByDescending(f => f.Timestamp).FirstOrDefault();
        }
    }

    /// <summary>
    /// Number of frames in collection.
    /// </summary>
    public int Count
    {
        get { lock (_lock) return _frames.Count; }
    }

    /// <summary>
    /// Adds a frame to the collection, maintaining max limit.
    /// </summary>
    /// <returns>True if frame was added (new), false if duplicate.</returns>
    public bool AddFrame(RadarFrame frame)
    {
        lock (_lock)
        {
            // Check for duplicate
            if (_frames.Any(f => f.Timestamp == frame.Timestamp))
                return false;

            _frames.Add(frame);

            // Prune oldest frames if over limit
            while (_frames.Count > MaxFrames)
            {
                var oldest = _frames.OrderBy(f => f.Timestamp).First();
                _frames.Remove(oldest);
            }

            return true;
        }
    }

    /// <summary>
    /// Gets frames within a time range.
    /// </summary>
    public IReadOnlyList<RadarFrame> GetFramesInRange(DateTime from, DateTime to)
    {
        lock (_lock)
        {
            return _frames
                .Where(f => f.Timestamp >= from && f.Timestamp <= to)
                .OrderBy(f => f.Timestamp)
                .ToList();
        }
    }

    /// <summary>
    /// Clears all frames.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
            _frames.Clear();
    }
}
