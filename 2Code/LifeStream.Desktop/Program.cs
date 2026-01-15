using DevExpress.LookAndFeel;
using DevExpress.Skins;
using LifeStream.Core.Infrastructure;
using LifeStream.Desktop.Forms;
using LifeStream.Desktop.Infrastructure;
using Serilog;

namespace LifeStream.Desktop;

static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // Ensure all application directories exist
        AppPaths.EnsureDirectoriesExist();

        // Initialize logging
        LoggingConfig.ConfigureLogging();

        try
        {
            Log.Information("{AppName} {Version} starting...", AppInfo.ProductName, AppInfo.Version);

            // Initialize XPO data layer (SQLite database)
            XpoDataLayerHelper.Initialize();

            // Initialize DevExpress
            SkinManager.EnableFormSkins();
            SkinManager.EnableMdiFormSkins();

            // Set the dark theme
            UserLookAndFeel.Default.SetSkinStyle(SkinStyle.Office2019Black);

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "LifeStream terminated unexpectedly");
            throw;
        }
        finally
        {
            LoggingConfig.CloseAndFlush();
        }
    }
}
