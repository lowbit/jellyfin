using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Jellyfin.Api.Helpers;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Display Preferences Controller.
/// </summary>
[Authorize]
[Tags("DisplayPreference")]
public class DisplayPreferencesController : BaseJellyfinApiController
{
    private readonly IDisplayPreferencesManager _displayPreferencesManager;
    private readonly ILogger<DisplayPreferencesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisplayPreferencesController"/> class.
    /// </summary>
    /// <param name="displayPreferencesManager">Instance of <see cref="IDisplayPreferencesManager"/> interface.</param>
    /// <param name="logger">Instance of <see cref="ILogger{DisplayPreferencesController}"/> interface.</param>
    public DisplayPreferencesController(IDisplayPreferencesManager displayPreferencesManager, ILogger<DisplayPreferencesController> logger)
    {
        _displayPreferencesManager = displayPreferencesManager;
        _logger = logger;
    }

    /// <summary>
    /// Get Display Preferences.
    /// </summary>
    /// <param name="displayPreferencesId">Display preferences id.</param>
    /// <param name="userId">User id.</param>
    /// <param name="client">Client.</param>
    /// <response code="200">Display preferences retrieved.</response>
    /// <returns>An <see cref="OkResult"/> containing the display preferences on success, or a <see cref="NotFoundResult"/> if the display preferences could not be found.</returns>
    [HttpGet("{displayPreferencesId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "displayPreferencesId", Justification = "Imported from ServiceStack")]
    public ActionResult<DisplayPreferencesDto> GetDisplayPreferences(
        [FromRoute, Required] string displayPreferencesId,
        [FromQuery] Guid? userId,
        [FromQuery, Required] string client)
    {
        userId = RequestHelpers.GetUserId(User, userId);

        if (!Guid.TryParse(displayPreferencesId, out var itemId))
        {
            itemId = displayPreferencesId.GetMD5();
        }

        var displayPreferences = _displayPreferencesManager.GetDisplayPreferences(userId.Value, itemId, client);
        var itemPreferences = _displayPreferencesManager.GetItemDisplayPreferences(displayPreferences.UserId, itemId, displayPreferences.Client);
        itemPreferences.ItemId = itemId;

        var dto = new DisplayPreferencesDto
        {
            Client = displayPreferences.Client,
            Id = displayPreferences.ItemId.ToString(),
            SortBy = itemPreferences.SortBy,
            SortOrder = itemPreferences.SortOrder,
            IndexBy = displayPreferences.IndexBy?.ToString(),
            RememberIndexing = itemPreferences.RememberIndexing,
            RememberSorting = itemPreferences.RememberSorting,
            ScrollDirection = displayPreferences.ScrollDirection,
            ShowBackdrop = displayPreferences.ShowBackdrop,
            ShowSidebar = displayPreferences.ShowSidebar
        };

        // Only the section types this format has always carried are reported. It is one bare name
        // per slot with nowhere to put a parameter, so a section bound to a collection or genre
        // cannot be described, nor can a hidden one, nor a kind added by a plugin. Reporting them
        // anyway would send clients a type they cannot render and have historically crashed on, so
        // they are skipped and the remaining slots renumbered to stay contiguous.
        var legacySlot = 0;
        foreach (var homeSection in displayPreferences.HomeSections
                     .Where(IsLegacyRepresentable)
                     .OrderBy(section => section.Order))
        {
            dto.CustomPrefs["homesection" + legacySlot] = homeSection.Key;
            legacySlot++;
        }

        dto.CustomPrefs["chromecastVersion"] = displayPreferences.ChromecastVersion.ToString().ToLowerInvariant();
        dto.CustomPrefs["skipForwardLength"] = displayPreferences.SkipForwardLength.ToString(CultureInfo.InvariantCulture);
        dto.CustomPrefs["skipBackLength"] = displayPreferences.SkipBackwardLength.ToString(CultureInfo.InvariantCulture);
        dto.CustomPrefs["enableNextVideoInfoOverlay"] = displayPreferences.EnableNextVideoInfoOverlay.ToString(CultureInfo.InvariantCulture);
        dto.CustomPrefs["tvhome"] = displayPreferences.TvHome;
        dto.CustomPrefs["dashboardTheme"] = displayPreferences.DashboardTheme;

        // Load all custom display preferences
        var customDisplayPreferences = _displayPreferencesManager.ListCustomItemDisplayPreferences(displayPreferences.UserId, itemId, displayPreferences.Client);
        foreach (var (key, value) in customDisplayPreferences)
        {
            dto.CustomPrefs.TryAdd(key, value);
        }

        return dto;
    }

    /// <summary>
    /// Update Display Preferences.
    /// </summary>
    /// <param name="displayPreferencesId">Display preferences id.</param>
    /// <param name="userId">User Id.</param>
    /// <param name="client">Client.</param>
    /// <param name="displayPreferences">New Display Preferences object.</param>
    /// <response code="204">Display preferences updated.</response>
    /// <returns>An <see cref="NoContentResult"/> on success.</returns>
    [HttpPost("{displayPreferencesId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "displayPreferencesId", Justification = "Imported from ServiceStack")]
    public ActionResult UpdateDisplayPreferences(
        [FromRoute, Required] string displayPreferencesId,
        [FromQuery] Guid? userId,
        [FromQuery, Required] string client,
        [FromBody, Required] DisplayPreferencesDto displayPreferences)
    {
        userId = RequestHelpers.GetUserId(User, userId);

        HomeSectionType[] defaults =
        {
            HomeSectionType.SmallLibraryTiles,
            HomeSectionType.Resume,
            HomeSectionType.ResumeAudio,
            HomeSectionType.ResumeBook,
            HomeSectionType.LiveTv,
            HomeSectionType.NextUp,
            HomeSectionType.LatestMedia,
            HomeSectionType.None,
        };

        if (!Guid.TryParse(displayPreferencesId, out var itemId))
        {
            itemId = displayPreferencesId.GetMD5();
        }

        var existingDisplayPreferences = _displayPreferencesManager.GetDisplayPreferences(userId.Value, itemId, client);
        existingDisplayPreferences.IndexBy = Enum.TryParse<IndexingKind>(displayPreferences.IndexBy, true, out var indexBy) ? indexBy : null;
        existingDisplayPreferences.ShowBackdrop = displayPreferences.ShowBackdrop;
        existingDisplayPreferences.ShowSidebar = displayPreferences.ShowSidebar;

        existingDisplayPreferences.ScrollDirection = displayPreferences.ScrollDirection;
        existingDisplayPreferences.ChromecastVersion = displayPreferences.CustomPrefs.TryGetValue("chromecastVersion", out var chromecastVersion)
                                                       && !string.IsNullOrEmpty(chromecastVersion)
            ? Enum.Parse<ChromecastVersion>(chromecastVersion, true)
            : ChromecastVersion.Stable;
        displayPreferences.CustomPrefs.Remove("chromecastVersion");

        existingDisplayPreferences.EnableNextVideoInfoOverlay = !displayPreferences.CustomPrefs.TryGetValue("enableNextVideoInfoOverlay", out var enableNextVideoInfoOverlay)
                                                                || string.IsNullOrEmpty(enableNextVideoInfoOverlay)
                                                                || bool.Parse(enableNextVideoInfoOverlay);
        displayPreferences.CustomPrefs.Remove("enableNextVideoInfoOverlay");

        existingDisplayPreferences.SkipBackwardLength = displayPreferences.CustomPrefs.TryGetValue("skipBackLength", out var skipBackLength)
                                                        && !string.IsNullOrEmpty(skipBackLength)
            ? int.Parse(skipBackLength, CultureInfo.InvariantCulture)
            : 15000;
        displayPreferences.CustomPrefs.Remove("skipBackLength");

        existingDisplayPreferences.SkipForwardLength = displayPreferences.CustomPrefs.TryGetValue("skipForwardLength", out var skipForwardLength)
                                                       && !string.IsNullOrEmpty(skipForwardLength)
            ? int.Parse(skipForwardLength, CultureInfo.InvariantCulture)
            : 15000;
        displayPreferences.CustomPrefs.Remove("skipForwardLength");

        existingDisplayPreferences.DashboardTheme = displayPreferences.CustomPrefs.TryGetValue("dashboardTheme", out var theme)
            ? theme
            : string.Empty;
        displayPreferences.CustomPrefs.Remove("dashboardTheme");

        existingDisplayPreferences.TvHome = displayPreferences.CustomPrefs.TryGetValue("tvhome", out var home)
            ? home
            : string.Empty;
        displayPreferences.CustomPrefs.Remove("tvhome");

        var legacyKeys = displayPreferences.CustomPrefs.Keys
            .Where(key => key.StartsWith("homesection", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sentSections = new List<HomeSection>(legacyKeys.Count);

        foreach (var key in legacyKeys)
        {
            var order = int.Parse(key.AsSpan().Slice("homesection".Length), CultureInfo.InvariantCulture);
            if (!Enum.TryParse<HomeSectionType>(displayPreferences.CustomPrefs[key], true, out var type))
            {
                type = order < defaults.Length ? defaults[order] : HomeSectionType.None;
            }

            displayPreferences.CustomPrefs.Remove(key);
            sentSections.Add(new HomeSection { Order = order, Key = ToKey(type) });
        }

        // Slot ten sorts before slot two as a string, so the order comes from the number.
        sentSections.Sort(static (x, y) => x.Order.CompareTo(y.Order));

        // A request that carries no sections at all, such as one that only changes the skip
        // length, must leave the layout alone rather than empty it.
        if (sentSections.Count > 0)
        {
            MergeLegacyHomeSections(existingDisplayPreferences, sentSections);
        }

        foreach (var key in displayPreferences.CustomPrefs.Keys.Where(key => key.StartsWith("landing-", StringComparison.OrdinalIgnoreCase)))
        {
            var viewType = displayPreferences.CustomPrefs[key];

            if (string.IsNullOrEmpty(viewType))
            {
                displayPreferences.CustomPrefs.Remove(key);
                continue;
            }

            if (!Enum.TryParse<ViewType>(viewType, true, out _))
            {
                _logger.LogError("Invalid ViewType: {LandingScreenOption}", viewType);
                displayPreferences.CustomPrefs.Remove(key);
            }
        }

        var itemPrefs = _displayPreferencesManager.GetItemDisplayPreferences(existingDisplayPreferences.UserId, itemId, existingDisplayPreferences.Client);
        itemPrefs.SortBy = displayPreferences.SortBy ?? "SortName";
        itemPrefs.SortOrder = displayPreferences.SortOrder;
        itemPrefs.RememberIndexing = displayPreferences.RememberIndexing;
        itemPrefs.RememberSorting = displayPreferences.RememberSorting;
        itemPrefs.ItemId = itemId;

        // Set all remaining custom preferences.
        _displayPreferencesManager.SetCustomItemDisplayPreferences(userId.Value, itemId, existingDisplayPreferences.Client, displayPreferences.CustomPrefs);
        _displayPreferencesManager.UpdateItemDisplayPreferences(itemPrefs);
        _displayPreferencesManager.UpdateDisplayPreferences(existingDisplayPreferences);
        return NoContent();
    }

    /// <summary>
    /// Replaces the sections a legacy client can describe, leaving the rest where they are.
    /// </summary>
    /// <remarks>
    /// A legacy client sends the whole layout back as one type name per slot, so taking it at face
    /// value would delete the sections it was never shown. Those keep the position they had and
    /// the sent sections fill the slots around them.
    /// </remarks>
    /// <param name="preferences">The stored preferences.</param>
    /// <param name="sentSections">The sections the client sent, in slot order.</param>
    private static void MergeLegacyHomeSections(DisplayPreferences preferences, IReadOnlyList<HomeSection> sentSections)
    {
        var keptSections = preferences.HomeSections
            .Where(section => !IsLegacyRepresentable(section))
            .OrderBy(section => section.Order)
            .Select(section => new HomeSection
            {
                Order = section.Order,
                Key = section.Key,
                ItemId = section.ItemId,
                MaxItems = section.MaxItems,
                Active = section.Active
            })
            .ToList();

        var merged = new List<HomeSection>(keptSections.Count + sentSections.Count);
        var keptIndex = 0;
        var sentIndex = 0;

        while (merged.Count < keptSections.Count + sentSections.Count)
        {
            var keepsPosition = keptIndex < keptSections.Count
                && (keptSections[keptIndex].Order <= merged.Count || sentIndex >= sentSections.Count);

            merged.Add(keepsPosition ? keptSections[keptIndex++] : sentSections[sentIndex++]);
        }

        preferences.HomeSections.Clear();

        for (var order = 0; order < merged.Count; order++)
        {
            var section = merged[order];
            section.Order = order;
            preferences.HomeSections.Add(section);
        }
    }

    /// <summary>
    /// Gets a value indicating whether a section is part of the layout a legacy client sees.
    /// </summary>
    /// <remarks>
    /// The legacy vocabulary is exactly <see cref="HomeSectionType"/>. Anything else, whether a
    /// section bound to an item or a kind a plugin added, is withheld, and so is a hidden section,
    /// because the format cannot express either. Both directions use this, so what a legacy client
    /// is not shown is exactly what is kept when it saves the layout back.
    /// </remarks>
    private static bool IsLegacyRepresentable(HomeSection section)
        => section.Active && Enum.TryParse<HomeSectionType>(section.Key, true, out _);

    /// <summary>
    /// Gets the provider key a legacy section type is stored as.
    /// </summary>
    private static string ToKey(HomeSectionType type)
        => type.ToString().ToLowerInvariant();
}
