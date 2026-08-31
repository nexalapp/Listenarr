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
namespace Listenarr.Application.Search.Nzb
{
    /// <summary>
    /// One article an index holds for a search string.
    /// </summary>
    /// <param name="Id">Index-native identifier used to fetch the NZB.</param>
    /// <param name="Subject">Usenet subject line.</param>
    /// <param name="SizeBytes">Total size, or null when the index does not say.</param>
    /// <param name="FileCount">Files in the collection.</param>
    /// <param name="Complete">
    /// False when parts are missing. An incomplete collection fails at extraction rather
    /// than at download, so this is worth acting on before spending the bandwidth.
    /// </param>
    /// <param name="Groups">Newsgroups the article was posted to.</param>
    /// <param name="Poster">Posting address, often obfuscated.</param>
    /// <param name="PostedUtc">When it was posted, for age checks.</param>
    public sealed record NzbCandidate(
        string Id,
        string Subject,
        long? SizeBytes = null,
        int? FileCount = null,
        bool? Complete = null,
        IReadOnlyList<string>? Groups = null,
        string? Poster = null,
        DateTime? PostedUtc = null);

    /// <summary>
    /// Why a resolver did not produce an NZB.
    /// </summary>
    public enum NzbResolutionFailure
    {
        /// <summary>The index has nothing for this search string.</summary>
        NotIndexed,

        /// <summary>
        /// Hits exist but every one is missing parts. Distinct from NotIndexed because the
        /// release does exist — it may complete later, and a manual NZB can still work.
        /// </summary>
        OnlyIncomplete,

        /// <summary>Refused locally to protect a metered index's allowance.</summary>
        BudgetExhausted,

        /// <summary>The index answered with an error, or could not be reached.</summary>
        Unavailable,

        /// <summary>No search string to resolve.</summary>
        NothingToResolve
    }

    /// <summary>
    /// What one resolver did. Recorded whether it succeeded or not: when a grab stalls,
    /// "which indexes were asked and what did each say" is the first question, and an
    /// answer of "nothing found" is uninformative without it.
    /// </summary>
    /// <param name="Resolver">Which index answered.</param>
    /// <param name="Succeeded">Whether an NZB URL was produced.</param>
    /// <param name="NzbUrl">Where to fetch the NZB.</param>
    /// <param name="Candidates">Hits considered, so a person can see what was rejected.</param>
    /// <param name="Failure">Why it did not succeed.</param>
    /// <param name="Detail">Operator-facing explanation.</param>
    public sealed record NzbResolverResult(
        string Resolver,
        bool Succeeded,
        string? NzbUrl = null,
        IReadOnlyList<NzbCandidate>? Candidates = null,
        NzbResolutionFailure? Failure = null,
        string? Detail = null)
    {
        public static NzbResolverResult Found(string resolver, string nzbUrl, IReadOnlyList<NzbCandidate> candidates) =>
            new(resolver, true, nzbUrl, candidates);

        public static NzbResolverResult Failed(
            string resolver,
            NzbResolutionFailure failure,
            string detail,
            IReadOnlyList<NzbCandidate>? candidates = null) =>
            new(resolver, false, null, candidates, failure, detail);
    }

    /// <summary>
    /// The whole attempt: every resolver asked, in order, and what each said.
    /// </summary>
    public sealed record NzbResolution(
        bool Succeeded,
        string? NzbUrl,
        string? ResolvedBy,
        IReadOnlyList<NzbResolverResult> Attempts)
    {
        /// <summary>
        /// True when some index holds the release but only in incomplete form, or a
        /// metered index was skipped — cases where trying again later may work. Distinct
        /// from a release nothing has ever indexed.
        /// </summary>
        public bool WorthRetrying => !Succeeded && Attempts.Any(a =>
            a.Failure is NzbResolutionFailure.OnlyIncomplete
                or NzbResolutionFailure.BudgetExhausted
                or NzbResolutionFailure.Unavailable);
    }
}
