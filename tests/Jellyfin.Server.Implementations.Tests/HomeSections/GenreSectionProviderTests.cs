using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.HomeSections.Providers;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Querying;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.HomeSections;

/// <summary>
/// Covers which genres <see cref="GenreSectionProvider"/> draws a row for, in what order, and
/// what each row asks the library for.
/// </summary>
public sealed class GenreSectionProviderTests
{
    private readonly User _user = new("viewer", "default", "default");
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IDtoService> _dtoService = new();
    private readonly GenreSectionProvider _provider;

    private readonly List<(BaseItem Item, ItemCounts ItemCounts)> _genres = [];
    private readonly List<InternalItemsQuery> _rowQueries = [];

    public GenreSectionProviderTests()
    {
        var localization = new Mock<ILocalizationManager>();
        localization.Setup(x => x.GetLocalizedString(It.IsAny<string>())).Returns<string>(key => key);

        _libraryManager
            .Setup(x => x.GetGenres(It.IsAny<InternalItemsQuery>()))
            .Returns(() => new QueryResult<(BaseItem, ItemCounts)>(_genres.Select(genre => (genre.Item, genre.ItemCounts)).ToList()));

        // Every genre row asks for its items; one is enough to keep the row from being empty.
        _libraryManager
            .Setup(x => x.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(_rowQueries.Add)
            .Returns(new QueryResult<BaseItem>([new Movie { Id = Guid.NewGuid(), Name = "Something" }]));

        _dtoService
            .Setup(x => x.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<bool>()))
            .Returns<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem, bool>((source, _, _, _, _) =>
                source.Select(item => new BaseItemDto { Id = item.Id, Name = item.Name }).ToList());

        _provider = new GenreSectionProvider(_libraryManager.Object, _dtoService.Object, localization.Object);
    }

    [Fact]
    public void TakesSeveralGenres()
    {
        // Which is what lets one section stand for "genre rows", rather than one section per genre.
        Assert.True(_provider.AllowsMultipleItems);
    }

    [Fact]
    public async Task BoundToNothingDrawsNothing()
    {
        Genre("Drama");

        Assert.Empty(await BuildAsync());
        _libraryManager.Verify(x => x.GetGenres(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task DrawsTheChosenGenresInTheOrderChosen()
    {
        var drama = Genre("Drama");
        Genre("Action");
        var comedy = Genre("Comedy");

        var rows = await BuildAsync(drama.Id, comedy.Id);

        Assert.Equal(["Drama", "Comedy"], rows.Select(row => row.DisplayText));
    }

    [Fact]
    public async Task ADeletedGenreIsSkippedRatherThanDrawnEmpty()
    {
        var drama = Genre("Drama");

        var rows = await BuildAsync(Guid.NewGuid(), drama.Id);

        Assert.Equal(["Drama"], rows.Select(row => row.DisplayText));
    }

    [Fact]
    public async Task EachRowIsBoundToItsGenre()
    {
        var action = Genre("Action");

        var row = Assert.Single(await BuildAsync(action.Id));

        Assert.Equal(action.Id, row.ParentId);
        Assert.Equal(BaseItemKind.Genre, row.ParentType);
    }

    [Fact]
    public async Task AFilmGenreRowAlsoAsksForItsTelevisionEquivalent()
    {
        // Shows are tagged Action & Adventure, not Action, and belong in the same row.
        var action = Genre("Action");
        var drama = Genre("Drama");

        await BuildAsync(action.Id, drama.Id);

        Assert.Equal(["Action", "Action & Adventure"], _rowQueries[0].Genres);
        Assert.Equal(["Drama"], _rowQueries[1].Genres);
    }

    private BaseItem Genre(string name)
    {
        var genre = new MediaBrowser.Controller.Entities.Genre { Id = Guid.NewGuid(), Name = name };
        _genres.Add((genre, new ItemCounts()));
        return genre;
    }

    private Task<IReadOnlyList<HomeSectionResult>> BuildAsync(params Guid[] itemIds)
        => _provider.GetSectionsAsync(
            new HomeSectionQuery { User = _user, ItemIds = itemIds, Limit = 16, DtoOptions = new DtoOptions() },
            CancellationToken.None);
}
