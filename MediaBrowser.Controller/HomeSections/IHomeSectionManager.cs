using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Model.HomeSections;

namespace MediaBrowser.Controller.HomeSections;

/// <summary>
/// Owns the home screen: which sections a user has, who builds them, and when they go stale.
/// </summary>
public interface IHomeSectionManager
{
    /// <summary>
    /// Occurs when a user's layout changes, carrying the user id.
    /// </summary>
    event EventHandler<Guid>? SectionsChanged;

    /// <summary>
    /// Occurs when some of a user's built sections stop being current.
    /// </summary>
    /// <remarks>
    /// This is the single place staleness is decided. Anything that needs to react, such as the
    /// WebSocket listener, consumes this rather than watching library and user data events itself.
    /// </remarks>
    event EventHandler<HomeSectionInvalidatedEventArgs>? Invalidated;

    /// <summary>
    /// Registers the providers discovered at startup.
    /// </summary>
    /// <param name="providers">The providers, from the server and from plugins.</param>
    void AddParts(IEnumerable<IHomeSectionProvider> providers);

    /// <summary>
    /// Gets every registered provider.
    /// </summary>
    /// <returns>The providers, in registration order.</returns>
    IReadOnlyList<IHomeSectionProvider> GetProviders();

    /// <summary>
    /// Gets the provider behind a key.
    /// </summary>
    /// <param name="key">The provider key, compared case-insensitively.</param>
    /// <returns>The provider, or null when nothing registered claims the key.</returns>
    IHomeSectionProvider? GetProvider(string key);

    /// <summary>
    /// Gets a user's home screen with its items, building it if nothing current is cached.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="client">The client the layout belongs to.</param>
    /// <param name="itemLimit">The number of items per row for sections that set no limit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rows to draw, in display order. Empty rows are left out.</returns>
    Task<IReadOnlyList<HomeSectionDto>> GetHomeSectionsAsync(User user, string client, int itemLimit, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a provider's rows stale.
    /// </summary>
    /// <remarks>
    /// For providers whose data changes for reasons the server cannot see, such as a plugin that
    /// refreshes from a remote service. Library and layout changes already invalidate everything,
    /// and user data changes cover providers that declare
    /// <see cref="IHomeSectionProvider.DependsOnUserData"/>.
    /// </remarks>
    /// <param name="key">The provider key.</param>
    /// <param name="userId">The user affected, or null for every user.</param>
    void Invalidate(string key, Guid? userId = null);

    /// <summary>
    /// Gets a user's stored sections, in display order.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="client">The client name the preferences belong to.</param>
    /// <returns>The configured sections, including inactive ones. Empty if the user has none.</returns>
    IReadOnlyList<HomeSection> GetSections(Guid userId, string client);

    /// <summary>
    /// Gets the sections to actually build for a user.
    /// </summary>
    /// <remarks>
    /// Falls back to the administrator's defaults, then to the built-in layout, so a user who
    /// has never touched their home screen still gets one.
    /// </remarks>
    /// <param name="userId">The user id.</param>
    /// <param name="client">The client name the preferences belong to.</param>
    /// <returns>The sections to build, including inactive ones.</returns>
    IReadOnlyList<HomeSection> GetEffectiveSections(Guid userId, string client);

    /// <summary>
    /// Gets the layout a user with no sections of their own is given.
    /// </summary>
    /// <remarks>
    /// The administrator defaults, or the built-in layout when none are set.
    /// </remarks>
    /// <returns>The default sections, in display order.</returns>
    IReadOnlyList<HomeSection> GetDefaultSections();

    /// <summary>
    /// Replaces a user's sections.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="client">The client name the preferences belong to.</param>
    /// <param name="sections">The sections to store. Order is taken from the list order.</param>
    void SetSections(Guid userId, string client, IReadOnlyList<HomeSection> sections);

    /// <summary>
    /// Replaces the layout users without sections of their own are given.
    /// </summary>
    /// <remarks>
    /// Only affects users who have not configured their own layout. An empty list restores the
    /// built-in layout.
    /// </remarks>
    /// <param name="sections">The default sections. Order is taken from the list order.</param>
    void SetDefaultSections(IReadOnlyList<HomeSection> sections);

    /// <summary>
    /// Removes a user's sections so the server defaults apply again.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="client">The client name the preferences belong to.</param>
    void ResetSections(Guid userId, string client);
}
