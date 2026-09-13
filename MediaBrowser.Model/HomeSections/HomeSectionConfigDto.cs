using System;

namespace MediaBrowser.Model.HomeSections;

/// <summary>
/// One row of a user's home screen layout, without its items.
/// </summary>
/// <remarks>
/// This is the editable shape. <see cref="HomeSectionDto"/> is the rendered shape and carries the
/// items, so a client configuring the layout does not have to download the content to do it.
/// </remarks>
public class HomeSectionConfigDto
{
    /// <summary>
    /// Gets or sets the key of the provider that builds this section.
    /// </summary>
    /// <remarks>
    /// One of the keys returned by <c>GET /HomeSections/Providers</c>.
    /// </remarks>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the item this section is bound to.
    /// </summary>
    /// <remarks>
    /// Required when the provider declares an <see cref="HomeSectionProviderDto.ItemKind"/>,
    /// ignored otherwise.
    /// </remarks>
    public Guid? ItemId { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of items, or null for the server default.
    /// </summary>
    public int? MaxItems { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the section is shown.
    /// </summary>
    public bool Active { get; set; } = true;
}
