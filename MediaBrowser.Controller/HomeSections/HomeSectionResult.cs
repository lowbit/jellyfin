using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.HomeSections;

namespace MediaBrowser.Controller.HomeSections;

/// <summary>
/// One row built by a provider, before the server has given it an identity.
/// </summary>
/// <remarks>
/// The row's id and provider key are filled in by the server, so a provider only describes what
/// to draw. See <see cref="HomeSectionDto"/> for the shape a client receives.
/// </remarks>
public sealed class HomeSectionResult
{
    /// <summary>
    /// Gets the heading to draw above the row.
    /// </summary>
    public required string DisplayText { get; init; }

    /// <summary>
    /// Gets the card shape the client should use.
    /// </summary>
    public required HomeSectionViewType ViewType { get; init; }

    /// <summary>
    /// Gets the item the row is about, when there is one.
    /// </summary>
    /// <remarks>
    /// Becomes part of the row's id, so two rows from the same provider must not share one.
    /// </remarks>
    public Guid? ParentId { get; init; }

    /// <summary>
    /// Gets the kind of item <see cref="ParentId"/> refers to.
    /// </summary>
    /// <remarks>
    /// Set it whenever <see cref="ParentId"/> is, so a client can link the heading to the item
    /// without knowing what the provider is about.
    /// </remarks>
    public BaseItemKind? ParentType { get; init; }

    /// <summary>
    /// Gets the items to display, in order.
    /// </summary>
    public required IReadOnlyList<BaseItemDto> Items { get; init; }
}
