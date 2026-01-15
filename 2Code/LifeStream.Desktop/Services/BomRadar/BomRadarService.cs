using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevExpress.Xpo;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using LifeStream.Desktop.Infrastructure;
using LifeStream.Domain.Data;
using LifeStream.Domain.Sources;
using Newtonsoft.Json;
using Serilog;
using Timer = System.Threading.Timer;

namespace LifeStream.Desktop.Services.BomRadar;

/// <summary>
/// Service for fetching BOM weather radar images via FTP.
/// Supports multiple radar ranges for a location.
/// Uses adaptive polling to efficiently track radar updates.
/// </summary>
public class BomRadarService : InformationServiceBase
{
    private static readonly ILogger Log = LoggingConfig.ForCategory($"{LoggingConfig.Categories.Sources}.BOMRadar");

    private const string FtpHost = "ftp.bom.gov.au";
    private const string RadarPath = "/anon/gen/radar";

    private readonly RadarLocation _location;
    private readonly string _basePath;
    private readonly AdaptiveRefreshStrategy _refreshStrategy;
    private readonly Dictionary<int, RadarFrameCollection> _framesByRange = new();
    private readonly Dictionary<int, RadarLayerManager> _layerManagersByRange = new();

    private int _sourceId;
    private Timer? _adaptiveTimer;
    private bool _isPolling;
    private int _currentRange;

    /// <summary>
    /// Creates a new BOM radar service for a location with multiple ranges.
    /// </summary>
    /// <param name="location">Radar location to monitor.</param>
    /// <param name="defaultRange">Default range in km (defaults to 128km).</param>
    public BomRadarService(RadarLocation location, int defaultRange = 128)
        : base($"bom-radar-{location.Id}", $"BOM {location.Name} Radar", "Radar")
    {
        _location = location;
        _basePath = AppPaths.GetServiceDataPath($"BOMRadar_{location.Id}");
        _currentRange = location.AvailableRanges.Contains(defaultRange) ? defaultRange : location.AvailableRanges.First();

        // Initialize frame collections and layer managers for each range
        foreach (var product in location.Products)
        {
            var rangePath = GetRangePath(product.RangeKm);
            Directory.CreateDirectory(rangePath);

            _framesByRange[product.RangeKm] = new RadarFrameCollection(product);
            _layerManagersByRange[product.RangeKm] = new RadarLayerManager(product, rangePath);
        }

        // Configure adaptive refresh strategy
        _refreshStrategy = new AdaptiveRefreshStrategy(
            baseInterval: TimeSpan.FromMinutes(6),
            initialSlack: TimeSpan.FromSeconds(30),
            minimumInterval: TimeSpan.FromSeconds(30),
            maximumInterval: TimeSpan.FromMinutes(15),
            retryInterval: TimeSpan.FromSeconds(20),
            maxRetries: 3,
            maxObservations: 20
        );
    }

    /// <summary>
    /// Creates a new BOM radar service for a specific product (legacy constructor).
    /// </summary>
    public BomRadarService(RadarProduct product)
        : this(CreateLocationFromProduct(product), product.RangeKm)
    {
    }

    private static RadarLocation CreateLocationFromProduct(RadarProduct product)
    {
        // Create a single-product location for backward compatibility
        return new RadarLocation
        {
            Id = product.ProductId.ToLower(),
            Name = product.Name,
            Description = product.Location,
            ProductIdPrefix = product.ProductId.Substring(0, Math.Min(5, product.ProductId.Length)),
            Products = new List<RadarProduct> { product }
        };
    }

    #region Properties

    /// <summary>
    /// Gets the radar location configuration.
    /// </summary>
    public RadarLocation Location => _location;

    /// <summary>
    /// Gets the current radar product (for the selected range).
    /// </summary>
    public RadarProduct? Product => _location.GetProduct(_currentRange);

    /// <summary>
    /// Gets the current range in km.
    /// </summary>
    public int CurrentRange => _currentRange;

    /// <summary>
    /// Gets the available ranges in km.
    /// </summary>
    public IReadOnlyList<int> AvailableRanges => _location.AvailableRanges;

    /// <summary>
    /// Gets the frame collection for the current range.
    /// </summary>
    public RadarFrameCollection Frames => _framesByRange[_currentRange];

