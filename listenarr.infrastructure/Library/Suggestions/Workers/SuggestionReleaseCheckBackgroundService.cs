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

using Listenarr.Application.Audiobooks.Suggestions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Suggestions.Workers
{
    /// <summary>
    /// Works through the Suggested page one book at a time, asking the indexers whether
    /// a release of it exists, so the page can show only books that can be had and adding
    /// one can grab it at once.
    ///
    /// Separate from the catalog worker on purpose: that one paces Audible and this one
    /// paces abook.link and the Newznab indexers, and neither should wait on the other.
    /// </summary>
    public class SuggestionReleaseCheckBackgroundService(
        ILogger<SuggestionReleaseCheckBackgroundService> logger,
        ISuggestionReleaseCheckProcessor processor,
        IWorkerCycleRunner cycleRunner) : BackgroundService
    {
        /// <summary>How often the loop wakes to see whether a check is due.</summary>
        private static readonly TimeSpan Tick = TimeSpan.FromSeconds(5);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation("SuggestionReleaseCheckBackgroundService started");

            await cycleRunner.RunPeriodicAsync(
                nameof(SuggestionReleaseCheckBackgroundService),
                initialDelay: TimeSpan.FromMinutes(3),
                intervalProvider: () => Tick,
                runCycle: processor.RunCycleAsync,
                stoppingToken);

            logger.LogInformation("SuggestionReleaseCheckBackgroundService stopped");
        }
    }

    public interface ISuggestionReleaseCheckProcessor
    {
        Task RunCycleAsync(CancellationToken cancellationToken);
    }

    public class SuggestionReleaseCheckProcessor(
        ILogger<SuggestionReleaseCheckProcessor> logger,
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider timeProvider) : ISuggestionReleaseCheckProcessor
    {
        /// <summary>A book nothing held is asked about again after this long.</summary>
        internal static readonly TimeSpan NotFoundRecheck = TimeSpan.FromDays(7);

        /// <summary>A found release is confirmed again after this long; posts get pulled.</summary>
        internal static readonly TimeSpan FoundRecheck = TimeSpan.FromDays(30);

        /// <summary>
        /// How long the list of books still to check is trusted. Building it reads the
        /// whole library and every cached catalog, which is too much to do per check.
        /// </summary>
        private static readonly TimeSpan WorkListLifetime = TimeSpan.FromMinutes(10);

        private readonly Queue<SuggestedBook> _work = new();
        private DateTime? _workBuiltAt;
        private DateTime? _lastCheckAt;

        public async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            using var scope = serviceScopeFactory.CreateScope();
            var settings = await scope.ServiceProvider
                .GetRequiredService<IConfigurationService>()
                .GetApplicationSettingsAsync();
            var interval = settings.SuggestionReleaseCheckIntervalSeconds;
            if (interval <= 0)
            {
                return;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (_lastCheckAt.HasValue && now - _lastCheckAt.Value < TimeSpan.FromSeconds(interval))
            {
                return;
            }

            if (_work.Count == 0 || _workBuiltAt is null || now - _workBuiltAt.Value > WorkListLifetime)
            {
                var snapshot = await scope.ServiceProvider
                    .GetRequiredService<ISuggestionService>()
                    .GetAsync(cancellationToken);
                _work.Clear();
                foreach (var book in Due(snapshot, now))
                {
                    _work.Enqueue(book);
                }

                _workBuiltAt = now;
            }

            if (!_work.TryDequeue(out var next))
            {
                return;
            }

            _lastCheckAt = now;
            try
            {
                var release = await scope.ServiceProvider
                    .GetRequiredService<ISuggestionReleaseService>()
                    .CheckAsync(next, cancellationToken);
                logger.LogDebug(
                    "Suggestion release check for '{Title}' ({Asin}): {Status} {Detail}",
                    next.Title, next.Asin, release.Status, release.Detail);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex))
            {
                logger.LogWarning(ex, "Suggestion release check failed for '{Title}' ({Asin})", next.Title, next.Asin);
            }
        }

        /// <summary>
        /// The books to check: series gaps first, because a missing book in a series the
        /// library has started is the likeliest add and there are far fewer of them, then
        /// author catalogs, each in the order the page shows them. Each book once, however
        /// many groups list it.
        /// </summary>
        internal static IEnumerable<SuggestedBook> Due(SuggestionSnapshot snapshot, DateTime now)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return snapshot.Series.SelectMany(group => group.Missing)
                .Concat(snapshot.Authors.SelectMany(group => group.Missing))
                .Where(book => !string.IsNullOrWhiteSpace(book.Asin) && seen.Add(book.Asin.Trim()))
                .Where(book => book.Release is not { } release
                    || now - release.CheckedAt > (release.Found ? FoundRecheck : NotFoundRecheck));
        }
    }
}
