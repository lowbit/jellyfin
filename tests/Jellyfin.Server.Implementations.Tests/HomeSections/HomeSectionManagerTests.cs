using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Server.Implementations.HomeSections;
using Jellyfin.Server.Implementations.Tests.Item;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.HomeSections;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.HomeSections;

/// <summary>
/// Covers <see cref="HomeSectionManager"/>: the stored layout, the provider registry, building
/// through providers, and when the built result stops being current.
/// </summary>
public sealed class HomeSectionManagerTests : SqliteDbTestFixture
{
    private const string Client = "emby";
    private const int ItemLimit = 16;

    private readonly ServerConfiguration _configuration = new();
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly Mock<ILibraryManager> _libraryManager = new();
    private readonly Mock<IUserDataManager> _userDataManager = new();
    private readonly Mock<IUserManager> _userManager = new();
    private readonly HomeSectionManager _manager;
    private readonly User _user;
    private readonly User _otherUser;

    public HomeSectionManagerTests()
    {
        // Display preferences carry a foreign key to the user, so the rows have to exist first.
        using (var context = CreateDbContext())
        {
            _user = new User("homesections", "default", "default");
            _otherUser = new User("other", "default", "default");
            context.Users.AddRange(_user, _otherUser);
            context.SaveChanges();
        }

        _userManager.Setup(x => x.GetUsers()).Returns([_user, _otherUser]);

        var configurationManager = new Mock<IServerConfigurationManager>();
        configurationManager.SetupGet(x => x.Configuration).Returns(_configuration);

        _manager = new HomeSectionManager(
            CreateDbContextFactory(),
            configurationManager.Object,
            _memoryCache,
            _libraryManager.Object,
            _userDataManager.Object,
            _userManager.Object,
            NullLogger<HomeSectionManager>.Instance);
    }

    [Fact]
    public void GetSections_ReturnsNothingBeforeAnythingIsStored()
    {
        Assert.Empty(_manager.GetSections(_user.Id, Client));
    }

    [Fact]
    public void GetEffectiveSections_FallsBackToBuiltInDefaults()
    {
        var sections = _manager.GetEffectiveSections(_user.Id, Client);

        Assert.NotEmpty(sections);
        Assert.Equal(HomeSectionKeys.SmallLibraryTiles, sections[0].Key);
        Assert.All(sections, section => Assert.True(section.Active));
    }

    [Fact]
    public void GetEffectiveSections_PrefersAdminDefaultsOverBuiltIn()
    {
        _configuration.DefaultHomeSections =
        [
            new HomeSectionOptions { Key = HomeSectionKeys.LatestMedia },
            new HomeSectionOptions { Key = HomeSectionKeys.Resume, Active = false }
        ];

        var sections = _manager.GetEffectiveSections(_user.Id, Client);

        Assert.Equal(2, sections.Count);
        Assert.Equal(HomeSectionKeys.LatestMedia, sections[0].Key);
        Assert.False(sections[1].Active);
    }

    [Fact]
    public void GetEffectiveSections_PrefersStoredSectionsOverAdminDefaults()
    {
        _configuration.DefaultHomeSections = [new HomeSectionOptions { Key = HomeSectionKeys.LatestMedia }];
        _manager.SetSections(_user.Id, Client, [new HomeSection { Key = HomeSectionKeys.NextUp }]);

        var section = Assert.Single(_manager.GetEffectiveSections(_user.Id, Client));
        Assert.Equal(HomeSectionKeys.NextUp, section.Key);
    }

    [Fact]
    public void SetDefaultSections_EmptyListRestoresTheBuiltInLayout()
    {
        _manager.SetDefaultSections([new HomeSection { Key = HomeSectionKeys.Resume }]);

        _manager.SetDefaultSections([]);

        Assert.Empty(_configuration.DefaultHomeSections);
        Assert.Equal(HomeSectionKeys.SmallLibraryTiles, _manager.GetDefaultSections()[0].Key);
    }

    [Fact]
    public void SetSections_NumbersOrderFromListPosition()
    {
        // Order is deliberately not taken from the caller, so a client cannot store a list whose
        // positions contradict its own ordering.
        _manager.SetSections(
            _user.Id,
            Client,
            [
                new HomeSection { Key = HomeSectionKeys.NextUp, Order = 99 },
                new HomeSection { Key = HomeSectionKeys.Resume, Order = 99 }
            ]);

        var sections = _manager.GetSections(_user.Id, Client);

        Assert.Equal([0, 1], sections.Select(section => section.Order));
        Assert.Equal(HomeSectionKeys.NextUp, sections[0].Key);
    }