    /// <summary>
    /// Gets the most recent radar frame for the current range.
    /// </summary>
    public RadarFrame? CurrentFrame => Frames.LatestFrame;

    /// <summary>
    /// Gets the adaptive refresh strategy for monitoring.
    /// </summary>
    public AdaptiveRefreshStrategy RefreshStrategy => _refreshStrategy;

    /// <summary>
    /// Gets the layer manager for the current range.
    /// </summary>
    public RadarLayerManager LayerManager => _layerManagersByRange[_currentRange];

    /// <summary>
    /// Base data path.
    /// </summary>
    public string DataPath => _basePath;

    /// <summary>
    /// Override base refresh interval - adaptive timer handles actual polling.
    /// </summary>
    protected override TimeSpan RefreshInterval => TimeSpan.FromHours(1);

    #endregion

    #region Range Selection

    /// <summary>
    /// Event raised when the radar range is changed.
    /// </summary>
    public event EventHandler<int>? RangeChanged;

    /// <summary>
    /// Sets the current radar range.
    /// </summary>
    /// <param name="rangeKm">Range in km (64, 128, 256, or 512).</param>
    public void SetRange(int rangeKm)
    {
        if (!_location.AvailableRanges.Contains(rangeKm))
        {
            Log.Warning("Invalid range {Range}km for {Location}, available: {Available}",
                rangeKm, _location.Name, string.Join(", ", _location.AvailableRanges));
            return;
        }

        if (_currentRange == rangeKm)
            return;

        Log.Information("Switching radar range from {Old}km to {New}km", _currentRange, rangeKm);
        _currentRange = rangeKm;

        // Ensure layers are downloaded for new range
        _layerManagersByRange[rangeKm].EnsureLayers();

        // Load cached frames if not already loaded
        var frames = _framesByRange[rangeKm];
        if (frames.Count == 0)
        {
            LoadCachedFramesForRange(rangeKm);
        }

        RangeChanged?.Invoke(this, rangeKm);

        // Notify UI of current frame for the new range
        if (frames.LatestFrame != null)
        {
            RaiseDataReceived(frames.LatestFrame, false);
        }
    }

    private string GetRangePath(int rangeKm)
    {
        return Path.Combine(_basePath, $"{rangeKm}km");
    }

    #endregion

    #region Initialization

    protected override void OnInitialize()
    {
        Log.Information("Initializing BOM Radar service for {Location}, data path: {DataPath}",
            _location.Name, _basePath);

        EnsureSourceRecord();

        // Download background layers for current range
        _layerManagersByRange[_currentRange].EnsureLayers();

        // Load cached frames for current range
        LoadCachedFramesForRange(_currentRange);

        // Set up adaptive timer
        ScheduleNextPoll();

        if (Frames.Count > 0)
        {
            Log.Information("Loaded {Count} cached radar frames for {Range}km, latest: {Time}",
                Frames.Count, _currentRange, Frames.LatestFrame?.DisplayTime);
            RaiseDataReceived(Frames.LatestFrame!, false);
        }
    }

    private void EnsureSourceRecord()
    {
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();

        var source = uow.FindObject<InformationSource>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse("Name = ? AND SourceType = ?", Name, SourceType));

        if (source == null)
        {
            Log.Debug("Creating InformationSource record for BOM Radar {Location}", _location.Name);
            source = new InformationSource(uow)
            {
                Name = Name,
                SourceType = SourceType,
                IsEnabled = true,
                RefreshIntervalSeconds = 360, // 6 minutes
                ConfigJson = JsonConvert.SerializeObject(new
                {
                    LocationId = _location.Id,
                    Location = _location.Name,
                    Ranges = _location.AvailableRanges
                })
            };
            uow.CommitChanges();
        }

