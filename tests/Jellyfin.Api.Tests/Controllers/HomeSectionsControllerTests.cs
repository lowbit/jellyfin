using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Controllers;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;

using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.HomeSections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

/// <summary>
/// Covers <see cref="HomeSectionsController"/>: who may read and write a layout, what is rejected,
/// and what the provider listing looks like. Building rows is the manager's job and is tested there.
/// </summary>
public sealed class HomeSectionsControllerTests
{
    private const string Client = "emby";

    private readonly User _user = new("sections", "default", "default");
    private readonly User _otherUser = new("other", "default", "default");

    private readonly Mock<IUserManager> _userManager = new();
    private readonly Mock<IHomeSectionManager> _homeSectionManager = new();

    public HomeSectionsControllerTests()
    {
        _userManager.Setup(x => x.GetUserById(_user.Id)).Returns(_user);
        _userManager.Setup(x => x.GetUserById(_otherUser.Id)).Returns(_otherUser);

        RegisterProvider(HomeSectionKeys.Resume, "Continue Watching");
        RegisterProvider(HomeSectionKeys.PinnedCollection, "Collection", BaseItemKind.BoxSet);
        RegisterProvider(HomeSectionKeys.Genre, "Genres", BaseItemKind.Genre, allowsMultipleItems: true);
        RegisterProvider("plugin.pinned", "Pinned item", BaseItemKind.Movie);
    }

    [Fact]
    public async Task GetHomeSections_ReturnsWhatTheManagerBuilt()
    {
        IReadOnlyList<HomeSectionDto> built = [new HomeSectionDto { Id = "resume", Key = "resume" }];
        _homeSectionManager
            .Setup(x => x.GetHomeSectionsAsync(_user, Client, 16, It.IsAny<CancellationToken>()))
            .ReturnsAsync(built);

        var result = await CreateController(_user).GetHomeSections(_user.Id, Client, 16, TestContext.Current.CancellationToken);

        Assert.Same(built, result.Value);
    }

