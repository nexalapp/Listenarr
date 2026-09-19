/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using Listenarr.Domain.FoundBooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Persistence.Configurations
{
    public sealed class FoundBookConfiguration : IEntityTypeConfiguration<FoundBook>
    {
        public void Configure(EntityTypeBuilder<FoundBook> builder)
        {
            builder.ToTable("FoundBooks");

            builder.Property(b => b.ClusterKey).HasMaxLength(64).IsRequired();
            builder.Property(b => b.Signature).HasMaxLength(64).IsRequired();
            builder.Property(b => b.WatchFolder).HasMaxLength(2000).IsRequired();
            builder.Property(b => b.BookFolder).HasMaxLength(2000).IsRequired();
            builder.Property(b => b.FilesJson).IsRequired();
            builder.Property(b => b.Format).HasMaxLength(16);
            builder.Property(b => b.DetectedTitle).HasMaxLength(500);
            builder.Property(b => b.DetectedAuthor).HasMaxLength(500);
            builder.Property(b => b.DetectedSeries).HasMaxLength(500);
            builder.Property(b => b.DetectedSeriesPosition).HasMaxLength(32);
            builder.Property(b => b.DetectedNarrator).HasMaxLength(500);
            builder.Property(b => b.DetectedYear).HasMaxLength(16);
            builder.Property(b => b.DetectedAsin).HasMaxLength(32);
            builder.Property(b => b.CompletenessReason).HasMaxLength(1000);
            builder.Property(b => b.BlockedReason).HasMaxLength(500);

            builder.Property(b => b.Completeness).HasConversion<string>().HasMaxLength(16);
            builder.Property(b => b.LibraryStatus).HasConversion<string>().HasMaxLength(16);
            builder.Property(b => b.State).HasConversion<string>().HasMaxLength(16);
            builder.Property(b => b.BlockedKind).HasConversion<string>().HasMaxLength(20).HasDefaultValue(FoundBookBlockedKind.None);
            builder.Property(b => b.AutoAdded).HasDefaultValue(false);

            // One row per cluster per watch folder: the key is what a scan uses to find
            // last time's row, and an Ignore decision is remembered against it.
            builder.HasIndex(b => new { b.WatchFolder, b.ClusterKey }).IsUnique();
            builder.HasIndex(b => b.State);
        }
    }
}
