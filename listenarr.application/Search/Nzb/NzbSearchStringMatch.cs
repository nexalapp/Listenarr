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

using System.Text.RegularExpressions;

namespace Listenarr.Application.Search.Nzb
{
    /// <summary>
    /// Decides whether an index hit is the release a search string names.
    ///
    /// A posted search string is an identifier, not a query: "abook.link - fm8lssqHWLW7Bownef"
    /// names one upload. Indexes treat it as words, though, and answer with whatever
    /// shares the common ones - every recent "abook.link - …" upload - newest first.
    /// Taking the top hit downloaded someone else's book, whose archives the post's
    /// password then could not open. So a hit is only accepted when every word of the
    /// search string is in its subject: the distinguishing code has to be there, not
    /// merely the uploader's prefix.
    /// </summary>
    public static partial class NzbSearchStringMatch
    {
        [GeneratedRegex(@"[^\p{L}\p{N}]+")]
        private static partial Regex Separators();

        /// <summary>
        /// True when every word of <paramref name="searchString"/> appears as a word of
        /// <paramref name="subject"/>, ignoring case and punctuation.
        /// </summary>
        public static bool Matches(string? searchString, string? subject)
        {
            var wanted = Words(searchString);
            if (wanted.Count == 0)
            {
                return false;
            }

            var present = Words(subject);
            return wanted.All(present.Contains);
        }

        /// <summary>The hits that are the named release, in their original order.</summary>
        public static IReadOnlyList<NzbCandidate> Filter(
            string? searchString,
            IReadOnlyList<NzbCandidate> candidates) =>
            candidates.Where(candidate => Matches(searchString, candidate.Subject)).ToList();

        private static HashSet<string> Words(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? []
                : Separators()
                    .Split(value)
                    .Where(word => word.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
