using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;

namespace Jellyfin.Server.Implementations.HomeSections.Providers;

/// <summary>
/// The user's libraries as a row of buttons.
/// </summary>
public sealed class LibraryButtonsSectionProvider : LibrarySectionProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryButtonsSectionProvider"/> class.
    /// </summary>
    /// <param name="userViewManager">Instance of the <see cref="IUserViewManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public LibraryButtonsSectionProvider(IUserViewManager userViewManager, IDtoService dtoService, ILocalizationManager localization)
        : base(userViewManager, dtoService, localization)
    {
    }

    /// <inheritdoc />
    public override string Key => HomeSectionKeys.LibraryButtons;

    /// <inheritdoc />
    public override string Name => Localization.GetLocalizedString("HeaderMyMediaSmall");
}
