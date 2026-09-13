using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Api.Helpers;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Extensions;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.HomeSections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers;

/// <summary>
/// Builds the rows of the home screen on the server, with their items included.
/// </summary>
/// <remarks>
/// Clients hardcode which rows exist and how each is queried, so a section that takes a parameter,
/// such as one collection or one genre, cannot be expressed. This endpoint describes the rows
/// instead, so adding a section type does not require a client release.
/// </remarks>
[Route("")]
[Authorize]
public class HomeSectionsController : BaseJellyfinApiController
{
    private const int DefaultItemLimit = 16;

    private readonly IUserManager _userManager;
    private readonly IHomeSectionManager _homeSectionManager;
    private readonly IServerConfigurationManager _configurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeSectionsController"/> class.
    /// </summary>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="homeSectionManager">Instance of the <see cref="IHomeSectionManager"/> interface.</param>
    /// <param name="configurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    public HomeSectionsController(
        IUserManager userManager,
        IHomeSectionManager homeSectionManager,
        IServerConfigurationManager configurationManager)
    {
        _userManager = userManager;
        _homeSectionManager = homeSectionManager;
        _configurationManager = configurationManager;
    }

    /// <summary>
    /// Gets a user's home sections with their items.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="client">The client the layout belongs to.</param>
    /// <param name="itemLimit">Default number of items per section, when a section sets no limit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Home sections returned.</response>
    /// <response code="404">User not found.</response>
    /// <returns>The sections, in display order.</returns>
    [HttpGet("HomeSections")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<HomeSectionDto>>> GetHomeSections(
        [FromQuery] Guid? userId,
        [FromQuery] string client = "emby",
        [FromQuery] int itemLimit = DefaultItemLimit,
        CancellationToken cancellationToken = default)
    {
        var requestUserId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(requestUserId);
        if (user is null)
        {
            return NotFound();
        }

        // Reading is only self or administrator. Requiring the preference permission here would
        // leave a user who may not change their layout with no home screen at all.
        if (!RequestHelpers.AssertCanUpdateUser(User, user, false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "User is not allowed to view this user's home sections.");
        }

        var sections = await _homeSectionManager.GetHomeSectionsAsync(user, client, itemLimit, cancellationToken).ConfigureAwait(false);

        return new ActionResult<IReadOnlyList<HomeSectionDto>>(sections);
    }

    /// <summary>
    /// Gets the kinds of section this server can build.
    /// </summary>
    /// <response code="200">Providers returned.</response>
    /// <returns>The providers, including any contributed by plugins, sorted by name.</returns>
    [HttpGet("HomeSections/Providers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<HomeSectionProviderDto>> GetHomeSectionProviders()
    {
        // Discovery order is assembly order, which is meaningless to a person picking a row.
        return _homeSectionManager.GetProviders()
            .Select(provider => new HomeSectionProviderDto
            {
                Key = provider.Key.ToLowerInvariant(),
                Name = provider.Name,
                ItemKind = provider.ItemKind
            })
            .OrderBy(provider => provider.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets a user's home screen layout, without items.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="client">The client the layout belongs to.</param>
    /// <response code="200">Layout returned.</response>
    /// <response code="404">User not found.</response>
    /// <returns>The configured sections, including hidden ones.</returns>
    [HttpGet("HomeSections/Config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<IReadOnlyList<HomeSectionConfigDto>> GetHomeSectionConfig(
        [FromQuery] Guid? userId,
        [FromQuery] string client = "emby")
    {
        var requestUserId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(requestUserId);
        if (user is null)
        {
            return NotFound();
        }

        // Without this any signed in user could read another user's layout by passing their id.
        // Administrators are still allowed through, which is what makes managing another account
        // work. Reading is not gated on the preference permission, only writing is.
        if (!RequestHelpers.AssertCanUpdateUser(User, user, false))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "User is not allowed to view this user's home sections.");
        }

        return _homeSectionManager.GetEffectiveSections(requestUserId, client)
            .Select(ToConfigDto)
            .ToList();
    }

    /// <summary>
    /// Replaces a user's home screen layout.
    /// </summary>
    /// <param name="sections">The sections to store. Display order is the list order.</param>
    /// <param name="userId">The user id.</param>
    /// <param name="client">The client the layout belongs to.</param>
    /// <response code="204">Layout updated.</response>
    /// <response code="400">A section is missing the item it needs, or appears twice.</response>
    /// <response code="404">User not found.</response>
    /// <returns>A <see cref="NoContentResult"/>.</returns>
    [HttpPost("HomeSections/Config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult UpdateHomeSectionConfig(
        [FromBody] IReadOnlyList<HomeSectionConfigDto> sections,
        [FromQuery] Guid? userId,
        [FromQuery] string client = "emby")
    {
        var requestUserId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(requestUserId);
        if (user is null)
        {
            return NotFound();
        }

        // Without this any signed in user could read or rewrite another user's layout by passing
        // their id. Administrators are still allowed through, which is what makes managing
        // another account work.
        if (!RequestHelpers.AssertCanUpdateUser(User, user, true))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "User is not allowed to manage this user's home sections.");
        }

        var problem = Validate(sections);
        if (problem is not null)
        {
            return BadRequest(problem);
        }

        _homeSectionManager.SetSections(
            requestUserId,
            client,
            sections.Select(section => new HomeSection
            {
                Key = section.Key,
                ItemId = section.ItemId,
                MaxItems = section.MaxItems,
                Active = section.Active
            }).ToList());

        return NoContent();
    }

    /// <summary>
    /// Clears a user's home screen layout so the defaults apply again.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="client">The client the layout belongs to.</param>
    /// <response code="204">Layout reset.</response>
    /// <response code="404">User not found.</response>
    /// <returns>A <see cref="NoContentResult"/>.</returns>
    [HttpDelete("HomeSections/Config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult ResetHomeSectionConfig(
        [FromQuery] Guid? userId,
        [FromQuery] string client = "emby")
    {
        var requestUserId = RequestHelpers.GetUserId(User, userId);
        var user = _userManager.GetUserById(requestUserId);
        if (user is null)
        {
            return NotFound();
        }

        if (!RequestHelpers.AssertCanUpdateUser(User, user, true))
        {
            return StatusCode(StatusCodes.Status403Forbidden, "User is not allowed to manage this user's home sections.");
        }

        _homeSectionManager.ResetSections(requestUserId, client);
        return NoContent();
    }

    /// <summary>
    /// Gets the home screen layout new users start with.
    /// </summary>
    /// <response code="200">Defaults returned.</response>
    /// <returns>The layout new users are given, in display order.</returns>
    [HttpGet("HomeSections/Defaults")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<HomeSectionConfigDto>> GetDefaultHomeSections()
    {
        // The built-in layout when nothing is configured, since that is what new users are
        // actually given. An empty list would read as "no rows at all".
        return _homeSectionManager.GetDefaultSections()
            .Select(ToConfigDto)
            .ToList();
    }

    /// <summary>
    /// Sets the home screen layout new users start with.
    /// </summary>
    /// <param name="sections">The default sections. An empty list restores the built-in layout.</param>
    /// <response code="204">Defaults updated.</response>
    /// <response code="400">A section is missing the item it needs, or appears twice.</response>
    /// <returns>A <see cref="NoContentResult"/>.</returns>
    /// <remarks>
    /// Only affects users who have not configured their own layout. Nobody's existing home screen
    /// is rewritten by changing this.
    /// </remarks>
    [HttpPost("HomeSections/Defaults")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult UpdateDefaultHomeSections([FromBody] IReadOnlyList<HomeSectionConfigDto> sections)
    {
        var problem = Validate(sections);
        if (problem is not null)
        {
            return BadRequest(problem);
        }

        _configurationManager.Configuration.DefaultHomeSections = sections
            .Select(section => new HomeSectionOptions
            {
                Key = section.Key.ToLowerInvariant(),
                ItemId = section.ItemId,
                MaxItems = section.MaxItems,
                Active = section.Active
            })
            .ToArray();

        _configurationManager.SaveConfiguration();

        return NoContent();
    }

    private static HomeSectionConfigDto ToConfigDto(HomeSection section)
        => new()
        {
            Key = section.Key,
            ItemId = section.ItemId,
            MaxItems = section.MaxItems,
            Active = section.Active
        };

    /// <summary>
    /// Checks a layout before it is stored.
    /// </summary>
    /// <remarks>
    /// A section whose provider takes an item is rejected without one, since it would render as a
    /// heading over nothing. The same section twice is rejected because its rows would share an
    /// id. A key nothing claims is allowed through: it is what a row from an uninstalled plugin
    /// looks like, and refusing it would make such a layout impossible to save again.
    /// </remarks>
    /// <returns>What is wrong, or null when the layout is acceptable.</returns>
    private string? Validate(IReadOnlyList<HomeSectionConfigDto> sections)
    {
        var seen = new HashSet<(string Key, Guid? ItemId)>();

        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section.Key))
            {
                return "A section requires a key.";
            }

            var hasItem = section.ItemId.HasValue && !section.ItemId.Value.IsEmpty();
            var provider = _homeSectionManager.GetProvider(section.Key);

            if (provider?.ItemKind is not null && !hasItem)
            {
                return $"A {section.Key} section requires an itemId.";
            }

            if (!seen.Add((section.Key.ToLowerInvariant(), hasItem ? section.ItemId : null)))
            {
                return $"The {section.Key} section appears more than once.";
            }
        }

        return null;
    }
}
