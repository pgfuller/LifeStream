using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.Utils;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using LifeStream.Desktop.Services.News;
using Serilog;

namespace LifeStream.Desktop.Controls;

/// <summary>
/// Panel control for displaying news headlines from RSS feeds.
/// </summary>
public class NewsPanel : XtraUserControl
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.UI);

    private GridControl _grid = null!;
    private GridView _gridView = null!;
    private LabelControl _statusLabel = null!;
    private SimpleButton _refreshButton = null!;
    private ComboBoxEdit _filterCombo = null!;
    private SimpleButton _holdButton = null!;
    private SimpleButton _doneButton = null!;
    private SimpleButton _rejectButton = null!;

    private NewsService? _service;
    private List<NewsArticle> _displayedArticles = new();
    private Dictionary<string, string> _sourceNames = new();

    // Colors
    private static readonly Color NewColor = Color.FromArgb(0, 122, 204);      // Blue
    private static readonly Color HoldColor = Color.FromArgb(255, 193, 7);     // Amber
    private static readonly Color DoneColor = Color.FromArgb(108, 117, 125);   // Gray
    private static readonly Color RejectedColor = Color.FromArgb(220, 53, 69); // Red

    public NewsPanel()
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
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Grid
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // Status

        // Header row with filter, state buttons, and refresh
        var headerPanel = new Panel { Dock = DockStyle.Fill, Height = 30 };

        _filterCombo = new ComboBoxEdit
        {
            Dock = DockStyle.Left,
            Width = 100
        };
        _filterCombo.Properties.Items.AddRange(new[] { "All", "New", "Hold", "Done", "Rejected" });
        _filterCombo.SelectedIndex = 0;
        _filterCombo.SelectedIndexChanged += OnFilterChanged;

        // State action buttons (left side, after filter)
        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(10, 0, 0, 0)
        };

        _holdButton = new SimpleButton { Text = "Hold", Width = 50 };
        _holdButton.Click += (s, e) => SetSelectedArticleState(ItemState.Hold);

        _doneButton = new SimpleButton { Text = "Done", Width = 50 };
        _doneButton.Click += (s, e) => SetSelectedArticleState(ItemState.Done);

        _rejectButton = new SimpleButton { Text = "Reject", Width = 55 };
        _rejectButton.Click += (s, e) => SetSelectedArticleState(ItemState.Rejected);

        buttonPanel.Controls.Add(_holdButton);
        buttonPanel.Controls.Add(_doneButton);
        buttonPanel.Controls.Add(_rejectButton);

        _refreshButton = new SimpleButton
        {
            Text = "Refresh",
            Dock = DockStyle.Right,
            Width = 70
        };
        _refreshButton.Click += OnRefreshClick;

        var markSeenButton = new SimpleButton
        {
            Text = "Mark All Seen",
            Dock = DockStyle.Right,
            Width = 90
        };
        markSeenButton.Click += OnMarkAllSeenClick;

        headerPanel.Controls.Add(_filterCombo);
        headerPanel.Controls.Add(buttonPanel);
        headerPanel.Controls.Add(markSeenButton);
        headerPanel.Controls.Add(_refreshButton);
        layout.Controls.Add(headerPanel, 0, 0);

        // Grid for articles
        _grid = new GridControl { Dock = DockStyle.Fill };
        _gridView = new GridView(_grid);
        _grid.MainView = _gridView;

        // Configure grid view
        _gridView.OptionsView.ShowGroupPanel = false;
        _gridView.OptionsView.ShowIndicator = false;
        _gridView.OptionsView.RowAutoHeight = true;
        _gridView.OptionsSelection.EnableAppearanceFocusedCell = false;
        _gridView.OptionsSelection.EnableAppearanceFocusedRow = true;
        _gridView.OptionsMenu.EnableColumnMenu = false;
        _gridView.RowHeight = 28;

        // Columns
        var stateCol = new GridColumn
        {
            FieldName = "StateIcon",
            Caption = "",
            Width = 30,
            MinWidth = 30,
            OptionsColumn = { AllowEdit = false, FixedWidth = true }
        };

        var sourceCol = new GridColumn
        {
            FieldName = "SourceId",
            Caption = "Source",
            Width = 70,
            MinWidth = 70,
            OptionsColumn = { AllowEdit = false, FixedWidth = true }
        };

        var titleCol = new GridColumn
        {
            FieldName = "Title",
            Caption = "Headline",
            Width = 300,
            MinWidth = 150,
            OptionsColumn = { AllowEdit = false }
        };

        var summaryCol = new GridColumn
        {
            FieldName = "Summary",
            Caption = "Summary",
            Width = 400,
            MinWidth = 100,
            OptionsColumn = { AllowEdit = false }
        };

        var ageCol = new GridColumn
        {
            FieldName = "AgeDisplay",
            Caption = "Age",
            Width = 70,
            MinWidth = 70,
            OptionsColumn = { AllowEdit = false, FixedWidth = true }
        };

        _gridView.Columns.AddRange(new[] { stateCol, sourceCol, titleCol, summaryCol, ageCol });

        // Set visible indices for all columns (required for DevExpress to show them)
        stateCol.VisibleIndex = 0;
        sourceCol.VisibleIndex = 1;
        titleCol.VisibleIndex = 2;
        summaryCol.VisibleIndex = 3;
        ageCol.VisibleIndex = 4;

        // Auto-size: let Title and Summary share remaining space, fixed columns stay fixed
        _gridView.OptionsView.ColumnAutoWidth = true;

        // Tooltips for full headline on hover
        _gridView.CalcRowHeight += (s, e) => e.RowHeight = 24;
        _grid.ToolTipController = new DevExpress.Utils.ToolTipController();
        _grid.ToolTipController.GetActiveObjectInfo += OnGetActiveObjectInfo;

        // Row styling
        _gridView.RowStyle += OnRowStyle;
        _gridView.DoubleClick += OnGridDoubleClick;
        _gridView.PopupMenuShowing += OnGridPopupMenu;
        _gridView.KeyDown += OnGridKeyDown;
        _gridView.CustomColumnDisplayText += OnSourceColumnDisplayText;

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

    /// <summary>
    /// Binds this panel to a NewsService to receive updates.
    /// </summary>
    public void BindToService(NewsService service)
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

            // Display current data if available
            if (_service.CurrentData != null)
            {
                DisplayData(_service.CurrentData);
            }
        }
    }

    private void OnDataReceived(object? sender, ServiceDataEventArgs e)
    {
        if (e.Data is NewsData data)
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

    private void DisplayData(NewsData data)
    {
        // Update source names lookup
        _sourceNames = data.Sources.ToDictionary(s => s.Id, s => s.Name);

        // Apply filter
        var filter = _filterCombo.SelectedItem?.ToString() ?? "All";
        _displayedArticles = filter switch
        {
            "New" => data.Articles.Where(a => a.State == ItemState.New).ToList(),
            "Hold" => data.Articles.Where(a => a.State == ItemState.Hold).ToList(),
            "Done" => data.Articles.Where(a => a.State == ItemState.Done).ToList(),
            "Rejected" => data.Articles.Where(a => a.State == ItemState.Rejected).ToList(),
            _ => data.Articles.Where(a => a.State != ItemState.Rejected).ToList() // All excludes rejected
        };

        _grid.DataSource = _displayedArticles;
        _gridView.RefreshData();

        UpdateStatus();
        Log.Debug("NewsPanel displaying {Count} articles", _displayedArticles.Count);
    }

    private void UpdateStatus()
    {
        if (_service?.CurrentData == null) return;

        var data = _service.CurrentData;
        _statusLabel.Text = $"{data.NewCount} new | {data.HoldCount} on hold | {data.Articles.Count} total | Last: {data.LastRefresh:HH:mm}";
        _statusLabel.Appearance.ForeColor = Color.Gray;
    }

    private void OnRowStyle(object sender, RowStyleEventArgs e)
    {
        if (e.RowHandle < 0 || e.RowHandle >= _displayedArticles.Count) return;

        var article = _displayedArticles[e.RowHandle];
        e.Appearance.ForeColor = article.State switch
        {
            ItemState.New => NewColor,
            ItemState.Hold => HoldColor,
            ItemState.Done => DoneColor,
            ItemState.Rejected => RejectedColor,
            _ => Color.Empty
        };

        // Bold for new items
        if (article.State == ItemState.New)
        {
            e.Appearance.Font = new Font(e.Appearance.Font, FontStyle.Bold);
        }
    }

    private void OnGridDoubleClick(object? sender, EventArgs e)
    {
        var article = GetSelectedArticle();
        if (article != null)
        {
            OpenArticle(article);
        }
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        var article = GetSelectedArticle();
        if (article == null) return;

        switch (e.KeyCode)
        {
            case Keys.Enter:
                OpenArticle(article);
                e.Handled = true;
                break;
            case Keys.H:
                SetArticleState(article, ItemState.Hold);
                e.Handled = true;
                break;
            case Keys.D:
                SetArticleState(article, ItemState.Done);
                e.Handled = true;
                break;
            case Keys.R:
            case Keys.Delete:
                SetArticleState(article, ItemState.Rejected);
                e.Handled = true;
                break;
        }
    }

    private void OnGridPopupMenu(object sender, PopupMenuShowingEventArgs e)
    {
        if (e.MenuType != GridMenuType.Row) return;

        var article = GetSelectedArticle();
        if (article == null) return;

        e.Menu.Items.Clear();

        e.Menu.Items.Add(CreateMenuItem("Open in Browser", () => OpenArticle(article)));
        e.Menu.Items.Add(CreateMenuItem("-", null)); // Separator

        if (article.State != ItemState.Hold)
            e.Menu.Items.Add(CreateMenuItem("Hold (H)", () => SetArticleState(article, ItemState.Hold)));
        if (article.State != ItemState.Done)
            e.Menu.Items.Add(CreateMenuItem("Mark Done (D)", () => SetArticleState(article, ItemState.Done)));
        if (article.State != ItemState.Rejected)
            e.Menu.Items.Add(CreateMenuItem("Reject (R)", () => SetArticleState(article, ItemState.Rejected)));
        if (article.State != ItemState.New)
            e.Menu.Items.Add(CreateMenuItem("Mark as New", () => SetArticleState(article, ItemState.New)));
    }

    private DevExpress.Utils.Menu.DXMenuItem CreateMenuItem(string caption, Action? action)
    {
        var item = new DevExpress.Utils.Menu.DXMenuItem(caption);
        if (action != null)
        {
            item.Click += (s, e) => action();
        }
        return item;
    }

    private NewsArticle? GetSelectedArticle()
    {
        var rowHandle = _gridView.FocusedRowHandle;
        if (rowHandle < 0 || rowHandle >= _displayedArticles.Count)
            return null;
        return _displayedArticles[rowHandle];
    }

    private void OpenArticle(NewsArticle article)
    {
        if (string.IsNullOrEmpty(article.Link)) return;

        try
        {
            Process.Start(new ProcessStartInfo(article.Link) { UseShellExecute = true });

            // Mark as seen if it was new
            if (article.State == ItemState.New)
            {
                SetArticleState(article, ItemState.Seen);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open article: {Link}", article.Link);
        }
    }

    private void SetArticleState(NewsArticle article, ItemState newState)
    {
        _service?.SetArticleState(article.Id, newState);
        article.State = newState;
        _gridView.RefreshData();
        UpdateStatus();
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

    private void OnMarkAllSeenClick(object? sender, EventArgs e)
    {
        _service?.MarkAllSeen();
        if (_service?.CurrentData != null)
        {
            DisplayData(_service.CurrentData);
        }
    }

    private void SetSelectedArticleState(ItemState state)
    {
        var article = GetSelectedArticle();
        if (article != null)
        {
            SetArticleState(article, state);
        }
    }

    private void OnSourceColumnDisplayText(object sender, DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventArgs e)
    {
        // Only format the SourceId column
        if (e.Column.FieldName != "SourceId") return;

        if (e.Value is string sourceId && _sourceNames.TryGetValue(sourceId, out var name))
        {
            // Use short name for display
            e.DisplayText = name switch
            {
                "ABC News Australia" => "ABC",
                "BBC News" => "BBC",
                "Sydney Morning Herald" => "SMH",
                _ => name.Length > 10 ? name[..10] : name
            };
        }
    }

    private void OnGetActiveObjectInfo(object sender, DevExpress.Utils.ToolTipControllerGetActiveObjectInfoEventArgs e)
    {
        if (e.SelectedControl != _grid) return;

        var view = _grid.FocusedView as GridView;
        if (view == null) return;

        var hitInfo = view.CalcHitInfo(e.ControlMousePosition);
        if (hitInfo.InRowCell && hitInfo.RowHandle >= 0 && hitInfo.RowHandle < _displayedArticles.Count)
        {
            var article = _displayedArticles[hitInfo.RowHandle];
            var tooltipText = $"{article.Title}\n\n{article.Summary}";

            if (_sourceNames.TryGetValue(article.SourceId, out var sourceName))
            {
                tooltipText = $"[{sourceName}] {article.AgeDisplay}\n\n{article.Title}\n\n{article.Summary}";
            }

            e.Info = new DevExpress.Utils.ToolTipControlInfo(hitInfo, tooltipText)
            {
                ToolTipType = ToolTipType.SuperTip
            };
        }
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
        }
        base.Dispose(disposing);
    }
}
