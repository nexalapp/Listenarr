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
using Listenarr.Application.Common.Contracts;
using Listenarr.Application.Search.Contracts;
using Listenarr.Domain.Search;
using Listenarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Listenarr.Infrastructure.Search.AbookLink
{
    /// <summary>
    /// A trailing-hour budget for abook.link's thank button, kept in the database so a
    /// deploy does not hand the next hour a clean slate.
    ///
    /// Two rules, and they answer different dangers. The rate stops a loop pressing the
    /// button a thousand times; the breaker stops a retry storm doing it after the forum
    /// has already said no. A refusal from the forum is the one failure here that is
    /// ours to avoid making worse, so consecutive failures close the lane outright
    /// rather than backing each item off on its own.
    /// </summary>
    public sealed class AbookThankBudget(
        ListenArrDbContext db,
        IConfigurationService configuration,
        TimeProvider time) : IAbookThankBudget
    {
        /// <summary>Refusals in a row before the lane closes.</summary>
        internal const int FailuresThatCloseTheLane = 3;

        /// <summary>How long the lane stays shut once it has closed.</summary>
        internal static readonly TimeSpan LaneReopensAfter = TimeSpan.FromMinutes(30);

        /// <summary>Used when the setting is missing or nonsense.</summary>
        internal const int DefaultPerHour = 60;

        public async Task<AbookThankDecision> CheckAsync(CancellationToken ct = default)
        {
            var now = time.GetUtcNow().UtcDateTime;
            var perHour = PerHourFrom(
                (await configuration.GetApplicationSettingsAsync()).AbookThanksPerHour);

            var recent = await db.AbookThanks
                .AsNoTracking()
                .Where(thank => thank.ThankedAt > now.AddHours(-1))
                .OrderByDescending(thank => thank.ThankedAt)
                .ToListAsync(ct);

            // The breaker reads the newest rows whatever their age, because a lane that
            // closed an hour ago should not reopen merely because its failures aged out.
            var newest = await db.AbookThanks
                .AsNoTracking()
                .OrderByDescending(thank => thank.ThankedAt)
                .Take(FailuresThatCloseTheLane)
                .ToListAsync(ct);

            if (newest.Count == FailuresThatCloseTheLane
                && newest.All(thank => !thank.Succeeded)
                && now - newest[0].ThankedAt < LaneReopensAfter)
            {
                return new AbookThankDecision(
                    false,
                    $"abook.link refused the last {FailuresThatCloseTheLane} thanks; "
                    + "not asking again for a while.",
                    recent.Count,
                    newest[0].ThankedAt + LaneReopensAfter);
            }

            if (recent.Count >= perHour)
            {
                var oldest = recent[^1].ThankedAt;
                return new AbookThankDecision(
                    false,
                    $"{recent.Count} thanks in the last hour, which is the configured limit of {perHour}.",
                    recent.Count,
                    oldest.AddHours(1));
            }

            return new AbookThankDecision(true, null, recent.Count, null);
        }

        public async Task RecordAsync(
            int topicId, bool succeeded, string? detail, CancellationToken ct = default)
        {
            db.AbookThanks.Add(new AbookThank
            {
                TopicId = topicId,
                ThankedAt = time.GetUtcNow().UtcDateTime,
                Succeeded = succeeded,
                Detail = detail is { Length: > 256 } ? detail[..256] : detail
            });

            await db.SaveChangesAsync(ct);
        }

        public async Task<(int Hour, int Day)> CountsAsync(CancellationToken ct = default)
        {
            var now = time.GetUtcNow().UtcDateTime;
            var hour = await db.AbookThanks.CountAsync(
                thank => thank.ThankedAt > now.AddHours(-1), ct);
            var day = await db.AbookThanks.CountAsync(
                thank => thank.ThankedAt > now.AddDays(-1), ct);
            return (hour, day);
        }

        internal static int PerHourFrom(int configured) =>
            configured > 0 ? configured : DefaultPerHour;
    }
}
