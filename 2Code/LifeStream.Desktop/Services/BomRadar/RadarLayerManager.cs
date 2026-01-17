using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using LifeStream.Core.Infrastructure;
using Serilog;

namespace LifeStream.Desktop.Services.BomRadar;

/// <summary>
/// Manages downloading and compositing of radar background layers.
/// Layers are downloaded once per day and cached locally.
/// </summary>
public class RadarLayerManager
{
    private static readonly ILogger Log = LoggingConfig.ForCategory($"{LoggingConfig.Categories.Sources}.BOMRadar.Layers");

    private const string FtpHost = "ftp.bom.gov.au";
    private const string TransparenciesPath = "/anon/gen/radar_transparencies";

    private readonly string _layerPath;
    private readonly RadarProduct _product;
    private DateTime _lastLayerRefresh = DateTime.MinValue;

    /// <summary>
    /// Layer types in compositing order (bottom to top).
    /// </summary>
    public static readonly string[] LayerTypes = new[]
    {
        "background",
        "topography",
        // radar frame goes here
        "locations",
        "range"
    };

    public RadarLayerManager(RadarProduct product, string basePath)
    {
        _product = product;
        _layerPath = Path.Combine(basePath, "Layers");
        Directory.CreateDirectory(_layerPath);
    }

    /// <summary>
    /// Whether this is a composite/mosaic product that doesn't need layer compositing.
    /// </summary>
    public bool IsCompositeProduct => _product.IsComposite;

    /// <summary>
    /// Gets the path to the layers folder.
    /// </summary>
    public string LayerPath => _layerPath;

    /// <summary>
    /// Checks if layers need refreshing (once per day).
    /// </summary>
    public bool NeedsRefresh => _lastLayerRefresh.Date < DateTime.Today;

    /// <summary>
    /// Downloads all layer files for the configured product.
    /// Composite products don't need separate layers.
    /// </summary>
    public void RefreshLayers()
    {
        // Composite products (like national radar) come pre-rendered with all layers
        if (_product.IsComposite)
        {
            Log.Debug("Skipping layer refresh for composite product {Product}", _product.ProductId);
            _lastLayerRefresh = DateTime.Now;
            return;
        }

        Log.Information("Refreshing radar layers for {Product}", _product.ProductId);

        var success = true;

        foreach (var layerType in LayerTypes)
        {
            var fileName = $"{_product.ProductId}.{layerType}.png";
            var localPath = Path.Combine(_layerPath, fileName);
            var remoteUrl = $"ftp://{FtpHost}{TransparenciesPath}/{fileName}";

            if (!DownloadLayer(remoteUrl, localPath))
            {
                success = false;
            }
        }

        // Also download the legend
        var legendFileName = "IDR.legend.0.png";
        var legendPath = Path.Combine(_layerPath, legendFileName);
        var legendUrl = $"ftp://{FtpHost}{TransparenciesPath}/{legendFileName}";
        DownloadLayer(legendUrl, legendPath);

        if (success)
        {
            _lastLayerRefresh = DateTime.Now;
            Log.Information("Radar layers refreshed successfully");
        }
        else
        {
            Log.Warning("Some radar layers failed to download");
        }
    }

    private bool DownloadLayer(string remoteUrl, string localPath)
    {
        try
        {
            Log.Debug("Downloading layer: {Url}", remoteUrl);

            var ftpRequest = (FtpWebRequest)WebRequest.Create(remoteUrl);
            ftpRequest.Method = WebRequestMethods.Ftp.DownloadFile;
            ftpRequest.Credentials = new NetworkCredential("anonymous", "lifestream@local");
            ftpRequest.Timeout = 30000;

            using var response = (FtpWebResponse)ftpRequest.GetResponse();
            using var stream = response.GetResponseStream();
            using var fileStream = File.Create(localPath);
            stream.CopyTo(fileStream);

            Log.Debug("Downloaded layer: {Path}", Path.GetFileName(localPath));
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to download layer: {Url}", remoteUrl);
            return false;
        }
    }

    /// <summary>
    /// Gets the local path for a layer file.
    /// </summary>
    public string? GetLayerPath(string layerType)
    {
        var fileName = $"{_product.ProductId}.{layerType}.png";
        var path = Path.Combine(_layerPath, fileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Gets the legend image path.
    /// </summary>
    public string? GetLegendPath()
    {
        var path = Path.Combine(_layerPath, "IDR.legend.0.png");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Checks if all required layers are available.
    /// Composite products don't need separate layers.
    /// </summary>
    public bool HasAllLayers()
    {
        // Composite products don't need layers
        if (_product.IsComposite)
            return true;

        foreach (var layerType in LayerTypes)
        {
            if (GetLayerPath(layerType) == null)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Creates a composite image with background layers and radar frame.
    /// For composite products, simply returns the radar frame as-is.
    /// </summary>
    /// <param name="radarFramePath">Path to the radar frame image.</param>
    /// <param name="includeLegend">Whether to include the legend.</param>
    /// <returns>Composite bitmap, or null if layers are missing.</returns>
    public Bitmap? CreateCompositeImage(string radarFramePath, bool includeLegend = true)
    {
        if (!File.Exists(radarFramePath))
        {
            Log.Warning("Radar frame not found: {Path}", radarFramePath);
            return null;
        }

        try
        {
            // Composite products come pre-rendered - just load and return
            if (_product.IsComposite)
            {
                return new Bitmap(radarFramePath);
            }

            // Load the radar frame to get dimensions
            using var radarImage = Image.FromFile(radarFramePath);
            var width = radarImage.Width;
            var height = radarImage.Height;

            // Create the composite bitmap
            var composite = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            using var graphics = Graphics.FromImage(composite);
            graphics.Clear(Color.Transparent);

            // Layer order: background, topography, radar, locations, range
            // Draw background
            DrawLayer(graphics, "background", width, height);

            // Draw topography
            DrawLayer(graphics, "topography", width, height);

            // Draw radar frame
            graphics.DrawImage(radarImage, 0, 0, width, height);

            // Draw locations
            DrawLayer(graphics, "locations", width, height);

            // Draw range circles
            DrawLayer(graphics, "range", width, height);

            // Optionally add legend in corner
            if (includeLegend)
            {
                var legendPath = GetLegendPath();
                if (legendPath != null && File.Exists(legendPath))
                {
                    using var legend = Image.FromFile(legendPath);
                    // Draw legend in bottom-left corner
                    graphics.DrawImage(legend, 10, height - legend.Height - 10);
                }
            }

            return composite;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create composite image");
            return null;
        }
    }

    private void DrawLayer(Graphics graphics, string layerType, int width, int height)
    {
        var layerPath = GetLayerPath(layerType);
        if (layerPath == null || !File.Exists(layerPath))
            return;

        try
        {
            using var layer = Image.FromFile(layerPath);
            graphics.DrawImage(layer, 0, 0, width, height);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to draw layer: {Layer}", layerType);
        }
    }

    /// <summary>
    /// Ensures layers are downloaded, refreshing if needed.
    /// </summary>
    public void EnsureLayers()
    {
        if (NeedsRefresh || !HasAllLayers())
        {
            RefreshLayers();
        }
    }
}
