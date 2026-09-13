using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.HomeSections;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Server.Implementations.HomeSections.Providers;

/// <summary>
/// One row of similar items for each film or series the user recently finished.
/// </summary>
/// <remarks>
/// Built on the same similar items providers as the details page, so a plugin that improves
/// those improves this. Seeds come from watch history, most recent first; a series counts once
/// however many of its episodes were played. A candidate the user has already seen, has started,
/// or that an earlier row of this section already holds is left out, and a seed whose row would
/// be nearly empty after that is skipped in favour of the next one.
/// </remarks>
public sealed class BecauseYouWatchedSectionProvider : IHomeSectionProvider
{
    /// <summary>
    /// How many rows one configured section expands to.
    /// </summary>
    private const int MaxRows = 3;

    /// <summary>
    /// How many recently played items to look through for seeds.
    /// </summary>
    /// <remarks>
    /// Episodes of one series collapse into one seed, so this has to be well above the number of
    /// rows for someone who mostly watches series.
    /// </remarks>
    private const int PlayedItemsToScan = 40;

    /// <summary>
    /// The fewest items worth a row of its own.
    /// </summary>
    private const int MinItemsPerRow = 4;

    private readonly ILibraryManager _libraryManager;
    private readonly ISimilarItemsManager _similarItemsManager;
    private readonly IDtoService _dtoService;
    private readonly ILocalizationManager _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="BecauseYouWatchedSectionProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="similarItemsManager">Instance of the <see cref="ISimilarItemsManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public BecauseYouWatchedSectionProvider(
        ILibraryManager libraryManager,
        ISimilarItemsManager similarItemsManager,
        IDtoService dtoService,
        ILocalizationManager localization)
    {
        _libraryManager = libraryManager;
        _similarItemsManager = similarItemsManager;
        _dtoService = dtoService;
        _localization = localization;
    }

    /// <inheritdoc />
    public string Key => HomeSectionKeys.BecauseYouWatched;

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("HeaderBecauseYouWatched");

    /// <inheritdoc />
    public BaseItemKind? ItemKind => null;

    /// <inheritdoc />
    public bool DependsOnUserData => true;

    /// <inheritdoc />
    public async Task<IReadOnlyList<HomeSectionResult>> GetSectionsAsync(HomeSectionQuery query, CancellationToken cancellationToken)
    {
        var seeds = GetSeeds(query.User);
        if (seeds.Count == 0)
        {
            return [];
        }

        var rows = new List<HomeSectionResult>(MaxRows);

        // Nothing the user just watched is a recommendation, and nothing is recommended twice.
        var excluded = new HashSet<Guid>(seeds.Select(seed => seed.Id));

        foreach (var seed in seeds)
        {
            if (rows.Count == MaxRows)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Ask for extra because the filters below thin the list out.
            var similar = await _similarItemsManager.GetSimilarItemsAsync(
                seed,
                [],
                query.User,
                query.DtoOptions,
                query.Limit * 2,
                _libraryManager.GetLibraryOptions(seed),
                cancellationToken).ConfigureAwait(false);

            var candidates = similar.Where(item => !excluded.Contains(item.Id)).ToList();
            var items = KeepUnwatched(candidates, query.User).Take(query.Limit).ToList();

            if (items.Count < MinItemsPerRow)
            {
                continue;
            }

            excluded.UnionWith(items.Select(item => item.Id));

            rows.Add(new HomeSectionResult
            {
                DisplayText = string.Format(
                    CultureInfo.CurrentCulture,
                    _localization.GetLocalizedString("HeaderBecauseYouWatchedItem"),
                    seed.Name),
                ViewType = HomeSectionViewType.Portrait,
                ParentId = seed.Id,
                Items = _dtoService.GetBaseItemDtos(items, query.DtoOptions, query.User)
            });
        }

        return rows;
    }

    /// <summary>
    /// Gets the films and series the user finished most recently, each once, most recent first.
    /// </summary>
    private List<BaseItem> GetSeeds(User user)
    {
        // One query over films and episodes keeps them interleaved by when they were played. An
        // episode stands in for its series, so a binge of one show yields one seed.
        var played = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
            OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)],
            IsPlayed = true,
            Limit = PlayedItemsToScan,
            Recursive = true,
            IsVirtualItem = false,
            EnableGroupByMetadataKey = true,
            EnableTotalRecordCount = false
        });

        var seeds = new List<BaseItem>();
        var seen = new HashSet<Guid>();
        var seenFranchises = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in played)
        {
            var seed = item is Episode episode ? episode.Series : item;
            if (seed is null || !seen.Add(seed.Id))
            {
                continue;
            }

            // Three films of one franchise in a row would give three near identical rows, so a
            // franchise seeds once, from its most recently watched entry.
            if (seed is Movie { CollectionName: { Length: > 0 } franchise } && !seenFranchises.Add(franchise))
            {
                continue;
            }

            seeds.Add(seed);
        }

        return seeds;
    }

    /// <summary>
    /// Drops what the user has already seen or started, keeping the ranking of the rest.
    /// </summary>
    private IEnumerable<BaseItem> KeepUnwatched(IReadOnlyList<BaseItem> candidates, User user)
    {
        if (candidates.Count == 0)
        {
            return candidates;
        }

        // One query rather than a user data lookup per item, which for a series would mean walking
        // its episodes each time.
        var unwatched = _libraryManager.GetItemList(new InternalItemsQuery(user)
        {
            ItemIds = candidates.Select(item => item.Id).ToArray(),
            IsPlayed = false,
            IsResumable = false,
            EnableTotalRecordCount = false
        });

        var unwatchedIds = unwatched.Select(item => item.Id).ToHashSet();

        return candidates.Where(item => unwatchedIds.Contains(item.Id));
    }
}