    [Fact]
    public async Task GetHomeSections_ReturnsNotFoundForAnUnknownUser()
    {
        // Only an administrator gets this far; anyone else is refused before the lookup.
        var result = await CreateController(_user, isAdministrator: true).GetHomeSections(Guid.NewGuid(), Client, 16, TestContext.Current.CancellationToken);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetHomeSections_IsRefusedForAnotherUser()
    {
        // RequestHelpers refuses a non administrator asking for another user before the controller
        // gets a say. The middleware turns this into a 403.
        await Assert.ThrowsAsync<SecurityException>(
            () => CreateController(_otherUser).GetHomeSections(_user.Id, Client, 16, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetHomeSections_IsAllowedWithoutThePreferencePermission()
    {
        // Reading is not managing. A user who may not change their layout still has a home screen.
        _user.EnableUserPreferenceAccess = false;
        _homeSectionManager
            .Setup(x => x.GetHomeSectionsAsync(_user, Client, 16, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateController(_user).GetHomeSections(_user.Id, Client, 16, TestContext.Current.CancellationToken);

        Assert.NotNull(result.Value);
    }

    [Fact]
    public void GetHomeSectionProviders_ListsWhatCanBeAddedSortedByName()
    {
        var providers = CreateController(_user).GetHomeSectionProviders().Value;

        Assert.NotNull(providers);
        Assert.Equal(["Collection", "Continue Watching", "Genres", "Pinned item"], providers!.Select(provider => provider.Name));
        Assert.Equal(BaseItemKind.BoxSet, providers[0].ItemKind);
        Assert.Null(providers[1].ItemKind);
    }

    [Fact]
    public void GetHomeSectionConfig_ReturnsHiddenSectionsToo()
    {
        SetLayout(
            new HomeSection { Order = 0, Key = HomeSectionKeys.Resume },
            new HomeSection { Order = 1, Key = HomeSectionKeys.LatestMedia, Active = false });

        var config = CreateController(_user).GetHomeSectionConfig(_user.Id, Client).Value;

        Assert.NotNull(config);
        Assert.Equal(2, config!.Count);
        Assert.False(config[1].Active);
    }

    [Fact]
    public void UpdateHomeSectionConfig_NeedsThePreferencePermission()
    {
        _user.EnableUserPreferenceAccess = false;

        var result = CreateController(_user).UpdateHomeSectionConfig(
            [new HomeSectionConfigDto { Key = HomeSectionKeys.Resume }],
            _user.Id,
            Client);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result).StatusCode);
        VerifyNothingStored();
    }

    [Fact]
    public void UpdateHomeSectionConfig_RejectsASectionWithoutItsItem()
    {
        var result = CreateController(_user).UpdateHomeSectionConfig(
            [new HomeSectionConfigDto { Key = HomeSectionKeys.PinnedCollection }],
            _user.Id,
            Client);

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNothingStored();
    }

    [Fact]
    public void UpdateHomeSectionConfig_RejectsTheSameSectionTwice()
    {
        // Two rows would share an id, and a client could not tell them apart.
        var genreId = Guid.NewGuid();

        var result = CreateController(_user).UpdateHomeSectionConfig(
            [
                new HomeSectionConfigDto { Key = HomeSectionKeys.Genre, ItemIds = [genreId] },
                new HomeSectionConfigDto { Key = "GENRE", ItemIds = [genreId] }
            ],
            _user.Id,
            Client);

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNothingStored();
    }

    [Fact]
    public void UpdateHomeSectionConfig_AllowsTheSameProviderBoundToDifferentItems()
    {
        // What a layout saved before one section could hold several genres looks like.
        var stored = CaptureStored();

        var result = CreateController(_user).UpdateHomeSectionConfig(
            [
                new HomeSectionConfigDto { Key = HomeSectionKeys.Genre, ItemIds = [Guid.NewGuid()] },
                new HomeSectionConfigDto { Key = HomeSectionKeys.Genre, ItemIds = [Guid.NewGuid()] }
            ],
            _user.Id,
            Client);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(2, stored.Value!.Count);
    }

    [Fact]
    public void UpdateHomeSectionConfig_AcceptsASectionThatTakesSeveralItemsWithNone()
    {
        // Every genre unticked draws nothing, which is a valid state of the section, not an error.
        var stored = CaptureStored();

        var result = CreateController(_user).UpdateHomeSectionConfig(
            [new HomeSectionConfigDto { Key = HomeSectionKeys.Genre }],
            _user.Id,
            Client);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(Assert.Single(stored.Value!).ItemIds);
    }

    [Fact]
    public void UpdateHomeSectionConfig_RejectsOneItemInTwoSections()
    {
        // Both would draw the same row.
        var genreId = Guid.NewGuid();

        var result = CreateController(_user).UpdateHomeSectionConfig(
            [
                new HomeSectionConfigDto { Key = HomeSectionKeys.Genre, ItemIds = [genreId, Guid.NewGuid()] },
                new HomeSectionConfigDto { Key = HomeSectionKeys.Genre, ItemIds = [genreId] }
            ],
            _user.Id,
            Client);

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNothingStored();
    }

    [Fact]
    public void UpdateHomeSectionConfig_RejectsTheUnboundSectionTwice()
    {
        // Two of them could only ever draw the same rows.
        var result = CreateController(_user).UpdateHomeSectionConfig(
            [
                new HomeSectionConfigDto { Key = HomeSectionKeys.Genre },
                new HomeSectionConfigDto { Key = HomeSectionKeys.Genre }
            ],
            _user.Id,
            Client);

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNothingStored();
    }

    [Fact]
    public void UpdateHomeSectionConfig_RejectsASectionThatTakesOneItemWithSeveral()
    {
        var result = CreateController(_user).UpdateHomeSectionConfig(
            [new HomeSectionConfigDto { Key = "plugin.pinned", ItemIds = [Guid.NewGuid(), Guid.NewGuid()] }],
            _user.Id,
            Client);

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNothingStored();
    }

    [Fact]
    public void UpdateHomeSectionConfig_DropsEmptyAndRepeatedItems()
    {
        var genreId = Guid.NewGuid();
        var stored = CaptureStored();

        var result = CreateController(_user).UpdateHomeSectionConfig(
            [new HomeSectionConfigDto { Key = HomeSectionKeys.Genre, ItemIds = [genreId, Guid.Empty, genreId] }],
            _user.Id,
            Client);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal([genreId], Assert.Single(stored.Value!).ItemIds);
    }

    [Fact]
    public void UpdateHomeSectionConfig_RejectsAnEmptyKey()
    {
        var result = CreateController(_user).UpdateHomeSectionConfig(
            [new HomeSectionConfigDto { Key = " " }],
            _user.Id,
            Client);

        Assert.IsType<BadRequestObjectResult>(result);
        VerifyNothingStored();
    }

    [Fact]
    public void UpdateHomeSectionConfig_AcceptsAKeyNothingClaims()
    {
        // A row from an uninstalled plugin must not make the layout impossible to save again.
        var stored = CaptureStored();

        var result = CreateController(_user).UpdateHomeSectionConfig(
            [new HomeSectionConfigDto { Key = "gone.plugin" }],
            _user.Id,
            Client);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal("gone.plugin", Assert.Single(stored.Value!).Key);
    }

    [Fact]
    public void GetHomeSectionProviders_SaysWhichTakeSeveralItems()
    {
        var providers = CreateController(_user).GetHomeSectionProviders().Value;
        Assert.NotNull(providers);

        Assert.True(providers.Single(provider => provider.Key == HomeSectionKeys.Genre).AllowsMultipleItems);
        Assert.False(providers.Single(provider => provider.Key == HomeSectionKeys.PinnedCollection).AllowsMultipleItems);
    }

    [Fact]
    public void UpdateHomeSectionConfig_StoresWhatWasSent()
    {
        var itemId = Guid.NewGuid();
        var stored = CaptureStored();

        var result = CreateController(_user).UpdateHomeSectionConfig(
            [
                new HomeSectionConfigDto { Key = HomeSectionKeys.Resume, MaxItems = 8 },
                new HomeSectionConfigDto { Key = HomeSectionKeys.Genre, ItemIds = [itemId], Active = false }
            ],
            _user.Id,
            Client);

        Assert.IsType<NoContentResult>(result);
        Assert.NotNull(stored.Value);
        Assert.Equal(8, stored.Value![0].MaxItems);
        Assert.Equal([itemId], stored.Value[1].ItemIds);
        Assert.False(stored.Value[1].Active);
    }

    [Fact]
    public void ResetHomeSectionConfig_ClearsTheLayout()
    {
        var result = CreateController(_user).ResetHomeSectionConfig(_user.Id, Client);

        Assert.IsType<NoContentResult>(result);
        _homeSectionManager.Verify(x => x.ResetSections(_user.Id, Client), Times.Once);
    }

    [Fact]
    public void GetDefaultHomeSections_ReturnsWhatNewUsersAreGiven()
    {
        // Not the raw configuration, which is empty until an administrator saves one.
        _homeSectionManager
            .Setup(x => x.GetDefaultSections())
            .Returns([new HomeSection { Order = 0, Key = HomeSectionKeys.SmallLibraryTiles }]);

        var defaults = CreateController(_user).GetDefaultHomeSections().Value;

        Assert.Equal(HomeSectionKeys.SmallLibraryTiles, Assert.Single(defaults!).Key);
    }

    [Fact]
    public void UpdateDefaultHomeSections_RejectsASectionWithoutItsItem()
    {
        var result = CreateController(_user).UpdateDefaultHomeSections(
            [new HomeSectionConfigDto { Key = HomeSectionKeys.PinnedCollection }]);

        Assert.IsType<BadRequestObjectResult>(result);
        _homeSectionManager.Verify(x => x.SetDefaultSections(It.IsAny<IReadOnlyList<HomeSection>>()), Times.Never);
    }

    [Fact]
    public void UpdateDefaultHomeSections_HandsTheLayoutToTheManager()
    {
        IReadOnlyList<HomeSection>? stored = null;
        _homeSectionManager
            .Setup(x => x.SetDefaultSections(It.IsAny<IReadOnlyList<HomeSection>>()))
            .Callback<IReadOnlyList<HomeSection>>(sections => stored = sections);

        var result = CreateController(_user).UpdateDefaultHomeSections(
            [new HomeSectionConfigDto { Key = "NextUp", MaxItems = 12 }]);

        Assert.IsType<NoContentResult>(result);
        var section = Assert.Single(stored!);
        Assert.Equal("NextUp", section.Key);
        Assert.Equal(12, section.MaxItems);
    }

    private void RegisterProvider(string key, string name, BaseItemKind? itemKind = null, bool allowsMultipleItems = false)
    {
        var provider = new Mock<IHomeSectionProvider>();
        provider.SetupGet(x => x.Key).Returns(key);
        provider.SetupGet(x => x.Name).Returns(name);
        provider.SetupGet(x => x.ItemKind).Returns(itemKind);
        provider.SetupGet(x => x.AllowsMultipleItems).Returns(allowsMultipleItems);

        _homeSectionManager
            .Setup(x => x.GetProvider(It.Is<string>(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))))
            .Returns(provider.Object);

        var existing = _homeSectionManager.Object.GetProviders() ?? [];
        _homeSectionManager
            .Setup(x => x.GetProviders())
            .Returns([.. existing, provider.Object]);
    }

    private void SetLayout(params HomeSection[] sections)
    {
        _homeSectionManager
            .Setup(x => x.GetEffectiveSections(_user.Id, Client))
            .Returns(sections);
    }

    private StrongBox<IReadOnlyList<HomeSection>?> CaptureStored()
    {
        var box = new StrongBox<IReadOnlyList<HomeSection>?>();
        _homeSectionManager
            .Setup(x => x.SetSections(_user.Id, Client, It.IsAny<IReadOnlyList<HomeSection>>()))
            .Callback<Guid, string, IReadOnlyList<HomeSection>>((_, _, sections) => box.Value = sections);
        return box;
    }

    private void VerifyNothingStored()
        => _homeSectionManager.Verify(
            x => x.SetSections(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<HomeSection>>()),
            Times.Never);

    private HomeSectionsController CreateController(User caller, bool isAdministrator = false)
    {
        var claims = new[]
        {
            new Claim(InternalClaimTypes.UserId, caller.Id.ToString("N", CultureInfo.InvariantCulture)),
            new Claim(InternalClaimTypes.IsApiKey, bool.FalseString),
            new Claim(ClaimTypes.Role, isAdministrator ? UserRoles.Administrator : UserRoles.User)
        };

        return new HomeSectionsController(_userManager.Object, _homeSectionManager.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, string.Empty))
                }
            }
        };
    }

    private sealed class StrongBox<T>
    {
        public T? Value { get; set; }
    }
}
