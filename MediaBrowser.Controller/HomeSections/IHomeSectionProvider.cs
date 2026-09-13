using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;

namespace MediaBrowser.Controller.HomeSections;

/// <summary>
/// Builds one kind of home screen section.
/// </summary>
/// <remarks>
/// <para>
/// Implementations are discovered from the server and from plugins at startup and registered with
/// <see cref="IHomeSectionManager.AddParts"/>, the same way similar items and search providers are.
/// A plugin adds a kind of row by implementing this; the row then shows up in the settings picker
/// of every client that reads <c>GET /HomeSections/Providers</c>, with no client change. Where it
/// sits is the user's choice, like every other section.
/// </para>
/// <para>
/// Instances are created once and shared, so they must be safe to call concurrently.
/// </para>
/// </remarks>
public interface IHomeSectionProvider
{
    /// <summary>
    /// Gets the key a stored layout refers to this provider by.
    /// </summary>
    /// <remarks>
    /// Compared case-insensitively and persisted, so it must never change once released. The
    /// built-in providers use the lower-cased names of the legacy section types. A plugin should
    /// prefix its keys with its own name, for example <c>trakt.trending</c>, so two plugins cannot
    /// collide.
    /// </remarks>
    string Key { get; }

    /// <summary>
    /// Gets the name shown when offering this section in a settings screen.
    /// </summary>
    /// <remarks>
    /// Read on each request, so it may be localized with <c>ILocalizationManager.GetLocalizedString</c>.
    /// </remarks>
    string Name { get; }

    /// <summary>
    /// Gets the kind of item a section must be bound to, or null when it takes none.
    /// </summary>
    /// <remarks>
    /// A section for a provider that declares a kind is rejected without an item id. The settings
    /// screen uses the kind to decide what to let the user pick.
    /// </remarks>
    BaseItemKind? ItemKind { get; }

    /// <summary>
    /// Gets a value indicating whether a section may be bound to several items of
    /// <see cref="ItemKind"/> instead of exactly one.
    /// </summary>
    /// <remarks>
    /// For providers that build one row per item, such as a row per genre. The rows follow the
    /// order of the binding, and bound to nothing the section draws nothing; a settings screen
    /// starts such a section off with everything ticked. Meaningless when <see cref="ItemKind"/>
    /// is null.
    /// </remarks>
    bool AllowsMultipleItems { get; }

    /// <summary>
    /// Gets a value indicating whether the rows change when the user's own data does.
    /// </summary>
    /// <remarks>
    /// Played state, playback position, favourites and ratings. When true, saving user data marks
    /// this provider's rows stale for that user; when false they only go stale with the library,
    /// the layout, or a call to <see cref="IHomeSectionManager.Invalidate"/>. Keep this false for
    /// rows that do not depend on watch state, otherwise every playback progress report rebuilds
    /// them.
    /// </remarks>
    bool DependsOnUserData { get; }

    /// <summary>
    /// Builds the rows for one configured section.
    /// </summary>
    /// <remarks>
    /// Most providers return one row, or none when there is nothing to show; an empty row is never
    /// drawn, so there is no need to return a heading over nothing. A provider may return several
    /// when one configured section naturally expands, such as a row per recently watched item. Each
    /// row that is about a specific item should set <see cref="HomeSectionResult.ParentId"/>, since
    /// that is what tells rows from the same provider apart.
    /// </remarks>
    /// <param name="query">The user, the section's settings and the options to build items with.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rows to show, in order.</returns>
    Task<IReadOnlyList<HomeSectionResult>> GetSectionsAsync(HomeSectionQuery query, CancellationToken cancellationToken);
}
