using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using LifeStream.Desktop.Services.Apod;
using Serilog;

namespace LifeStream.Desktop.Controls;

/// <summary>
/// Panel control for displaying NASA Astronomy Picture of the Day.
/// Supports navigation through historical images.
/// </summary>
public class ApodPanel : XtraUserControl
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.UI);

    private readonly HttpClient _httpClient;
    private PictureEdit _pictureBox = null!;
    private LabelControl _titleLabel = null!;
    private LabelControl _dateLabel = null!;
    private MemoEdit _explanationText = null!;
    private LabelControl _statusLabel = null!;
    private SimpleButton _refreshButton = null!;
    private SimpleButton _prevButton = null!;
    private SimpleButton _nextButton = null!;
    private SimpleButton _browseButton = null!;

    private ApodService? _service;
    private string? _currentImageUrl;
    private List<ApodData> _history = new();
    private int _historyIndex = -1;

    public ApodPanel()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        // Main layout using TableLayoutPanel for responsive design
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8)
        };

        // Row styles: Image (fill), Title row (auto), Status (auto), Explanation (fixed), Navigation (auto)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Image
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Date + Title + Refresh
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Status
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); // Explanation
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Navigation buttons

        // Image display
        _pictureBox = new PictureEdit
        {
            Dock = DockStyle.Fill,
            Properties = { SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom },
            Margin = new Padding(0, 0, 0, 8)
        };
        _pictureBox.Properties.ShowCameraMenuItem = DevExpress.XtraEditors.Controls.CameraMenuItemVisibility.Never;
        layout.Controls.Add(_pictureBox, 0, 0);

        // Title row: Date | Title (centered) | Refresh
        var titlePanel = new Panel { Dock = DockStyle.Fill, Height = 30 };

        _dateLabel = new LabelControl
        {
            Text = "",
            Dock = DockStyle.Left,
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { ForeColor = Color.Gray }
        };

        _titleLabel = new LabelControl
        {
            Text = "Loading...",
            Dock = DockStyle.Fill,
            AutoSizeMode = LabelAutoSizeMode.None,
            Appearance =
            {
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center }
            }
        };

        _refreshButton = new SimpleButton
        {
            Text = "Refresh",
            Dock = DockStyle.Right,
            Width = 70
        };
        _refreshButton.Click += OnRefreshClick;

        titlePanel.Controls.Add(_titleLabel);
        titlePanel.Controls.Add(_dateLabel);
        titlePanel.Controls.Add(_refreshButton);
        layout.Controls.Add(titlePanel, 0, 1);

        // Status row
        var statusPanel = new Panel { Dock = DockStyle.Fill, Height = 20 };

        _statusLabel = new LabelControl
        {
            Text = "",
            Dock = DockStyle.Fill,
            AutoSizeMode = LabelAutoSizeMode.None,
            Appearance =
            {
                ForeColor = Color.Gray,
                TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center }
            }
        };

        statusPanel.Controls.Add(_statusLabel);
        layout.Controls.Add(statusPanel, 0, 2);

        // Explanation text
        _explanationText = new MemoEdit
        {
            Dock = DockStyle.Fill,
            Properties = { ReadOnly = true, ScrollBars = ScrollBars.Vertical }
        };
        layout.Controls.Add(_explanationText, 0, 3);

        // Navigation row (below explanation): Back | Forward | ... | Browse
        var navPanel = new Panel { Dock = DockStyle.Fill, Height = 30 };

        _prevButton = new SimpleButton
        {
            Text = "< Back",
            Dock = DockStyle.Left,
            Width = 70,
            Enabled = false
        };
        _prevButton.Click += OnPrevClick;

        _nextButton = new SimpleButton
        {
            Text = "Forward >",
            Dock = DockStyle.Left,
            Width = 80,
            Enabled = false
        };
        _nextButton.Click += OnNextClick;

        _browseButton = new SimpleButton
        {
            Text = "Browse...",
            Dock = DockStyle.Right,
            Width = 70
        };
        _browseButton.Click += OnBrowseClick;

        navPanel.Controls.Add(_nextButton);
        navPanel.Controls.Add(_prevButton);
        navPanel.Controls.Add(_browseButton);
        layout.Controls.Add(navPanel, 0, 4);

        Controls.Add(layout);
    }

    /// <summary>
    /// Binds this panel to an APOD service to receive updates.
    /// </summary>
    public void BindToService(ApodService service)
    {
        Log.Information("ApodPanel.BindToService called");

        if (_service != null)
        {
            _service.DataReceived -= OnDataReceived;
            _service.StatusChanged -= OnStatusChanged;
            _service.ErrorOccurred -= OnErrorOccurred;
        }

        _service = service;

        if (_service != null)
        {
            _service.DataReceived += OnDataReceived;
            _service.StatusChanged += OnStatusChanged;
            _service.ErrorOccurred += OnErrorOccurred;

            Log.Information("ApodPanel subscribed to service events");

            // Load history from service
            LoadHistory();

            // Display current data if available
            if (_service.CurrentApod != null)
            {
                Log.Information("ApodPanel displaying existing CurrentApod: {Title}", _service.CurrentApod.Title);
                NavigateToApod(_service.CurrentApod);
            }
            else
            {
                Log.Information("ApodPanel: CurrentApod is null (service not yet started)");
            }

            UpdateStatus();
        }
    }

    private void LoadHistory()
    {
        if (_service == null) return;

        // Get history from service (last 30 days)
        _history = _service.GetHistory(30);

        // Also check cache folder for any additional files
        var cachePath = _service.CachePath;
        if (Directory.Exists(cachePath))
        {
            var jsonFiles = Directory.GetFiles(cachePath, "APOD_*.json");
            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var apod = Newtonsoft.Json.JsonConvert.DeserializeObject<ApodData>(json);
                    if (apod != null && !_history.Any(h => h.Date == apod.Date))
                    {
                        // Find local image
                        var imagePattern = $"APOD_{apod.Date}.*";
                        var imageFiles = Directory.GetFiles(cachePath, imagePattern)
                            .Where(f => !f.EndsWith(".json"))
                            .ToList();
                        if (imageFiles.Count > 0)
                        {
                            apod.LocalImagePath = imageFiles[0];
                        }
                        _history.Add(apod);
                    }
                }
                catch
                {
                    // Skip invalid files
                }
            }
        }

        // Sort by date descending (newest first)
        _history = _history.OrderByDescending(a => a.Date).ToList();

        Log.Information("Loaded {Count} APOD history items", _history.Count);
        UpdateNavigationButtons();
    }

    private void OnDataReceived(object? sender, ServiceDataEventArgs e)
    {
        Log.Information("ApodPanel.OnDataReceived fired, IsNewData={IsNew}", e.IsNewData);

        if (e.Data is ApodData apod)
        {
            Log.Information("ApodPanel received APOD: {Title} ({Date})", apod.Title, apod.Date);

            // Add to history if not already there
            if (!_history.Any(h => h.Date == apod.Date))
            {
                _history.Insert(0, apod);
            }
            else
            {
                // Update existing entry
                var index = _history.FindIndex(h => h.Date == apod.Date);
                if (index >= 0)
                {
                    _history[index] = apod;
                }
            }

            // Navigate to the new apod
            NavigateToApod(apod);
        }
        else
        {
            Log.Warning("ApodPanel received non-APOD data: {Type}", e.Data?.GetType().Name ?? "null");
        }
    }

    private void OnStatusChanged(object? sender, ServiceStatusChangedEventArgs e)
    {
        UpdateStatus();
    }

    private void OnErrorOccurred(object? sender, ServiceErrorEventArgs e)
    {
        _statusLabel.Text = $"Error: {e.Message}";
        _statusLabel.Appearance.ForeColor = Color.OrangeRed;
    }

    private void OnRefreshClick(object? sender, EventArgs e)
    {
        _service?.RefreshNow();
        _statusLabel.Text = "Refreshing...";
        _statusLabel.Appearance.ForeColor = Color.Gray;
    }

    private void OnPrevClick(object? sender, EventArgs e)
    {
        // Go to older image
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            DisplayApod(_history[_historyIndex]);
            UpdateNavigationButtons();
        }
    }

    private void OnNextClick(object? sender, EventArgs e)
    {
        // Go to newer image
        if (_historyIndex > 0)
        {
            _historyIndex--;
            DisplayApod(_history[_historyIndex]);
            UpdateNavigationButtons();
        }
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        if (_service == null) return;

        // Show the thumbnail browser
        using var browser = new ImageBrowserForm(_service.CachePath, "APOD Gallery");
        browser.ImageSelected += (s, path) =>
        {
            // Find the corresponding APOD data
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (fileName.StartsWith("APOD_"))
            {
                var date = fileName.Substring(5);
                var apod = _history.FirstOrDefault(a => a.Date == date);
                if (apod != null)
                {
                    NavigateToApod(apod);
                }
            }
        };
        browser.ShowDialog(this);
    }

    private void NavigateToApod(ApodData apod)
    {
        // Find in history
        var index = _history.FindIndex(h => h.Date == apod.Date);
        if (index >= 0)
        {
            _historyIndex = index;
        }
        else
        {
            // Add to beginning
            _history.Insert(0, apod);
            _historyIndex = 0;
        }

        DisplayApod(apod);
        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        _prevButton.Enabled = _historyIndex < _history.Count - 1;
        _nextButton.Enabled = _historyIndex > 0;

        // Update button text to show what's next/prev
        if (_historyIndex < _history.Count - 1)
        {
            var prevApod = _history[_historyIndex + 1];
            _prevButton.ToolTip = prevApod.Date;
        }
        if (_historyIndex > 0)
        {
            var nextApod = _history[_historyIndex - 1];
            _nextButton.ToolTip = nextApod.Date;
        }
    }

    private void DisplayApod(ApodData apod)
    {
        Log.Debug("Displaying APOD: {Title}", apod.Title);

        _titleLabel.Text = apod.Title;
        _dateLabel.Text = apod.Date;
        _explanationText.Text = apod.Explanation;

        if (apod.Copyright != null)
        {
            _dateLabel.Text += $" | {apod.Copyright}";
        }

        // Prefer local cached image, fall back to remote URL
        var imageSource = !string.IsNullOrEmpty(apod.LocalImagePath) && File.Exists(apod.LocalImagePath)
            ? apod.LocalImagePath
            : apod.GetDisplayUrl();

        if (imageSource != _currentImageUrl)
        {
            _currentImageUrl = imageSource;
            LoadImageAsync(imageSource);
        }

        UpdateStatus();
    }

    private async void LoadImageAsync(string imageSource)
    {
        try
        {
            _statusLabel.Text = "Loading image...";
            _statusLabel.Appearance.ForeColor = Color.Gray;

            Image image;

            // Check if it's a local file path or a URL
            if (File.Exists(imageSource))
            {
                // Load from local cache
                Log.Debug("Loading APOD image from local cache: {Path}", imageSource);
                var imageData = await Task.Run(() => File.ReadAllBytes(imageSource));
                using var stream = new MemoryStream(imageData);
                image = Image.FromStream(stream);
            }
            else
            {
                // Download from URL
                Log.Debug("Loading APOD image from URL: {Url}", imageSource);
                var imageData = await _httpClient.GetByteArrayAsync(imageSource);
                using var stream = new MemoryStream(imageData);
                image = Image.FromStream(stream);
            }

            // Update on UI thread
            if (!IsDisposed)
            {
                _pictureBox.Image = image;
                var sourceType = File.Exists(imageSource) ? "cached" : "remote";
                _statusLabel.Text = $"Last updated: {DateTime.Now:HH:mm} ({sourceType})";
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load APOD image from {Source}", imageSource);
            if (!IsDisposed)
            {
                _statusLabel.Text = "Failed to load image";
                _statusLabel.Appearance.ForeColor = Color.OrangeRed;
            }
        }
    }

    private void UpdateStatus()
    {
        if (_service == null) return;

        var statusText = _service.Status switch
        {
            ServiceStatus.Running => _service.LastRefresh.HasValue
                ? $"Updated: {_service.LastRefresh.Value:HH:mm}"
                : "Running",
            ServiceStatus.Degraded => $"Degraded ({_service.ConsecutiveFailures} failures)",
            ServiceStatus.Starting => "Starting...",
            ServiceStatus.Stopped => "Stopped",
            ServiceStatus.Faulted => "Faulted",
            _ => ""
        };

        // Add position in history
        if (_history.Count > 0 && _historyIndex >= 0)
        {
            statusText += $" | {_historyIndex + 1}/{_history.Count}";
        }

        _statusLabel.Text = statusText;
        _statusLabel.Appearance.ForeColor = _service.Status switch
        {
            ServiceStatus.Degraded => Color.Orange,
            ServiceStatus.Faulted => Color.Red,
            _ => Color.Gray
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_service != null)
            {
                _service.DataReceived -= OnDataReceived;
                _service.StatusChanged -= OnStatusChanged;
                _service.ErrorOccurred -= OnErrorOccurred;
            }
            _httpClient.Dispose();
        }
        base.Dispose(disposing);
    }
}
