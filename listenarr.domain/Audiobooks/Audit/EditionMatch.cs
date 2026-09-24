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
    /// <summary>One edition a catalogue offers, with the length that identifies it.</summary>
    public sealed record EditionCandidate(
        string Id,
        string? Title,
        IReadOnlyList<string> Narrators,
        string? Publisher,
        int? RuntimeMinutes);

    /// <summary>What comparing a file's length to the catalogue could establish.</summary>
    public enum EditionMatchOutcome
    {
        /// <summary>Not checked, or nothing to check with.</summary>
        Unknown,

        /// <summary>The file is the length of the edition the record names.</summary>
        Agrees,

        /// <summary>The file is the length of a different edition, and only that one.</summary>
        OtherEdition,

        /// <summary>More than one edition fits. Which one cannot be said from length.</summary>
        Ambiguous,

        /// <summary>No edition on offer is near the file's length.</summary>
        NoCandidate,

        /// <summary>
        /// The length names one edition and the audio names another. Both cannot be right,
        /// so neither is offered.
        /// </summary>
        Contested
    }

    /// <summary>The outcome as the API spells it, one word the client can switch on.</summary>
    public static class EditionMatchOutcomeNames
    {
        public static string Of(EditionMatchOutcome outcome) => outcome switch
        {
            EditionMatchOutcome.Agrees => "agrees",
            EditionMatchOutcome.OtherEdition => "other-edition",
            EditionMatchOutcome.Ambiguous => "ambiguous",
            EditionMatchOutcome.NoCandidate => "no-candidate",
            EditionMatchOutcome.Contested => "contested",
            _ => "unknown"
        };
    }

    /// <param name="Outcome">What could be established.</param>
    /// <param name="Best">The edition the file's length points at, when one does.</param>
    /// <param name="RunnerUp">The next nearest, which is why an answer is ambiguous.</param>
    /// <param name="NarratorAgrees">
    /// Whether a narrator heard in the audio also names the winning edition. Null when no
    /// narrator was heard, which is the difference between two signals agreeing and one
    /// signal alone.
    /// </param>
    /// <param name="Reason">One sentence for the page.</param>
    public sealed record EditionMatchResult(
        EditionMatchOutcome Outcome,
        EditionCandidate? Best,
        EditionCandidate? RunnerUp,
        bool? NarratorAgrees,
        string Reason)
    {
        public static EditionMatchResult Nothing { get; } =
            new(EditionMatchOutcome.Unknown, null, null, null, string.Empty);
    }

    /// <summary>
    /// Which edition a file actually is, from how long it runs.
    ///
    /// <para>
    /// A catalogue publishes a runtime per edition, and two recordings of one book differ
    /// in length far more than a recording differs from its own stated runtime. That makes
    /// length a fingerprint. Ender's Shadow on this library runs 386 minutes: the Phoenix
    /// Books abridgement read by Michael Gross is 374 and the Macmillan reading by Scott
    /// Brick is 942, so the file names itself without ambiguity, and the record - which
    /// matched Macmillan to within twelve seconds - was describing a book the owner did
    /// not have.
    /// </para>
    /// <para>
    /// It answers by refusing far more often than by choosing. Two editions of similar
    /// length cannot be told apart this way and the honest answer is that they cannot;
    /// an edition absent from every catalogue, which is the common case for a cassette-era
    /// rip, must not be snapped onto whichever modern release happens to sit nearest.
    /// </para>
    /// </summary>
    public static class EditionMatch
    {
        /// <summary>
        /// How far a file may sit from an edition's published runtime and still be it.
        ///
        /// <para>
        /// Wide, because a published runtime and a measured file disagree for dull reasons:
        /// a shop's ident, a closing advertisement, a different master. The file that
        /// prompted this sits 3.1% from its own edition. Anything genuinely different is
        /// an order of magnitude further away - the wrong edition of that same book was
        /// 144% off - so the width costs nothing and guessing does.
        /// </para>
        /// </summary>
        public const double Tolerance = 0.08;

        public static EditionMatchResult Judge(
            double? fileMinutes,
            string? recordEditionId,
            IReadOnlyList<EditionCandidate>? candidates,
            IReadOnlyList<string>? heardNarrators = null)
        {
            if (fileMinutes is not { } minutes || minutes <= 0 || candidates is not { Count: > 0 })
            {
                return EditionMatchResult.Nothing;
            }

            var ranked = candidates
                .Where(candidate => candidate.RuntimeMinutes is > 0)
                .Select(candidate => (Candidate: candidate, Distance: Distance(minutes, candidate.RuntimeMinutes!.Value)))
                .OrderBy(pair => pair.Distance)
                .ToList();

            if (ranked.Count == 0)
            {
                return EditionMatchResult.Nothing;
            }

            var closest = ranked[0];
            if (closest.Distance > Tolerance)
            {
                return new EditionMatchResult(
                    EditionMatchOutcome.NoCandidate,
                    null,
                    null,
                    null,
                    $"Nothing on offer runs for {Clock(minutes)}; the nearest is {Describe(closest.Candidate)}.");
            }

            var runnerUp = ranked.Count > 1 ? ranked[1] : default;
            if (ranked.Count > 1 && runnerUp.Distance <= Tolerance)
            {
                return new EditionMatchResult(
                    EditionMatchOutcome.Ambiguous,
                    closest.Candidate,
                    runnerUp.Candidate,
                    null,
                    $"Two editions run for about {Clock(minutes)} - {Describe(closest.Candidate)} and "
                    + $"{Describe(runnerUp.Candidate)} - so the length cannot say which this is.");
            }

            var narratorAgrees = NarratorAgreement(closest.Candidate, heardNarrators);

            if (!string.IsNullOrWhiteSpace(recordEditionId)
                && string.Equals(closest.Candidate.Id, recordEditionId, StringComparison.OrdinalIgnoreCase))
            {
                return new EditionMatchResult(
                    EditionMatchOutcome.Agrees,
                    closest.Candidate,
                    runnerUp.Candidate,
                    narratorAgrees,
                    $"The files run {Clock(minutes)}, which is the edition on record.");
            }

            // Length and voice disagreeing is not a near miss to be reported with a caveat.
            // One of them is describing a different recording, and proposing a re-match on
            // the strength of the weaker signal is how a wrong edition gets written in.
            if (narratorAgrees == false)
            {
                return new EditionMatchResult(
                    EditionMatchOutcome.Contested,
                    closest.Candidate,
                    runnerUp.Candidate,
                    false,
                    $"The files run {Clock(minutes)}, which would be {Describe(closest.Candidate)}, "
                    + "but that is not the narrator heard in the audio. Length and voice disagree, so neither is offered.");
            }

            var heard = narratorAgrees == true
                ? " The narrator heard in the audio names the same edition."
                : " No narrator was heard, so this is the length alone.";

            return new EditionMatchResult(
                EditionMatchOutcome.OtherEdition,
                closest.Candidate,
                runnerUp.Candidate,
                narratorAgrees,
                $"The files run {Clock(minutes)}, which is {Describe(closest.Candidate)}, not the edition on record.{heard}");
        }

        /// <summary>
        /// Whether a narrator heard in the audio is one of the winning edition's, by sound
        /// rather than by spelling: the transcriber writes what it hears.
        /// </summary>
        private static bool? NarratorAgreement(EditionCandidate candidate, IReadOnlyList<string>? heard)
        {
            if (heard is not { Count: > 0 } || candidate.Narrators.Count == 0)
            {
                return null;
            }

            return heard.Any(name => SpokenNameSimilarity.IsAnyOf(name, candidate.Narrators));
        }

        private static double Distance(double minutes, int runtime) =>
            Math.Abs(minutes - runtime) / runtime;

        private static string Describe(EditionCandidate candidate)
        {
            var who = candidate.Narrators.Count > 0 ? string.Join(" / ", candidate.Narrators) : "an uncredited reader";
            var where = string.IsNullOrWhiteSpace(candidate.Publisher) ? string.Empty : $", {candidate.Publisher}";
            return $"the {Clock(candidate.RuntimeMinutes ?? 0)} reading by {who}{where}";
        }

        private static string Clock(double minutes)
        {
            var whole = (int)Math.Round(minutes);
            var hours = whole / 60;
            var rest = whole % 60;
            return hours == 0 ? $"{rest}m" : rest == 0 ? $"{hours}h" : $"{hours}h {rest}m";
        }
    }
}
