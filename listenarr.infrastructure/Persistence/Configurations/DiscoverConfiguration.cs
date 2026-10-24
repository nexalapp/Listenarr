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
using Listenarr.Domain.Audiobooks.Discover;
using Listenarr.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Listenarr.Infrastructure.Persistence.Configurations
{
    public class DiscoverSectionConfiguration : IEntityTypeConfiguration<DiscoverSection>
    {
        public void Configure(EntityTypeBuilder<DiscoverSection> builder)
        {
            builder.HasKey(section => section.Id);

            builder.Property(section => section.Source)
                .IsRequired()
                .HasMaxLength(32);

            builder.Property(section => section.SectionId)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(section => section.SectionName)
                .IsRequired()
                .HasMaxLength(128);

            builder.Property(section => section.LastError)
                .HasMaxLength(512);

            builder.HasIndex(section => new { section.Source, section.SectionId })
                .IsUnique();
        }
    }

    public class DiscoverItemConfiguration : IEntityTypeConfiguration<DiscoverItem>
    {
        public void Configure(EntityTypeBuilder<DiscoverItem> builder)
        {
            builder.HasKey(item => item.Id);

            builder.Property(item => item.Source)
                .IsRequired()
                .HasMaxLength(32);

            builder.Property(item => item.SectionId)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(item => item.ExternalId)
                .IsRequired()
                .HasMaxLength(64);

            builder.Property(item => item.SourceUrl)
                .HasMaxLength(2048);

            builder.Property(item => item.SourceNote)
                .HasMaxLength(512);

            // The book travels as one JSON value, the same way a cached catalog entry
            // does: it is read and written whole and never queried field by field.
            var converter = (ValueConverter<DiscoverBook, string>)new JsonValueConverter<DiscoverBook>();
            var comparer = JsonValueComparer.Create<DiscoverBook>();
            var property = builder.Property(item => item.Book)
                .HasConversion(converter)
                .HasColumnType("TEXT");
            property.Metadata.SetValueComparer(comparer);

            builder.HasIndex(item => new { item.Source, item.SectionId, item.ExternalId })
                .IsUnique();

            builder.HasIndex(item => new { item.Source, item.Enriched });
        }
    }
}
