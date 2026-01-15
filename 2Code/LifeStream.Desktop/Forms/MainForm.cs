using DevExpress.Utils;
using DevExpress.XtraBars;
using DevExpress.XtraBars.Docking;
using DevExpress.XtraBars.Ribbon;
using DevExpress.XtraEditors;
using LifeStream.Core.Infrastructure;
using LifeStream.Desktop.Controls;
using LifeStream.Desktop.Infrastructure;
using LifeStream.Desktop.Services;
using LifeStream.Desktop.Services.Apod;
using LifeStream.Desktop.Services.BomForecast;
using LifeStream.Desktop.Services.BomRadar;
using Serilog;

namespace LifeStream.Desktop.Forms;

/// <summary>
/// Main application form with docking panel support.
/// </summary>
public partial class MainForm : RibbonForm
{
    private readonly ILogger _log = LoggingConfig.ForCategory(LoggingConfig.Categories.UI);

    private System.ComponentModel.IContainer? components;
    private RibbonControl _ribbon = null!;
    private DockManager _dockManager = null!;
    private LayoutManager _layoutManager = null!;
    private ServiceManager _serviceManager = null!;
    private ApodPanel _apodPanel = null!;
    private BomRadarPanel _radarPanel = null!;
    private BomForecastPanel _forecastPanel = null!;
    private ServicesPanel _servicesPanel = null!;
    private string _currentLayoutName = LayoutManager.DefaultLayoutName;

    public MainForm()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        // Form properties
        Text = AppInfo.WindowTitle;
        Size = new System.Drawing.Size(1400, 900);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;

        // TODO: Investigate DevExpress 22.2 API for increasing title bar font size

        // Initialize Ribbon control (provides title bar and menu structure)
        _ribbon = new RibbonControl();
        _ribbon.ShowApplicationButton = DefaultBoolean.False;
        _ribbon.ShowToolbarCustomizeItem = false;
        _ribbon.ShowExpandCollapseButton = DefaultBoolean.True;
        _ribbon.ShowPageHeadersMode = ShowPageHeadersMode.Show;

        // Customize appearance
        _ribbon.AllowMinimizeRibbon = true;

        // Create Home ribbon page
        var homePage = new RibbonPage("Home");
        _ribbon.Pages.Add(homePage);

        // Dashboard group
        var dashboardGroup = new RibbonPageGroup("Dashboard");
        homePage.Groups.Add(dashboardGroup);

        // Layout buttons
        var layoutDefaultBtn = new BarButtonItem { Caption = "Default Layout" };
        var layoutMinimalBtn = new BarButtonItem { Caption = "Minimal" };
        var saveLayoutBtn = new BarButtonItem { Caption = "Save Layout" };
        dashboardGroup.ItemLinks.Add(layoutDefaultBtn);
        dashboardGroup.ItemLinks.Add(layoutMinimalBtn);
        dashboardGroup.ItemLinks.Add(saveLayoutBtn);

        // Wire up layout button events
        layoutDefaultBtn.ItemClick += (s, e) => LoadLayout(LayoutManager.DefaultLayoutName);
        layoutMinimalBtn.ItemClick += (s, e) => LoadLayout(LayoutManager.MinimalLayoutName);
        saveLayoutBtn.ItemClick += (s, e) => SaveCurrentLayout();

        // View group
        var viewGroup = new RibbonPageGroup("View");
        homePage.Groups.Add(viewGroup);

        var refreshBtn = new BarButtonItem { Caption = "Refresh All" };
        viewGroup.ItemLinks.Add(refreshBtn);
        refreshBtn.ItemClick += (s, e) => _serviceManager?.RefreshAll();

        // Add ribbon to form
        Controls.Add(_ribbon);

        // Initialize dock manager for panel docking
        _dockManager = new DockManager(components)
        {
            Form = this
        };

        // Create bottom panel for Services status (create first for proper docking order)
        var servicesDockPanel = _dockManager.AddPanel(DockingStyle.Bottom);
        servicesDockPanel.Text = "Services";
        servicesDockPanel.Height = 100;

