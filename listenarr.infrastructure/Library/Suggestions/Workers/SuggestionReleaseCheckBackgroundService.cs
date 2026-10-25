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

    /// <summary>One book to ask the indexers about, wherever it came from.</summary>
    internal sealed record AvailabilityCheckTarget(
        string Asin, string Title, IReadOnlyList<string> Authors, string? SeriesNumber = null);

    public interface ISuggestionReleaseCheckProcessor
    {
        Task RunCycleAsync(CancellationToken cancellationToken);
    }

    public class SuggestionReleaseCheckProcessor(
        ILogger<SuggestionReleaseCheckProcessor> logger,
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider timeProvider) : ISuggestionReleaseCheckProcessor
    {
        /// <summary>
        /// How long a book nothing held is left before asking again, when the setting is
        /// missing or nonsense. A day is the floor: anything less is a checking loop.
        /// </summary>
        internal static readonly TimeSpan DefaultNotFoundRecheck = TimeSpan.FromDays(7);
        internal static readonly TimeSpan MinimumNotFoundRecheck = TimeSpan.FromDays(1);

        /// <summary>A found release is confirmed again after this long; posts get pulled.</summary>
        internal static readonly TimeSpan FoundRecheck = TimeSpan.FromDays(30);

        /// <summary>
        /// A check that learned nothing is tried again after this long: an indexer outage
        /// is usually over in hours, and until it is there is no point asking again every
        /// cycle - nor waiting a week, which is the answer for a book nobody holds.
        /// </summary>
        internal static readonly TimeSpan UnknownRecheck = TimeSpan.FromHours(6);

        /// <summary>
        /// How long the list of books still to check is trusted. Building it reads the
        /// whole library and every cached catalog, which is too much to do per check.
        /// </summary>
        private static readonly TimeSpan WorkListLifetime = TimeSpan.FromMinutes(10);

        private readonly Queue<AvailabilityCheckTarget> _work = new();
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

            var notFoundRecheck = NotFoundRecheckFrom(settings.UnobtainableRecheckDays);

            if (_work.Count == 0 || _workBuiltAt is null || now - _workBuiltAt.Value > WorkListLifetime)
            {
                var snapshot = await scope.ServiceProvider
                    .GetRequiredService<ISuggestionService>()
                    .GetAsync(cancellationToken);
                // The snapshot does not report a check that learned nothing, so the stored
                // checks are read directly to pace those apart from the books nobody holds.
                var checks = await scope.ServiceProvider
                    .GetRequiredService<ISuggestionReleaseRepository>()
                    .GetAllAsync(cancellationToken);
                var library = await scope.ServiceProvider
                    .GetRequiredService<ILibraryListService>()
                    .GetAllAsync();

                _work.Clear();
                // The library's own missing books lead: there are far fewer of them than
                // there are suggestions, and their answer decides whether a book stands in
                // the Wanted list or sits in the collapsed group beneath it.
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var target in DueInLibrary(library, checks, now, notFoundRecheck)
                    .Concat(Due(snapshot, checks, now, notFoundRecheck)))
                {
                    if (seen.Add(target.Asin))
                    {
                        _work.Enqueue(target);
                    }
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
                    .CheckAsync(next.Asin, next.Title, next.Authors, cancellationToken, next.SeriesNumber);
                logger.LogDebug(
                    "Release availability check for '{Title}' ({Asin}): {Status} {Detail}",
                    next.Title, next.Asin, release.Status, release.Detail);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (WorkerExceptionClassifier.IsNonFatal(ex))
            {
                logger.LogWarning(ex, "Release availability check failed for '{Title}' ({Asin})", next.Title, next.Asin);
            }
        }

        /// <summary>
        /// The books to check: series gaps first, because a missing book in a series the
        /// library has started is the likeliest add and there are far fewer of them, then
        /// author catalogs, each in the order the page shows them. Each book once, however
        /// many groups list it.
        /// </summary>
        internal static IEnumerable<AvailabilityCheckTarget> Due(
            SuggestionSnapshot snapshot,
            IReadOnlyDictionary<string, SuggestionRelease> checks,
            DateTime now,
            TimeSpan notFoundRecheck)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return snapshot.Series.SelectMany(group => group.Missing)
                .Concat(snapshot.Authors.SelectMany(group => group.Missing))
                .Where(book => !string.IsNullOrWhiteSpace(book.Asin) && seen.Add(book.Asin.Trim()))
                .Where(book => IsDue(book.Asin!.Trim(), checks, now, notFoundRecheck))
                .Select(book => new AvailabilityCheckTarget(book.Asin!.Trim(), book.Title, book.Authors, book.SeriesNumber));
        }

        /// <summary>
        /// The library's own books that are monitored and have no file: the ones whose
        /// answer decides whether they stand in the Wanted list or in the collapsed group
        /// of books nothing holds. A book with no ASIN cannot be asked about by ASIN and
        /// is left to a manual search.
        /// </summary>
        internal static IEnumerable<AvailabilityCheckTarget> DueInLibrary(
            IEnumerable<LibraryAudiobookListItem> library,
            IReadOnlyDictionary<string, SuggestionRelease> checks,
            DateTime now,
            TimeSpan notFoundRecheck)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return library
                .Where(item => item.Wanted || item.Unobtainable)
                .Where(item => !string.IsNullOrWhiteSpace(item.Asin) && seen.Add(item.Asin!.Trim()))
                .Where(item => IsDue(item.Asin!.Trim(), checks, now, notFoundRecheck))
                .Select(item => new AvailabilityCheckTarget(
                    item.Asin!.Trim(),
                    item.Title ?? string.Empty,
                    item.Authors ?? [],
                    item.SeriesNumber));
        }

        /// <summary>The recheck window for a book nothing held, as the settings ask for it.</summary>
        internal static TimeSpan NotFoundRecheckFrom(int configuredDays)
        {
            if (configuredDays <= 0)
            {
                return DefaultNotFoundRecheck;
            }

            var requested = TimeSpan.FromDays(configuredDays);
            return requested < MinimumNotFoundRecheck ? MinimumNotFoundRecheck : requested;
        }

        /// <summary>
        /// Whether this ASIN is due another check: never checked, or checked long enough
        /// ago for the answer it gave to be worth asking again.
        /// </summary>
        internal static bool IsDue(
            string asin,
            IReadOnlyDictionary<string, SuggestionRelease> checks,
            DateTime now,
            TimeSpan notFoundRecheck)
        {
            if (!checks.TryGetValue(asin, out var check))
            {
                return true;
            }

            var after = check.Status switch
            {
                SuggestionReleaseStatus.Found => FoundRecheck,
                SuggestionReleaseStatus.Unknown => UnknownRecheck,
                _ => notFoundRecheck
            };

            return now - check.CheckedAt > after;
        }
    }
}
