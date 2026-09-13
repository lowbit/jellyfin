using System;
using System.Collections.Generic;

namespace MediaBrowser.Controller.HomeSections;

/// <summary>
/// Describes which of a user's home sections stopped being current.
/// </summary>
public sealed class HomeSectionInvalidatedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the user whose sections changed.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Gets a value indicating whether every section should be considered stale.
    /// </summary>
    public bool AllStale { get; init; }

    /// <summary>
    /// Gets the keys of the providers whose rows went stale.
    /// </summary>
    /// <remarks>
    /// Empty when <see cref="AllStale"/> is true. Keys rather than row ids, because the server
    /// knows which provider an event touches without loading every user's layout.
    /// </remarks>
    public IReadOnlyList<string> StaleSectionKeys { get; init; } = Array.Empty<string>();
}
