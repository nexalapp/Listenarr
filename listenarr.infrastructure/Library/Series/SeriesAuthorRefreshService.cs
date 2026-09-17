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
using Listenarr.Application.Audiobooks.Series;
using Listenarr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Series
{
    /// <summary>
    /// Keeps <see cref="ISeriesAuthorSnapshot"/> current: a rebuild at startup, one soon
    /// after any save marks it stale, and one every few minutes regardless. The query is
    /// one pass over the library's series rows, cheap enough not to need anything finer.
    /// </summary>
    public sealed class SeriesAuthorRefreshService(
        IServiceScopeFactory scopeFactory,
        ISeriesAuthorSnapshot snapshot,
        IApplicationSettingsSnapshot settings,
        ILogger<SeriesAuthorRefreshService> logger) : BackgroundService
    {
        private static readonly TimeSpan Poll = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan FullRefresh = TimeSpan.FromMinutes(5);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var last = DateTime.MinValue;
            string? overridesSeen = null;
            while (!stoppingToken.IsCancellationRequested)
            {
                // Edited overrides count as a change too; the settings save does not know
                // this snapshot exists.
                var overrides = settings.Current?.SeriesAuthorOverridesJson;
                if (snapshot.IsStale || overrides != overridesSeen || DateTime.UtcNow - last > FullRefresh)
                {
                    await RefreshAsync(stoppingToken);
                    last = DateTime.UtcNow;
                    overridesSeen = overrides;
                }

                try
                {
                    await Task.Delay(Poll, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ListenArrDbContext>();
                var books = await db.Audiobooks
                    .AsNoTracking()
                    .Where(book => book.Series != null && book.Series != "")
                    .Select(book => new { book.Series, book.SeriesNumber, book.PublishYear, book.Authors })
                    .ToListAsync(cancellationToken);

                var candidates = books.Select(book => new SeriesAuthorCandidate(
                    book.Series!,
                    book.SeriesNumber,
                    int.TryParse(book.PublishYear, out var year) ? year : null,
                    book.Authors?.FirstOrDefault(author => !string.IsNullOrWhiteSpace(author))));

                snapshot.Update(SeriesAuthorRule.Compute(
                    candidates,
                    SeriesAuthorRule.ParseOverrides(settings.Current?.SeriesAuthorOverridesJson)));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Could not refresh the series author snapshot");
            }
        }
    }
}
