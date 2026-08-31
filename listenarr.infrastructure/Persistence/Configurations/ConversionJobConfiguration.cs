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
    public sealed class ConversionJobConfiguration : IEntityTypeConfiguration<ConversionJob>
    {
        public void Configure(EntityTypeBuilder<ConversionJob> builder)
        {
            builder.ToTable("ConversionJobs");

            builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(32);
            builder.Property(job => job.Phase)
                .HasConversion<string>()
                .HasMaxLength(32)
                .HasDefaultValue(ConversionJobPhase.None);
            builder.Property(job => job.Trigger)
                .HasConversion<string>()
                .HasMaxLength(16)
                .HasDefaultValue(ConversionTrigger.Automatic);

            builder.Property(job => job.ActiveDeduplicationKey).HasMaxLength(256);
            builder.Property(job => job.OutputPath).HasMaxLength(2000);
            builder.Property(job => job.FailureKind).HasMaxLength(32);
            builder.Property(job => job.LeaseOwner).HasMaxLength(200);
            builder.Property(job => job.LeaseGeneration).HasDefaultValue(0);
            builder.Property(job => job.MaxAttempts).HasDefaultValue(3);
            builder.Property(job => job.CanRetry).HasDefaultValue(true);

            // One active conversion per book. The filter keeps the constraint off terminal
            // rows, so a book can be converted again later without colliding with history.
            builder.HasIndex(job => job.ActiveDeduplicationKey)
                .IsUnique()
                .HasFilter("\"ActiveDeduplicationKey\" IS NOT NULL");

            // The claim query orders by EnqueuedAt over runnable rows.
            builder.HasIndex(job => new { job.Status, job.NextAttemptAt, job.LeaseExpiresAt });
            builder.HasIndex(job => job.AudiobookId);

            // The concurrency token for claiming: a competing claim bumps the generation,
            // so the losing worker's save fails instead of stealing an owned job.
            builder.Property(job => job.LeaseGeneration).IsConcurrencyToken();
        }
    }
}
