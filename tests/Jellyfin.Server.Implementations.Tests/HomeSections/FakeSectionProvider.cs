using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.HomeSections;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.HomeSections;

namespace Jellyfin.Server.Implementations.Tests.HomeSections;

/// <summary>
/// A provider whose rows are whatever the test hands it, standing in for a built-in or a plugin.
/// </summary>
internal sealed class FakeSectionProvider : IHomeSectionProvider
{
    private readonly Func<HomeSectionQuery, IReadOnlyList<HomeSectionResult>> _build;

    public FakeSectionProvider(string key, Func<HomeSectionQuery, IReadOnlyList<HomeSectionResult>> build)
    {
        Key = key;
        _build = build;
    }

    public FakeSectionProvider(string key, params string[] itemNames)
        : this(key, _ => [Row(key, itemNames)])
    {
    }

    public string Key { get; }

    public string Name => Key;

    public BaseItemKind? ItemKind { get; init; }

    public bool DependsOnUserData { get; init; }

    public bool AllowsMultipleItems { get; init; }

    public int Calls { get; private set; }

    public HomeSectionQuery? LastQuery { get; private set; }

    public static HomeSectionResult Row(string displayText, params string[] itemNames)
        => new()
        {
            DisplayText = displayText,
            ViewType = HomeSectionViewType.Portrait,
            Items = itemNames.Select(name => new BaseItemDto { Id = Guid.NewGuid(), Name = name }).ToList()
        };

    public static HomeSectionResult Row(string displayText, Guid parentId, params string[] itemNames)
        => new()
        {
            DisplayText = displayText,
            ViewType = HomeSectionViewType.Portrait,
            ParentId = parentId,
            ParentType = BaseItemKind.BoxSet,
            Items = itemNames.Select(name => new BaseItemDto { Id = Guid.NewGuid(), Name = name }).ToList()
        };

    public Task<IReadOnlyList<HomeSectionResult>> GetSectionsAsync(HomeSectionQuery query, CancellationToken cancellationToken)
    {
        Calls++;
        LastQuery = query;
        return Task.FromResult(_build(query));
    }
}
