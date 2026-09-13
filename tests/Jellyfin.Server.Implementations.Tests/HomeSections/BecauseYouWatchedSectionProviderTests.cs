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
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Globalization;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.HomeSections;

/// <summary>
/// Covers how <see cref="BecauseYouWatchedSectionProvider"/> picks its seeds and what it leaves
/// out of each row.
/// </summary>
public sealed class BecauseYouWatchedSectionProviderTests
{
    private readonly User _user = new("watcher", "default", "default");
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<ISimilarItemsManager> _similarItems = new();
    private readonly Mock<IDtoService> _dtoService = new();
    private readonly BecauseYouWatchedSectionProvider _provider;

    private readonly Dictionary<Guid, BaseItem[]> _similarBySeed = new();
    private readonly HashSet<Guid> _watched = new();

    public BecauseYouWatchedSectionProviderTests()
    {
        var localization = new Mock<ILocalizationManager>();
        localization.Setup(x => x.GetLocalizedString(It.IsAny<string>())).Returns<string>(key => key);
        localization.Setup(x => x.GetLocalizedString("HeaderBecauseYouWatchedItem")).Returns("Because you watched {0}");

        _similarItems
            .Setup(x => x.GetSimilarItemsAsync(
                It.IsAny<BaseItem>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<User>(),
                It.IsAny<DtoOptions>(),
                It.IsAny<int?>(),
                It.IsAny<LibraryOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BaseItem seed, IReadOnlyList<Guid> _, User _, DtoOptions _, int? _, LibraryOptions _, CancellationToken _)
                => _similarBySeed.TryGetValue(seed.Id, out var items) ? items : []);

        _dtoService
            .Setup(x => x.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<bool>()))
            .Returns<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem, bool>((source, _, _, _, _) =>
                source.Select(item => new BaseItemDto { Id = item.Id, Name = item.Name }).ToList());

        // Episode.Series resolves through this static, as it does on a running server.
        BaseItem.LibraryManager = _libraryManager.Object;

