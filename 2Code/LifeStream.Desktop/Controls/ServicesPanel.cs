using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using LifeStream.Core.Infrastructure;
using LifeStream.Core.Services;
using LifeStream.Desktop.Services;
using Serilog;

namespace LifeStream.Desktop.Controls;

/// <summary>
/// Panel displaying status of all registered services.
/// </summary>
public class ServicesPanel : XtraUserControl
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.UI);

    private GridControl _grid = null!;
    private GridView _gridView = null!;
    private SimpleButton _refreshAllButton = null!;
    private BindingList<ServiceStatusRow> _dataSource = null!;
    private ServiceManager? _serviceManager;
    private System.Windows.Forms.Timer _refreshTimer = null!;

    public ServicesPanel()
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
            RowCount = 2,
            Padding = new Padding(4)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Grid
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // Buttons

        // Data source
        _dataSource = new BindingList<ServiceStatusRow>();

        // Grid control
        _grid = new GridControl
        {
            Dock = DockStyle.Fill,
            DataSource = _dataSource
        };

        _gridView = new GridView(_grid)
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
            }
        };
        _grid.MainView = _gridView;

        // Define columns
        _gridView.Columns.AddRange(new[]
        {
            new GridColumn
            {
                FieldName = nameof(ServiceStatusRow.Name),
                Caption = "Service",
                VisibleIndex = 0,
                Width = 120
            },
            new GridColumn
            {
                FieldName = nameof(ServiceStatusRow.SourceType),
                Caption = "Type",
                VisibleIndex = 1,
                Width = 60
            },
            new GridColumn
            {
                FieldName = nameof(ServiceStatusRow.Status),
                Caption = "Status",
                VisibleIndex = 2,
                Width = 70
            },
            new GridColumn
            {
                FieldName = nameof(ServiceStatusRow.LastRefresh),
                Caption = "Last Refresh",
                VisibleIndex = 3,
                Width = 110,
                DisplayFormat = { FormatType = DevExpress.Utils.FormatType.DateTime, FormatString = "yyyy-MM-dd HH:mm:ss" }
            },
            new GridColumn
            {
                FieldName = nameof(ServiceStatusRow.NextRefresh),
                Caption = "Next Refresh",
                VisibleIndex = 4,
                Width = 110,
                DisplayFormat = { FormatType = DevExpress.Utils.FormatType.DateTime, FormatString = "yyyy-MM-dd HH:mm:ss" }
            },
            new GridColumn
            {
                FieldName = nameof(ServiceStatusRow.Message),
                Caption = "Message",
                VisibleIndex = 5,
                Width = 200
            }
        });

        // Row style based on status
        _gridView.RowStyle += OnGridViewRowStyle;

        layout.Controls.Add(_grid, 0, 0);

        // Button panel
        var buttonPanel = new Panel { Dock = DockStyle.Fill, Height = 30 };

        _refreshAllButton = new SimpleButton
        {
            Text = "Refresh All",
            Dock = DockStyle.Left,
            Width = 100
        };
        _refreshAllButton.Click += OnRefreshAllClick;

        buttonPanel.Controls.Add(_refreshAllButton);
        layout.Controls.Add(buttonPanel, 0, 1);

        Controls.Add(layout);

        // Timer to update status display
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += OnRefreshTimerTick;
    }

    private void OnGridViewRowStyle(object sender, RowStyleEventArgs e)
    {
        if (_gridView.GetRow(e.RowHandle) is ServiceStatusRow row)
        {
            e.Appearance.ForeColor = row.Status switch
            {
                "Running" => Color.Green,
                "Degraded" => Color.Orange,
                "Faulted" => Color.Red,
                "Stopped" => Color.Gray,
                "Starting" => Color.Blue,
                _ => Color.Black
            };
        }
    }

    /// <summary>
    /// Binds this panel to a ServiceManager to display its services.
    /// </summary>
    public void BindToServiceManager(ServiceManager serviceManager)
    {
        Log.Information("ServicesPanel binding to ServiceManager with {Count} services", serviceManager.Services.Count);

        _serviceManager = serviceManager;

        // Subscribe to events from all services
        foreach (var service in _serviceManager.Services)
        {
            service.StatusChanged += OnServiceStatusChanged;
            service.DataReceived += OnServiceDataReceived;
            service.ErrorOccurred += OnServiceErrorOccurred;
        }

        // Initial population
        RefreshServiceList();

        // Start the refresh timer
        _refreshTimer.Start();
    }

    private void RefreshServiceList()
    {
        if (_serviceManager == null) return;

        _dataSource.Clear();

        foreach (var service in _serviceManager.Services)
        {
            _dataSource.Add(new ServiceStatusRow
            {
                ServiceId = service.ServiceId,
                Name = service.Name,
                SourceType = service.SourceType,
                Status = service.Status.ToString(),
                LastRefresh = service.LastRefresh,
                NextRefresh = service.NextRefresh,
                Message = service.LastError ?? (service.IsRunning ? "OK" : "")
            });
        }
    }

    private void UpdateServiceRow(IInformationService service)
    {
        var row = _dataSource.FirstOrDefault(r => r.ServiceId == service.ServiceId);
        if (row != null)
        {
            row.Status = service.Status.ToString();
            row.LastRefresh = service.LastRefresh;
            row.NextRefresh = service.NextRefresh;
            row.Message = service.LastError ?? (service.IsRunning ? "OK" : "");

            // Force grid refresh
            _gridView.RefreshData();
        }
    }

    private void OnServiceStatusChanged(object? sender, ServiceStatusChangedEventArgs e)
    {
        Log.Debug("ServicesPanel: Status changed for {Service}: {Old} -> {New}", e.Service.Name, e.OldStatus, e.NewStatus);
        UpdateServiceRow(e.Service);
    }

    private void OnServiceDataReceived(object? sender, ServiceDataEventArgs e)
    {
        // Skip logging for high-frequency services (SystemMonitor updates every second)
        if (e.Service.SourceType != "SystemMonitor")
        {
            Log.Debug("ServicesPanel: Data received from {Service}", e.Service.Name);
        }
        UpdateServiceRow(e.Service);
    }

    private void OnServiceErrorOccurred(object? sender, ServiceErrorEventArgs e)
    {
        Log.Debug("ServicesPanel: Error from {Service}: {Message}", e.Service.Name, e.Message);
        var row = _dataSource.FirstOrDefault(r => r.ServiceId == e.Service.ServiceId);
        if (row != null)
        {
            row.Message = e.Message;
            _gridView.RefreshData();
        }
    }

    private void OnRefreshAllClick(object? sender, EventArgs e)
    {
        _serviceManager?.RefreshAll();
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        // Periodically refresh the grid to update time displays
        if (_serviceManager != null)
        {
            foreach (var service in _serviceManager.Services)
            {
                UpdateServiceRow(service);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();

            if (_serviceManager != null)
            {
                foreach (var service in _serviceManager.Services)
                {
                    service.StatusChanged -= OnServiceStatusChanged;
                    service.DataReceived -= OnServiceDataReceived;
                    service.ErrorOccurred -= OnServiceErrorOccurred;
                }
            }
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Row data for services grid.
/// </summary>
public class ServiceStatusRow : INotifyPropertyChanged
{
    public string ServiceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); }
    }

    private DateTime? _lastRefresh;
    public DateTime? LastRefresh
    {
        get => _lastRefresh;
        set { _lastRefresh = value; OnPropertyChanged(nameof(LastRefresh)); }
    }

    private DateTime? _nextRefresh;
    public DateTime? NextRefresh
    {
        get => _nextRefresh;
        set { _nextRefresh = value; OnPropertyChanged(nameof(NextRefresh)); }
    }

    private string _message = string.Empty;
    public string Message
    {
        get => _message;
        set { _message = value; OnPropertyChanged(nameof(Message)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
