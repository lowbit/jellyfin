using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.HomeSections;
using MediaBrowser.Model.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Implementations.HomeSections;

/// <summary>
/// Stores home screen layouts, builds them through the registered providers, and decides when
/// the built result stops being current.
/// </summary>
/// <remarks>
/// <para>
/// Built rows are cached per section for a few minutes, so a change to one row does not rebuild
/// the others. Entries are keyed by generation numbers rather than evicted directly, because
/// <see cref="IMemoryCache"/> cannot enumerate its keys: one per user, bumped when everything went
/// stale, and one per user and provider key, bumped when only that provider's rows did. The
/// orphaned entries fall out on their own expiry.
/// </para>
/// <para>
/// Everything that reacts to stale sections listens to <see cref="Invalidated"/> rather than
/// hooking the library and user data events itself, so the rules live here only.
/// </para>
/// </remarks>
public sealed class HomeSectionManager : IHomeSectionManager, IDisposable
{
    /// <summary>
    /// The display preferences row home sections have always been stored against.
    /// </summary>
    /// <remarks>
    /// The legacy display preferences API derives this from the literal "usersettings", so reusing
    /// it means the new API reads and writes the same rows the old one does.
    /// </remarks>
    private static readonly Guid SettingsItemId = "usersettings".GetMD5();

    /// <summary>
    /// The layout used when neither the user nor the administrator has configured one.
    /// </summary>
    /// <remarks>
    /// Matches what Jellyfin has always shown, so upgrading does not rearrange anyone's home
    /// screen. The richer section types are opt-in.
    /// </remarks>
    private static readonly string[] BuiltInDefaults =
    [
        HomeSectionKeys.SmallLibraryTiles,
        HomeSectionKeys.Resume,
        HomeSectionKeys.ResumeAudio,
        HomeSectionKeys.ResumeBook,
        HomeSectionKeys.LiveTv,
        HomeSectionKeys.NextUp,
        HomeSectionKeys.LatestMedia
    ];

    private readonly IDbContextFactory<JellyfinDbContext> _dbContextFactory;
    private readonly IServerConfigurationManager _configurationManager;
    private readonly IMemoryCache _memoryCache;
    private readonly ILibraryManager _libraryManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IUserManager _userManager;
    private readonly ILogger<HomeSectionManager> _logger;

    private readonly ConcurrentDictionary<Guid, long> _generations = new();
    private readonly ConcurrentDictionary<(Guid UserId, string Key), long> _keyGenerations = new();

    private Dictionary<string, IHomeSectionProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<IHomeSectionProvider> _orderedProviders = [];
    private string[] _userDataKeys = [];

    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeSectionManager"/> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    /// <param name="configurationManager">The server configuration manager.</param>
    /// <param name="memoryCache">Instance of the <see cref="IMemoryCache"/> interface.</param>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="userDataManager">Instance of the <see cref="IUserDataManager"/> interface.</param>
    /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
    /// <param name="logger">The logger.</param>
    public HomeSectionManager(
        IDbContextFactory<JellyfinDbContext> dbContextFactory,
        IServerConfigurationManager configurationManager,
        IMemoryCache memoryCache,
        ILibraryManager libraryManager,
        IUserDataManager userDataManager,
        IUserManager userManager,
        ILogger<HomeSectionManager> logger)
    {
        _dbContextFactory = dbContextFactory;
        _configurationManager = configurationManager;
        _memoryCache = memoryCache;
        _libraryManager = libraryManager;
        _userDataManager = userDataManager;
        _userManager = userManager;
        _logger = logger;

        // ItemUpdated is deliberately not handled. It also fires for updates caused by user data
        // changes, so it would mark everything stale on every playback progress report.
        _libraryManager.ItemAdded += OnLibraryChanged;
        _libraryManager.ItemRemoved += OnLibraryChanged;
        _userDataManager.UserDataSaved += OnUserDataSaved;
    }

    /// <inheritdoc />
    public event EventHandler<Guid>? SectionsChanged;

    /// <inheritdoc />
    public event EventHandler<HomeSectionInvalidatedEventArgs>? Invalidated;

    private static TimeSpan CacheLength => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the identifier a row is addressed by.
    /// </summary>
    /// <param name="key">The provider key.</param>
    /// <param name="parentId">The item the row is about, if any.</param>
    /// <returns>An identifier that is stable across requests for the same row.</returns>
    public static string GetId(string key, Guid? parentId = null)
    {
        var normalized = key.ToLowerInvariant();

        return parentId.HasValue
            ? string.Concat(normalized, "-", parentId.Value.ToString("N"))
            : normalized;
    }

