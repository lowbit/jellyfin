using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.HomeSections;
using MediaBrowser.Model.Library;

namespace Jellyfin.Server.Implementations.HomeSections.Providers;

/// <summary>
/// The user's libraries. Tiles and buttons are the same row drawn differently, so they share this.
/// </summary>
public abstract class LibrarySectionProvider : IHomeSectionProvider
{
    private readonly IUserViewManager _userViewManager;
    private readonly IDtoService _dtoService;
    private readonly ILocalizationManager _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibrarySectionProvider"/> class.
    /// </summary>
    /// <param name="userViewManager">Instance of the <see cref="IUserViewManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    protected LibrarySectionProvider(IUserViewManager userViewManager, IDtoService dtoService, ILocalizationManager localization)
    {
        _userViewManager = userViewManager;
        _dtoService = dtoService;
        _localization = localization;
    }

    /// <inheritdoc />
    public abstract string Key { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public BaseItemKind? ItemKind => null;

    /// <inheritdoc />
    public bool DependsOnUserData => false;

    /// <summary>
    /// Gets the localization manager.
    /// </summary>
    protected ILocalizationManager Localization => _localization;

    /// <inheritdoc />
    public Task<IReadOnlyList<HomeSectionResult>> GetSectionsAsync(HomeSectionQuery query, CancellationToken cancellationToken)
    {
        var folders = _userViewManager.GetUserViews(new UserViewQuery { User = query.User });
        var items = Array.ConvertAll(folders, folder => _dtoService.GetBaseItemDto(folder, query.DtoOptions, query.User));

        IReadOnlyList<HomeSectionResult> result =
        [
            new HomeSectionResult
            {
                DisplayText = _localization.GetLocalizedString("HeaderMyMedia"),
                ViewType = HomeSectionViewType.Landscape,
                Items = items
            }
        ];

        return Task.FromResult(result);
    }
}
