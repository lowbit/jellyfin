using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.HomeSections;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Server.Implementations.HomeSections.Providers;

/// <summary>
/// What was recently added to the libraries.
/// </summary>
public sealed class LatestMediaSectionProvider : IHomeSectionProvider
{
    private readonly IUserViewManager _userViewManager;
    private readonly IDtoService _dtoService;
    private readonly ILocalizationManager _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="LatestMediaSectionProvider"/> class.
    /// </summary>
    /// <param name="userViewManager">Instance of the <see cref="IUserViewManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public LatestMediaSectionProvider(IUserViewManager userViewManager, IDtoService dtoService, ILocalizationManager localization)
    {
        _userViewManager = userViewManager;
        _dtoService = dtoService;
        _localization = localization;
    }

    /// <inheritdoc />
    public string Key => HomeSectionKeys.LatestMedia;

    /// <inheritdoc />
    public string Name => _localization.GetLocalizedString("HeaderRecentlyAdded");

    /// <inheritdoc />
    public BaseItemKind? ItemKind => null;

    /// <inheritdoc />
    public bool AllowsMultipleItems => false;

    /// <inheritdoc />
    public bool DependsOnUserData => false;

    /// <inheritdoc />
    public Task<IReadOnlyList<HomeSectionResult>> GetSectionsAsync(HomeSectionQuery query, CancellationToken cancellationToken)
    {
        // The same path as /Items/Latest, so the libraries a user excluded, their "hide watched"
        // setting and the grouping of new episodes under their series all still apply.
        var latest = _userViewManager.GetLatestItems(
            new LatestItemsQuery
            {
                User = query.User,
                Limit = query.Limit,
                GroupItems = true,
                IsPlayed = query.User.HidePlayedInLatest ? false : null,
                IncludeItemTypes = []
            },
            query.DtoOptions);

        var items = new BaseItem[latest.Count];
        var childCounts = new int[latest.Count];

        for (var i = 0; i < latest.Count; i++)
        {
            var (container, children) = latest[i];
            var isGroup = container is not null && (children.Count > 1 || container is MusicAlbum);

            items[i] = isGroup ? container! : children[0];
            childCounts[i] = isGroup ? children.Count : 0;
        }

        // GetLatestItems has already checked visibility.
        var dtos = _dtoService.GetBaseItemDtos(items, query.DtoOptions, query.User, skipVisibilityCheck: true);

        for (var i = 0; i < dtos.Count; i++)
        {
            if (childCounts[i] > 0)
            {
                dtos[i].ChildCount = childCounts[i];
            }
        }

        IReadOnlyList<HomeSectionResult> rows =
        [
            new HomeSectionResult
            {
                DisplayText = _localization.GetLocalizedString("HeaderRecentlyAdded"),
                ViewType = HomeSectionViewType.Portrait,
                Items = dtos
            }
        ];

        return Task.FromResult(rows);
    }
}
