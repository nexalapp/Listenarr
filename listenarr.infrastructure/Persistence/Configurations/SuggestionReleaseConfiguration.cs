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
    public class SuggestionReleaseConfiguration : IEntityTypeConfiguration<SuggestionRelease>
    {
        public void Configure(EntityTypeBuilder<SuggestionRelease> builder)
        {
            builder.HasKey(release => release.Id);

            builder.Property(release => release.Asin)
                .IsRequired()
                .HasMaxLength(32);

            builder.HasIndex(release => release.Asin).IsUnique();

            builder.Property(release => release.IndexerName).HasMaxLength(256);
            builder.Property(release => release.ReleaseTitle).HasMaxLength(1024);
            builder.Property(release => release.Detail).HasMaxLength(1024);

            // Stored by name: a number would renumber silently if a member were ever
            // inserted, and these rows outlive a release.
            builder.Property(release => release.Preparation)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(ReleasePreparationState.NotPrepared);

            builder.Property(release => release.ResolvedVia).HasMaxLength(64);
            builder.Property(release => release.PayloadFingerprint).HasMaxLength(64);
            builder.Property(release => release.PreparationDetail).HasMaxLength(1024);
        }
    }
}
