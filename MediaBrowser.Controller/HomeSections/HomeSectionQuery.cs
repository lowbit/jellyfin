using System;
using System.Collections.Generic;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Dto;

namespace MediaBrowser.Controller.HomeSections;

/// <summary>
/// What a provider is given to build the rows of one configured section.
/// </summary>
public class HomeSectionQuery
{
    /// <summary>
    /// Gets the user the rows are for.
    /// </summary>
    public required User User { get; init; }

    /// <summary>
    /// Gets the items the section is bound to: one for a provider that takes one, any number
    /// for a provider that takes several, none otherwise.
    /// </summary>
    public IReadOnlyList<Guid> ItemIds { get; init; } = [];

    /// <summary>
    /// Gets the item the section is bound to, for a provider that takes one.
    /// </summary>
    public Guid? ItemId => ItemIds.Count > 0 ? ItemIds[0] : null;

    /// <summary>
    /// Gets the maximum number of items per row.
    /// </summary>
    /// <remarks>
    /// The section's own limit when it has one, otherwise the limit the client asked for.
    /// </remarks>
    public int Limit { get; init; }

    /// <summary>
    /// Gets the options to build item DTOs with.
    /// </summary>
    /// <remarks>
    /// Shared by every row so that cards render alike. Pass it to <c>IDtoService</c> unchanged.
    /// </remarks>
    public required DtoOptions DtoOptions { get; init; }
}
