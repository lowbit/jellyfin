using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Jellyfin.Api.Constants;
using Jellyfin.Api.Controllers;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.Controllers;

/// <summary>
/// Covers how <see cref="DisplayPreferencesController"/> handles the home sections a legacy client
/// cannot describe.
/// </summary>
public class DisplayPreferencesControllerTests
{
    private const string Client = "emby";
    private const string PreferencesId = "usersettings";

    private static readonly Guid _userId = Guid.NewGuid();
    private static readonly Guid _collectionId = Guid.NewGuid();

    [Fact]
    public void UpdateDisplayPreferences_WithoutHomeSections_LeavesTheLayoutAlone()
    {
        // A request that only changes an unrelated preference must not empty the home screen.
        var stored = CreatePreferences(
            new HomeSection { Order = 0, Key = HomeSectionKeys.SmallLibraryTiles },
            new HomeSection { Order = 1, Key = HomeSectionKeys.PinnedCollection, ItemIds = [_collectionId] });

        Update(stored, new Dictionary<string, string?> { ["skipForwardLength"] = "30000" });

        Assert.Collection(
            stored.HomeSections.OrderBy(section => section.Order),
            section => Assert.Equal(HomeSectionKeys.SmallLibraryTiles, section.Key),
            section => Assert.Equal(HomeSectionKeys.PinnedCollection, section.Key));
    }

    [Fact]
    public void UpdateDisplayPreferences_KeepsSectionsTheClientCannotDescribe()
    {
        var stored = CreatePreferences(
            new HomeSection { Order = 0, Key = HomeSectionKeys.SmallLibraryTiles },
            new HomeSection { Order = 1, Key = HomeSectionKeys.PinnedCollection, ItemIds = [_collectionId], MaxItems = 5 },
            new HomeSection { Order = 2, Key = HomeSectionKeys.LatestMedia, Active = false },
            new HomeSection { Order = 3, Key = HomeSectionKeys.NextUp });

        // What the client was given is slots 0 and 1, being the visible sections it can render.
        Update(stored, new Dictionary<string, string?>
        {
            ["homesection0"] = "nextup",
            ["homesection1"] = "resume"
        });

        var sections = stored.HomeSections.OrderBy(section => section.Order).ToList();

        Assert.Equal([0, 1, 2, 3], sections.Select(section => section.Order));
        Assert.Equal(HomeSectionKeys.NextUp, sections[0].Key);

        // The pinned row keeps its position, its item and its limit.
        Assert.Equal(HomeSectionKeys.PinnedCollection, sections[1].Key);
        Assert.Equal([_collectionId], sections[1].ItemIds);
        Assert.Equal(5, sections[1].MaxItems);

        // As does the hidden one, which the client was never shown either.
        Assert.Equal(HomeSectionKeys.LatestMedia, sections[2].Key);
        Assert.False(sections[2].Active);

        Assert.Equal(HomeSectionKeys.Resume, sections[3].Key);
    }

    [Fact]
    public void GetDisplayPreferences_OnlyReportsSectionsTheClientCanDescribe()
    {
        var stored = CreatePreferences(
            new HomeSection { Order = 0, Key = HomeSectionKeys.SmallLibraryTiles },
            new HomeSection { Order = 1, Key = HomeSectionKeys.PinnedCollection, ItemIds = [_collectionId] },
            new HomeSection { Order = 2, Key = HomeSectionKeys.LatestMedia, Active = false },
            new HomeSection { Order = 3, Key = HomeSectionKeys.NextUp });

        var manager = new Mock<IDisplayPreferencesManager>();
        manager
            .Setup(x => x.GetDisplayPreferences(_userId, PreferencesId.GetMD5(), Client))
            .Returns(stored);
        manager
            .Setup(x => x.GetItemDisplayPreferences(_userId, PreferencesId.GetMD5(), Client))
            .Returns(new ItemDisplayPreferences(_userId, PreferencesId.GetMD5(), Client));
        manager
            .Setup(x => x.ListCustomItemDisplayPreferences(_userId, PreferencesId.GetMD5(), Client))
            .Returns(new Dictionary<string, string?>());

        var result = CreateController(manager.Object)
            .GetDisplayPreferences(PreferencesId, _userId, Client);

        var prefs = Assert.IsType<DisplayPreferencesDto>(result.Value).CustomPrefs;

        // Slots stay contiguous, so the hidden and pinned rows leave no gap behind.
        Assert.Equal("smalllibrarytiles", prefs["homesection0"]);
        Assert.Equal("nextup", prefs["homesection1"]);
        Assert.False(prefs.ContainsKey("homesection2"));
    }

    private static DisplayPreferences CreatePreferences(params HomeSection[] sections)
    {
        var preferences = new DisplayPreferences(_userId, PreferencesId.GetMD5(), Client);

        foreach (var section in sections)
        {
            preferences.HomeSections.Add(section);
        }

        return preferences;
    }

    private static DisplayPreferencesController CreateController(IDisplayPreferencesManager manager)
    {
        var claims = new[]
        {
            new Claim(InternalClaimTypes.UserId, _userId.ToString("N", CultureInfo.InvariantCulture)),
            new Claim(InternalClaimTypes.IsApiKey, bool.FalseString)
        };

        return new DisplayPreferencesController(manager, NullLogger<DisplayPreferencesController>.Instance)
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

    private static void Update(DisplayPreferences stored, Dictionary<string, string?> customPrefs)
    {
        var manager = new Mock<IDisplayPreferencesManager>();
        manager
            .Setup(x => x.GetDisplayPreferences(_userId, PreferencesId.GetMD5(), Client))
            .Returns(stored);
        manager
            .Setup(x => x.GetItemDisplayPreferences(_userId, PreferencesId.GetMD5(), Client))
            .Returns(new ItemDisplayPreferences(_userId, PreferencesId.GetMD5(), Client));

        CreateController(manager.Object).UpdateDisplayPreferences(
            PreferencesId,
            _userId,
            Client,
            new DisplayPreferencesDto { CustomPrefs = customPrefs });
    }
}
