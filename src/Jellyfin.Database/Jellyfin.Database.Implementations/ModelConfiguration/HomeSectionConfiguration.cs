using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jellyfin.Database.Implementations.ModelConfiguration;

/// <summary>
/// FluentAPI configuration for the HomeSection entity.
/// </summary>
public class HomeSectionConfiguration : IEntityTypeConfiguration<HomeSection>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<HomeSection> builder)
    {
        // A handful of ids at most, so one text column rather than a table of its own.
        builder
            .Property(e => e.ItemIds)
            .HasConversion(
                ids => string.Join(',', ids.Select(id => id.ToString("N"))),
                text => string.IsNullOrEmpty(text) ? Array.Empty<Guid>() : text.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToArray(),
                new ValueComparer<IReadOnlyList<Guid>>(
                    (a, b) => a!.SequenceEqual(b!),
                    ids => ids.Aggregate(0, (hash, id) => HashCode.Combine(hash, id)),
                    ids => ids.ToArray()));
    }
}
