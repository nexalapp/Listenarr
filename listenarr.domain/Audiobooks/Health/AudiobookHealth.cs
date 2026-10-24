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
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Quality;

namespace Listenarr.Domain.Audiobooks.Health
{
    public enum HealthState
    {
        /// <summary>The check has not run, so this category says nothing about the book.</summary>
        Unknown,

        Good,

        /// <summary>Something is off and a one-click fix exists.</summary>
        Warn,

        /// <summary>Something is wrong that a fix cannot promise to repair.</summary>
        Bad
    }

    /// <summary>One category's verdict and the sentence behind it.</summary>
    public sealed record HealthCategory(HealthState State, string? Reason)
    {
        public static readonly HealthCategory Unknown = new(HealthState.Unknown, null);
    }

    /// <summary>The four categories and the number that sums them up.</summary>
    /// <param name="Tags"></param>
    /// <param name="Naming"></param>
    /// <param name="Chapters"></param>
    /// <param name="Audio"></param>
    /// <param name="Score">
    /// 0–100 over the categories that have an answer, or null while none do. A book with
    /// one known category is scored on that category alone rather than punished for the
    /// checks that have not run yet.
    /// </param>
    public sealed record AudiobookHealthReport(
        HealthCategory Tags,
        HealthCategory Naming,
        HealthCategory Chapters,
        HealthCategory Audio,
        int? Score);

    /// <summary>The facts the judgement reads, all of them already stored on the book or its files.</summary>
    /// <param name="AuditVerdict">Whether the audio introduces itself as this book.</param>
    /// <param name="AuditAccepted">Someone listened and vouched for the record in spite of the verdict.</param>
    /// <param name="TagsClean">Whether a tag write would change nothing, or null while no tag snapshot exists. Locked tags already count as clean — the planner reports them unwritable, not wrong.</param>
    /// <param name="TagsReason">What the tag check said when it last ran.</param>
    /// <param name="NamingClean">Whether a rename would change nothing, or null while nothing has looked. Path-locked files already count as clean.</param>
    /// <param name="NamingReason">What the naming check said when it last ran.</param>
    /// <param name="WorstChapters">The worst chapter verdict among the book's files.</param>
    /// <param name="AudioQuality">What the recording sounds like.</param>
    /// <param name="AudioReason">The probe's sentence.</param>
    public sealed record AudiobookHealthFacts(
        AudioAuditVerdict AuditVerdict,
        bool AuditAccepted,
        bool? TagsClean,
        string? TagsReason,
        bool? NamingClean,
        string? NamingReason,
        ChapterHealth WorstChapters,
        AudioQuality AudioQuality,
        string? AudioReason);

    /// <summary>
    /// Sums a book up as four categories and a number. Pure — every input is a stored
    /// fact, so the score is computed at query time and never persisted, and a change in
    /// how it is weighed re-scores the whole library at once.
    /// </summary>
    public static class AudiobookHealth
    {
        /// <summary>
        /// Warn costs more than a dead average would give it: a category needing a click
        /// should read as a dent, not as half-broken.
        /// </summary>
        private const int GoodPoints = 100;
        private const int WarnPoints = 60;
        private const int BadPoints = 0;

        public static AudiobookHealthReport Judge(AudiobookHealthFacts facts)
        {
            var tags = JudgeTags(facts);
            var naming = JudgeNaming(facts);
            var chapters = JudgeChapters(facts.WorstChapters);
            var audio = JudgeAudio(facts.AudioQuality, facts.AudioReason);

            var known = new[] { tags, naming, chapters, audio }
                .Where(category => category.State != HealthState.Unknown)
                .Select(category => category.State switch
                {
                    HealthState.Good => GoodPoints,
                    HealthState.Warn => WarnPoints,
                    _ => BadPoints
                })
                .ToList();

            var score = known.Count == 0 ? (int?)null : (int)Math.Round(known.Average());
            return new AudiobookHealthReport(tags, naming, chapters, audio, score);
        }

