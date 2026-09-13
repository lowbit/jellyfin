using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.HomeSections;
using MediaBrowser.Model.LiveTv;

namespace Jellyfin.Server.Implementations.HomeSections.Providers;

/// <summary>
/// Recordings in progress.
/// </summary>
public sealed class ActiveRecordingsSectionProvider : IHomeSectionProvider
{
    private readonly ILiveTvManager _liveTvManager;
    private readonly ILocalizationManager _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActiveRecordingsSectionProvider"/> class.
    /// </summary>
    /// <param name="liveTvManager">Instance of the <see cref="ILiveTvManager"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public ActiveRecordingsSectionProvider(ILiveTvManager liveTvManager, ILocalizationManager localization)
    {
        _liveTvManager = liveTvManager;
        _localization = localization;
    }

    /// <inheritdoc />
    public string Key => HomeSectionKeys.ActiveRecordings;

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("HeaderActiveRecordings");

    /// <inheritdoc />
    public BaseItemKind? ItemKind => null;

    /// <inheritdoc />
    public bool DependsOnUserData => false;

    /// <inheritdoc />
    public async Task<IReadOnlyList<HomeSectionResult>> GetSectionsAsync(HomeSectionQuery query, CancellationToken cancellationToken)
    {
        if (!query.User.HasPermission(PermissionKind.EnableLiveTvAccess))
        {
            return [];
        }

        var result = await _liveTvManager.GetRecordingsAsync(
            new RecordingQuery
            {
                UserId = query.User.Id,
                IsInProgress = true,
                Limit = query.Limit,
                EnableTotalRecordCount = false
            },
            query.DtoOptions).ConfigureAwait(false);

        return
        [
            new HomeSectionResult
            {
                DisplayText = _localization.GetLocalizedString("HeaderActiveRecordings"),
                ViewType = HomeSectionViewType.Landscape,
                Items = result.Items
            }
        ];
    }
}