    [Fact]
    public void SetSections_RoundTripsSeveralBoundItemsInOrder()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        _manager.SetSections(_user.Id, Client, [new HomeSection { Key = HomeSectionKeys.Genre, ItemIds = [first, second] }]);

        var section = Assert.Single(_manager.GetSections(_user.Id, Client));
        Assert.Equal([first, second], section.ItemIds);
    }

    [Fact]
    public async Task GetHomeSectionsAsync_PassesEveryBoundItemToTheProvider()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var provider = new FakeSectionProvider(HomeSectionKeys.Genre, "Something") { AllowsMultipleItems = true };
        _manager.AddParts([provider]);
        SetLayout(new HomeSection { Key = HomeSectionKeys.Genre, ItemIds = [first, second] });

        await GetSectionsAsync();

        Assert.Equal([first, second], provider.LastQuery!.ItemIds);
        Assert.Equal(first, provider.LastQuery.ItemId);
    }

    [Fact]
    public void SetSections_StoresKeysLowerCased()
    {
        // Keys are compared case-insensitively everywhere, but stored in one form so the legacy
        // endpoint and row ids see the same string.
        _manager.SetSections(_user.Id, Client, [new HomeSection { Key = "NextUp" }]);

        Assert.Equal("nextup", Assert.Single(_manager.GetSections(_user.Id, Client)).Key);
    }

    [Fact]
    public void SetSections_RoundTripsItemBindingAndLimit()
    {
        var collectionId = Guid.NewGuid();

        _manager.SetSections(
            _user.Id,
            Client,
            [new HomeSection { Key = HomeSectionKeys.PinnedCollection, ItemIds = [collectionId], MaxItems = 5, Active = false }]);

        var section = Assert.Single(_manager.GetSections(_user.Id, Client));

        Assert.Equal(HomeSectionKeys.PinnedCollection, section.Key);
        Assert.Equal([collectionId], section.ItemIds);
        Assert.Equal(5, section.MaxItems);
        Assert.False(section.Active);
    }

    [Fact]
    public void SetSections_ReplacesRatherThanAppends()
    {
        _manager.SetSections(_user.Id, Client, [new HomeSection { Key = HomeSectionKeys.NextUp }]);
        _manager.SetSections(_user.Id, Client, [new HomeSection { Key = HomeSectionKeys.Resume }]);

        Assert.Equal(HomeSectionKeys.Resume, Assert.Single(_manager.GetSections(_user.Id, Client)).Key);
    }

    [Fact]
    public void SetSections_KeepsClientsApart()
    {
        _manager.SetSections(_user.Id, Client, [new HomeSection { Key = HomeSectionKeys.NextUp }]);
        _manager.SetSections(_user.Id, "jellyfin-androidtv", [new HomeSection { Key = HomeSectionKeys.Resume }]);

        Assert.Equal(HomeSectionKeys.NextUp, _manager.GetSections(_user.Id, Client)[0].Key);
        Assert.Equal(HomeSectionKeys.Resume, _manager.GetSections(_user.Id, "jellyfin-androidtv")[0].Key);
    }

    [Fact]
    public void ResetSections_RestoresDefaults()
    {
        _manager.SetSections(_user.Id, Client, [new HomeSection { Key = HomeSectionKeys.NextUp }]);
        _manager.ResetSections(_user.Id, Client);

        Assert.Empty(_manager.GetSections(_user.Id, Client));
        Assert.Equal(HomeSectionKeys.SmallLibraryTiles, _manager.GetEffectiveSections(_user.Id, Client)[0].Key);
    }

    [Fact]
    public void SectionsChanged_FiresForWritesSoOtherDevicesCanRefresh()
    {
        var raised = 0;
        _manager.SectionsChanged += (_, userId) =>
        {
            Assert.Equal(_user.Id, userId);
            raised++;
        };

        _manager.SetSections(_user.Id, Client, [new HomeSection { Key = HomeSectionKeys.NextUp }]);
        _manager.ResetSections(_user.Id, Client);

        Assert.Equal(2, raised);
    }

    [Fact]
    public void AddParts_FindsProvidersByKeyRegardlessOfCase()
    {
        _manager.AddParts([new FakeSectionProvider("acme.trending")]);

        Assert.NotNull(_manager.GetProvider("ACME.Trending"));
        Assert.Null(_manager.GetProvider("acme.other"));
    }

    [Fact]
    public void AddParts_KeepsTheFirstProviderThatClaimsAKey()
    {
        // Built-ins are registered before plugins, so a plugin cannot replace Continue Watching by
        // reusing its key.
        var builtIn = new FakeSectionProvider(HomeSectionKeys.Resume);
        var impostor = new FakeSectionProvider(HomeSectionKeys.Resume);

        _manager.AddParts([builtIn, impostor]);

        Assert.Same(builtIn, _manager.GetProvider(HomeSectionKeys.Resume));
        Assert.Single(_manager.GetProviders());
    }

    [Fact]
    public async Task GetHomeSectionsAsync_BuildsThroughTheProvidersInConfiguredOrder()
    {
        _manager.AddParts(
        [
            new FakeSectionProvider(HomeSectionKeys.Resume, "Halfway"),
            new FakeSectionProvider(HomeSectionKeys.LatestMedia, "New")
        ]);
        SetLayout(
            new HomeSection { Key = HomeSectionKeys.LatestMedia },
            new HomeSection { Key = HomeSectionKeys.Resume });

        var sections = await GetSectionsAsync();

        Assert.Collection(
            sections,
            section => Assert.Equal(HomeSectionKeys.LatestMedia, section.Key),
            section => Assert.Equal(HomeSectionKeys.Resume, section.Key));
        Assert.Equal("New", Assert.Single(sections[0].Items).Name);
    }

    [Fact]
    public async Task GetHomeSectionsAsync_PassesTheSectionSettingsToTheProvider()
    {
        var provider = new FakeSectionProvider(HomeSectionKeys.PinnedCollection, "In it");
        var collectionId = Guid.NewGuid();
        _manager.AddParts([provider]);
        SetLayout(new HomeSection { Key = HomeSectionKeys.PinnedCollection, ItemIds = [collectionId], MaxItems = 4 });

        await GetSectionsAsync();

        Assert.NotNull(provider.LastQuery);
        Assert.Same(_user, provider.LastQuery!.User);
        Assert.Equal(collectionId, provider.LastQuery.ItemId);
        Assert.Equal(4, provider.LastQuery.Limit);
    }

    [Fact]
    public async Task GetHomeSectionsAsync_UsesTheRequestLimitWhenTheSectionSetsNone()
    {
        var provider = new FakeSectionProvider(HomeSectionKeys.Resume, "Halfway");
        _manager.AddParts([provider]);
        SetLayout(new HomeSection { Key = HomeSectionKeys.Resume });

        await GetSectionsAsync();

        Assert.Equal(ItemLimit, provider.LastQuery!.Limit);
    }

    [Fact]
    public async Task GetHomeSectionsAsync_SkipsInactiveSections()
    {
        var provider = new FakeSectionProvider(HomeSectionKeys.Resume, "Halfway");
        _manager.AddParts([provider]);
        SetLayout(new HomeSection { Key = HomeSectionKeys.Resume, Active = false });

        Assert.Empty(await GetSectionsAsync());
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task GetHomeSectionsAsync_SkipsKeysNothingClaims()
    {
        // An empty legacy slot and a row left behind by an uninstalled plugin look the same.
        _manager.AddParts([new FakeSectionProvider(HomeSectionKeys.Resume, "Halfway")]);
        SetLayout(
            new HomeSection { Key = HomeSectionKeys.None },
            new HomeSection { Key = "gone.plugin" },
            new HomeSection { Key = HomeSectionKeys.Resume });

        Assert.Equal(HomeSectionKeys.Resume, Assert.Single(await GetSectionsAsync()).Key);
    }

    [Fact]
    public async Task GetHomeSectionsAsync_DropsRowsWithNoItems()
    {
        // An empty row is worse than no row.
        _manager.AddParts([new FakeSectionProvider(HomeSectionKeys.Resume)]);
        SetLayout(new HomeSection { Key = HomeSectionKeys.Resume });

        Assert.Empty(await GetSectionsAsync());
    }

    [Fact]
    public async Task GetHomeSectionsAsync_GivesRowsAnIdFromTheKeyAndWhatTheyAreAbout()
    {
        var collectionId = Guid.NewGuid();
        _manager.AddParts(
        [
            new FakeSectionProvider(HomeSectionKeys.Resume, "Halfway"),
            new FakeSectionProvider(HomeSectionKeys.PinnedCollection, _ => [FakeSectionProvider.Row("Marvel", collectionId, "Iron Man")])
        ]);
        SetLayout(
            new HomeSection { Key = HomeSectionKeys.Resume },
            new HomeSection { Key = HomeSectionKeys.PinnedCollection, ItemIds = [collectionId] });

        var sections = await GetSectionsAsync();

        Assert.Equal("resume", sections[0].Id);
        Assert.Equal($"pinnedcollection-{collectionId:N}", sections[1].Id);
        Assert.Equal(collectionId, sections[1].ParentId);
        Assert.Equal(BaseItemKind.BoxSet, sections[1].ParentType);
    }

    [Fact]
    public async Task GetHomeSectionsAsync_LetsOneSectionExpandToSeveralRows()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        _manager.AddParts(
        [
            new FakeSectionProvider("plugin.recommended", _ =>
            [
                FakeSectionProvider.Row("Because you watched A", first, "B"),
                FakeSectionProvider.Row("Because you watched C", second, "D")
            ])
        ]);
        SetLayout(new HomeSection { Key = "plugin.recommended" });

        var sections = await GetSectionsAsync();

        Assert.Equal(2, sections.Count);
        Assert.All(sections, section => Assert.Equal("plugin.recommended", section.Key));
        Assert.Equal($"plugin.recommended-{first:N}", sections[0].Id);
        Assert.Equal($"plugin.recommended-{second:N}", sections[1].Id);
    }

    [Fact]
    public async Task GetHomeSectionsAsync_DropsASecondRowWithTheSameId()
    {
        // The legacy API can store the same section twice, and a client cannot tell two rows with
        // one id apart.
        _manager.AddParts([new FakeSectionProvider(HomeSectionKeys.Resume, "Halfway")]);
        SetLayout(
            new HomeSection { Key = HomeSectionKeys.Resume },
            new HomeSection { Key = HomeSectionKeys.Resume });

        Assert.Single(await GetSectionsAsync());
    }

    [Fact]
    public async Task GetHomeSectionsAsync_SurvivesAProviderThatThrows()
    {
        // One broken plugin must not take the home screen down.
        _manager.AddParts(
        [
            new FakeSectionProvider("acme.broken", _ => throw new InvalidOperationException("boom")),
            new FakeSectionProvider(HomeSectionKeys.Resume, "Halfway")
        ]);
        SetLayout(
            new HomeSection { Key = "acme.broken" },
            new HomeSection { Key = HomeSectionKeys.Resume });

        Assert.Equal(HomeSectionKeys.Resume, Assert.Single(await GetSectionsAsync()).Key);
    }

    [Fact]
    public async Task GetHomeSectionsAsync_ServesTheCachedResultUntilInvalidated()
    {
        var provider = new FakeSectionProvider(HomeSectionKeys.Resume, "Halfway");
        _manager.AddParts([provider]);
        SetLayout(new HomeSection { Key = HomeSectionKeys.Resume });

        var first = await GetSectionsAsync();
        var second = await GetSectionsAsync();

        Assert.Same(first, second);
        Assert.Equal(1, provider.Calls);

        _manager.Invalidate(HomeSectionKeys.Resume, _user.Id);

        Assert.NotSame(first, await GetSectionsAsync());
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public async Task GetHomeSectionsAsync_KeepsUsersClientsAndLimitsApart()
    {
        var provider = new FakeSectionProvider(HomeSectionKeys.Resume, "Halfway");
        _manager.AddParts([provider]);
        SetLayout(new HomeSection { Key = HomeSectionKeys.Resume });

        await GetSectionsAsync();
        await _manager.GetHomeSectionsAsync(_otherUser, Client, ItemLimit, CancellationToken.None);
        await _manager.GetHomeSectionsAsync(_user, "jellyfin-androidtv", ItemLimit, CancellationToken.None);
        // A smaller limit is a different result set, so it must not be served the larger one.
        await _manager.GetHomeSectionsAsync(_user, Client, 4, CancellationToken.None);

        Assert.Equal(4, provider.Calls);
    }

    [Fact]
    public void UserDataSaved_MarksOnlyTheProvidersThatDependOnItStale()
    {
        // Playback must not invalidate collection or genre rows, or every progress report would
        // rebuild the whole home screen.
        _manager.AddParts(
        [
            new FakeSectionProvider(HomeSectionKeys.Resume) { DependsOnUserData = true },
            new FakeSectionProvider(HomeSectionKeys.Genre) { DependsOnUserData = false },
            new FakeSectionProvider("Acme.History") { DependsOnUserData = true }
        ]);

        var raised = Capture();
        _userDataManager.Raise(x => x.UserDataSaved += null, this, new UserDataSaveEventArgs { UserId = _user.Id });

        var e = Assert.Single(raised);
        Assert.Equal(_user.Id, e.UserId);
        Assert.False(e.AllStale);
        Assert.Equal(["resume", "acme.history"], e.StaleSectionKeys);
    }

    [Fact]
    public async Task UserDataSaved_DropsOnlyThatUsersCache()
    {
        var provider = new FakeSectionProvider(HomeSectionKeys.Resume, "Halfway") { DependsOnUserData = true };
        _manager.AddParts([provider]);
        SetLayout(new HomeSection { Key = HomeSectionKeys.Resume });

        await GetSectionsAsync();
        await _manager.GetHomeSectionsAsync(_otherUser, Client, ItemLimit, CancellationToken.None);

        _userDataManager.Raise(x => x.UserDataSaved += null, this, new UserDataSaveEventArgs { UserId = _user.Id });

        await GetSectionsAsync();
        await _manager.GetHomeSectionsAsync(_otherUser, Client, ItemLimit, CancellationToken.None);

        Assert.Equal(3, provider.Calls);
    }

    [Fact]
    public void ItemAdded_MarksEverythingStaleForEveryUser()
    {
        var raised = Capture();

        _libraryManager.Raise(x => x.ItemAdded += null, this, new ItemChangeEventArgs());

        Assert.Equal(2, raised.Count);
        Assert.All(raised, e => Assert.True(e.AllStale));
        Assert.Equal([_user.Id, _otherUser.Id], raised.Select(e => e.UserId));
    }

    [Fact]
    public void SetSections_MarksEverythingStaleForThatUser()
    {
        var raised = Capture();

        _manager.SetSections(_user.Id, Client, [new HomeSection { Key = HomeSectionKeys.NextUp }]);

        var e = Assert.Single(raised);
        Assert.True(e.AllStale);
        Assert.Equal(_user.Id, e.UserId);
    }

    [Fact]
    public void Invalidate_ReachesEveryUserWhenNoneIsNamed()
    {
        // A plugin refreshing from a remote service has new rows for everyone.
        var raised = Capture();

        _manager.Invalidate("Acme.Trending");

        Assert.Equal(2, raised.Count);
        Assert.All(raised, e => Assert.False(e.AllStale));
        Assert.All(raised, e => Assert.Equal(["acme.trending"], e.StaleSectionKeys));
    }

    [Fact]
    public void GetId_IsTheKeyPlusWhatTheRowIsAbout()
    {
        // Clients match these across refreshes, so the format is fixed here and nowhere else.
        var parentId = Guid.NewGuid();

        Assert.Equal("resume", HomeSectionManager.GetId("Resume"));
        Assert.Equal($"genre-{parentId:N}", HomeSectionManager.GetId(HomeSectionKeys.Genre, parentId));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _manager.Dispose();
            _memoryCache.Dispose();
        }

        base.Dispose(disposing);
    }

    private List<HomeSectionInvalidatedEventArgs> Capture()
    {
        var raised = new List<HomeSectionInvalidatedEventArgs>();
        _manager.Invalidated += (_, e) => raised.Add(e);
        return raised;
    }

    private void SetLayout(params HomeSection[] sections)
        => _manager.SetSections(_user.Id, Client, sections);

    private Task<IReadOnlyList<HomeSectionDto>> GetSectionsAsync()
        => _manager.GetHomeSectionsAsync(_user, Client, ItemLimit, CancellationToken.None);
}
