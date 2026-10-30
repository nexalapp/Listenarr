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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Listenarr.Infrastructure.Persistence.Configurations
{
    public class BlockedReleaseConfiguration : IEntityTypeConfiguration<BlockedRelease>
    {
        public void Configure(EntityTypeBuilder<BlockedRelease> builder)
        {
            builder.HasKey(blocked => blocked.Id);

            builder.Property(blocked => blocked.ReleaseTitle)
                .IsRequired()
                .HasMaxLength(1024);

            builder.Property(blocked => blocked.Fingerprint)
                .IsRequired()
                .HasMaxLength(512);

            builder.Property(blocked => blocked.Indexer).HasMaxLength(256);
            builder.Property(blocked => blocked.ReleaseId).HasMaxLength(512);
            builder.Property(blocked => blocked.Detail).HasMaxLength(1024);
            builder.Property(blocked => blocked.DownloadId).HasMaxLength(64);

            // Every automatic search for a book reads its blocks, so that is the lookup
            // the index has to serve. The fingerprint rides along because the match is
            // made on it and the whole row never needs reading to answer "is this one".
            builder.HasIndex(blocked => new { blocked.AudiobookId, blocked.Fingerprint });
        }
    }
}
