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
using Listenarr.Application.Search.Contracts;
using Listenarr.Application.Audiobooks.Discover;
using Listenarr.Application.Audiobooks.Suggestions;
using Listenarr.Domain.Audiobooks.Discover;
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.AutoGrab
{
    /// <summary>
    /// Adds the books the Suggested page offers, in the order the lists are followed,
    /// and lets the add path send the release the background check already found.
    ///
    /// <para>
    /// Nothing here searches or resolves anything: a book is a candidate only when a
    /// check has already found a release for it, and adding it is the same call the Add
    /// button makes, which grabs that stored release as a post-commit step. So this is
    /// the Add button, run down a list, with a limit on how many it presses.
    /// </para>
    /// </summary>
    public interface IAutoGrabService
    {
        /// <summary>
        /// What a run would add right now, in order, capped. Nothing is added and no
        /// source is contacted.
        /// </summary>
        Task<AutoGrabPlan> PreviewAsync(CancellationToken cancellationToken = default);

        /// <summary>Adds up to the configured number of books, newest list first.</summary>
        Task<AutoGrabReport> RunAsync(CancellationToken cancellationToken = default);
    }

    public sealed class AutoGrabService(
        IConfigurationService configuration,
        ISuggestionService suggestions,
        IDiscoverService discover,
        ISuggestionReleaseRepository releases,
        ILibraryAddService library,
        IAbookThankBudget thanks,
        TimeProvider timeProvider,
        ILogger<AutoGrabService> logger) : IAutoGrabService
    {
        public async Task<AutoGrabPlan> PreviewAsync(CancellationToken cancellationToken = default)
        {
            var settings = AutoGrabSettingsJson.Read(
                (await configuration.GetApplicationSettingsAsync()).AutoGrabSettingsJson);

            if (settings.Sources.All(source => !source.Enabled))
            {
                return new AutoGrabPlan(settings.Enabled, 0, [], [], "No list is ticked.");
            }

            var found = await releases.GetAllAsync(cancellationToken);
            var candidates = new List<AutoGrabCandidate>();
            var counts = new List<AutoGrabSourceCount>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var source in settings.Active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (label, books) = await BooksForAsync(source, cancellationToken);

                var fromThisSource = 0;
                foreach (var book in books)
                {
                    if (string.IsNullOrWhiteSpace(book.Asin) || !seen.Add(book.Asin.Trim()))
                    {
                        continue;
                    }

                    if (!found.TryGetValue(book.Asin.Trim(), out var release)
                        || release.Status != SuggestionReleaseStatus.Found)
                    {
                        continue;
                    }

                    fromThisSource++;
                    candidates.Add(new AutoGrabCandidate(
                        book.Asin.Trim(),
                        book,
                        source.Kind,
                        label,
                        release.ReleaseTitle,
                        release.IndexerName,
                        release.SizeBytes));
                }

                counts.Add(new AutoGrabSourceCount(source.Kind, source.SectionId, label, fromThisSource));
            }

            return new AutoGrabPlan(
                settings.Enabled,
                candidates.Count,
                // Free first. A Newznab release costs nothing to grab, an abook.link post
                // costs a public thank and is rationed; leaving them in one queue put
                // eighty books that could all be done in a minute behind eight hundred
                // that cannot. The sort is stable, so the followed lists keep their order
                // within each lane.
                [.. candidates.OrderBy(candidate => NeedsAThank(candidate) ? 1 : 0)],
                counts,
                candidates.Count == 0 ? "Nothing to grab: every followed list is empty or already in the library." : null);
        }

        public async Task<AutoGrabReport> RunAsync(CancellationToken cancellationToken = default)
        {
            var startedAt = timeProvider.GetUtcNow().UtcDateTime;
            var plan = await PreviewAsync(cancellationToken);
            var outcomes = new List<AutoGrabOutcome>();
            string? stopped = null;
            var deferred = 0;
            var abookIsFull = false;

            foreach (var candidate in plan.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Ask before adding, not during the grab. A thank refused halfway through
                // an add leaves a book in the library that nothing will download, which is
                // how a backlog of eighty-two of them accumulated unnoticed. A book not
                // added is simply still waiting, and the next run finds it.
                if (NeedsAThank(candidate))
                {
                    if (!abookIsFull)
                    {
                        var budget = await thanks.CheckAsync(cancellationToken);
                        if (!budget.Allowed)
                        {
                            abookIsFull = true;
                            stopped = budget.Reason;
                        }
                    }

                    if (abookIsFull)
                    {
                        // Defer this one and carry on down the list. Ending the run here
                        // would strand every release further down that needs no thank at
                        // all, and the order being stable, it would strand the same ones
                        // next run and the run after.
                        deferred++;
                        continue;
                    }
                }

                var metadata = MetadataFor(candidate);

                try
                {
                    // Monitored and AutoSearch both on: that pair is what makes the add
                    // path send the release the check found, which is the whole point.
                    var result = await library.AddToLibraryAsync(
                        new LibraryAddOperationRequest
                        {
                            Metadata = metadata,
                            Monitored = true,
                            AutoSearch = true
                        },
                        cancellationToken);

                    // Already in the library counts as nothing to do rather than a
                    // failure: two lists offering one book is ordinary.
                    var added = result.Added;
                    var downloaded = result.DownloadSent;
                    outcomes.Add(new AutoGrabOutcome(
                        candidate.Asin,
                        candidate.Title,
                        candidate.SourceLabel,
                        added,
                        downloaded,
                        added || result.AlreadyExists ? null : Reason(result)));
                }
                catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException && ex is not StackOverflowException)
                {
                    // One book that will not add is not a reason to stop the run; the
                    // next one is unrelated to it.
                    logger.LogWarning(ex, "Auto-grab could not add {Title} ({Asin})", candidate.Title, candidate.Asin);
                    outcomes.Add(new AutoGrabOutcome(
                        candidate.Asin, candidate.Title, candidate.SourceLabel, false, false, ex.Message));
                }
            }

            var report = new AutoGrabReport(
                startedAt,
                timeProvider.GetUtcNow().UtcDateTime,
                outcomes.Count(outcome => outcome.Added),
                outcomes.Count(outcome => outcome.Downloaded),
                outcomes.Count(outcome => !outcome.Added),
                outcomes,
                deferred,
                stopped);

            if (outcomes.Count > 0 || deferred > 0)
            {
                logger.LogInformation(
                    "Auto-grab added {Added} and sent {Downloaded} download(s) of {Planned} planned; "
                    + "{Failed} failed, {Deferred} left for next time{Stopped}",
                    report.Added, report.Downloaded, plan.Candidates.Count, report.Failed, deferred,
                    stopped is null ? string.Empty : $" because {stopped}");
            }

            return report;
        }

        /// <summary>
        /// Whether grabbing this book means pressing abook.link's thank button. Posts
        /// already thanked do not, but that is only knowable by reading the post, so a
        /// book from that source is assumed to cost one. Erring this way spends the
        /// budget slightly early; erring the other way spends somebody's goodwill.
        /// </summary>
        private static bool NeedsAThank(AutoGrabCandidate candidate) =>
            string.Equals(candidate.IndexerName, AbookLinkName, StringComparison.OrdinalIgnoreCase);

        private const string AbookLinkName = "Abook.link";

        private async Task<(string Label, IReadOnlyList<SuggestedBook> Books)> BooksForAsync(
            AutoGrabSource source,
            CancellationToken cancellationToken)
        {
            switch (source.Kind)
            {
                case AutoGrabSources.Authors:
                    {
                        var snapshot = await suggestions.GetAsync(cancellationToken);
                        return ("From your authors",
                            snapshot.Authors.SelectMany(group => group.Missing).ToList());
                    }

                case AutoGrabSources.Series:
                    {
                        var snapshot = await suggestions.GetAsync(cancellationToken);
                        return ("From your series",
                            snapshot.Series.SelectMany(group => group.Missing).ToList());
                    }

                case AutoGrabSources.Discover:
                case AutoGrabSources.AbookLink:
                    {
                        var feed = await discover.GetFeedAsync(
                            source.Kind == AutoGrabSources.Discover
                                ? DiscoverSources.AudibleCharts
                                : DiscoverSources.AbookLink,
                            cancellationToken);
                        var section = feed.Sections
                            .FirstOrDefault(candidate => candidate.SectionId == source.SectionId);
                        return (section?.SectionName ?? source.SectionId ?? source.Kind,
                            section?.Books.Select(entry => entry.Book).ToList() ?? []);
                    }

                default:
                    return (source.Kind, []);
            }
        }

        /// <summary>
        /// The record the add path wants. The same fields the Add button sends, because
        /// a book added by a run and the same book added by hand must not differ.
        /// </summary>
        private static AudibleBookMetadata MetadataFor(AutoGrabCandidate candidate)
        {
            var book = candidate.Book;
            return new AudibleBookMetadata
            {
                Asin = candidate.Asin,
                Title = book.Title,
                Subtitle = book.Subtitle,
                Authors = [.. book.Authors ?? []],
                Narrators = [.. book.Narrators ?? []],
                Genres = [.. book.Genres ?? []],
                ImageUrl = book.ImageUrl,
                Runtime = book.Runtime,
                Language = book.Language,
                Publisher = book.Publisher,
                Series = book.Series,
                SeriesNumber = book.SeriesNumber,
                PublishedDate = book.PublishedDate,
                PublishYear = Year(book.PublishedDate),
                Isbn = book.Isbn is { Length: > 0 } isbn ? [isbn] : [],
                Description = book.Description,
                Source = book.MetadataSource ?? "Audible"
            };
        }

        private static string Reason(LibraryAddOperationResult result) =>
            result.ValidationMessage is { Length: > 0 } validation
                ? validation
                : result.Message is { Length: > 0 } message
                    ? message
                    : "The book could not be added.";

        /// <summary>The year out of a published date, which the catalogue writes as a full date.</summary>
        private static string? Year(string? publishedDate) =>
            publishedDate is { Length: >= 4 } date && int.TryParse(date[..4], out _) ? date[..4] : null;
    }
}
