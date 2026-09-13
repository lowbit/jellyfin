using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.HomeSections;

namespace Jellyfin.Server.Implementations.HomeSections.Providers;

/// <summary>
/// What is airing right now, which is what the Live TV row has always shown.
/// </summary>
public sealed class LiveTvSectionProvider : IHomeSectionProvider
{
    private readonly ILiveTvManager _liveTvManager;
    private readonly ILocalizationManager _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveTvSectionProvider"/> class.
    /// </summary>
    /// <param name="liveTvManager">Instance of the <see cref="ILiveTvManager"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public LiveTvSectionProvider(ILiveTvManager liveTvManager, ILocalizationManager localization)
    {
        _liveTvManager = liveTvManager;
        _localization = localization;
    }

    /// <inheritdoc />
    public string Key => HomeSectionKeys.LiveTv;

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("HeaderLiveTV");

    /// <inheritdoc />
    public BaseItemKind? ItemKind => null;

    /// <inheritdoc />
    public bool AllowsMultipleItems => false;

    /// <inheritdoc />
    public bool DependsOnUserData => false;

    /// <inheritdoc />
    public async Task<IReadOnlyList<HomeSectionResult>> GetSectionsAsync(HomeSectionQuery query, CancellationToken cancellationToken)
    {
        if (!query.User.HasPermission(PermissionKind.EnableLiveTvAccess))
        {
            return [];
        }

        var result = await _liveTvManager.GetRecommendedProgramsAsync(
            new InternalItemsQuery(query.User)
            {
                IsAiring = true,
                Limit = query.Limit,
                EnableTotalRecordCount = false
            },
            query.DtoOptions,
            cancellationToken).ConfigureAwait(false);

        return
        [
            new HomeSectionResult
            {
                DisplayText = _localization.GetLocalizedString("HeaderOnNow"),
                ViewType = HomeSectionViewType.Landscape,
                Items = result.Items
            }
        ];
    }
}