        _servicesPanel = new ServicesPanel { Dock = DockStyle.Fill };
        servicesDockPanel.ControlContainer.Controls.Add(_servicesPanel);

        // Create forecast panel at top
        var forecastDockPanel = _dockManager.AddPanel(DockingStyle.Top);
        forecastDockPanel.Text = "Weather Forecast";
        forecastDockPanel.Height = 160;

        _forecastPanel = new BomForecastPanel { Dock = DockStyle.Fill };
        forecastDockPanel.ControlContainer.Controls.Add(_forecastPanel);

        // Create left panel for weather radar (wider for image display)
        var radarDockPanel = _dockManager.AddPanel(DockingStyle.Left);
        radarDockPanel.Text = "Weather Radar";
        radarDockPanel.Width = 500;

        _radarPanel = new BomRadarPanel { Dock = DockStyle.Fill };
        radarDockPanel.ControlContainer.Controls.Add(_radarPanel);

        // Create right panel for APOD (fill remaining space)
        var apodDockPanel = _dockManager.AddPanel(DockingStyle.Fill);
        apodDockPanel.Text = "Astronomy Picture of the Day";

        _apodPanel = new ApodPanel { Dock = DockStyle.Fill };
        apodDockPanel.ControlContainer.Controls.Add(_apodPanel);

        // Initialize layout manager
        _layoutManager = new LayoutManager(_dockManager);
        _layoutManager.EnsureBuiltInLayouts();

        // NOTE: Skip loading saved layout for now - saved layouts from before don't include new panels
        // TODO: Implement layout versioning or panel discovery to handle layout changes gracefully
        // if (!_layoutManager.LoadLayout(_currentLayoutName))
        // {
        //     _layoutManager.SaveAsDefault();
        // }

        // Save current layout as the new default (with all current panels)
        _layoutManager.SaveAsDefault();

        // Initialize services
        InitializeServices();

        // Log initialization
        _log.Information("MainForm initialized with Ribbon");
    }

    private void InitializeServices()
    {
        _log.Information("Initializing services");

        _serviceManager = new ServiceManager();

        // Register APOD service
        // TODO: Read API key from configuration file
        var apodService = new ApodService(apiKey: "pIqoFfoZ91pHjHvjYRLrMUDdNjxuFAPnPcwwhn0t", catchupDays: 7);
        _serviceManager.RegisterService(apodService);

        // Register BOM Radar service (Sydney - all ranges, default 128km)
        var radarService = new BomRadarService(RadarLocation.Sydney, defaultRange: 128);
        _serviceManager.RegisterService(radarService);

        // Register BOM Forecast service (Sydney)
        var forecastService = new BomForecastService(ForecastLocations.NSW.Sydney);
        _serviceManager.RegisterService(forecastService);

        // Bind panels to services (before start so events are subscribed)
        _apodPanel.BindToService(apodService);
        _radarPanel.BindToService(radarService);
        _forecastPanel.BindToService(forecastService);

        // Start all services
        _serviceManager.StartAll();

        // Bind services panel to service manager (after start so it can show status)
        _servicesPanel.BindToServiceManager(_serviceManager);

        _log.Information("Services initialized and started");
    }

    private void LoadLayout(string layoutName)
    {
        if (_layoutManager.LoadLayout(layoutName))
        {
            _currentLayoutName = layoutName;
            _log.Information("Switched to layout: {LayoutName}", layoutName);
        }
    }

    private void SaveCurrentLayout()
    {
        _layoutManager.SaveLayout(_currentLayoutName);
        _log.Information("Saved current layout: {LayoutName}", _currentLayoutName);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _log.Information("MainForm closing");

        // Stop all services
        try
        {
            _serviceManager?.StopAll();
            _log.Information("Services stopped");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error stopping services");
        }

        // Save current layout before closing
        try
        {
            _layoutManager.SaveLayout(_currentLayoutName);
            _log.Information("Layout saved on close: {LayoutName}", _currentLayoutName);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save layout on close");
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _serviceManager?.Dispose();
            components?.Dispose();
        }
        base.Dispose(disposing);
    }
}
