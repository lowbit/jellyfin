using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.TV;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.HomeSections;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Server.Implementations.HomeSections.Providers;

/// <summary>
/// The next unwatched episode of each series the user is watching.
/// </summary>
public sealed class NextUpSectionProvider : IHomeSectionProvider
{
    private readonly ITVSeriesManager _tvSeriesManager;
    private readonly IDtoService _dtoService;
    private readonly ILocalizationManager _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="NextUpSectionProvider"/> class.
    /// </summary>
    /// <param name="tvSeriesManager">Instance of the <see cref="ITVSeriesManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public NextUpSectionProvider(ITVSeriesManager tvSeriesManager, IDtoService dtoService, ILocalizationManager localization)
    {
        _tvSeriesManager = tvSeriesManager;
        _dtoService = dtoService;
        _localization = localization;
    }

    /// <inheritdoc />
    public string Key => HomeSectionKeys.NextUp;

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("HeaderNextUp");

    /// <inheritdoc />
    public BaseItemKind? ItemKind => null;

    /// <inheritdoc />
    public bool DependsOnUserData => true;

    /// <inheritdoc />
    public Task<IReadOnlyList<HomeSectionResult>> GetSectionsAsync(HomeSectionQuery query, CancellationToken cancellationToken)
    {
        var result = _tvSeriesManager.GetNextUp(
            new NextUpQuery { User = query.User, Limit = query.Limit, EnableTotalRecordCount = false },
            query.DtoOptions);

        IReadOnlyList<HomeSectionResult> rows =
        [
            new HomeSectionResult
            {
                DisplayText = _localization.GetLocalizedString("HeaderNextUp"),
                ViewType = HomeSectionViewType.Landscape,
                Items = _dtoService.GetBaseItemDtos(result.Items, query.DtoOptions, query.User)
            }
        ];

        return Task.FromResult(rows);
    }
}
