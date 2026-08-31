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
using System.Text;

namespace Listenarr.Application.Search.AbookLink
{
    /// <summary>
    /// One post the parser could not fully read.
    /// </summary>
    /// <param name="Reference">Topic id or fixture name, so the post can be found again.</param>
    /// <param name="Outcome">How far the parse got.</param>
    /// <param name="Detail">What was missing, in words a person can act on.</param>
    /// <param name="UnrecognisedLabels">Labels seen but not understood.</param>
    public sealed record AbookParseShortfall(
        string Reference,
        AbookParseOutcome Outcome,
        string Detail,
        IReadOnlyList<string> UnrecognisedLabels);

    /// <summary>
    /// Aggregates parse results.
    ///
    /// Deliberately shared between the offline scoring harness and the running
    /// application, because they need the same thing: which posts we failed to read and
    /// what we did not recognise in them. Offline it measures whether the parser is good
    /// enough; in the application it is what turns real usage into the sample that makes
    /// the parser better, instead of somebody hand-picking posts forever.
    ///
    /// An unrecognised label appearing repeatedly is a synonym set that needs a new entry,
    /// and this is how that becomes visible rather than staying a hunch.
    /// </summary>
    public sealed class AbookParseReport
    {
        private readonly Dictionary<AbookParseOutcome, int> _outcomes = new();
        private readonly Dictionary<string, int> _unrecognised = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<AbookParseShortfall> _shortfalls = new();

        public int Total { get; private set; }

        public IReadOnlyList<AbookParseShortfall> Shortfalls => _shortfalls;

        /// <summary>Unrecognised labels by how often they appeared, most frequent first.</summary>
        public IReadOnlyList<KeyValuePair<string, int>> UnrecognisedLabels =>
            _unrecognised.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).ToList();

        /// <summary>Posts that yielded both an identity and a search string.</summary>
        public int Complete => Count(AbookParseOutcome.Complete);

        /// <summary>
        /// Posts whose book we could identify, whether or not a payload was visible.
        ///
        /// This is the measure that means anything while searching: a payload is gated
        /// behind a "thanks" that only a deliberate grab posts, so during search every
        /// post legitimately lacks one and a completion rate reads as nought per cent
        /// however well the parsing went.
        /// </summary>
        public int Identified => Complete + Count(AbookParseOutcome.MissingSearchString);

        /// <summary>
        /// Share of posts fully understood, excluding those that were never releases.
        /// Requests and archive imports are correctly-classified skips, not failures, and
        /// counting them would make the parser look worse the more junk the forum holds.
        /// </summary>
        public double SuccessRate
        {
            get
            {
                var eligible = Eligible;
                return eligible <= 0 ? 1d : (double)Complete / eligible;
            }
        }

        /// <summary>
        /// Share of posts we could identify. The meaningful figure for a search, where no
        /// payload is expected to be visible.
        /// </summary>
        public double IdentificationRate
        {
            get
            {
                var eligible = Eligible;
                return eligible <= 0 ? 1d : (double)Identified / eligible;
            }
        }

        /// <summary>
        /// Posts that could have been understood. Requests and archive imports are
        /// correctly-classified skips, not failures.
        /// </summary>
        private int Eligible =>
            Total - Count(AbookParseOutcome.NotARelease) - Count(AbookParseOutcome.ArchiveSpot);

        public int Count(AbookParseOutcome outcome) => _outcomes.GetValueOrDefault(outcome);

        public void Add(string reference, AbookPost post)
        {
            ArgumentNullException.ThrowIfNull(post);

            Total++;
            _outcomes[post.Outcome] = _outcomes.GetValueOrDefault(post.Outcome) + 1;

            foreach (var label in post.UnrecognisedLabels)
            {
                _unrecognised[label] = _unrecognised.GetValueOrDefault(label) + 1;
            }

            if (post.Outcome is AbookParseOutcome.Complete
                or AbookParseOutcome.NotARelease
                or AbookParseOutcome.ArchiveSpot)
            {
                return;
            }

            _shortfalls.Add(new AbookParseShortfall(
                reference,
                post.Outcome,
                Describe(post),
                post.UnrecognisedLabels.ToList()));
        }

        private static string Describe(AbookPost post) => post.Outcome switch
        {
            AbookParseOutcome.MissingSearchString =>
                "Read the book but found no search string — the post may still be gated, or its payload is shaped in a way we do not recognise.",
            AbookParseOutcome.MissingIdentity =>
                "Found a search string but could not tell which book this is.",
            _ => "Nothing usable could be read from this post."
        };

        /// <summary>Human-readable summary for a harness run or a diagnostics screen.</summary>
        public string Summarise()
        {
            var text = new StringBuilder();
            text.AppendLine(
                $"Parsed {Total} posts — {IdentificationRate:P1} identified, {SuccessRate:P1} grabbable");

            foreach (var outcome in Enum.GetValues<AbookParseOutcome>())
            {
                var count = Count(outcome);
                if (count > 0)
                {
                    text.AppendLine($"  {outcome,-20} {count,5}");
                }
            }

            if (_unrecognised.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Unrecognised labels (candidates for the synonym sets):");
                foreach (var (label, count) in UnrecognisedLabels.Take(20))
                {
                    text.AppendLine($"  {count,4}x  {label}");
                }
            }

            if (_shortfalls.Count > 0)
            {
                text.AppendLine();
                text.AppendLine("Shortfalls:");
                foreach (var shortfall in _shortfalls.Take(25))
                {
                    text.AppendLine($"  [{shortfall.Outcome}] {shortfall.Reference}");
                }
            }

            return text.ToString();
        }
    }
}
