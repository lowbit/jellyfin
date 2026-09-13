using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.HomeSections;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Server.Implementations.HomeSections.Providers;

/// <summary>
/// One row per chosen genre, in the order they were chosen.
/// </summary>
/// <remarks>
/// One section rather than one per genre, so genre rows are turned on by adding it and off by
/// removing it, and narrowed by unticking genres. A settings screen starts it off with every
/// genre ticked, so adding it takes no further setup; bound to nothing it draws nothing.
/// </remarks>
public sealed class GenreSectionProvider : IHomeSectionProvider
{
    /// <summary>
    /// The television genres that belong in a row bound to a film genre.
    /// </summary>
    /// <remarks>
    /// Films and shows are tagged from different vocabularies, so an Action row bound to the film
    /// genre would otherwise hold no shows at all while a near duplicate Action &amp; Adventure row
    /// sat next to it. These are the English TMDB names; on a server with another metadata
    /// language the map does nothing.
    /// </remarks>
    private static readonly Dictionary<string, string[]> _genreAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Action"] = ["Action & Adventure"],
        ["Adventure"] = ["Action & Adventure"],
        ["Science Fiction"] = ["Sci-Fi & Fantasy"],
        ["Fantasy"] = ["Sci-Fi & Fantasy"],
        ["War"] = ["War & Politics"],
        ["Family"] = ["Kids"]
    };

    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly ILocalizationManager _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenreSectionProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public GenreSectionProvider(ILibraryManager libraryManager, IDtoService dtoService, ILocalizationManager localization)
    {
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _localization = localization;
    }

    /// <inheritdoc />
    public string Key => HomeSectionKeys.Genre;

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("Genres");

    /// <inheritdoc />
    public BaseItemKind? ItemKind => BaseItemKind.Genre;

    /// <inheritdoc />
    public bool AllowsMultipleItems => true;

    /// <inheritdoc />
    public bool DependsOnUserData => false;

    /// <summary>
    /// Gets the genre names a row should query, being the one it is bound to and its television
    /// equivalent.
    /// </summary>
    /// <param name="name">The name of the genre the row is bound to.</param>
    /// <returns>The names to query.</returns>
    public static IReadOnlyList<string> GetGenreNames(string name)
        => _genreAliases.TryGetValue(name, out var aliases)
            ? aliases.Prepend(name).ToArray()
            : [name];

    /// <inheritdoc />
    public Task<IReadOnlyList<HomeSectionResult>> GetSectionsAsync(HomeSectionQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<HomeSectionResult> none = [];

        if (query.ItemIds.Count == 0)
        {
            return Task.FromResult(none);
        }

        var genres = _libraryManager.GetGenres(new InternalItemsQuery(query.User)
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            Recursive = true,
            IsVirtualItem = false,
            DtoOptions = new DtoOptions(false),
            EnableTotalRecordCount = false
        }).Items.ToDictionary(genre => genre.Item.Id, genre => genre.Item);

        var rows = new List<HomeSectionResult>(query.ItemIds.Count);

        // In the order chosen, since that order is the one thing the binding says about how the
        // rows should sit. A genre that no longer exists is passed over rather than drawn empty.
        foreach (var id in query.ItemIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (genres.TryGetValue(id, out var genre))
            {
                rows.Add(BuildRow(genre, query));
            }
        }

        return Task.FromResult<IReadOnlyList<HomeSectionResult>>(rows);
    }

    /// <summary>
    /// Builds the row for one genre: a random pick of its films and shows.
    /// </summary>
    private HomeSectionResult BuildRow(BaseItem genre, HomeSectionQuery query)
    {
        var items = _libraryManager.GetItemsResult(new InternalItemsQuery(query.User)
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series],
            Genres = GetGenreNames(genre.Name),
            OrderBy = [(ItemSortBy.Random, SortOrder.Ascending)],
            Limit = query.Limit,
            Recursive = true,
            DtoOptions = query.DtoOptions,
            IsVirtualItem = false,
            EnableTotalRecordCount = false
        });

        return new HomeSectionResult
        {
            DisplayText = genre.Name,
            ViewType = HomeSectionViewType.Portrait,
            ParentId = genre.Id,
            ParentType = BaseItemKind.Genre,
            Items = _dtoService.GetBaseItemDtos(items.Items, query.DtoOptions, query.User)
        };
    }
}
