using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Base;
using DevExpress.XtraGrid.Views.Layout;
using DevExpress.XtraGrid.Views.Layout.ViewInfo;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using LifeStream.Desktop.Services.Media;
using Serilog;

namespace LifeStream.Desktop.Controls;

/// <summary>
/// Panel control for displaying YouTube videos in a card/tile layout.
/// </summary>
public class MediaPanel : XtraUserControl
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.UI);

    private GridControl _grid = null!;
    private LayoutView _layoutView = null!;
    private LabelControl _statusLabel = null!;
    private SimpleButton _refreshButton = null!;
    private ComboBoxEdit _filterCombo = null!;
    private SimpleButton _watchLaterButton = null!;
    private SimpleButton _watchedButton = null!;
    private SimpleButton _notInterestedButton = null!;

    private YouTubeService? _service;
    private List<YouTubeVideo> _displayedVideos = new();
    private Dictionary<string, Image> _thumbnailCache = new();

    // Card dimensions
    private const int CardWidth = 320;
    private const int CardHeight = 200;

    public MediaPanel()
    {
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        // Main layout
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(4)
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // Header
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Cards
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // Status

        // Header row with filter, state buttons, and refresh
        var headerPanel = new Panel { Dock = DockStyle.Fill, Height = 30 };

        _filterCombo = new ComboBoxEdit
        {
            Dock = DockStyle.Left,
            Width = 110
        };
        _filterCombo.Properties.Items.AddRange(new[] { "All", "New", "Watch Later", "Watched", "Not Interested" });
        _filterCombo.SelectedIndex = 0;
        _filterCombo.SelectedIndexChanged += OnFilterChanged;

        // State action buttons
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(10, 0, 0, 0)
        };

        _watchLaterButton = new SimpleButton { Text = "Watch Later", Width = 80 };
        _watchLaterButton.Click += (s, e) => SetSelectedVideoState(MediaItemState.WatchLater);

        _watchedButton = new SimpleButton { Text = "Watched", Width = 65 };
        _watchedButton.Click += (s, e) => SetSelectedVideoState(MediaItemState.Watched);

        _notInterestedButton = new SimpleButton { Text = "Not Interested", Width = 90 };
        _notInterestedButton.Click += (s, e) => SetSelectedVideoState(MediaItemState.NotInterested);

        buttonPanel.Controls.Add(_watchLaterButton);
        buttonPanel.Controls.Add(_watchedButton);
        buttonPanel.Controls.Add(_notInterestedButton);

        _refreshButton = new SimpleButton
        {
            Text = "Refresh",
            Dock = DockStyle.Right,
            Width = 70
        };
        _refreshButton.Click += OnRefreshClick;

        headerPanel.Controls.Add(_filterCombo);
        headerPanel.Controls.Add(buttonPanel);
        headerPanel.Controls.Add(_refreshButton);
        layout.Controls.Add(headerPanel, 0, 0);

        // Grid with LayoutView for card display
        _grid = new GridControl { Dock = DockStyle.Fill };
        _layoutView = new LayoutView(_grid);
        _grid.MainView = _layoutView;

        // Configure layout view for cards
        ConfigureLayoutView();

        layout.Controls.Add(_grid, 0, 1);

        // Status row
        _statusLabel = new LabelControl
        {
            Text = "Loading...",
            Dock = DockStyle.Fill,
            AutoSizeMode = LabelAutoSizeMode.None,
            Appearance = { ForeColor = Color.Gray }
        };
        layout.Controls.Add(_statusLabel, 0, 2);

        Controls.Add(layout);
    }

    private void ConfigureLayoutView()
    {
        // Card layout settings
        _layoutView.OptionsBehavior.AutoPopulateColumns = false;
        _layoutView.OptionsView.ShowCardCaption = false;
        _layoutView.OptionsView.ViewMode = LayoutViewMode.MultiColumn;
        _layoutView.OptionsView.ShowHeaderPanel = false;

        // Card size (320x180 thumbnail + text below)
        _layoutView.CardMinSize = new Size(CardWidth, CardHeight);

        // Create thumbnail column with PictureEdit
        var thumbnailCol = _layoutView.Columns.AddVisible("ThumbnailImage");
        thumbnailCol.Caption = "";
        thumbnailCol.UnboundType = DevExpress.Data.UnboundColumnType.Object;

        var picEdit = new RepositoryItemPictureEdit
        {
            SizeMode = DevExpress.XtraEditors.Controls.PictureSizeMode.Zoom,
            ShowMenu = false,
            ReadOnly = true
        };
        _grid.RepositoryItems.Add(picEdit);
        thumbnailCol.ColumnEdit = picEdit;

        // Create text columns
        var titleCol = _layoutView.Columns.AddVisible("Title");
        titleCol.Caption = "";

        var infoCol = _layoutView.Columns.AddVisible("CardInfo");
        infoCol.Caption = "";
        infoCol.UnboundType = DevExpress.Data.UnboundColumnType.String;

        // Handle unbound column data
        _layoutView.CustomUnboundColumnData += OnCustomUnboundColumnData;

        // Configure card template layout
        ConfigureCardTemplate();

        // Double-click to open video
        _layoutView.DoubleClick += OnLayoutDoubleClick;
        _layoutView.KeyDown += OnLayoutKeyDown;
        _layoutView.MouseUp += OnLayoutMouseUp;
    }

    private void ConfigureCardTemplate()
    {
        var card = _layoutView.TemplateCard;
        card.BeginUpdate();

        // Find the layout items for our columns
        foreach (var item in card.Items)
        {
            if (item is LayoutViewField field)
            {
                if (field.FieldName == "ThumbnailImage")
                {
                    field.TextVisible = false;
                    field.MinSize = new Size(CardWidth - 20, 135);
                    field.MaxSize = new Size(CardWidth - 20, 135);
                    field.SizeConstraintsType = DevExpress.XtraLayout.SizeConstraintsType.Custom;
                }
                else if (field.FieldName == "Title")
                {
                    field.TextVisible = false;
                    field.AppearanceItemCaption.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
                    field.AppearanceItemCaption.Options.UseFont = true;
                }
                else if (field.FieldName == "CardInfo")
                {
                    field.TextVisible = false;
                    field.AppearanceItemCaption.ForeColor = Color.Gray;
                    field.AppearanceItemCaption.Font = new Font("Segoe UI", 8f);
                    field.AppearanceItemCaption.Options.UseFont = true;
                    field.AppearanceItemCaption.Options.UseForeColor = true;
                }
            }
        }

        card.EndUpdate();
    }

    private void OnCustomUnboundColumnData(object sender, CustomColumnDataEventArgs e)
    {
        if (e.ListSourceRowIndex < 0 || e.ListSourceRowIndex >= _displayedVideos.Count)
            return;

        var video = _displayedVideos[e.ListSourceRowIndex];

        if (e.Column.FieldName == "ThumbnailImage" && e.IsGetData)
        {
            if (_thumbnailCache.TryGetValue(video.VideoId, out var image))
            {
                e.Value = image;
            }
        }
        else if (e.Column.FieldName == "CardInfo" && e.IsGetData)
        {
            var stateIndicator = video.State != MediaItemState.New ? $"{video.StateIcon} " : "";
            e.Value = $"{stateIndicator}{video.ChannelName} | {video.DurationDisplay} | {video.ViewCountDisplay} | {video.AgeDisplay}";
        }
    }

    /// <summary>
    /// Binds this panel to a YouTubeService to receive updates.
    /// </summary>
    public void BindToService(YouTubeService service)
    {
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

            if (_service.CurrentData != null)
            {
                DisplayData(_service.CurrentData);
            }
        }
    }

    private void OnDataReceived(object? sender, ServiceDataEventArgs e)
    {
        if (e.Data is MediaData data)
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => DisplayData(data));
            }
            else
            {
                DisplayData(data);
            }
        }
    }

    private void OnStatusChanged(object? sender, ServiceStatusChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(UpdateStatus);
        }
        else
        {
            UpdateStatus();
        }
    }

    private void OnErrorOccurred(object? sender, ServiceErrorEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() =>
            {
                _statusLabel.Text = $"Error: {e.Message}";
                _statusLabel.Appearance.ForeColor = Color.OrangeRed;
            });
        }
    }

    private void DisplayData(MediaData data)
    {
        // Apply filter
        var filter = _filterCombo.SelectedItem?.ToString() ?? "All";
        _displayedVideos = filter switch
        {
            "New" => data.Videos.Where(v => v.State == MediaItemState.New).ToList(),
            "Watch Later" => data.Videos.Where(v => v.State == MediaItemState.WatchLater).ToList(),
            "Watched" => data.Videos.Where(v => v.State == MediaItemState.Watched).ToList(),
            "Not Interested" => data.Videos.Where(v => v.State == MediaItemState.NotInterested).ToList(),
            _ => data.Videos.Where(v => v.State != MediaItemState.NotInterested).ToList()
        };

        _grid.DataSource = _displayedVideos;
        _layoutView.RefreshData();

        // Load thumbnails asynchronously
        _ = LoadThumbnailsAsync();

        UpdateStatus();
        Log.Debug("MediaPanel displaying {Count} videos", _displayedVideos.Count);
    }

    private async Task LoadThumbnailsAsync()
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        foreach (var video in _displayedVideos)
        {
            if (_thumbnailCache.ContainsKey(video.VideoId))
                continue;

            try
            {
                // Check if cached on disk first
                var cachePath = Path.Combine(
                    AppPaths.GetServiceDataPath("Media"),
                    "thumbnails",
                    $"{video.VideoId}.jpg");

                Image? image = null;

                if (File.Exists(cachePath))
                {
                    // Load from disk cache
                    using var stream = new FileStream(cachePath, FileMode.Open, FileAccess.Read);
                    image = Image.FromStream(stream);
                }
                else
                {
                    // Download from YouTube
                    var imageBytes = await httpClient.GetByteArrayAsync(video.ThumbnailUrl);

                    // Save to disk cache
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    await File.WriteAllBytesAsync(cachePath, imageBytes);

                    using var ms = new MemoryStream(imageBytes);
                    image = Image.FromStream(ms);
                }

                if (image != null)
                {
                    _thumbnailCache[video.VideoId] = image;

                    // Refresh the view to show the thumbnail
                    if (InvokeRequired)
                        BeginInvoke(() => _layoutView.RefreshData());
                    else
                        _layoutView.RefreshData();
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to load thumbnail for {VideoId}", video.VideoId);
            }
        }
    }

    private void UpdateStatus()
    {
        if (_service?.CurrentData == null)
        {
            _statusLabel.Text = "Configure API key in youtube-channels.json to enable YouTube feeds.";
            _statusLabel.Appearance.ForeColor = Color.Gray;
            return;
        }

        var data = _service.CurrentData;
        if (data.Videos.Count == 0)
        {
            _statusLabel.Text = "No videos. Add API key and enable channels in youtube-channels.json";
            _statusLabel.Appearance.ForeColor = Color.Gray;
            return;
        }

        _statusLabel.Text = $"{data.NewCount} new | {data.WatchLaterCount} watch later | {data.Videos.Count} total | Last: {data.LastRefresh:HH:mm}";
        _statusLabel.Appearance.ForeColor = Color.Gray;
    }

    private static Color GetStateColor(MediaItemState state) => state switch
    {
        MediaItemState.New => Color.FromArgb(0, 122, 204),
        MediaItemState.WatchLater => Color.FromArgb(255, 193, 7),
        MediaItemState.InProgress => Color.FromArgb(40, 167, 69),
        MediaItemState.Watched => Color.FromArgb(108, 117, 125),
        MediaItemState.NotInterested => Color.FromArgb(220, 53, 69),
        _ => Color.Gray
    };

    private void OnLayoutDoubleClick(object? sender, EventArgs e)
    {
        var video = GetSelectedVideo();
        if (video != null)
        {
            OpenVideo(video);
        }
    }

    private void OnLayoutKeyDown(object? sender, KeyEventArgs e)
    {
        var video = GetSelectedVideo();
        if (video == null) return;

        switch (e.KeyCode)
        {
            case Keys.Enter:
                OpenVideo(video);
                e.Handled = true;
                break;
            case Keys.L:
                SetVideoState(video, MediaItemState.WatchLater);
                e.Handled = true;
                break;
            case Keys.W:
                SetVideoState(video, MediaItemState.Watched);
                e.Handled = true;
                break;
            case Keys.Delete:
            case Keys.N:
                SetVideoState(video, MediaItemState.NotInterested);
                e.Handled = true;
                break;
        }
    }

    private void OnLayoutMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;

        var video = GetSelectedVideo();
        if (video == null) return;

        var menu = new ContextMenuStrip();

        menu.Items.Add("Open in Browser", null, (s, ev) => OpenVideo(video));
        menu.Items.Add("-");

        if (video.State != MediaItemState.WatchLater)
            menu.Items.Add("Watch Later (L)", null, (s, ev) => SetVideoState(video, MediaItemState.WatchLater));
        if (video.State != MediaItemState.Watched)
            menu.Items.Add("Mark Watched (W)", null, (s, ev) => SetVideoState(video, MediaItemState.Watched));
        if (video.State != MediaItemState.NotInterested)
            menu.Items.Add("Not Interested (N)", null, (s, ev) => SetVideoState(video, MediaItemState.NotInterested));
        if (video.State != MediaItemState.New)
            menu.Items.Add("Mark as New", null, (s, ev) => SetVideoState(video, MediaItemState.New));

        menu.Show(_grid, e.Location);
    }

    private YouTubeVideo? GetSelectedVideo()
    {
        var rowHandle = _layoutView.FocusedRowHandle;
        if (rowHandle < 0 || rowHandle >= _displayedVideos.Count)
            return null;
        return _displayedVideos[rowHandle];
    }

    private void OpenVideo(YouTubeVideo video)
    {
        try
        {
            Process.Start(new ProcessStartInfo(video.WatchUrl) { UseShellExecute = true });

            // Mark as watched if it was new
            if (video.State == MediaItemState.New)
            {
                SetVideoState(video, MediaItemState.Watched);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open video: {Url}", video.WatchUrl);
        }
    }

    private void SetVideoState(YouTubeVideo video, MediaItemState newState)
    {
        _service?.SetVideoState(video.Id, newState);
        video.State = newState;
        _layoutView.RefreshData();
        UpdateStatus();
    }

    private void SetSelectedVideoState(MediaItemState state)
    {
        var video = GetSelectedVideo();
        if (video != null)
        {
            SetVideoState(video, state);
        }
    }

    private void OnFilterChanged(object? sender, EventArgs e)
    {
        if (_service?.CurrentData != null)
        {
            DisplayData(_service.CurrentData);
        }
    }

    private void OnRefreshClick(object? sender, EventArgs e)
    {
        _service?.RefreshNow();
        _statusLabel.Text = "Refreshing...";
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

            // Dispose cached images
            foreach (var image in _thumbnailCache.Values)
            {
                image.Dispose();
            }
            _thumbnailCache.Clear();
        }
        base.Dispose(disposing);
    }
}
