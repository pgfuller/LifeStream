using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DevExpress.Xpo;
using DevExpress.XtraBars.Docking;
using LifeStream.Core.Infrastructure;
using LifeStream.Domain.Dashboard;
using Serilog;

namespace LifeStream.Desktop.Infrastructure;

/// <summary>
/// Manages dashboard layouts including save, load, and layout switching.
/// </summary>
public class LayoutManager
{
    private static readonly ILogger Log = LoggingConfig.ForCategory(LoggingConfig.Categories.UI);

    private readonly DockManager _dockManager;

    /// <summary>
    /// Name of the default layout.
    /// </summary>
    public const string DefaultLayoutName = "Default";

    /// <summary>
    /// Name of the minimal layout.
    /// </summary>
    public const string MinimalLayoutName = "Minimal";

    public LayoutManager(DockManager dockManager)
    {
        _dockManager = dockManager ?? throw new ArgumentNullException(nameof(dockManager));
    }

    /// <summary>
    /// Gets all available layouts.
    /// </summary>
    public IEnumerable<DashboardLayout> GetAllLayouts()
    {
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();
        var layouts = new XPCollection<DashboardLayout>(uow);
        layouts.Sorting.Add(new SortProperty(nameof(DashboardLayout.Name), DevExpress.Xpo.DB.SortingDirection.Ascending));
        return layouts.ToList();
    }

    /// <summary>
    /// Gets a layout by name.
    /// </summary>
    public DashboardLayout? GetLayout(string name)
    {
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();
        return uow.FindObject<DashboardLayout>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse("Name = ?", name));
    }

    /// <summary>
    /// Saves the current DockManager state to a layout with the given name.
    /// Creates a new layout or updates an existing one.
    /// </summary>
    public void SaveLayout(string name, string? description = null)
    {
        Log.Information("Saving layout: {LayoutName}", name);

        using var uow = XpoDataLayerHelper.CreateUnitOfWork();

        var layout = uow.FindObject<DashboardLayout>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse("Name = ?", name));

        if (layout == null)
        {
            layout = new DashboardLayout(uow)
            {
                Name = name,
                Description = description,
                CreatedAt = DateTime.Now
            };
        }
        else
        {
            layout.ModifiedAt = DateTime.Now;
            if (description != null)
            {
                layout.Description = description;
            }
        }

        // Serialize the current dock layout to XML
        using var stream = new MemoryStream();
        _dockManager.SaveLayoutToStream(stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        layout.LayoutXml = reader.ReadToEnd();

        uow.CommitChanges();
        Log.Information("Layout saved: {LayoutName}", name);
    }

    /// <summary>
    /// Loads a layout by name and applies it to the DockManager.
    /// </summary>
    public bool LoadLayout(string name)
    {
        Log.Information("Loading layout: {LayoutName}", name);

        using var uow = XpoDataLayerHelper.CreateUnitOfWork();
        var layout = uow.FindObject<DashboardLayout>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse("Name = ?", name));

        if (layout == null)
        {
            Log.Warning("Layout not found: {LayoutName}", name);
            return false;
        }

        if (string.IsNullOrEmpty(layout.LayoutXml))
        {
            Log.Warning("Layout has no XML data: {LayoutName}", name);
            return false;
        }

        try
        {
            using var stream = new MemoryStream();
            using var writer = new StreamWriter(stream);
            writer.Write(layout.LayoutXml);
            writer.Flush();
            stream.Position = 0;
            _dockManager.RestoreLayoutFromStream(stream);
            Log.Information("Layout loaded: {LayoutName}", name);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load layout: {LayoutName}", name);
            return false;
        }
    }

    /// <summary>
    /// Deletes a layout by name.
    /// </summary>
    public bool DeleteLayout(string name)
    {
        Log.Information("Deleting layout: {LayoutName}", name);

        using var uow = XpoDataLayerHelper.CreateUnitOfWork();
        var layout = uow.FindObject<DashboardLayout>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse("Name = ?", name));

        if (layout == null)
        {
            Log.Warning("Layout not found for deletion: {LayoutName}", name);
            return false;
        }

        if (layout.IsBuiltIn)
        {
            Log.Warning("Cannot delete built-in layout: {LayoutName}", name);
            return false;
        }

        uow.Delete(layout);
        uow.CommitChanges();
        Log.Information("Layout deleted: {LayoutName}", name);
        return true;
    }

    /// <summary>
    /// Ensures built-in layouts exist in the database.
    /// </summary>
    public void EnsureBuiltInLayouts()
    {
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();

        // Check if Default layout exists
        var defaultLayout = uow.FindObject<DashboardLayout>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse("Name = ?", DefaultLayoutName));

        if (defaultLayout == null)
        {
            Log.Information("Creating built-in layout: {LayoutName}", DefaultLayoutName);
            defaultLayout = new DashboardLayout(uow)
            {
                Name = DefaultLayoutName,
                Description = "Default dashboard layout with all panels visible",
                IsBuiltIn = true,
                CreatedAt = DateTime.Now
            };
        }

        // Check if Minimal layout exists
        var minimalLayout = uow.FindObject<DashboardLayout>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse("Name = ?", MinimalLayoutName));

        if (minimalLayout == null)
        {
            Log.Information("Creating built-in layout: {LayoutName}", MinimalLayoutName);
            minimalLayout = new DashboardLayout(uow)
            {
                Name = MinimalLayoutName,
                Description = "Minimal layout for half-screen usage",
                IsBuiltIn = true,
                CreatedAt = DateTime.Now
            };
        }

        uow.CommitChanges();
    }

    /// <summary>
    /// Saves the current layout as the default layout.
    /// </summary>
    public void SaveAsDefault()
    {
        SaveLayout(DefaultLayoutName, "Default dashboard layout with all panels visible");
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();
        var layout = uow.FindObject<DashboardLayout>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse("Name = ?", DefaultLayoutName));
        if (layout != null)
        {
            layout.IsBuiltIn = true;
            uow.CommitChanges();
        }
    }

    /// <summary>
    /// Saves the current layout as the minimal layout.
    /// </summary>
    public void SaveAsMinimal()
    {
        SaveLayout(MinimalLayoutName, "Minimal layout for half-screen usage");
        using var uow = XpoDataLayerHelper.CreateUnitOfWork();
        var layout = uow.FindObject<DashboardLayout>(
            DevExpress.Data.Filtering.CriteriaOperator.Parse("Name = ?", MinimalLayoutName));
        if (layout != null)
        {
            layout.IsBuiltIn = true;
            uow.CommitChanges();
        }
    }
}
