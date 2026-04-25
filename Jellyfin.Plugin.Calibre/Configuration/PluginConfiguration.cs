using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Calibre;

/// <summary>
/// Plugin configuration persisted as XML by <see cref="MediaBrowser.Common.Plugins.BasePlugin{T}"/>.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the Calibre library path.
    /// </summary>
    public string CalibreLibraryPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Calibre server URL (for host access mode).
    /// </summary>
    public string CalibreServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Calibre username.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Calibre password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the metadata provider is active.
    /// </summary>
    public bool EnableMetadataProvider { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether host access mode is enabled.
    /// </summary>
    public bool UseHostAccessMode { get; set; } = false;

    /// <summary>
    /// Gets or sets the Jellyfin library IDs to sync with Calibre.
    /// Empty list means all book libraries are synced.
    /// </summary>
    public List<string> IncludedLibraryIds { get; set; } = new();
}