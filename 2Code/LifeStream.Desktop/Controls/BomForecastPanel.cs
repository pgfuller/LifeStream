using System;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using LifeStream.Desktop.Services.BomForecast;
using Serilog;

namespace LifeStream.Desktop.Controls;

/// <summary>
/// Panel control for displaying BOM weather forecast.
/// Shows current conditions and 7-day outlook.
/// </summary>
public class BomForecastPanel : XtraUserControl
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.UI);

    private LabelControl _titleLabel = null!;
    private LabelControl _todayLabel = null!;
    private LabelControl _todayTempLabel = null!;
    private LabelControl _todaySummaryLabel = null!;
    private LabelControl _todayDetailsLabel = null!;
    private GridControl _forecastGrid = null!;
    private GridView _forecastView = null!;
    private LabelControl _statusLabel = null!;
    private SimpleButton _refreshButton = null!;

    private BomForecastService? _service;

    public BomForecastPanel()
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

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Title
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); // Today's forecast
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 7-day grid
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Status

        // Title row
        var titlePanel = new Panel { Dock = DockStyle.Fill, Height = 30 };

        _titleLabel = new LabelControl
        {
            Text = "Weather Forecast",
            Dock = DockStyle.Left,
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 11, FontStyle.Bold) }
        };

        _refreshButton = new SimpleButton
        {
            Text = "Refresh",
            Dock = DockStyle.Right,
            Width = 70
        };
        _refreshButton.Click += OnRefreshClick;

        titlePanel.Controls.Add(_titleLabel);
        titlePanel.Controls.Add(_refreshButton);
        layout.Controls.Add(titlePanel, 0, 0);

        // Today's forecast panel
        var todayPanel = new Panel { Dock = DockStyle.Fill };

        _todayLabel = new LabelControl
        {
            Text = "Today",
            Location = new Point(0, 5),
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 10, FontStyle.Bold) }
        };

        _todayTempLabel = new LabelControl
        {
            Text = "--° / --°",
            Location = new Point(0, 28),
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 14, FontStyle.Bold) }
        };

        _todaySummaryLabel = new LabelControl
        {
            Text = "",
            Location = new Point(100, 5),
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 10) }
        };

        _todayDetailsLabel = new LabelControl
        {
            Text = "",
            Location = new Point(100, 28),
            AutoSizeMode = LabelAutoSizeMode.None,
            Size = new Size(300, 40),
            Appearance = { Font = new Font("Segoe UI", 9), ForeColor = Color.Gray }
        };

        todayPanel.Controls.Add(_todayLabel);
        todayPanel.Controls.Add(_todayTempLabel);
        todayPanel.Controls.Add(_todaySummaryLabel);
        todayPanel.Controls.Add(_todayDetailsLabel);
        layout.Controls.Add(todayPanel, 0, 1);

        // 7-day forecast grid
        _forecastGrid = new GridControl { Dock = DockStyle.Fill };
        _forecastView = new GridView(_forecastGrid)
        {
            OptionsView =
            {
                ShowGroupPanel = false,
                ShowIndicator = false,
                ColumnAutoWidth = true
            },
            OptionsSelection =
            {
                EnableAppearanceFocusedCell = false
            },
            RowHeight = 24
        };
        _forecastGrid.MainView = _forecastView;

        _forecastView.Columns.AddRange(new[]
        {
            new GridColumn { FieldName = "DayName", Caption = "Day", VisibleIndex = 0, Width = 80 },
            new GridColumn { FieldName = "TemperatureRange", Caption = "Temp", VisibleIndex = 1, Width = 80 },
            new GridColumn { FieldName = "Summary", Caption = "Conditions", VisibleIndex = 2, Width = 150 },
            new GridColumn { FieldName = "PrecipitationDisplay", Caption = "Rain", VisibleIndex = 3, Width = 80 }
        });

        layout.Controls.Add(_forecastGrid, 0, 2);

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
    }

    /// <summary>
    /// Binds this panel to a BOM forecast service.
    /// </summary>
    public void BindToService(BomForecastService service)
    {
        Log.Information("BomForecastPanel.BindToService called for {Location}", service.Location.Name);

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

            _titleLabel.Text = $"{_service.Location.Name} Forecast";

            if (_service.CurrentForecast != null)
            {
                DisplayForecast(_service.CurrentForecast);
            }

            UpdateStatus();
        }
    }

    private void OnDataReceived(object? sender, ServiceDataEventArgs e)
    {
        if (e.Data is ForecastData forecast)
        {
            DisplayForecast(forecast);
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

    private void DisplayForecast(ForecastData forecast)
    {
        // Update today's forecast
        var today = forecast.Today;
        if (today != null)
        {
            _todayTempLabel.Text = today.TemperatureRange;
            _todaySummaryLabel.Text = today.Summary;

            var details = new System.Collections.Generic.List<string>();
            if (today.PrecipitationChance.HasValue)
                details.Add($"Rain: {today.PrecipitationChance}%");
            if (!string.IsNullOrEmpty(today.RainfallRange))
                details.Add(today.RainfallRange);
            if (!string.IsNullOrEmpty(today.UvAlert))
                details.Add($"UV: {today.UvAlert}");

            _todayDetailsLabel.Text = string.Join(" | ", details);
        }

        // Update 7-day grid
        _forecastGrid.DataSource = forecast.Days;
        _forecastView.RefreshData();

        // Update title with location
        _titleLabel.Text = $"{forecast.Location} Forecast";
    }

    private void UpdateStatus()
    {
        if (_service?.CurrentForecast == null)
        {
            _statusLabel.Text = "No data";
            return;
        }

        var forecast = _service.CurrentForecast;
        var statusParts = new System.Collections.Generic.List<string>();

        statusParts.Add($"Issued: {forecast.IssuedAt:HH:mm}");
        statusParts.Add($"Fetched: {forecast.FetchedAt:HH:mm}");

        if (_service.NextRefresh.HasValue)
        {
            statusParts.Add($"Next: {_service.NextRefresh:HH:mm}");
        }

        _statusLabel.Text = string.Join(" | ", statusParts);
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
        }
        base.Dispose(disposing);
    }
}
