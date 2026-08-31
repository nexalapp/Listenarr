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
    public class NzbKingApiAccessConfiguration : IEntityTypeConfiguration<NzbKingApiAccess>
    {
        public void Configure(EntityTypeBuilder<NzbKingApiAccess> builder)
        {
            builder.HasKey(access => access.Id);

            builder.Property(access => access.KeyFingerprint)
                .IsRequired()
                .HasMaxLength(64);

            // Stored as text: this table is read by a human working out where the
            // allowance went, and integer discriminators would obscure that.
            builder.Property(access => access.Purpose)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(32);

            builder.Property(access => access.Outcome)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(32);

            builder.Property(access => access.Query)
                .HasMaxLength(512);
        }
    }
}
