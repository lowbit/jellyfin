using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.HomeSections;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Server.Implementations.HomeSections.Providers;

/// <summary>
/// The contents of one collection.
/// </summary>
public sealed class PinnedCollectionSectionProvider : IHomeSectionProvider
{
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly ILocalizationManager _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="PinnedCollectionSectionProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public PinnedCollectionSectionProvider(ILibraryManager libraryManager, IDtoService dtoService, ILocalizationManager localization)
    {
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _localization = localization;
    }

    /// <inheritdoc />
    public string Key => HomeSectionKeys.PinnedCollection;

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("HeaderCollection");

    /// <inheritdoc />
    public BaseItemKind? ItemKind => BaseItemKind.BoxSet;

    /// <inheritdoc />
    public bool DependsOnUserData => false;

    /// <inheritdoc />
    public Task<IReadOnlyList<HomeSectionResult>> GetSectionsAsync(HomeSectionQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<HomeSectionResult> none = [];

        if (!query.ItemId.HasValue)
        {
            return Task.FromResult(none);
        }

        var boxSet = _libraryManager.GetItemById<BaseItem>(query.ItemId.Value, query.User);
        if (boxSet is null)
        {
            // The collection was deleted. Drop the row instead of failing the whole request.
            return Task.FromResult(none);
        }

        // Collection children need Recursive, otherwise the query returns nothing and the
        // collection looks empty when it is not.
        var children = _libraryManager.GetItemsResult(new InternalItemsQuery(query.User)
        {
            ParentId = boxSet.Id,
            OrderBy = [(ItemSortBy.PremiereDate, SortOrder.Ascending)],
            Limit = query.Limit,
            Recursive = true,
            DtoOptions = query.DtoOptions,
            CollapseBoxSetItems = false,
            IsVirtualItem = false,
            EnableTotalRecordCount = false
        });

        IReadOnlyList<HomeSectionResult> rows =
        [
            new HomeSectionResult
            {
                DisplayText = boxSet.Name,
                ViewType = HomeSectionViewType.Portrait,
                ParentId = boxSet.Id,
                Items = _dtoService.GetBaseItemDtos(children.Items, query.DtoOptions, query.User)
            }
        ];

        return Task.FromResult(rows);
    }
}