    /// <inheritdoc />
    public void AddParts(IEnumerable<IHomeSectionProvider> providers)
    {
        var byKey = new Dictionary<string, IHomeSectionProvider>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<IHomeSectionProvider>();

        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.Key))
            {
                _logger.LogWarning("Home section provider {Provider} has no key and was skipped", provider.GetType().FullName);
                continue;
            }

            if (!byKey.TryAdd(provider.Key, provider))
            {
                // First one wins. The built-in providers are registered before plugins, so a plugin
                // cannot silently replace Continue Watching by reusing its key.
                _logger.LogWarning(
                    "Home section provider {Provider} reuses key {Key} already claimed by {Existing} and was skipped",
                    provider.GetType().FullName,
                    provider.Key,
                    byKey[provider.Key].GetType().FullName);
                continue;
            }

            ordered.Add(provider);
        }

        _providers = byKey;
        _orderedProviders = ordered;
        _userDataKeys = ordered.Where(p => p.DependsOnUserData).Select(p => p.Key.ToLowerInvariant()).ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<IHomeSectionProvider> GetProviders() => _orderedProviders;

    /// <inheritdoc />
    public IHomeSectionProvider? GetProvider(string key)
        => _providers.TryGetValue(key, out var provider) ? provider : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<HomeSectionDto>> GetHomeSectionsAsync(
        User user,
        string client,
        int itemLimit,
        IReadOnlyCollection<string>? keys,
        IReadOnlyCollection<ItemFields>? fields,
        CancellationToken cancellationToken)
    {
        // Cards need the aspect ratio to lay out; anything else is sent only when asked for, as on
        // the other item endpoints. The same options go to every provider so rows render alike.
        var dtoOptions = new DtoOptions
        {
            Fields = (fields ?? []).Append(ItemFields.PrimaryImageAspectRatio).Distinct().Order().ToArray(),
            EnableImages = true,
            EnableUserData = true,
            ImageTypes = [ImageType.Primary, ImageType.Backdrop, ImageType.Thumb]
        };

        var configured = GetEffectiveSections(user.Id, client)
            .Where(section => section.Active)
            .Where(section => keys is null || keys.Count == 0 || keys.Contains(section.Key, StringComparer.OrdinalIgnoreCase))
            .ToList();

        // Sections are independent queries, so run them together rather than serially. The
        // configured order is restored afterwards because it is what the user sees.
        var built = await Task.WhenAll(
            configured.Select(section => GetSectionAsync(section, user, dtoOptions, section.MaxItems ?? itemLimit, cancellationToken)))
            .ConfigureAwait(false);

        var sections = new List<HomeSectionDto>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rows in built)
        {
            foreach (var row in rows)
            {
                // A layout can carry the same section twice through the legacy API, and two rows
                // with one id cannot be told apart by a client, so the second is dropped.
                if (seenIds.Add(row.Id))
                {
                    sections.Add(row);
                }
            }
        }

        return sections;
    }

    /// <inheritdoc />
    public void Invalidate(string key, Guid? userId = null)
    {
        var keys = new[] { key.ToLowerInvariant() };

        if (userId.HasValue)
        {
            Invalidate(userId.Value, false, keys);
            return;
        }

        foreach (var user in _userManager.GetUsers())
        {
            Invalidate(user.Id, false, keys);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<HomeSection> GetSections(Guid userId, string client)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        return FoldListSections(dbContext.DisplayPreferences
            .Where(pref => pref.UserId.Equals(userId) && pref.Client == client && pref.ItemId.Equals(SettingsItemId))
            .SelectMany(pref => pref.HomeSections)
            .OrderBy(section => section.Order)
            .ToList());
    }

    /// <summary>
    /// Folds a layout written when a list section was one section per item, so genre or
    /// collection sections that sit apart become one section where the first was, with the
    /// items in the order they had.
    /// </summary>
    private List<HomeSection> FoldListSections(List<HomeSection> sections)
    {
        var folded = new List<HomeSection>(sections.Count);
        var first = new Dictionary<string, HomeSection>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in sections)
        {
            if (GetProvider(section.Key)?.AllowsMultipleItems != true)
            {
                folded.Add(section);
                continue;
            }

            if (first.TryGetValue(section.Key, out var head))
            {
                head.ItemIds = head.ItemIds.Concat(section.ItemIds).Distinct().ToArray();
                continue;
            }

            first[section.Key] = section;
            folded.Add(section);
        }

        return folded;
    }

    /// <inheritdoc />
    public IReadOnlyList<HomeSection> GetEffectiveSections(Guid userId, string client)
    {
        var stored = GetSections(userId, client);

        return stored.Count > 0 ? stored : GetDefaultSections();
    }

    /// <inheritdoc />
    public IReadOnlyList<HomeSection> GetDefaultSections()
    {
        var adminDefaults = _configurationManager.Configuration.DefaultHomeSections;
        if (adminDefaults.Length > 0)
        {
            return FoldListSections(adminDefaults
                .Select((option, index) => new HomeSection
                {
                    Order = index,
                    Key = option.Key,
                    ItemIds = option.ItemIds,
                    MaxItems = option.MaxItems,
                    Active = option.Active
                })
                .ToList());
        }

        return BuiltInDefaults
            .Select((key, index) => new HomeSection { Order = index, Key = key, Active = true })
            .ToList();
    }

    /// <inheritdoc />
    public void SetSections(Guid userId, string client, IReadOnlyList<HomeSection> sections)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        var prefs = GetOrCreatePreferences(dbContext, userId, client);

        prefs.HomeSections.Clear();

        foreach (var section in Normalize(sections))
        {
            prefs.HomeSections.Add(section);
        }

        dbContext.SaveChanges();
        OnSectionsChanged(userId);
    }

    /// <inheritdoc />
    public void SetDefaultSections(IReadOnlyList<HomeSection> sections)
    {
        _configurationManager.Configuration.DefaultHomeSections = Normalize(sections)
            .Select(section => new HomeSectionOptions
            {
                Key = section.Key,
                ItemIds = section.ItemIds.ToArray(),
                MaxItems = section.MaxItems,
                Active = section.Active
            })
            .ToArray();

        _configurationManager.SaveConfiguration();
    }

    /// <inheritdoc />
    public void ResetSections(Guid userId, string client)
    {
        using var dbContext = _dbContextFactory.CreateDbContext();

        var prefs = dbContext.DisplayPreferences
            .Include(pref => pref.HomeSections)
            .FirstOrDefault(pref =>
                pref.UserId.Equals(userId) && pref.Client == client && pref.ItemId.Equals(SettingsItemId));

        if (prefs is null)
        {
            return;
        }

        prefs.HomeSections.Clear();
        dbContext.SaveChanges();
        OnSectionsChanged(userId);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _libraryManager.ItemAdded -= OnLibraryChanged;
        _libraryManager.ItemRemoved -= OnLibraryChanged;
        _userDataManager.UserDataSaved -= OnUserDataSaved;
        _disposed = true;
    }

    /// <summary>
    /// Copies a layout for storing, with keys lower-cased and positions from the list order.
    /// </summary>
    /// <remarks>
    /// Position comes from the list order so callers cannot store a layout whose positions
    /// contradict its own ordering.
    /// </remarks>
    private static List<HomeSection> Normalize(IReadOnlyList<HomeSection> sections)
        => sections
            .Select((section, index) => new HomeSection
            {
                Order = index,
                Key = section.Key.ToLowerInvariant(),
                ItemIds = section.ItemIds,
                MaxItems = section.MaxItems,
                Active = section.Active
            })
            .ToList();

    private static DisplayPreferences GetOrCreatePreferences(JellyfinDbContext dbContext, Guid userId, string client)
    {
        var prefs = dbContext.DisplayPreferences
            .Include(pref => pref.HomeSections)
            .FirstOrDefault(pref =>
                pref.UserId.Equals(userId) && pref.Client == client && pref.ItemId.Equals(SettingsItemId));

        if (prefs is null)
        {
            prefs = new DisplayPreferences(userId, SettingsItemId, client);
            dbContext.DisplayPreferences.Add(prefs);
        }

        return prefs;
    }

    private async Task<IReadOnlyList<HomeSectionDto>> GetSectionAsync(
        HomeSection section,
        User user,
        DtoOptions dtoOptions,
        int limit,
        CancellationToken cancellationToken)
    {
        // The key is taken before building, so rows built across an invalidation are stored under
        // the generation they belong to and are never served afterwards.
        var cacheKey = GetCacheKey(user.Id, section, limit, dtoOptions.Fields);
        if (_memoryCache.TryGetValue(cacheKey, out IReadOnlyList<HomeSectionDto>? cached) && cached is not null)
        {
            return cached;
        }

        var rows = await BuildSectionAsync(section, user, dtoOptions, limit, cancellationToken).ConfigureAwait(false);
        if (rows is null)
        {
            // A provider that failed is asked again next time instead of being remembered as empty.
            return [];
        }

        _memoryCache.Set(cacheKey, rows, DateTimeOffset.UtcNow.Add(CacheLength));

        return rows;
    }

    private async Task<IReadOnlyList<HomeSectionDto>?> BuildSectionAsync(
        HomeSection section,
        User user,
        DtoOptions dtoOptions,
        int limit,
        CancellationToken cancellationToken)
    {
        var provider = GetProvider(section.Key);
        if (provider is null)
        {
            // "none" is a real stored value meaning an empty legacy slot, and a row left behind by
            // an uninstalled plugin looks the same. Neither is an error.
            return [];
        }

        var query = new HomeSectionQuery
        {
            User = user,
            ItemIds = section.ItemIds,
            Limit = limit,
            DtoOptions = dtoOptions
        };

        IReadOnlyList<HomeSectionResult> results;
        try
        {
            results = await provider.GetSectionsAsync(query, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // One broken provider, most likely from a plugin, must not take the home screen down.
            _logger.LogWarning(ex, "Home section provider {Key} failed for user {UserId}", provider.Key, user.Id);
            return null;
        }

        var rows = new List<HomeSectionDto>(results.Count);
        foreach (var result in results)
        {
            // An empty row is worse than no row, so drop it rather than draw a heading over nothing.
            if (result.Items.Count == 0)
            {
                continue;
            }

            rows.Add(new HomeSectionDto
            {
                Id = GetId(provider.Key, result.ParentId),
                Key = provider.Key.ToLowerInvariant(),
                DisplayText = result.DisplayText,
                ViewType = result.ViewType,
                ParentId = result.ParentId,
                ParentType = result.ParentType,
                Items = result.Items
            });
        }

        return rows;
    }

    private string GetCacheKey(Guid userId, HomeSection section, int limit, IEnumerable<ItemFields> fields)
    {
        var key = section.Key.ToLowerInvariant();
        var userGeneration = _generations.GetOrAdd(userId, 0);
        var keyGeneration = _keyGenerations.GetOrAdd((userId, key), 0);
        var itemIds = string.Join(',', section.ItemIds.Select(id => id.ToString("N", CultureInfo.InvariantCulture)));

        // Everything that changes what the provider is asked for is part of the key. The client is
        // not: two clients showing the same row share it.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"homesection-{userId:N}-{key}-{itemIds}-{limit}-{string.Join(',', fields)}-{userGeneration}-{keyGeneration}");
    }

    private void Invalidate(Guid userId, bool allStale, IReadOnlyList<string> staleKeys)
    {
        if (allStale)
        {
            _generations.AddOrUpdate(userId, 1, (_, generation) => generation + 1);
        }
        else
        {
            foreach (var key in staleKeys)
            {
                _keyGenerations.AddOrUpdate((userId, key), 1, (_, generation) => generation + 1);
            }
        }

        Invalidated?.Invoke(this, new HomeSectionInvalidatedEventArgs
        {
            UserId = userId,
            AllStale = allStale,
            StaleSectionKeys = allStale ? Array.Empty<string>() : staleKeys
        });
    }

    private void OnLibraryChanged(object? sender, ItemChangeEventArgs e)
    {
        // A new or removed item can land in the latest row, a genre row or a collection row, and
        // working out which is more expensive than rebuilding. Everyone is affected, not just the
        // user who triggered it.
        foreach (var user in _userManager.GetUsers())
        {
            Invalidate(user.Id, true, Array.Empty<string>());
        }
    }

    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        // Playback only moves the rows that depend on watch state. Marking everything stale here
        // would rebuild every collection and genre row on each progress report.
        Invalidate(e.UserId, false, _userDataKeys);
    }

    private void OnSectionsChanged(Guid userId)
    {
        SectionsChanged?.Invoke(this, userId);

        // The layout itself changed, so rows can have moved, appeared or been hidden.
        Invalidate(userId, true, Array.Empty<string>());
    }
}
