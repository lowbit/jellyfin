using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.HomeSections;

namespace Jellyfin.Server.Implementations.HomeSections.Providers;

/// <summary>
/// Films and episodes the user is part way through.
/// </summary>
public sealed class ResumeVideoSectionProvider : ResumeSectionProvider
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResumeVideoSectionProvider"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="dtoService">Instance of the <see cref="IDtoService"/> interface.</param>
    /// <param name="localization">Instance of the <see cref="ILocalizationManager"/> interface.</param>
    public ResumeVideoSectionProvider(ILibraryManager libraryManager, IDtoService dtoService, ILocalizationManager localization)
        : base(libraryManager, dtoService, localization)
    {
    }

    /// <inheritdoc />
    public override string Key => HomeSectionKeys.Resume;

    /// <inheritdoc />
    protected override MediaType MediaType => MediaType.Video;

    /// <inheritdoc />
    protected override string HeadingKey => "HeaderContinueWatching";

    /// <inheritdoc />
    protected override HomeSectionViewType ViewType => HomeSectionViewType.Landscape;
}
