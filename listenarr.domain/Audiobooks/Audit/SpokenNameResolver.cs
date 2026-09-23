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
namespace Listenarr.Domain.Audiobooks.Audit
{
    /// <summary>One heard name, and the known name it turned out to be.</summary>
    /// <param name="Heard">Exactly what the transcriber wrote.</param>
    /// <param name="Resolved">The known name it matched, or the heard name when none did.</param>
    /// <param name="Confidence">How near the match was, 0 to 1; 0 when nothing matched.</param>
    public sealed record ResolvedName(string Heard, string Resolved, double Confidence)
    {
        /// <summary>Whether a known name was recognised, as against passing the heard one through.</summary>
        public bool WasRecognised => Confidence > 0;
    }

    /// <summary>
    /// Turning a name as heard into the name as it is spelled.
    ///
    /// <para>
    /// The transcriber spells what it hears, so a real narrator arrives misspelled:
    /// Garrick Hagon as "Garak Hagen", George Guidall as "George Guadal", Wanda McCaddon
    /// as "Wanda McCadden", Phil Gigante as "Phil Giganti". Written through unchecked,
    /// each becomes a second, near-duplicate narrator in the library, and a search for
    /// the right edition finds nothing.
    /// </para>
    /// <para>
    /// The library is a better dictionary than any general one: it already holds the
    /// spellings its own books use. A heard name is matched against those by sound, and
    /// only adopted when one candidate clearly wins. Where several are equally near,
    /// nothing is chosen - a dense shelf of similar names is exactly where snapping to
    /// the nearest would invent a wrong answer.
    /// </para>
    /// </summary>
    public static class SpokenNameResolver
    {
        /// <summary>
        /// A second candidate this near to the winner makes the choice ambiguous, and the
        /// heard name is kept rather than guessed at.
        /// </summary>
        public const double AmbiguityMargin = 0.05;

        /// <summary>
        /// The heard name resolved against names already known, or passed through
        /// unchanged when none is near enough or two are equally near.
        /// </summary>
        public static ResolvedName Resolve(string? heard, IEnumerable<string?>? known)
        {
            var trimmed = (heard ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return new ResolvedName(trimmed, trimmed, 0);
            }

            string? best = null;
            var bestScore = 0.0;
            var runnerUp = 0.0;

            foreach (var candidate in known ?? [])
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                var score = SpokenNameSimilarity.Best(trimmed, [candidate]);

                // An exact spelling already in the library settles it outright.
                if (string.Equals(candidate.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return new ResolvedName(trimmed, candidate.Trim(), 1);
                }

                if (score > bestScore)
                {
                    runnerUp = bestScore;
                    bestScore = score;
                    best = candidate.Trim();
                }
                else if (score > runnerUp)
                {
                    runnerUp = score;
                }
            }

            if (best == null || bestScore < SpokenNameSimilarity.SameNameThreshold)
            {
                return new ResolvedName(trimmed, trimmed, 0);
            }

            // Two names equally close is not a spelling correction, it is a coin toss.
            // The same surname read by two people is common enough to matter.
            if (bestScore - runnerUp < AmbiguityMargin && runnerUp >= SpokenNameSimilarity.SameNameThreshold)
            {
                return new ResolvedName(trimmed, trimmed, 0);
            }

            return new ResolvedName(trimmed, best, bestScore);
        }

        /// <summary>
        /// A credit naming several readers - "Angela Dawe and Phil Gigante" - resolved one
        /// name at a time. Productions credit two readers far more often than anyone is
        /// called "and".
        /// </summary>
        public static IReadOnlyList<ResolvedName> ResolveEach(string? heard, IEnumerable<string?>? known)
        {
            var list = known?.ToList();
            return Split(heard).Select(name => Resolve(name, list)).ToList();
        }

        /// <summary>The readers in one credit, in the order they were named.</summary>
        public static IReadOnlyList<string> Split(string? heard)
        {
            if (string.IsNullOrWhiteSpace(heard))
            {
                return [];
            }

            return System.Text.RegularExpressions.Regex
                .Split(heard, @"\s*(?:,|&|\band\b)\s*", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Select(part => part.Trim())
                .Where(part => part.Length > 0)
                .ToList();
        }
    }
}
