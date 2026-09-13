namespace MediaBrowser.Model.HomeSections;

/// <summary>
/// The shape a client should use when rendering the cards of a home section.
/// </summary>
public enum HomeSectionViewType
{
    /// <summary>
    /// Poster shaped cards, the usual choice for movies and series.
    /// </summary>
    Portrait = 0,

    /// <summary>
    /// Thumbnail shaped cards, the usual choice for episodes and resumable items.
    /// </summary>
    Landscape = 1,

    /// <summary>
    /// Square cards, used for music.
    /// </summary>
    Square = 2
}