        /// <summary>
        /// Tags are only as right as the record they are rendered from, which is why the
        /// audit's verdict lives here: a file perfectly tagged with the wrong book's
        /// metadata is not correct, it is confidently wrong.
        /// </summary>
        private static HealthCategory JudgeTags(AudiobookHealthFacts facts)
        {
            if (!facts.AuditAccepted)
            {
                switch (facts.AuditVerdict)
                {
                    case AudioAuditVerdict.Mismatch:
                        return new HealthCategory(
                            HealthState.Bad,
                            "The audio does not introduce itself as this book, so the tags describe the wrong one.");
                    case AudioAuditVerdict.NarratorMismatch:
                        return new HealthCategory(
                            HealthState.Warn,
                            "The audio names a different narrator than the record the tags come from.");
                    case AudioAuditVerdict.Incomplete:
                        return new HealthCategory(HealthState.Warn, "The audio cuts off before the book ends.");
                    case AudioAuditVerdict.Inconclusive:
                        return new HealthCategory(
                            HealthState.Warn,
                            "Too little was heard to say whether the record is right.");
                }
            }

            return facts.TagsClean switch
            {
                true => new HealthCategory(HealthState.Good, null),
                false => new HealthCategory(
                    HealthState.Warn,
                    facts.TagsReason ?? "The files do not carry the tags the record would write."),
                // The record checks out (or nobody has listened) but no tag snapshot
                // exists yet — for an audited book that is still a shrug about the files.
                null => facts.AuditVerdict == AudioAuditVerdict.NotAudited && !facts.AuditAccepted
                    ? HealthCategory.Unknown
                    : new HealthCategory(HealthState.Unknown, "The files' tags have not been read yet."),
            };
        }

        private static HealthCategory JudgeNaming(AudiobookHealthFacts facts) => facts.NamingClean switch
        {
            true => new HealthCategory(HealthState.Good, null),
            false => new HealthCategory(
                HealthState.Warn,
                facts.NamingReason ?? "The folder or file names differ from the naming pattern."),
            null => HealthCategory.Unknown,
        };

        private static HealthCategory JudgeChapters(ChapterHealth worst) => worst switch
        {
            ChapterHealth.Healthy => new HealthCategory(HealthState.Good, null),
            ChapterHealth.Corrupt => new HealthCategory(
                HealthState.Bad,
                "A chapter atom does not parse, or plays differently from what the bytes say."),
            ChapterHealth.Oversegmented => new HealthCategory(
                HealthState.Warn,
                "Many short, evenly-sized chapters: CD tracks that were never merged."),
            ChapterHealth.GenericTitles => new HealthCategory(
                HealthState.Warn,
                "The marks may be right but every title is a placeholder."),
            ChapterHealth.None => new HealthCategory(HealthState.Warn, "A long book with no chapter marks at all."),
            _ => HealthCategory.Unknown,
        };

        private static HealthCategory JudgeAudio(AudioQuality quality, string? reason) => quality switch
        {
            AudioQuality.Clean => new HealthCategory(HealthState.Good, null),
            // A score or production bed is loud but it is the production, not a defect.
            AudioQuality.ProductionAudio => new HealthCategory(HealthState.Good, reason),
            AudioQuality.Hiss => new HealthCategory(HealthState.Warn, reason),
            AudioQuality.Muffled => new HealthCategory(HealthState.Warn, reason),
            AudioQuality.HissAndMuffled => new HealthCategory(HealthState.Bad, reason),
            AudioQuality.CodecDamaged => new HealthCategory(HealthState.Bad, reason),
            _ => HealthCategory.Unknown,
        };

        /// <summary>The state as the API spells it, one word the client can filter on.</summary>
        public static string NameOf(HealthState state) => state switch
        {
            HealthState.Good => "good",
            HealthState.Warn => "warn",
            HealthState.Bad => "bad",
            _ => "unknown"
        };
    }
}
