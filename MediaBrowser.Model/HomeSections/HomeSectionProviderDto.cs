using Jellyfin.Data.Enums;

namespace MediaBrowser.Model.HomeSections;

/// <summary>
/// A kind of home section that can be added to a layout.
/// </summary>
/// <remarks>
/// Lets a settings screen offer what this server can actually build, including sections that
/// plugins contribute, instead of a list compiled into the client.
/// </remarks>
public class HomeSectionProviderDto
{
    /// <summary>
    /// Gets or sets the key a layout refers to this provider by.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name to show when offering this section.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of item a section must be bound to, or null when it takes none.
    /// </summary>
    /// <remarks>
    /// A settings screen uses this to know what to let the user pick, for example a collection
    /// or a genre.
    /// </remarks>
    public BaseItemKind? ItemKind { get; set; }
}
