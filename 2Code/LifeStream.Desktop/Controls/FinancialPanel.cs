using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using DevExpress.XtraCharts;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using LifeStream.Desktop.Services.Financial;
using Serilog;

namespace LifeStream.Desktop.Controls;

/// <summary>
/// Panel control for displaying financial market data.
/// Shows indices, commodities, holdings grid, and price chart.
/// </summary>
public class FinancialPanel : XtraUserControl
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.UI);

    // Colors
    private static readonly Color UpColor = Color.FromArgb(40, 167, 69);    // Green
    private static readonly Color DownColor = Color.FromArgb(220, 53, 69);  // Red
    private static readonly Color NeutralColor = Color.Gray;

    // KPI Labels - Indices
    private LabelControl _asx200Label = null!;
    private LabelControl _asx200ValueLabel = null!;
    private LabelControl _asx200ChangeLabel = null!;
    private LabelControl _allOrdsLabel = null!;
    private LabelControl _allOrdsValueLabel = null!;
    private LabelControl _allOrdsChangeLabel = null!;
    private LabelControl _marketStatusLabel = null!;

    // KPI Labels - Currency
    private LabelControl _audUsdLabel = null!;
    private LabelControl _audUsdValueLabel = null!;
    private LabelControl _audUsdChangeLabel = null!;

    // KPI Labels - Commodities
    private LabelControl _goldLabel = null!;
    private LabelControl _goldValueLabel = null!;
    private LabelControl _goldChangeLabel = null!;
    private LabelControl _silverLabel = null!;
    private LabelControl _silverValueLabel = null!;
    private LabelControl _silverChangeLabel = null!;

    // Portfolio
    private LabelControl _portfolioLabel = null!;
    private LabelControl _portfolioValueLabel = null!;
    private LabelControl _portfolioChangeLabel = null!;

    // Holdings Grid
    private GridControl _holdingsGrid = null!;
    private GridView _holdingsView = null!;

    // Chart
    private ChartControl _chart = null!;
    private ComboBoxEdit _chartSymbolSelector = null!;
    private ComboBoxEdit _chartRangeSelector = null!;

    // Controls
    private SimpleButton _refreshButton = null!;
    private LabelControl _statusLabel = null!;

    // Service
    private FinancialService? _service;
    private bool _isLoadingChart;

    public FinancialPanel()
    {
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8)
        };

        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));  // KPI Cards
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));   // Holdings Grid
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));   // Chart
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Status

        // Row 0: Header
        var headerPanel = CreateHeaderPanel();
        mainLayout.Controls.Add(headerPanel, 0, 0);

        // Row 1: KPI Cards
        var kpiPanel = CreateKpiPanel();
        mainLayout.Controls.Add(kpiPanel, 0, 1);

        // Row 2: Holdings Grid
        var holdingsPanel = CreateHoldingsPanel();
        mainLayout.Controls.Add(holdingsPanel, 0, 2);

        // Row 3: Chart
        var chartPanel = CreateChartPanel();
        mainLayout.Controls.Add(chartPanel, 0, 3);

        // Row 4: Status
        _statusLabel = new LabelControl
        {
            Text = "Not connected",
            Dock = DockStyle.Fill,
            AutoSizeMode = LabelAutoSizeMode.None,
            Appearance = { ForeColor = Color.Gray }
        };
        mainLayout.Controls.Add(_statusLabel, 0, 4);

        Controls.Add(mainLayout);
    }

    private Panel CreateHeaderPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Height = 30 };

        var titleLabel = new LabelControl
        {
            Text = "Financial Markets",
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

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(_refreshButton);

        return panel;
    }

    private Panel CreateKpiPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1
        };

        // Equal width columns
        for (int i = 0; i < 6; i++)
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.67f));

        // ASX 200
        var asx200Card = CreateKpiCard("ASX 200", out _asx200Label, out _asx200ValueLabel, out _asx200ChangeLabel);
        panel.Controls.Add(asx200Card, 0, 0);

        // AUD/USD
        var audUsdCard = CreateKpiCard("AUD/USD", out _audUsdLabel, out _audUsdValueLabel, out _audUsdChangeLabel);
        panel.Controls.Add(audUsdCard, 1, 0);

        // Gold (AUD/oz)
        var goldCard = CreateKpiCard("Gold AUD/oz", out _goldLabel, out _goldValueLabel, out _goldChangeLabel);
        panel.Controls.Add(goldCard, 2, 0);

        // Silver (AUD/kg)
        var silverCard = CreateKpiCard("Silver AUD/kg", out _silverLabel, out _silverValueLabel, out _silverChangeLabel);
        panel.Controls.Add(silverCard, 3, 0);

        // All Ordinaries
        var allOrdsCard = CreateKpiCard("All Ords", out _allOrdsLabel, out _allOrdsValueLabel, out _allOrdsChangeLabel);
        panel.Controls.Add(allOrdsCard, 4, 0);

        // Portfolio
        var portfolioCard = CreateKpiCard("Portfolio", out _portfolioLabel, out _portfolioValueLabel, out _portfolioChangeLabel);
        panel.Controls.Add(portfolioCard, 5, 0);

        return panel;
    }

    private Panel CreateKpiCard(string title, out LabelControl titleLabel, out LabelControl valueLabel, out LabelControl changeLabel)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(4),
            Margin = new Padding(2)
        };

        titleLabel = new LabelControl
        {
            Text = title,
            Location = new Point(4, 4),
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 9), ForeColor = Color.Gray }
        };

        valueLabel = new LabelControl
        {
            Text = "--",
            Location = new Point(4, 24),
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 14, FontStyle.Bold) }
        };

        changeLabel = new LabelControl
        {
            Text = "",
            Location = new Point(4, 52),
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 9) }
        };

        card.Controls.Add(titleLabel);
        card.Controls.Add(valueLabel);
        card.Controls.Add(changeLabel);

        return card;
    }

    private Panel CreateHoldingsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        // Header with market status
        var headerPanel = new Panel { Dock = DockStyle.Top, Height = 25 };

        var holdingsTitle = new LabelControl
        {
            Text = "Holdings",
            Dock = DockStyle.Left,
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 10, FontStyle.Bold) }
        };

        _marketStatusLabel = new LabelControl
        {
            Text = "",
            Dock = DockStyle.Right,
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 9) }
        };

        headerPanel.Controls.Add(holdingsTitle);
        headerPanel.Controls.Add(_marketStatusLabel);

        // Grid
        _holdingsGrid = new GridControl { Dock = DockStyle.Fill };
        _holdingsView = new GridView(_holdingsGrid)
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
        _holdingsGrid.MainView = _holdingsView;

        // Configure columns
        _holdingsView.Columns.AddRange(new[]
        {
            new GridColumn { FieldName = "Symbol", Caption = "Symbol", VisibleIndex = 0, Width = 80 },
            new GridColumn { FieldName = "Name", Caption = "Name", VisibleIndex = 1, Width = 120 },
            new GridColumn { FieldName = "PriceDisplay", Caption = "Price", VisibleIndex = 2, Width = 100 },
            new GridColumn { FieldName = "ChangeDisplay", Caption = "Change", VisibleIndex = 3, Width = 80 },
            new GridColumn { FieldName = "QuantityDisplay", Caption = "Holding", VisibleIndex = 4, Width = 100 },
            new GridColumn { FieldName = "HoldingValueDisplay", Caption = "Value", VisibleIndex = 5, Width = 100 }
        });

        // Row style for up/down colors
        _holdingsView.RowStyle += OnHoldingsRowStyle;

        panel.Controls.Add(_holdingsGrid);
        panel.Controls.Add(headerPanel);

        return panel;
    }

    private Panel CreateChartPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        // Chart header with selectors
        var headerPanel = new Panel { Dock = DockStyle.Top, Height = 30 };

        var chartTitle = new LabelControl
        {
            Text = "Chart",
            Dock = DockStyle.Left,
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 10, FontStyle.Bold) }
        };

        _chartSymbolSelector = new ComboBoxEdit
        {
            Dock = DockStyle.Left,
            Width = 120,
            Properties = { TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor }
        };
        _chartSymbolSelector.Properties.Items.AddRange(new object[]
        {
            "ASX 200", "All Ords", "Gold", "Silver"
        });
        _chartSymbolSelector.SelectedIndex = 0;
        _chartSymbolSelector.SelectedIndexChanged += OnChartSymbolChanged;

        _chartRangeSelector = new ComboBoxEdit
        {
            Dock = DockStyle.Left,
            Width = 80,
            Properties = { TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor }
        };
        _chartRangeSelector.Properties.Items.AddRange(new object[]
        {
            "1D", "1W", "1M", "3M", "1Y", "5Y"
        });
        _chartRangeSelector.SelectedIndex = 2; // Default to 1M
        _chartRangeSelector.SelectedIndexChanged += OnChartRangeChanged;

        headerPanel.Controls.Add(_chartRangeSelector);
        headerPanel.Controls.Add(_chartSymbolSelector);
        headerPanel.Controls.Add(chartTitle);

        // Chart control
        _chart = new ChartControl
        {
            Dock = DockStyle.Fill,
            BorderOptions = { Visibility = DevExpress.Utils.DefaultBoolean.False }
        };
        _chart.Legend.Visibility = DevExpress.Utils.DefaultBoolean.False;

        // Initialize chart with empty series
        InitializeChart();

        panel.Controls.Add(_chart);
        panel.Controls.Add(headerPanel);

        return panel;
    }

    private void InitializeChart()
    {
        _chart.Series.Clear();

        var series = new Series("Price", ViewType.Line);
        var view = (LineSeriesView)series.View;
        view.Color = Color.FromArgb(70, 130, 220);
        view.LineStyle.Thickness = 2;
        view.MarkerVisibility = DevExpress.Utils.DefaultBoolean.False;

        _chart.Series.Add(series);

        // Configure diagram
        if (_chart.Diagram is XYDiagram diagram)
        {
            diagram.AxisX.DateTimeScaleOptions.MeasureUnit = DateTimeMeasureUnit.Day;
            diagram.AxisX.GridLines.Visible = false;
            diagram.AxisY.GridLines.Visible = true;
            diagram.AxisY.GridLines.Color = Color.FromArgb(50, 128, 128, 128);
        }
    }

    /// <summary>
    /// Binds this panel to a Financial service.
    /// </summary>
    public void BindToService(FinancialService service)
    {
        Log.Information("FinancialPanel.BindToService called");

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

            // Add holding symbols to chart selector
            UpdateChartSymbolSelector();

            // Display current data if available
            if (_service.CurrentData != null)
            {
                DisplayData(_service.CurrentData);
            }

            // Load initial chart data
            _ = LoadChartDataAsync();

            UpdateStatus();
        }
    }

    private void UpdateChartSymbolSelector()
    {
        if (_service == null) return;

        _chartSymbolSelector.Properties.Items.Clear();
        _chartSymbolSelector.Properties.Items.AddRange(new object[] { "ASX 200", "All Ords", "Gold", "Silver" });

        // Add holding symbols
        foreach (var holding in _service.HoldingsManager.Holdings)
        {
            if (holding.AssetType == AssetType.Stock)
            {
                _chartSymbolSelector.Properties.Items.Add(holding.Symbol);
            }
        }

        _chartSymbolSelector.SelectedIndex = 0;
    }

    private void OnDataReceived(object? sender, ServiceDataEventArgs e)
    {
        if (e.Data is FinancialData data)
        {
            if (InvokeRequired)
                BeginInvoke(new Action(() => DisplayData(data)));
            else
                DisplayData(data);
        }
        UpdateStatus();
    }

    private void DisplayData(FinancialData data)
    {
        Log.Debug("FinancialPanel.DisplayData called - ASX200: {Asx200}, Gold: {Gold}, Holdings: {Holdings}",
            data.Asx200?.Price, data.Gold?.Price, data.Holdings?.Count ?? 0);

        // Update ASX 200
        if (data.Asx200 != null)
        {
            _asx200ValueLabel.Text = $"{data.Asx200.Price:N2}";
            _asx200ChangeLabel.Text = data.Asx200.ChangeDisplay;
            _asx200ChangeLabel.Appearance.ForeColor = GetChangeColor(data.Asx200.Change);
        }

        // Update AUD/USD
        if (data.AudUsd != null)
        {
            _audUsdValueLabel.Text = $"{data.AudUsd.Price:N4}";
            _audUsdChangeLabel.Text = data.AudUsd.ChangeDisplay;
            _audUsdChangeLabel.Appearance.ForeColor = GetChangeColor(data.AudUsd.Change);
        }

        // Update All Ordinaries
        if (data.AllOrdinaries != null)
        {
            _allOrdsValueLabel.Text = $"{data.AllOrdinaries.Price:N2}";
            _allOrdsChangeLabel.Text = data.AllOrdinaries.ChangeDisplay;
            _allOrdsChangeLabel.Appearance.ForeColor = GetChangeColor(data.AllOrdinaries.Change);
        }

        // Update Gold (AUD per troy ounce)
        if (data.Gold != null)
        {
            _goldValueLabel.Text = $"${data.Gold.Price:N0}";
            _goldChangeLabel.Text = data.Gold.ChangeDisplay;
            _goldChangeLabel.Appearance.ForeColor = GetChangeColor(data.Gold.Change);
        }

        // Update Silver (AUD per kg)
        if (data.Silver != null)
        {
            _silverValueLabel.Text = $"${data.Silver.Price:N0}";
            _silverChangeLabel.Text = data.Silver.ChangeDisplay;
            _silverChangeLabel.Appearance.ForeColor = GetChangeColor(data.Silver.Change);
        }

        // Update Portfolio
        if (data.Portfolio != null)
        {
            _portfolioValueLabel.Text = data.Portfolio.TotalValueDisplay;
            _portfolioChangeLabel.Text = data.Portfolio.DailyChangeDisplay;
            _portfolioChangeLabel.Appearance.ForeColor = GetChangeColor(data.Portfolio.DailyChange);
        }
        else
        {
            _portfolioValueLabel.Text = "--";
            _portfolioChangeLabel.Text = "";
        }

        // Update Market Status
        _marketStatusLabel.Text = $"Market: {data.MarketStatusDisplay}";
        _marketStatusLabel.Appearance.ForeColor = data.IsMarketOpen ? UpColor : NeutralColor;

        // Update Holdings Grid
        _holdingsGrid.DataSource = data.Holdings;
        _holdingsView.RefreshData();
    }

    private void OnHoldingsRowStyle(object sender, RowStyleEventArgs e)
    {
        if (_holdingsView.GetRow(e.RowHandle) is HoldingItem holding)
        {
            if (holding.CurrentQuote != null)
            {
                e.Appearance.ForeColor = holding.IsUp ? UpColor : DownColor;
            }
        }
    }

    private void OnStatusChanged(object? sender, ServiceStatusChangedEventArgs e)
    {
        if (InvokeRequired)
            BeginInvoke(new Action(UpdateStatus));
        else
            UpdateStatus();
    }

    private void OnErrorOccurred(object? sender, ServiceErrorEventArgs e)
    {
        Action updateAction = () =>
        {
            _statusLabel.Text = $"Error: {e.Message}";
            _statusLabel.Appearance.ForeColor = Color.OrangeRed;
        };

        if (InvokeRequired)
            BeginInvoke(updateAction);
        else
            updateAction();
    }

    private void OnRefreshClick(object? sender, EventArgs e)
    {
        _service?.RefreshNow();
        _statusLabel.Text = "Refreshing...";
        _statusLabel.Appearance.ForeColor = Color.Gray;
    }

    private async void OnChartSymbolChanged(object? sender, EventArgs e)
    {
        await LoadChartDataAsync();
    }

    private async void OnChartRangeChanged(object? sender, EventArgs e)
    {
        await LoadChartDataAsync();
    }

    private async Task LoadChartDataAsync()
    {
        if (_service == null || _isLoadingChart) return;

        _isLoadingChart = true;
        try
        {
            // Map selector to symbol
            var selectedText = _chartSymbolSelector.Text;
            var symbol = selectedText switch
            {
                "ASX 200" => "^AXJO",
                "All Ords" => "^AORD",
                "Gold" => "XAU",
                "Silver" => "XAG",
                _ => selectedText // Assume it's a stock symbol
            };

            _service.ChartSymbol = symbol;

            // Map range selector
            var range = _chartRangeSelector.SelectedIndex switch
            {
                0 => ChartTimeRange.OneDay,
                1 => ChartTimeRange.OneWeek,
                2 => ChartTimeRange.OneMonth,
                3 => ChartTimeRange.ThreeMonths,
                4 => ChartTimeRange.OneYear,
                5 => ChartTimeRange.FiveYears,
                _ => ChartTimeRange.OneMonth
            };

            await _service.LoadChartHistoryAsync(range);

            // Update chart with new data
            UpdateChart();
        }
        finally
        {
            _isLoadingChart = false;
        }
    }

    private void UpdateChart()
    {
        if (_service == null || _chart.Series.Count == 0) return;

        var history = _service.ChartHistory;
        if (history.Count == 0) return;

        var series = _chart.Series[0];
        series.Points.Clear();

        // Add points (history is most-recent first, so reverse for chart)
        foreach (var point in history.Reverse())
        {
            series.Points.Add(new SeriesPoint(point.Date, (double)point.Close));
        }

        // Update title
        _chart.Titles.Clear();
        _chart.Titles.Add(new ChartTitle
        {
            Text = _chartSymbolSelector.Text,
            Font = new Font("Segoe UI", 10)
        });
    }

    private void UpdateStatus()
    {
        if (_service?.CurrentData == null)
        {
            _statusLabel.Text = "No data";
            return;
        }

        var data = _service.CurrentData;
        var statusParts = new List<string>();

        statusParts.Add($"Updated: {data.LastRefresh:HH:mm}");

        if (_service.NextRefresh.HasValue)
        {
            statusParts.Add($"Next: {_service.NextRefresh:HH:mm}");
        }

        if (_service.CurrentData != null)
        {
            var providerInfo = _service.CurrentData.ApiCallsRemaining > 0
                ? $"API: {_service.CurrentData.ApiCallsToday}/{_service.CurrentData.ApiCallsRemaining + _service.CurrentData.ApiCallsToday}"
                : "";
            if (!string.IsNullOrEmpty(providerInfo))
                statusParts.Add(providerInfo);
        }

        _statusLabel.Text = string.Join(" | ", statusParts);
        _statusLabel.Appearance.ForeColor = _service.Status switch
        {
            ServiceStatus.Degraded => Color.Orange,
            ServiceStatus.Faulted => Color.Red,
            _ => Color.Gray
        };
    }

    private static Color GetChangeColor(decimal change)
    {
        if (change > 0) return UpColor;
        if (change < 0) return DownColor;
        return NeutralColor;
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
