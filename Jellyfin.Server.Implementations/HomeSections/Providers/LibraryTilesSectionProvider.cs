using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;

namespace Jellyfin.Server.Implementations.HomeSections.Providers;

/// <summary>
/// The user's libraries as tiles.
/// </summary>
public sealed class LibraryTilesSectionProvider : LibrarySectionProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryTilesSectionProvider"/> class.
    /// </summary>
    /// <param name="userViewManager">Instance of the <see cref="IUserViewManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public LibraryTilesSectionProvider(IUserViewManager userViewManager, IDtoService dtoService, ILocalizationManager localization)
        : base(userViewManager, dtoService, localization)
    {
    }

    /// <inheritdoc />
    public override string Key => HomeSectionKeys.SmallLibraryTiles;

    /// <inheritdoc />
    public override string Name => Localization.GetLocalizedString("HeaderMyMedia");
}