        _provider = new BecauseYouWatchedSectionProvider(_libraryManager.Object, _similarItems.Object, _dtoService.Object, localization.Object);
    }

    [Fact]
    public async Task ReturnsNothingWhenNothingHasBeenWatched()
    {
        SetPlayed();

        Assert.Empty(await BuildAsync());
    }

    [Fact]
    public async Task NamesEachRowAfterItsSeedAndBindsItToIt()
    {
        var seed = Movie("Iron Man");
        SetPlayed(seed);
        SetSimilar(seed, Movies("A", "B", "C", "D"));

        var row = Assert.Single(await BuildAsync());

        Assert.Equal("Because you watched Iron Man", row.DisplayText);
        Assert.Equal(seed.Id, row.ParentId);
        Assert.Equal(["A", "B", "C", "D"], row.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task BuildsOneRowPerSeedMostRecentFirstUpToThree()
    {
        var seeds = new[] { Movie("First"), Movie("Second"), Movie("Third"), Movie("Fourth") };
        SetPlayed(seeds);
        foreach (var seed in seeds)
        {
            SetSimilar(seed, Movies($"{seed.Name} 1", $"{seed.Name} 2", $"{seed.Name} 3", $"{seed.Name} 4"));
        }

        var rows = await BuildAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal(["First", "Second", "Third"], rows.Select(row => row.DisplayText.Replace("Because you watched ", string.Empty, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task AnEpisodeSeedsItsSeriesOnce()
    {
        // A binge of one show is one seed, not one per episode.
        var series = new Series { Name = "The Bear", Id = Guid.NewGuid() };
        _libraryManager.Setup(x => x.GetItemById(series.Id)).Returns(series);
        SetPlayed(Episode(series), Episode(series), Episode(series));
        SetSimilar(series, [Series("Beef"), Series("Shrinking"), Series("Fleabag"), Series("Atlanta")]);

        var row = Assert.Single(await BuildAsync());

        Assert.Equal("Because you watched The Bear", row.DisplayText);
        Assert.Equal(series.Id, row.ParentId);
    }

    [Fact]
    public async Task AFranchiseSeedsOnceFromItsMostRecentEntry()
    {
        var third = Movie("Iron Man 3", "Iron Man Collection");
        var second = Movie("Iron Man 2", "Iron Man Collection");
        var other = Movie("Dune");
        SetPlayed(third, second, other);
        SetSimilar(third, Movies("A", "B", "C", "D"));
        SetSimilar(second, Movies("E", "F", "G", "H"));
        SetSimilar(other, Movies("I", "J", "K", "L"));

        var rows = await BuildAsync();

        Assert.Equal(["Because you watched Iron Man 3", "Because you watched Dune"], rows.Select(row => row.DisplayText));
    }

    [Fact]
    public async Task LeavesOutWhatWasWatchedWhatIsInProgressAndTheSeedsThemselves()
    {
        var seed = Movie("Iron Man");
        var otherSeed = Movie("Dune");
        var seen = Movie("Seen it");
        var started = Movie("Started it");
        var fresh = Movies("A", "B", "C", "D");
        SetPlayed(seed, otherSeed);
        SetSimilar(seed, [seen, started, otherSeed, .. fresh]);
        SetSimilar(otherSeed, []);
        _watched.Add(seen.Id);
        _watched.Add(started.Id);

        var row = Assert.Single(await BuildAsync());

        Assert.Equal(["A", "B", "C", "D"], row.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task DoesNotRecommendTheSameItemInTwoRows()
    {
        var first = Movie("First");
        var second = Movie("Second");
        var shared = Movies("Shared 1", "Shared 2");
        SetPlayed(first, second);
        SetSimilar(first, [.. shared, .. Movies("A", "B")]);
        SetSimilar(second, [.. shared, .. Movies("C", "D", "E", "F")]);

        var rows = await BuildAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(["Shared 1", "Shared 2", "A", "B"], rows[0].Items.Select(item => item.Name));
        Assert.Equal(["C", "D", "E", "F"], rows[1].Items.Select(item => item.Name));
    }

    [Fact]
    public async Task SkipsASeedWhoseRowWouldBeNearlyEmpty()
    {
        var thin = Movie("Thin");
        var rich = Movie("Rich");
        SetPlayed(thin, rich);
        SetSimilar(thin, Movies("Only one"));
        SetSimilar(rich, Movies("A", "B", "C", "D", "E"));

        var row = Assert.Single(await BuildAsync());

        Assert.Equal("Because you watched Rich", row.DisplayText);
    }

    [Fact]
    public async Task CapsEachRowAtTheRequestedLimit()
    {
        var seed = Movie("Iron Man");
        SetPlayed(seed);
        SetSimilar(seed, Movies("A", "B", "C", "D", "E", "F"));

        var row = Assert.Single(await BuildAsync(limit: 4));

        Assert.Equal(4, row.Items.Count);
    }

    private static Movie Movie(string name, string? franchise = null)
        => new() { Name = name, Id = Guid.NewGuid(), CollectionName = franchise };

    private static BaseItem[] Movies(params string[] names)
        => names.Select(name => (BaseItem)Movie(name)).ToArray();

    private static Series Series(string name)
        => new() { Name = name, Id = Guid.NewGuid() };

    private static Episode Episode(Series series)
        => new() { Name = "An episode", Id = Guid.NewGuid(), SeriesId = series.Id };

    private void SetPlayed(params BaseItem[] items)
    {
        // The played query is the only one that orders by DatePlayed; the unwatched filter is
        // the only one that names item ids. Everything else the provider asks is answered empty.
        _libraryManager
            .Setup(x => x.GetItemList(It.Is<InternalItemsQuery>(query => query.IsPlayed == true)))
            .Returns(items.ToList());

        _libraryManager
            .Setup(x => x.GetItemList(It.Is<InternalItemsQuery>(query => query.IsPlayed == false)))
            .Returns<InternalItemsQuery>(query => query.ItemIds
                .Where(id => !_watched.Contains(id))
                .Select(id => (BaseItem)new Movie { Id = id })
                .ToList());
    }

    private void SetSimilar(BaseItem seed, BaseItem[] items)
        => _similarBySeed[seed.Id] = items;

    private Task<IReadOnlyList<HomeSectionResult>> BuildAsync(int limit = 16)
        => _provider.GetSectionsAsync(
            new HomeSectionQuery { User = _user, Limit = limit, DtoOptions = new DtoOptions() },
            CancellationToken.None);
}
