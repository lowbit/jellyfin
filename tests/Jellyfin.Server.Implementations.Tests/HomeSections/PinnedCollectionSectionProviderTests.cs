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
/// Covers which collections <see cref="PinnedCollectionSectionProvider"/> draws a row for, in
/// what order, and what each row asks the library for.
/// </summary>
public sealed class PinnedCollectionSectionProviderTests
{
    private readonly User _user = new("viewer", "default", "default");
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IDtoService> _dtoService = new();
    private readonly PinnedCollectionSectionProvider _provider;

    private readonly List<InternalItemsQuery> _rowQueries = [];

    public PinnedCollectionSectionProviderTests()
    {
        var localization = new Mock<ILocalizationManager>();
        localization.Setup(x => x.GetLocalizedString(It.IsAny<string>())).Returns<string>(key => key);

        _libraryManager
            .Setup(x => x.GetItemsResult(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(_rowQueries.Add)
            .Returns(new QueryResult<BaseItem>([new Movie { Id = Guid.NewGuid(), Name = "Something" }]));

        _dtoService
            .Setup(x => x.GetBaseItemDtos(It.IsAny<IReadOnlyList<BaseItem>>(), It.IsAny<DtoOptions>(), It.IsAny<User>(), It.IsAny<BaseItem>(), It.IsAny<bool>()))
            .Returns<IReadOnlyList<BaseItem>, DtoOptions, User, BaseItem, bool>((source, _, _, _, _) =>
                source.Select(item => new BaseItemDto { Id = item.Id, Name = item.Name }).ToList());

        _provider = new PinnedCollectionSectionProvider(_libraryManager.Object, _dtoService.Object, localization.Object);
    }

    [Fact]
    public void TakesSeveralCollections()
    {
        // One section stands for "collection rows", the same way the genre section does.
        Assert.True(_provider.AllowsMultipleItems);
        Assert.Equal(BaseItemKind.BoxSet, _provider.ItemKind);
    }

    [Fact]
    public async Task BoundToNothingDrawsNothing()
    {
        Collection("Marvel");

        Assert.Empty(await BuildAsync());
        Assert.Empty(_rowQueries);
    }

    [Fact]
    public async Task DrawsTheChosenCollectionsInTheOrderChosen()
    {
        var marvel = Collection("Marvel");
        Collection("Bond");
        var potter = Collection("Harry Potter");

        var rows = await BuildAsync(potter.Id, marvel.Id);

        Assert.Equal(["Harry Potter", "Marvel"], rows.Select(row => row.DisplayText));
    }

    [Fact]
    public async Task ADeletedCollectionIsSkippedRatherThanDrawnEmpty()
    {
        var marvel = Collection("Marvel");

        var rows = await BuildAsync(Guid.NewGuid(), marvel.Id);

        Assert.Equal(["Marvel"], rows.Select(row => row.DisplayText));
    }

    [Fact]
    public async Task EachRowIsBoundToItsCollectionAndAsksForItsChildren()
    {
        var marvel = Collection("Marvel");

        var row = Assert.Single(await BuildAsync(marvel.Id));

        Assert.Equal(marvel.Id, row.ParentId);
        Assert.Equal(BaseItemKind.BoxSet, row.ParentType);
        var query = Assert.Single(_rowQueries);
        Assert.Equal(marvel.Id, query.ParentId);
        Assert.True(query.Recursive);
        Assert.False(query.CollapseBoxSetItems);
    }

    private BoxSet Collection(string name)
    {
        var boxSet = new BoxSet { Id = Guid.NewGuid(), Name = name };
        _libraryManager.Setup(x => x.GetItemById<BaseItem>(boxSet.Id, _user)).Returns(boxSet);
        return boxSet;
    }

    private Task<IReadOnlyList<HomeSectionResult>> BuildAsync(params Guid[] itemIds)
        => _provider.GetSectionsAsync(
            new HomeSectionQuery { User = _user, ItemIds = itemIds, Limit = 16, DtoOptions = new DtoOptions() },
            CancellationToken.None);
}
