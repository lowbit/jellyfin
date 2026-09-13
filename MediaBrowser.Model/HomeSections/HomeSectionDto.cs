using System;
using System.Collections.Generic;
using MediaBrowser.Model.Dto;

namespace MediaBrowser.Model.HomeSections;

/// <summary>
/// A single row on the home screen, together with the items it should show.
/// </summary>
/// <remarks>
/// Items are returned inline rather than as a separate request per section. A client that had to
/// fetch the section list first and only then start fetching items would pay two round trips before
/// it could draw anything.
/// </remarks>
public class HomeSectionDto
{
    /// <summary>
    /// Gets or sets a stable identifier for this row.
    /// </summary>
    /// <remarks>
    /// Stable across requests for the same row so a client can track it between refreshes. Rows
    /// with no parent use the provider key; rows bound to an item append it, for example
    /// <c>pinnedcollection-6f2a...</c>.
    /// </remarks>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the key of the provider that built this row.
    /// </summary>
    /// <remarks>
    /// This is a string rather than an enum so that a provider added by a plugin, or by a later
    /// server release, does not break clients generated from the API specification.
    /// </remarks>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the heading to draw above the row.
    /// </summary>
    public string DisplayText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the card shape the client should use.
    /// </summary>
    public HomeSectionViewType ViewType { get; set; }

    /// <summary>
    /// Gets or sets the item the row is about, when there is one.
    /// </summary>
    /// <remarks>
    /// The collection for a pinned collection, the genre for a genre row, the item that was watched
    /// for a recommendation row. Null for rows that are not about one item, such as Continue Watching.
    /// A client can use it to make the heading a link.
    /// </remarks>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the items to display in the row.
    /// </summary>
    public IReadOnlyList<BaseItemDto> Items { get; set; } = Array.Empty<BaseItemDto>();
}