        _sourceId = source.Oid;
    }

    private void LoadCachedFramesForRange(int rangeKm)
    {
        var product = _location.GetProduct(rangeKm);
        if (product == null) return;

        var rangePath = GetRangePath(rangeKm);
        if (!Directory.Exists(rangePath)) return;

        var files = Directory.GetFiles(rangePath, $"{product.ProductId}*.png")
            .Concat(Directory.GetFiles(rangePath, $"{product.ProductId}*.gif"))
            .ToList();

        var frames = _framesByRange[rangeKm];
        foreach (var file in files)
        {
            var frame = ParseFrameFromFilename(Path.GetFileName(file), product.ProductId);
            if (frame != null)
            {
                frame.LocalFilePath = file;
                frame.FileSize = new FileInfo(file).Length;
                frames.AddFrame(frame);
            }
        }

        Log.Debug("Loaded {Count} cached frames for {Range}km", frames.Count, rangeKm);
    }

    #endregion

    #region Adaptive Polling

    private void ScheduleNextPoll()
    {
        var delay = _refreshStrategy.GetDelayUntilNextCheck();
        var nextTime = DateTime.Now.Add(delay);

        Log.Debug("BOM Radar {Location}: Next poll in {Delay:F0}s at {Time:HH:mm:ss}",
            _location.Name, delay.TotalSeconds, nextTime);

        _adaptiveTimer?.Dispose();
        _adaptiveTimer = new Timer(
            _ => OnAdaptiveTimerTick(),
            null,
            delay,
            Timeout.InfiniteTimeSpan);

        NextRefresh = nextTime;
    }

    private async void OnAdaptiveTimerTick()
    {
        if (_isPolling || !IsRunning)
            return;

        _isPolling = true;

        try
        {
            // Poll for all ranges to keep them all updated
            var anyNewFrames = false;
            foreach (var product in _location.Products)
            {
                var newFrame = await PollForNewFrameAsync(product);
                if (newFrame != null)
                {
                    anyNewFrames = true;

                    // Download and cache the frame
                    await DownloadFrameAsync(newFrame, product.RangeKm);
                    _framesByRange[product.RangeKm].AddFrame(newFrame);

                    // Store in database
                    StoreFrameRecord(newFrame, product);

                    Log.Information("BOM Radar {Location} {Range}km: New frame {Time}",
                        _location.Name, product.RangeKm, newFrame.DisplayTime);

                    // Notify UI only if this is the current range
                    if (product.RangeKm == _currentRange)
                    {
                        RaiseDataReceived(newFrame, true);
                    }
                }
            }

            if (anyNewFrames)
            {
                _refreshStrategy.RecordSuccess(DateTime.UtcNow);
            }
            else
            {
                _refreshStrategy.RecordMiss();

                if (_refreshStrategy.ShouldRetry)
                {
                    Log.Debug("BOM Radar {Location}: No new data, retry {Count}/{Max}",
                        _location.Name, _refreshStrategy.ConsecutiveMisses, _refreshStrategy.MaxRetries);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "BOM Radar {Location}: Error polling for new frames", _location.Name);
            _refreshStrategy.RecordMiss();
        }
        finally
        {
            _isPolling = false;

            if (IsRunning)
            {
                ScheduleNextPoll();
            }
        }
    }

    private async Task<RadarFrame?> PollForNewFrameAsync(RadarProduct product)
    {
        var latestRemote = await GetLatestRemoteFrameAsync(product);

        if (latestRemote == null)
            return null;

        var currentLatest = _framesByRange[product.RangeKm].LatestFrame;

        // Check if this is newer than what we have
        if (currentLatest == null || latestRemote.Timestamp > currentLatest.Timestamp)
        {
            return latestRemote;
        }

        return null;
    }

    #endregion

    #region FTP Operations

    private async Task<RadarFrame?> GetLatestRemoteFrameAsync(RadarProduct product)
    {
        return await Task.Run(() =>
        {
            try
            {
                var ftpRequest = (FtpWebRequest)WebRequest.Create($"ftp://{FtpHost}{RadarPath}/");
                ftpRequest.Method = WebRequestMethods.Ftp.ListDirectory;
                ftpRequest.Credentials = new NetworkCredential("anonymous", "lifestream@local");
                ftpRequest.Timeout = 30000;
                ftpRequest.ReadWriteTimeout = 30000;

                using var response = (FtpWebResponse)ftpRequest.GetResponse();
                using var stream = response.GetResponseStream();
                using var reader = new StreamReader(stream);

                var files = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    // Filter for this product ID with timestamp
                    if (line.StartsWith(product.ProductId) && line.Contains(".T."))
                    {
                        files.Add(line);
                    }
                }

                // Find the latest file by timestamp in filename
                RadarFrame? latest = null;
                foreach (var file in files)
                {
                    var frame = ParseFrameFromFilename(file, product.ProductId);
                    if (frame != null && (latest == null || frame.Timestamp > latest.Timestamp))
                    {
                        latest = frame;
                    }
                }

                return latest;
            }
            catch (WebException ex)
            {
                Log.Warning(ex, "BOM Radar FTP error listing directory for {Product}", product.ProductId);
                return null;
            }
        });
    }

    private async Task DownloadFrameAsync(RadarFrame frame, int rangeKm)
    {
        await Task.Run(() =>
        {
            try
            {
                var remoteUrl = $"ftp://{FtpHost}{RadarPath}/{frame.RemoteFileName}";
                var localPath = Path.Combine(GetRangePath(rangeKm), frame.RemoteFileName);

                // Skip if already cached
                if (File.Exists(localPath))
                {
                    frame.LocalFilePath = localPath;
                    frame.FileSize = new FileInfo(localPath).Length;
                    return;
                }

                Log.Debug("BOM Radar: Downloading {File}", frame.RemoteFileName);

                var ftpRequest = (FtpWebRequest)WebRequest.Create(remoteUrl);
                ftpRequest.Method = WebRequestMethods.Ftp.DownloadFile;
                ftpRequest.Credentials = new NetworkCredential("anonymous", "lifestream@local");
                ftpRequest.Timeout = 60000;

                using var response = (FtpWebResponse)ftpRequest.GetResponse();
                using var ftpStream = response.GetResponseStream();
                using var fileStream = File.Create(localPath);
                ftpStream.CopyTo(fileStream);

                frame.LocalFilePath = localPath;
                frame.FileSize = new FileInfo(localPath).Length;

                Log.Debug("BOM Radar: Downloaded {File} ({Size} bytes)", frame.RemoteFileName, frame.FileSize);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BOM Radar: Failed to download {File}", frame.RemoteFileName);
            }
        });
    }

    private RadarFrame? ParseFrameFromFilename(string filename, string productId)
    {
        // Format: IDR713.T.202601151234.png
        var pattern = $@"^({Regex.Escape(productId)})\.T\.(\d{{12}})\.(\w+)$";
        var match = Regex.Match(filename, pattern);

        if (!match.Success)
            return null;

        var timestampStr = match.Groups[2].Value;
        if (!DateTime.TryParseExact(timestampStr, "yyyyMMddHHmm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp))
        {
            return null;
        }

        return new RadarFrame
        {
            ProductId = productId,
            Timestamp = timestamp,
            RemoteFileName = filename
        };
    }

    #endregion

    #region Data Storage

    private void StoreFrameRecord(RadarFrame frame, RadarProduct product)
    {
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();

        // Check for existing record
        var seriesName = $"{product.ProductId}_{product.RangeKm}km";
        var existing = uow.FindObject<DataPoint>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse(
                "SourceId = ? AND SeriesName = ? AND Timestamp = ?",
                _sourceId, seriesName, frame.Timestamp));

        if (existing == null)
        {
            var dataPoint = new DataPoint(uow)
            {
                SourceId = _sourceId,
                SeriesName = seriesName,
                Timestamp = frame.Timestamp,
                StringValue = frame.LocalFilePath,
                MetadataJson = JsonConvert.SerializeObject(new
                {
                    frame.ProductId,
                    frame.RemoteFileName,
                    frame.FileSize,
                    product.RangeKm
                })
            };
        }

        // Update source last refresh time
        var source = uow.GetObjectByKey<InformationSource>(_sourceId);
        if (source != null)
        {
            source.LastRefreshAt = DateTime.Now;
            source.LastErrorMessage = null;
        }

        uow.CommitChanges();
    }

    protected override Task<object?> FetchDataAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<object?>(Frames.LatestFrame);
    }

    protected override void StoreData(object data)
    {
        // Handled in OnAdaptiveTimerTick
    }

    protected override bool HasDataChanged(object newData, object? previousData)
    {
        if (newData is RadarFrame newFrame && previousData is RadarFrame oldFrame)
        {
            return newFrame.Timestamp > oldFrame.Timestamp;
        }
        return true;
    }

    #endregion

    #region Lifecycle

    protected override void OnShutdown()
    {
        _adaptiveTimer?.Dispose();
        _adaptiveTimer = null;
        base.OnShutdown();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _adaptiveTimer?.Dispose();
        }
        base.Dispose(disposing);
    }

    #endregion
}
