using System;
using System.Reflection;

namespace LifeStream.Core.Infrastructure;

/// <summary>
/// Provides application metadata from the entry assembly.
/// </summary>
public static class AppInfo
{
    private static Assembly? _entryAssembly;
    private static string? _productName;
    private static string? _version;
    private static string? _title;
    private static string? _company;
    private static string? _copyright;

    /// <summary>
    /// The entry assembly (main executable).
    /// </summary>
    public static Assembly EntryAssembly => _entryAssembly ??= Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

    /// <summary>
    /// Product name from assembly metadata (e.g., "LifeStream").
    /// </summary>
    public static string ProductName => _productName ??= GetAttribute<AssemblyProductAttribute>()?.Product ?? "LifeStream";

    /// <summary>
    /// Application title from assembly metadata.
    /// </summary>
    public static string Title => _title ??= GetAttribute<AssemblyTitleAttribute>()?.Title ?? ProductName;

    /// <summary>
    /// Version string with 'v' prefix (e.g., "v0.1.0").
    /// </summary>
    public static string Version => _version ??= $"v{VersionNumber}";

    /// <summary>
    /// Version number without prefix (e.g., "0.1.0").
    /// </summary>
    public static string VersionNumber => EntryAssembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Full version including build number (e.g., "0.1.0.0").
    /// </summary>
    public static string FullVersion => EntryAssembly.GetName().Version?.ToString() ?? "0.0.0.0";

    /// <summary>
    /// Company name from assembly metadata.
    /// </summary>
    public static string Company => _company ??= GetAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;

    /// <summary>
    /// Copyright notice from assembly metadata.
    /// </summary>
    public static string Copyright => _copyright ??= GetAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    /// <summary>
    /// Formatted application title with version (e.g., "LifeStream v0.1.0").
    /// </summary>
    public static string TitleWithVersion => $"{ProductName} {Version}";

    /// <summary>
    /// Full window title for main form (e.g., "LifeStream v0.1.0 - Personal Life Dashboard").
    /// </summary>
    public static string WindowTitle => $"{TitleWithVersion} - Personal Life Dashboard";

    private static T? GetAttribute<T>() where T : Attribute
    {
        return EntryAssembly.GetCustomAttribute<T>();
    }
}
