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
/// Items the user is part way through, for one media type.
/// </summary>
public abstract class ResumeSectionProvider : IHomeSectionProvider
{
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;
    private readonly ILocalizationManager _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResumeSectionProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    protected ResumeSectionProvider(ILibraryManager libraryManager, IDtoService dtoService, ILocalizationManager localization)
    {
        _libraryManager = libraryManager;
        _dtoService = dtoService;
        _localization = localization;
    }

    /// <inheritdoc />
    public abstract string Key { get; }

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString(HeadingKey);

    /// <inheritdoc />
    public BaseItemKind? ItemKind => null;

    /// <inheritdoc />
    public bool AllowsMultipleItems => false;

    /// <inheritdoc />
    public bool DependsOnUserData => true;

    /// <summary>
    /// Gets the media type the row resumes.
    /// </summary>
    protected abstract MediaType MediaType { get; }

    /// <summary>
    /// Gets the localization key of the heading.
    /// </summary>
    protected abstract string HeadingKey { get; }

    /// <summary>
    /// Gets the card shape for the row.
    /// </summary>
    protected abstract HomeSectionViewType ViewType { get; }

    /// <inheritdoc />
    public Task<IReadOnlyList<HomeSectionResult>> GetSectionsAsync(HomeSectionQuery query, CancellationToken cancellationToken)
    {
        var result = _libraryManager.GetItemsResult(new InternalItemsQuery(query.User)
        {
            OrderBy = [(ItemSortBy.DatePlayed, SortOrder.Descending)],
            IsResumable = true,
            Limit = query.Limit,
            Recursive = true,
            DtoOptions = query.DtoOptions,
            MediaTypes = [MediaType],
            IsVirtualItem = false,
            CollapseBoxSetItems = false,
            EnableTotalRecordCount = false
        });

        IReadOnlyList<HomeSectionResult> rows =
        [
            new HomeSectionResult
            {
                DisplayText = _localization.GetLocalizedString(HeadingKey),
                ViewType = ViewType,
                Items = _dtoService.GetBaseItemDtos(result.Items, query.DtoOptions, query.User)
            }
        ];

        return Task.FromResult(rows);
    }
}
