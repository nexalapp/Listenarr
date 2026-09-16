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
    public class SuggestionDismissalConfiguration : IEntityTypeConfiguration<SuggestionDismissal>
    {
        public void Configure(EntityTypeBuilder<SuggestionDismissal> builder)
        {
            builder.HasKey(dismissal => dismissal.Id);

            builder.Property(dismissal => dismissal.Key)
                .IsRequired()
                .HasMaxLength(600);

            builder.HasIndex(dismissal => dismissal.Key).IsUnique();

            builder.Property(dismissal => dismissal.Title)
                .IsRequired()
                .HasMaxLength(512);

            builder.Property(dismissal => dismissal.Author)
                .HasMaxLength(256);
        }
    }
}
