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
namespace Listenarr.Application.Audiobooks.AutoGrab
{
    /// <summary>
    /// A list the Suggested page offers, as a thing auto-grab can draw from.
    ///
    /// Stored values: renaming one orphans a saved choice, which would silently change
    /// which list a person's books come from.
    /// </summary>
    public static class AutoGrabSources
    {
        /// <summary>Books by an author already in the library.</summary>
        public const string Authors = "authors";

        /// <summary>Gaps in a series already started.</summary>
        public const string Series = "series";

        /// <summary>One followed Audible chart category.</summary>
        public const string Discover = "discover";

        /// <summary>One followed abook.link board.</summary>
        public const string AbookLink = "abook";

        public static bool IsKnown(string? kind) =>
            kind is Authors or Series or Discover or AbookLink;

        /// <summary>Whether a source names one section of a tab rather than the whole tab.</summary>
        public static bool TakesSection(string? kind) =>
            kind is Discover or AbookLink;
    }

    /// <summary>
    /// One list auto-grab draws from, in the order it is drawn from.
    ///
    /// <para>
    /// The order is the list's position in <see cref="AutoGrabSettings.Sources"/>. It
    /// decides who is served first when a lane runs out of room - abook.link's thank
    /// budget is the usual one - and therefore whose books arrive today rather than
    /// tomorrow.
    /// </para>
    /// <para>
    /// <c>SectionId</c> names the chart category or board for the two tabs that have
    /// them, and is null for the tabs that are one list.
    /// </para>
    /// </summary>
    public sealed record AutoGrabSource(string Kind, string? SectionId = null, bool Enabled = true)
    {
        public string Key => SectionId is { Length: > 0 } section ? $"{Kind}:{section}" : Kind;
    }

    /// <summary>
    /// What auto-grab has been told to do. Stored in settings, read by the worker.
    ///
    /// <para>
    /// There is no count of books per run, and that is deliberate. A fixed cap stood in
    /// for limits it could not express: it throttled releases from a Newznab indexer,
    /// which cost nothing, at the same rate as abook.link posts, which cost a public
    /// thank - and at ten an hour it left nine hundred books waiting on a number nobody
    /// had reasoned about. A run now draws until the lanes themselves say stop.
    /// </para>
    /// </summary>
    public sealed record AutoGrabSettings(
        bool Enabled,
        int IntervalMinutes,
        IReadOnlyList<AutoGrabSource> Sources)
    {
        public static AutoGrabSettings Off => new(false, DefaultIntervalMinutes, []);

        public const int DefaultIntervalMinutes = 60;

        public const int MaxSources = 64;

        /// <summary>The followed lists, in the order a run draws from them.</summary>
        public IEnumerable<AutoGrabSource> Active => Sources.Where(source => source.Enabled);
    }

    /// <summary>
    /// A book a run would add, and the release it would send.
    ///
    /// The whole catalogue record travels with it, not just a title: a book added by a
    /// run must arrive with the cover, series and runtime that the same book added by
    /// hand would have.
    /// </summary>
    public sealed record AutoGrabCandidate(
        string Asin,
        Suggestions.SuggestedBook Book,
        string SourceKind,
        string SourceLabel,
        string? ReleaseTitle,
        string? IndexerName,
        long? SizeBytes)
    {
        public string Title => Book.Title;

        public string? Author => Book.Authors?.FirstOrDefault();
    }

    /// <summary>
    /// What a run would do right now, without doing any of it. The page shows this
    /// before the switch is thrown, because "grab everything I follow" is not a sentence
    /// anyone should have to take on trust.
    /// <para>
    /// <c>Available</c> is how many books could be grabbed in total, before the cap;
    /// the plan itself holds every one it would try.
    /// </para>
    /// </summary>

    public sealed record AutoGrabPlan(
        bool Enabled,
        int Available,
        IReadOnlyList<AutoGrabCandidate> Candidates,
        IReadOnlyList<AutoGrabSourceCount> BySource,
        string? Unavailable);

    public sealed record AutoGrabSourceCount(string Kind, string? SectionId, string Label, int Available);

    /// <summary>What a run did.</summary>
    /// <param name="Added">Books added to the library.</param>
    /// <param name="Downloaded">Of those, how many had a release sent. This is the
    /// number that matters, and the one a run used not to report.</param>
    /// <param name="Deferred">Books left for the next run because their lane was full.
    /// They are not failures and nothing was done to them.</param>
    /// <param name="StoppedBecause">Why those were left, when a lane ran out of room.</param>
    public sealed record AutoGrabReport(
        DateTime StartedAt,
        DateTime FinishedAt,
        int Added,
        int Downloaded,
        int Failed,
        IReadOnlyList<AutoGrabOutcome> Outcomes,
        int Deferred = 0,
        string? StoppedBecause = null);

    /// <param name="Added">The book was added to the library.</param>
    /// <param name="Downloaded">A release was actually sent to the download client.</param>
    public sealed record AutoGrabOutcome(
        string Asin, string Title, string SourceLabel, bool Added, bool Downloaded, string? Error);
}
