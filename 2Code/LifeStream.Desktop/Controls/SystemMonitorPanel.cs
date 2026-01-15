using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using DevExpress.XtraCharts;
using DevExpress.XtraEditors;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using LifeStream.Desktop.Services.SystemMonitor;
using Serilog;

namespace LifeStream.Desktop.Controls;

/// <summary>
/// Panel control for displaying system performance metrics with real-time charts.
/// Shows CPU, Memory, Disk I/O, and Network I/O in four side-by-side charts.
/// </summary>
public class SystemMonitorPanel : XtraUserControl
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.UI);

    // Charts
    private ChartControl _cpuChart = null!;
    private ChartControl _memoryChart = null!;
    private ChartControl _diskChart = null!;
    private ChartControl _networkChart = null!;

    // Labels for current values
    private LabelControl _cpuLabel = null!;
    private LabelControl _memoryLabel = null!;
    private LabelControl _diskLabel = null!;
    private LabelControl _networkLabel = null!;
    private LabelControl _statusLabel = null!;

    // Controls
    private ComboBoxEdit _timeRangeSelector = null!;
    private SimpleButton _refreshButton = null!;

    // Service
    private SystemMonitorService? _service;
    private int _displaySamples = 60; // Default: 1 minute
    private bool _updatingCharts;

    // Colors
    private static readonly Color SystemColor = Color.FromArgb(70, 130, 220);   // Blue
    private static readonly Color AppColor = Color.FromArgb(80, 180, 80);       // Green
    private static readonly Color WriteColor = Color.FromArgb(220, 140, 50);    // Orange

    public SystemMonitorPanel()
    {
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8)
        };

        // Row styles: Header (auto), Charts with labels (fill), Status (auto)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // Charts with labels
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Status

        // Header row
        var headerPanel = new Panel { Dock = DockStyle.Fill, Height = 30 };

        var titleLabel = new LabelControl
        {
            Text = "System Monitor",
            Dock = DockStyle.Left,
            AutoSizeMode = LabelAutoSizeMode.Default,
            Appearance = { Font = new Font("Segoe UI", 11, FontStyle.Bold) }
        };

        _timeRangeSelector = new ComboBoxEdit
        {
            Dock = DockStyle.Left,
            Width = 100,
            Properties = { TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor }
        };
        _timeRangeSelector.Properties.Items.AddRange(new object[] { "1 min", "5 min", "15 min", "1 hour" });
        _timeRangeSelector.SelectedIndex = 0;
        _timeRangeSelector.SelectedIndexChanged += OnTimeRangeChanged;

        _refreshButton = new SimpleButton
        {
            Text = "Refresh",
            Dock = DockStyle.Right,
            Width = 70
        };
        _refreshButton.Click += OnRefreshClick;

        headerPanel.Controls.Add(_timeRangeSelector);
        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(_refreshButton);
        mainLayout.Controls.Add(headerPanel, 0, 0);

        // Create value labels (will be added to chart rows)
        _cpuLabel = CreateValueLabel("CPU: --");
        _memoryLabel = CreateValueLabel("Memory: --");
        _diskLabel = CreateValueLabel("Disk: --");
        _networkLabel = CreateValueLabel("Network: --");

        // Charts panel - 4 rows stacked vertically for better time series display
        var chartsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,  // Label column + Chart column
            RowCount = 4
        };
        chartsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));  // Fixed width for labels
        chartsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // Chart takes remaining space
        chartsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        chartsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        chartsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        chartsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

        _cpuChart = CreateChart("CPU %");
        _memoryChart = CreateChart("Memory");
        _diskChart = CreateChart("Disk KB/s");
        _networkChart = CreateChart("Network KB/s");

        // Row 0: CPU
        chartsPanel.Controls.Add(_cpuLabel, 0, 0);
        chartsPanel.Controls.Add(_cpuChart, 1, 0);

        // Row 1: Memory
        chartsPanel.Controls.Add(_memoryLabel, 0, 1);
        chartsPanel.Controls.Add(_memoryChart, 1, 1);

        // Row 2: Disk
        chartsPanel.Controls.Add(_diskLabel, 0, 2);
        chartsPanel.Controls.Add(_diskChart, 1, 2);

        // Row 3: Network
        chartsPanel.Controls.Add(_networkLabel, 0, 3);
        chartsPanel.Controls.Add(_networkChart, 1, 3);

        mainLayout.Controls.Add(chartsPanel, 0, 1);

        // Status row
        _statusLabel = new LabelControl
        {
            Text = "Not connected",
            Dock = DockStyle.Fill,
            AutoSizeMode = LabelAutoSizeMode.None,
            Appearance = { ForeColor = Color.Gray }
        };
        mainLayout.Controls.Add(_statusLabel, 0, 2);

        Controls.Add(mainLayout);
    }

    private LabelControl CreateValueLabel(string text)
    {
        return new LabelControl
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoSizeMode = LabelAutoSizeMode.None,
            Appearance =
            {
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center }
            }
        };
    }

    private ChartControl CreateChart(string title)
    {
        var chart = new ChartControl
        {
            Dock = DockStyle.Fill,
            BorderOptions = { Visibility = DevExpress.Utils.DefaultBoolean.False }
        };

        // Remove legend (we show values in labels)
        chart.Legend.Visibility = DevExpress.Utils.DefaultBoolean.False;

        // Add title
        chart.Titles.Add(new ChartTitle { Text = title, Font = new Font("Segoe UI", 9) });

        // Disable animations for performance
        chart.AnimationStartMode = ChartAnimationMode.OnLoad;

        return chart;
    }

    private void ConfigureChartDiagram(ChartControl chart, bool fixedYAxis, double maxY = 100)
    {
        // Diagram is created automatically when series are added
        if (chart.Diagram is XYDiagram diagram)
        {
            // Configure X axis (time)
            diagram.AxisX.Visibility = DevExpress.Utils.DefaultBoolean.False;
            diagram.AxisX.GridLines.Visible = false;

            // Configure Y axis
            diagram.AxisY.GridLines.Visible = true;
            diagram.AxisY.GridLines.Color = Color.FromArgb(50, 128, 128, 128);

            if (fixedYAxis)
            {
                diagram.AxisY.WholeRange.Auto = false;
                diagram.AxisY.WholeRange.SetMinMaxValues(0, maxY);
                diagram.AxisY.VisualRange.Auto = false;
                diagram.AxisY.VisualRange.SetMinMaxValues(0, maxY);
            }
        }
    }

    private void AddSeriesToChart(ChartControl chart, string name, Color color, bool isSecondary = false)
    {
        var series = new Series(name, ViewType.Line);

        var view = (LineSeriesView)series.View;
        view.Color = color;
        view.LineStyle.Thickness = isSecondary ? 1 : 2;
        view.MarkerVisibility = DevExpress.Utils.DefaultBoolean.False;

        chart.Series.Add(series);
    }

    /// <summary>
    /// Binds this panel to a System Monitor service.
    /// </summary>
    public void BindToService(SystemMonitorService service)
    {
        Log.Information("SystemMonitorPanel.BindToService called");

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

            Log.Information("SystemMonitorPanel subscribed to service events");

            // Initialize charts with series
            InitializeChartSeries();

            UpdateStatus();
        }
    }

    private void InitializeChartSeries()
    {
        // Clear existing series
        _cpuChart.Series.Clear();
        _memoryChart.Series.Clear();
        _diskChart.Series.Clear();
        _networkChart.Series.Clear();

        // CPU chart: System and App (fixed 0-100% axis)
        AddSeriesToChart(_cpuChart, "System", SystemColor);
        AddSeriesToChart(_cpuChart, "App", AppColor, isSecondary: true);
        ConfigureChartDiagram(_cpuChart, fixedYAxis: true, maxY: 100);

        // Memory chart: Used % and App MB (auto-scale)
        AddSeriesToChart(_memoryChart, "Used %", SystemColor);
        AddSeriesToChart(_memoryChart, "App MB", AppColor, isSecondary: true);
        ConfigureChartDiagram(_memoryChart, fixedYAxis: false);

        // Disk chart: Read and Write (auto-scale)
        AddSeriesToChart(_diskChart, "Read", SystemColor);
        AddSeriesToChart(_diskChart, "Write", WriteColor, isSecondary: true);
        ConfigureChartDiagram(_diskChart, fixedYAxis: false);

        // Network chart: Received and Sent (auto-scale)
        AddSeriesToChart(_networkChart, "Received", SystemColor);
        AddSeriesToChart(_networkChart, "Sent", WriteColor, isSecondary: true);
        ConfigureChartDiagram(_networkChart, fixedYAxis: false);
    }

    private void OnDataReceived(object? sender, ServiceDataEventArgs e)
    {
        if (e.Data is not SystemMetrics metrics)
            return;

        // Update on UI thread
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => UpdateDisplay(metrics)));
        }
        else
        {
            UpdateDisplay(metrics);
        }
    }

    private void UpdateDisplay(SystemMetrics metrics)
    {
        if (_updatingCharts || _service == null)
            return;

        _updatingCharts = true;
        try
        {
            // Update value labels
            _cpuLabel.Text = $"CPU: Sys {metrics.SystemCpuPercent:F0}% / App {metrics.AppCpuPercent:F1}%";
            _memoryLabel.Text = $"Mem: {metrics.MemoryUsedPercent:F0}% (App {metrics.AppWorkingSetMB} MB)";
            _diskLabel.Text = $"Disk: R {FormatKBps(metrics.DiskReadKBps)} / W {FormatKBps(metrics.DiskWriteKBps)}";
            _networkLabel.Text = $"Net: In {FormatKBps(metrics.NetworkReceivedKBps)} / Out {FormatKBps(metrics.NetworkSentKBps)}";

            // Update charts with historical data
            var samples = _service.GetRecentSamples(_displaySamples);
            if (samples.Count > 0)
            {
                UpdateChartData(_cpuChart, samples,
                    m => (double)m.SystemCpuPercent,
                    m => (double)m.AppCpuPercent);

                UpdateChartData(_memoryChart, samples,
                    m => (double)m.MemoryUsedPercent,
                    m => (double)m.AppWorkingSetMB);

                UpdateChartData(_diskChart, samples,
                    m => (double)m.DiskReadKBps,
                    m => (double)m.DiskWriteKBps);

                UpdateChartData(_networkChart, samples,
                    m => (double)m.NetworkReceivedKBps,
                    m => (double)m.NetworkSentKBps);
            }

            UpdateStatus();
        }
        finally
        {
            _updatingCharts = false;
        }
    }

    private void UpdateChartData(ChartControl chart, List<SystemMetrics> samples,
        Func<SystemMetrics, double> getValue1, Func<SystemMetrics, double> getValue2)
    {
        if (chart.Series.Count < 2)
            return;

        // Create data points using index as X value for simplicity
        var points1 = new SeriesPoint[samples.Count];
        var points2 = new SeriesPoint[samples.Count];

        for (int i = 0; i < samples.Count; i++)
        {
            points1[i] = new SeriesPoint(i, getValue1(samples[i]));
            points2[i] = new SeriesPoint(i, getValue2(samples[i]));
        }

        // Update series
        chart.Series[0].Points.Clear();
        chart.Series[0].Points.AddRange(points1);

        chart.Series[1].Points.Clear();
        chart.Series[1].Points.AddRange(points2);
    }

    private string FormatKBps(float kbps)
    {
        if (kbps >= 1024)
            return $"{kbps / 1024:F1} MB/s";
        return $"{kbps:F0} KB/s";
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
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
            {
                _statusLabel.Text = $"Error: {e.Message}";
                _statusLabel.Appearance.ForeColor = Color.OrangeRed;
            }));
        }
        else
        {
            _statusLabel.Text = $"Error: {e.Message}";
            _statusLabel.Appearance.ForeColor = Color.OrangeRed;
        }
    }

    private void OnTimeRangeChanged(object? sender, EventArgs e)
    {
        _displaySamples = _timeRangeSelector.SelectedIndex switch
        {
            0 => 60,    // 1 min
            1 => 300,   // 5 min
            2 => 900,   // 15 min
            3 => 3600,  // 1 hour
            _ => 60
        };

        // Force refresh with new time range
        if (_service?.CurrentMetrics != null)
        {
            UpdateDisplay(_service.CurrentMetrics);
        }
    }

    private void OnRefreshClick(object? sender, EventArgs e)
    {
        if (_service?.CurrentMetrics != null)
        {
            UpdateDisplay(_service.CurrentMetrics);
        }
    }

    private void UpdateStatus()
    {
        if (_service == null)
        {
            _statusLabel.Text = "Not connected";
            _statusLabel.Appearance.ForeColor = Color.Gray;
            return;
        }

        var statusParts = new List<string>();

        statusParts.Add($"Samples: {_service.SampleCount:N0}/{_service.BufferCapacity:N0}");
        statusParts.Add($"Interval: {_service.CollectionInterval.TotalSeconds:F0}s");

        if (_service.CurrentMetrics != null)
        {
            statusParts.Add($"Pages/sec: {_service.CurrentMetrics.MemoryPagesPerSec:F0}");
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
