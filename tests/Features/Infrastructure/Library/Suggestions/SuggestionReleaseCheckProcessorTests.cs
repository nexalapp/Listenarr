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
using Listenarr.Infrastructure.Library.Suggestions.Workers;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Library.Suggestions
{
    /// <summary>Which suggested books the background check asks about, and in what order.</summary>
    [Trait("Name", "SuggestionReleaseCheckProcessorTests")]
    [Trait("Category", "Suggestions")]
    public sealed class SuggestionReleaseCheckProcessorTests : BaseTests
    {
        private static readonly DateTime Now = new(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc);

        private static SuggestedBook Book(string asin, SuggestedRelease? release = null) => new(
            asin, asin, null, ["Author"], [], null, null, "english", null, [], null, null, null, null, null, null,
            null, null, null, null, release);

        private static SuggestionSnapshot Snapshot(IReadOnlyList<SuggestedBook> authorBooks, IReadOnlyList<SuggestedBook> seriesBooks) => new(
            [new AuthorSuggestionGroup("Author", null, null, 3, false, authorBooks)],
            [new SeriesSuggestionGroup("Series", null, 1, false, seriesBooks)],
            [],
            new SuggestionCoverage(1, 1, 1, 1),
            []);

        private static IReadOnlyDictionary<string, SuggestionRelease> Checks(params SuggestionRelease[] checks) =>
            checks.ToDictionary(check => check.Asin, StringComparer.OrdinalIgnoreCase);

        private static SuggestionRelease Check(string asin, SuggestionReleaseStatus status, DateTime checkedAt) =>
            new() { Asin = asin, Status = status, CheckedAt = checkedAt };

        [Fact]
        public void SeriesGapsComeBeforeAuthorCatalogsAndEachBookOnlyOnce()
        {
            var due = SuggestionReleaseCheckProcessor.Due(
                Snapshot([Book("A1"), Book("A2")], [Book("A2"), Book("S1")]), Checks(), Now);

            Assert.Equal(["A2", "S1", "A1"], due.Select(book => book.Asin));
        }

        [Fact]
        public void ACheckedBookWaitsUntilItsAnswerIsStale()
        {
            var due = SuggestionReleaseCheckProcessor.Due(
                Snapshot([Book("fresh-miss"), Book("stale-miss"), Book("fresh-hit"), Book("stale-hit")], []),
                Checks(
                    Check("fresh-miss", SuggestionReleaseStatus.NotFound, Now.AddDays(-6)),
                    Check("stale-miss", SuggestionReleaseStatus.NotFound, Now.AddDays(-8)),
                    Check("fresh-hit", SuggestionReleaseStatus.Found, Now.AddDays(-29)),
                    Check("stale-hit", SuggestionReleaseStatus.Found, Now.AddDays(-31))),
                Now);

            Assert.Equal(["stale-miss", "stale-hit"], due.Select(book => book.Asin));
        }

        /// <summary>
        /// An indexer outage is not evidence that nobody holds the book, so the answer is
        /// worth asking again in hours rather than the week a real miss waits.
        /// </summary>
        [Fact]
        public void ACheckThatLearnedNothingIsTriedAgainInHoursNotDays()
        {
            var due = SuggestionReleaseCheckProcessor.Due(
                Snapshot([Book("fresh-unknown"), Book("stale-unknown"), Book("fresh-miss")], []),
                Checks(
                    Check("fresh-unknown", SuggestionReleaseStatus.Unknown, Now.AddHours(-5)),
                    Check("stale-unknown", SuggestionReleaseStatus.Unknown, Now.AddHours(-7)),
                    Check("fresh-miss", SuggestionReleaseStatus.NotFound, Now.AddHours(-7))),
                Now);

            Assert.Equal(["stale-unknown"], due.Select(book => book.Asin));
        }

        [Fact]
        public void ABookNothingIsStoredForIsCheckedAtOnce()
        {
            Assert.True(SuggestionReleaseCheckProcessor.IsDue("never-seen", Checks(), Now));
        }
    }
}
