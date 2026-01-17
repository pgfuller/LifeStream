using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using LifeStream.Desktop.Services.BomRadar;
using Serilog;

namespace LifeStream.Desktop.Controls;

/// <summary>
/// Panel control for displaying BOM weather radar images.
/// Supports viewing current frame, playback of historical frames, and range selection.
/// </summary>
public class BomRadarPanel : XtraUserControl
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.UI);

    private PictureEdit _pictureBox = null!;
    private LabelControl _titleLabel = null!;
    private LabelControl _timeLabel = null!;
    private LabelControl _statusLabel = null!;
    private SimpleButton _refreshButton = null!;
    private ComboBoxEdit _rangeSelector = null!;
    private TrackBarControl _playbackSlider = null!;
    private SimpleButton _playButton = null!;
    private LabelControl _frameCountLabel = null!;

    private BomRadarService? _service;
    private RadarFrame? _displayedFrame;
    private System.Windows.Forms.Timer? _playbackTimer;
    private bool _isPlaying;
    private int _playbackIndex;
    private bool _updatingRangeSelector;

    public BomRadarPanel()
    {
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };

        // Row styles: Image (fill), Title (auto), Playback controls (auto), Status (auto)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Image
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Title + range + time
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // Playback controls
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Status

        // Image display
        _pictureBox = new PictureEdit
        {
            Dock = DockStyle.Fill,
            Properties = { SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom },
            Margin = new Padding(0, 0, 0, 8)
        };
        _pictureBox.Properties.ShowCameraMenuItem = DevExpress.XtraEditors.Controls.CameraMenuItemVisibility.Never;
        layout.Controls.Add(_pictureBox, 0, 0);

        // Title row with range selector, time and refresh button
        var titlePanel = new Panel { Dock = DockStyle.Fill, Height = 30 };

        _titleLabel = new LabelControl
        {
            Text = "Weather Radar",
            Dock = DockStyle.Left,
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 11, FontStyle.Bold) }
        };

        _rangeSelector = new ComboBoxEdit
        {
            Dock = DockStyle.Left,
            Width = 80,
            Properties = { TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor }
        };
        _rangeSelector.SelectedIndexChanged += OnRangeSelectorChanged;

        _timeLabel = new LabelControl
        {
            Text = "",
            Dock = DockStyle.Fill,
            AutoSizeMode = LabelAutoSizeMode.None,
            Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center } }
        };

        _refreshButton = new SimpleButton
        {
            Text = "Refresh",
            Dock = DockStyle.Right,
            Width = 70
        };
        _refreshButton.Click += OnRefreshClick;

        titlePanel.Controls.Add(_timeLabel);
        titlePanel.Controls.Add(_rangeSelector);
        titlePanel.Controls.Add(_titleLabel);
        titlePanel.Controls.Add(_refreshButton);
        layout.Controls.Add(titlePanel, 0, 1);

        // Playback controls row
        var playbackPanel = new Panel { Dock = DockStyle.Fill, Height = 40 };

        _playButton = new SimpleButton
        {
            Text = "Play",
            Dock = DockStyle.Left,
            Width = 60
        };
        _playButton.Click += OnPlayButtonClick;

        _playbackSlider = new TrackBarControl
        {
            Dock = DockStyle.Fill,
            Properties = { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10 }
        };
        _playbackSlider.EditValueChanged += OnSliderValueChanged;

        _frameCountLabel = new LabelControl
        {
            Text = "0/0",
            Dock = DockStyle.Right,
            AutoSizeMode = LabelAutoSizeMode.Default,
            Width = 60,
            Appearance = { TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center } }
        };

        playbackPanel.Controls.Add(_playbackSlider);
        playbackPanel.Controls.Add(_playButton);
        playbackPanel.Controls.Add(_frameCountLabel);
        layout.Controls.Add(playbackPanel, 0, 2);

        // Status row
        _statusLabel = new LabelControl
        {
            Text = "",
            Dock = DockStyle.Fill,
            AutoSizeMode = LabelAutoSizeMode.None,
            Appearance = { ForeColor = Color.Gray }
        };
        layout.Controls.Add(_statusLabel, 0, 3);

        Controls.Add(layout);

        // Playback timer
        _playbackTimer = new System.Windows.Forms.Timer { Interval = 500 }; // 2 fps
        _playbackTimer.Tick += OnPlaybackTimerTick;
    }

    /// <summary>
    /// Binds this panel to a BOM radar service to receive updates.
    /// </summary>
    public void BindToService(BomRadarService service)
    {
        Log.Information("BomRadarPanel.BindToService called for {Location}", service.Location.Name);

        if (_service != null)
        {
            _service.DataReceived -= OnDataReceived;
            _service.StatusChanged -= OnStatusChanged;
            _service.ErrorOccurred -= OnErrorOccurred;
            _service.RangeChanged -= OnRangeChanged;
        }

        _service = service;

        if (_service != null)
        {
            _service.DataReceived += OnDataReceived;
            _service.StatusChanged += OnStatusChanged;
            _service.ErrorOccurred += OnErrorOccurred;
            _service.RangeChanged += OnRangeChanged;

            _titleLabel.Text = _service.Location.Name;

            // Populate range selector
            PopulateRangeSelector();

            Log.Information("BomRadarPanel subscribed to service events");

            // Display current frame if available
            if (_service.CurrentFrame != null)
            {
                DisplayFrame(_service.CurrentFrame);
            }

            UpdatePlaybackControls();
            UpdateStatus();
        }
    }

    private void PopulateRangeSelector()
    {
        if (_service == null) return;

        _updatingRangeSelector = true;
        try
        {
            _rangeSelector.Properties.Items.Clear();

            foreach (var range in _service.AvailableRanges)
            {
                // Special display for Australia-wide composite (RangeKm = 9999)
                var displayText = range >= 9999 ? "Australia" : $"{range}km";
                _rangeSelector.Properties.Items.Add(displayText);
            }

            // Select current range
            var currentIndex = _service.AvailableRanges.ToList().IndexOf(_service.CurrentRange);
            if (currentIndex >= 0)
            {
                _rangeSelector.SelectedIndex = currentIndex;
            }
        }
        finally
        {
            _updatingRangeSelector = false;
        }
    }

    private void OnRangeSelectorChanged(object? sender, EventArgs e)
    {
        if (_updatingRangeSelector || _service == null) return;

        var selectedIndex = _rangeSelector.SelectedIndex;
        if (selectedIndex >= 0 && selectedIndex < _service.AvailableRanges.Count)
        {
            var newRange = _service.AvailableRanges[selectedIndex];
            var rangeDisplay = newRange >= 9999 ? "Australia" : $"{newRange}km";
            Log.Debug("User selected range: {Range}", rangeDisplay);

            // Stop playback when changing range
            if (_isPlaying)
            {
                StopPlayback();
            }

            _service.SetRange(newRange);
        }
    }

    private void OnRangeChanged(object? sender, int newRange)
    {
        // Update selector if range changed externally
        _updatingRangeSelector = true;
        try
        {
            var index = _service?.AvailableRanges.ToList().IndexOf(newRange) ?? -1;
            if (index >= 0)
            {
                _rangeSelector.SelectedIndex = index;
            }
        }
        finally
        {
            _updatingRangeSelector = false;
        }

        // Update title
        if (_service?.Product != null)
        {
            var rangeDisplay = newRange >= 9999 ? "Australia" : $"{_service.Location.Name} {newRange}km";
            _titleLabel.Text = rangeDisplay;
        }

        UpdatePlaybackControls();
        UpdateStatus();
    }

    private void OnDataReceived(object? sender, ServiceDataEventArgs e)
    {
        Log.Debug("BomRadarPanel.OnDataReceived fired");

        if (e.Data is RadarFrame frame)
        {
            Log.Debug("BomRadarPanel received frame: {Time}", frame.DisplayTime);

            // If not playing, show the new frame
            if (!_isPlaying)
            {
                DisplayFrame(frame);
            }

            UpdatePlaybackControls();
        }

        UpdateStatus();
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

    private void DisplayFrame(RadarFrame frame)
    {
        _displayedFrame = frame;
        _timeLabel.Text = frame.Timestamp.ToLocalTime().ToString("HH:mm dd/MM/yyyy");

        if (frame.IsCached && File.Exists(frame.LocalFilePath))
        {
            try
            {
                // Try to create composite image with background layers
                if (_service?.LayerManager != null && _service.LayerManager.HasAllLayers())
                {
                    var composite = _service.LayerManager.CreateCompositeImage(frame.LocalFilePath!, includeLegend: true);
                    if (composite != null)
                    {
                        _pictureBox.Image = composite;
                        return;
                    }
                }

                // Fallback to raw radar frame
                using var stream = new FileStream(frame.LocalFilePath!, FileMode.Open, FileAccess.Read);
                _pictureBox.Image = Image.FromStream(stream);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load radar image from {Path}", frame.LocalFilePath);
                _pictureBox.Image = null;
            }
        }
        else
        {
            _pictureBox.Image = null;
        }
    }

    private void UpdatePlaybackControls()
    {
        if (_service == null) return;

        var frames = _service.Frames.Frames;
        var count = frames.Count;

        _playbackSlider.Properties.Maximum = Math.Max(0, count - 1);
        _frameCountLabel.Text = $"{(_isPlaying ? _playbackIndex + 1 : count)}/{count}";

        if (!_isPlaying && count > 0)
        {
            _playbackSlider.Value = count - 1; // Latest frame
        }

        _playButton.Enabled = count > 1;
        _playbackSlider.Enabled = count > 1;
    }

    private void UpdateStatus()
    {
        if (_service == null) return;

        var strategy = _service.RefreshStrategy;
        var frames = _service.Frames;

        var statusParts = new System.Collections.Generic.List<string>();

        // Current range
        statusParts.Add($"{_service.CurrentRange}km");

        // Frame count
        statusParts.Add($"{frames.Count} frames");

        // Next check
        var nextCheck = strategy.GetNextCheckTime().ToLocalTime();
        var delay = strategy.GetDelayUntilNextCheck();
        if (delay.TotalSeconds > 0)
        {
            statusParts.Add($"Next: {nextCheck:HH:mm:ss}");
        }

        // Adaptive info
        if (strategy.ConsecutiveMisses > 0)
        {
            statusParts.Add($"Retry {strategy.ConsecutiveMisses}/{strategy.MaxRetries}");
        }

        _statusLabel.Text = string.Join(" | ", statusParts);
        _statusLabel.Appearance.ForeColor = _service.Status switch
        {
            ServiceStatus.Degraded => Color.Orange,
            ServiceStatus.Faulted => Color.Red,
            _ => Color.Gray
        };
    }

    #region Playback

    private void OnPlayButtonClick(object? sender, EventArgs e)
    {
        if (_isPlaying)
        {
            StopPlayback();
        }
        else
        {
            StartPlayback();
        }
    }

    private void StartPlayback()
    {
        if (_service == null || _service.Frames.Count < 2) return;

        _isPlaying = true;
        _playbackIndex = 0;
        _playButton.Text = "Stop";
        _playbackTimer?.Start();

        DisplayFrameAtIndex(_playbackIndex);
    }

    private void StopPlayback()
    {
        _isPlaying = false;
        _playbackTimer?.Stop();
        _playButton.Text = "Play";

        // Return to latest frame
        if (_service?.CurrentFrame != null)
        {
            DisplayFrame(_service.CurrentFrame);
        }

        UpdatePlaybackControls();
    }

    private void OnPlaybackTimerTick(object? sender, EventArgs e)
    {
        if (_service == null || !_isPlaying) return;

        var frames = _service.Frames.Frames;
        if (frames.Count == 0) return;

        _playbackIndex++;
        if (_playbackIndex >= frames.Count)
        {
            _playbackIndex = 0; // Loop
        }

        DisplayFrameAtIndex(_playbackIndex);
        _playbackSlider.Value = _playbackIndex;
        _frameCountLabel.Text = $"{_playbackIndex + 1}/{frames.Count}";
    }

    private void OnSliderValueChanged(object? sender, EventArgs e)
    {
        if (_service == null) return;

        var index = (int)_playbackSlider.Value;
        DisplayFrameAtIndex(index);

        if (!_isPlaying)
        {
            var frames = _service.Frames.Frames;
            _frameCountLabel.Text = $"{index + 1}/{frames.Count}";
        }
    }

    private void DisplayFrameAtIndex(int index)
    {
        if (_service == null) return;

        var frames = _service.Frames.Frames;
        if (index >= 0 && index < frames.Count)
        {
            DisplayFrame(frames[index]);
        }
    }

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _playbackTimer?.Stop();
            _playbackTimer?.Dispose();

            if (_service != null)
            {
                _service.DataReceived -= OnDataReceived;
                _service.StatusChanged -= OnStatusChanged;
                _service.ErrorOccurred -= OnErrorOccurred;
                _service.RangeChanged -= OnRangeChanged;
            }
        }
        base.Dispose(disposing);
    }
}
