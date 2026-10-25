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
using Listenarr.Application.Search.AbookLink;

namespace Listenarr.Application.Search.Contracts
{
    /// <summary>A topic that has been fetched and parsed.</summary>
    public sealed record AbookCandidate(int TopicId, string TopicTitle, AbookPost Post);

    /// <summary>
    /// The outcome of browsing abook.link, with the parse report attached so callers can
    /// see how much was understood rather than only what came out.
    /// </summary>
    public sealed record AbookBrowseResult(
        bool Succeeded,
        int HitCount,
        IReadOnlyList<AbookCandidate> Candidates,
        AbookParseReport Report,
        string? Reason);

    /// <summary>
    /// What a listing page yielded. Separate from <see cref="AbookBrowseResult"/>
    /// because a listing is a list of titles, not parsed posts: nothing has been opened.
    /// </summary>
    public sealed record AbookListingResult<T>(bool Succeeded, IReadOnlyList<T> Items, string? Reason)
    {
        public static AbookListingResult<T> Failed(string reason) => new(false, [], reason);
    }

    /// <summary>
    /// Reads abook.link. Everything here is free — no "thanks" is posted and no metered
    /// index is queried — so it is safe to call as often as needed.
    /// </summary>
    public interface IAbookLinkBrowser
    {
        /// <summary>The boards the forum lists, with the topic counts it shows.</summary>
        Task<AbookListingResult<AbookBoard>> ListBoardsAsync(CancellationToken ct = default);

        /// <summary>One page of a single board's topics. <paramref name="page"/> is 1-based.</summary>
        Task<AbookListingResult<AbookTopicListing>> BoardTopicsAsync(
            string boardId,
            int page,
            CancellationToken ct = default);

        /// <summary>
        /// Searches, then fetches and parses up to <paramref name="inspect"/> of the hits.
        /// </summary>
        Task<AbookBrowseResult> SearchAsync(string query, int inspect, CancellationToken ct = default);

        Task<AbookBrowseResult> GetTopicAsync(int topicId, CancellationToken ct = default);

        /// <summary>
        /// Reports what the forum replies to a sign-in, for diagnosing a login that does
        /// not take. Never returns the credentials themselves.
        /// </summary>
        Task<IReadOnlyDictionary<string, string>> DiagnoseLoginAsync(CancellationToken ct = default);

        /// <summary>
        /// Current state of every configured NZBKing key, in the order they would be
        /// spent, or empty when none is configured.
        /// </summary>
        Task<IReadOnlyList<NzbKingKeyStatus>> GetNzbKingPoolStatusAsync(CancellationToken ct = default);

        /// <summary>
        /// The most recent NZBKing access attempts across every configured key, newest
        /// first. Refusals are included: they are what the budget did to protect a key.
        /// </summary>
        Task<IReadOnlyList<NzbKingApiAccess>> GetNzbKingLedgerAsync(
            int limit,
            CancellationToken ct = default);
    }
}
