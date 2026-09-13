namespace MediaBrowser.Controller.HomeSections;

/// <summary>
/// The keys of the built-in home section providers.
/// </summary>
/// <remarks>
/// These are the lower-cased names of the legacy <c>HomeSectionType</c> values, which is the form
/// the legacy display preferences API has always emitted, so a layout stored by either API reads
/// the same through both.
/// </remarks>
public static class HomeSectionKeys
{
    /// <summary>
    /// An empty slot in a legacy layout. Nothing is built for it.
    /// </summary>
    public const string None = "none";

    /// <summary>
    /// My Media, as tiles.
    /// </summary>
    public const string SmallLibraryTiles = "smalllibrarytiles";

    /// <summary>
    /// My Media, as buttons.
    /// </summary>
    public const string LibraryButtons = "librarybuttons";

    /// <summary>
    /// Recordings in progress.
    /// </summary>
    public const string ActiveRecordings = "activerecordings";

    /// <summary>
    /// Continue Watching.
    /// </summary>
    public const string Resume = "resume";

    /// <summary>
    /// Continue Listening.
    /// </summary>
    public const string ResumeAudio = "resumeaudio";

    /// <summary>
    /// Recently added.
    /// </summary>
    public const string LatestMedia = "latestmedia";

    /// <summary>
    /// Next Up.
    /// </summary>
    public const string NextUp = "nextup";

    /// <summary>
    /// What is airing now.
    /// </summary>
    public const string LiveTv = "livetv";

    /// <summary>
    /// Continue Reading.
    /// </summary>
    public const string ResumeBook = "resumebook";

    /// <summary>
    /// The contents of one collection.
    /// </summary>
    public const string PinnedCollection = "pinnedcollection";

    /// <summary>
    /// Items of one genre.
    /// </summary>
    public const string Genre = "genre";

    /// <summary>
    /// Items similar to ones the user recently finished.
    /// </summary>
    public const string BecauseYouWatched = "becauseyouwatched";
}
