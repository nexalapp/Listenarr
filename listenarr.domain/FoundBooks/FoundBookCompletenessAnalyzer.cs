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
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Listenarr.Domain.FoundBooks
{
    /// <summary>What one audio file of a cluster says about itself, as far as completeness goes.</summary>
    public sealed record FoundBookAudioObservation(
        string FileName,
        bool ProbeFailed,
        double DurationSeconds,
        int ChapterCount,
        int? TrackNumber,
        int? TrackTotal,
        int? SequenceHint);

    public sealed record FoundBookCompletenessVerdict(FoundBookCompleteness Completeness, string Reason);

    /// <summary>
    /// Decides whether a cluster of files is the whole book from the files alone.
    ///
    /// The bar for <see cref="FoundBookCompleteness.Complete"/> is one positive signal
    /// and no negative one. The positive signals are a numbered run that closes ("01 of
    /// 20" through "20 of 20"), a declared running time the files add up to, or a single
    /// file that is plainly a book. A gap in the numbering or a shortfall against the
    /// declared time is negative. Files numbered from one with no total anywhere are
    /// neither: the last part could be missing and nothing would say so, so they stay
    /// <see cref="FoundBookCompleteness.Unknown"/> and are offered but never auto-added.
    /// </summary>
    public static partial class FoundBookCompletenessAnalyzer
    {
        // "01 of 20", "01/20"
        [GeneratedRegex(@"(?<!\d)(\d{1,3})\s*(?:of|/)\s*(\d{1,3})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex OfPattern();

        // "01-20", "01--20", "[01-30]"
        [GeneratedRegex(@"(?<!\d)(\d{1,3})\s*-{1,2}\s*(\d{1,3})(?!\d)", RegexOptions.CultureInvariant)]
        private static partial Regex DashPattern();

        private const double DeclaredTolerance = 0.03;
        private const double SingleFileMinimumSeconds = 20 * 60;

        public static FoundBookCompletenessVerdict Analyze(
            IReadOnlyList<FoundBookAudioObservation> files,
            double? declaredDurationSeconds)
        {
            ArgumentNullException.ThrowIfNull(files);
            if (files.Count == 0)
            {
                return new(FoundBookCompleteness.Corrupt, "No audio files.");
            }

            var unreadable = files.Where(f => f.ProbeFailed || f.DurationSeconds <= 0).ToList();
            if (unreadable.Count > 0)
            {
                var names = string.Join(", ", unreadable.Take(3).Select(f => f.FileName));
                var more = unreadable.Count > 3 ? $" and {unreadable.Count - 3} more" : string.Empty;
                return new(
                    FoundBookCompleteness.Corrupt,
                    $"{unreadable.Count} file{(unreadable.Count == 1 ? string.Empty : "s")} unreadable: {names}{more}.");
            }

            var negatives = new List<string>();
            var positives = new List<string>();
            var neutrals = new List<string>();

            var total = files.Sum(f => f.DurationSeconds);
            if (declaredDurationSeconds is > 0)
            {
                var ratio = total / declaredDurationSeconds.Value;
                if (ratio < 1 - DeclaredTolerance)
                {
                    negatives.Add($"{Clock(total)} of the declared {Clock(declaredDurationSeconds.Value)}.");
                }
                else if (ratio <= 1 + DeclaredTolerance)
                {
                    positives.Add($"Matches the declared {Clock(declaredDurationSeconds.Value)}.");
                }
                else
                {
                    neutrals.Add($"{Clock(total)} against a declared {Clock(declaredDurationSeconds.Value)}.");
                }
            }

            CheckNumbering(files, negatives, positives, neutrals);

            if (files.Count == 1)
            {
                var only = files[0];
                if (only.ChapterCount > 0)
                {
                    positives.Add($"One file with {only.ChapterCount} chapters.");
                }
                else if (only.DurationSeconds >= SingleFileMinimumSeconds)
                {
                    positives.Add($"One file, {Clock(only.DurationSeconds)}.");
                }
                else
                {
                    neutrals.Add($"One {Clock(only.DurationSeconds)} file with no chapters.");
                }
            }

            if (negatives.Count > 0)
            {
                return new(FoundBookCompleteness.Incomplete, string.Join(" ", negatives));
            }

            if (positives.Count > 0)
            {
                return new(FoundBookCompleteness.Complete, string.Join(" ", positives));
            }

            return new(
                FoundBookCompleteness.Unknown,
                neutrals.Count > 0
                    ? string.Join(" ", neutrals)
                    : "No part numbering and no declared length.");
        }

        private static void CheckNumbering(
            IReadOnlyList<FoundBookAudioObservation> files,
            List<string> negatives,
            List<string> positives,
            List<string> neutrals)
        {
            var numbered = files.Select(Number).ToList();
            var fileTotals = files.Select(FileNameTotal).Where(t => t.HasValue).Select(t => t!.Value).Distinct().ToList();
            var tagTotals = files.Select(f => f.TrackTotal).Where(t => t is > 1).Select(t => t!.Value).Distinct().ToList();

            // A total counts only when every file that states one agrees; "1-12" beside
            // "1-05" is a disc/track pair, not a part count.
            int? total = fileTotals.Count == 1 && FileNameTotalCoversAll(files, fileTotals[0])
                ? fileTotals[0]
                : tagTotals.Count == 1 ? tagTotals[0] : null;

            if (numbered.Any(n => n == null))
            {
                if (total.HasValue && files.Count < total.Value)
                {
                    negatives.Add($"{files.Count} of {total.Value} parts.");
                }

                return;
            }

            var numbers = numbered.Select(n => n!.Value).Distinct().OrderBy(n => n).ToList();
            var min = numbers[0];
            var max = numbers[^1];

            if (total.HasValue)
            {
                var missing = Enumerable.Range(1, total.Value).Except(numbers).ToList();
                if (missing.Count > 0)
                {
                    negatives.Add($"Parts {min}–{max} of {total.Value}; missing {Ranges(missing)}.");
                }
                else
                {
                    positives.Add($"Parts 1–{total.Value} of {total.Value}.");
                }

                return;
            }

            var gaps = Enumerable.Range(min, max - min + 1).Except(numbers).ToList();
            if (min > 1)
            {
                gaps.InsertRange(0, Enumerable.Range(1, min - 1));
            }

            if (gaps.Count > 0)
            {
                negatives.Add($"Numbered {min}–{max}; missing {Ranges(gaps)}.");
            }
            else if (files.Count > 1)
            {
                neutrals.Add($"Numbered 1–{max} with no total declared.");
            }
        }

        private static int? Number(FoundBookAudioObservation file)
        {
            var name = Path.GetFileNameWithoutExtension(file.FileName);
            var of = OfPattern().Match(name);
            if (of.Success)
            {
                return Int(of.Groups[1]);
            }

            var dash = DashPattern().Match(name);
            if (dash.Success && Int(dash.Groups[2]) >= Int(dash.Groups[1]) && Int(dash.Groups[2]) >= 2)
            {
                return Int(dash.Groups[1]);
            }

            return file.TrackNumber ?? file.SequenceHint;
        }

        private static int? FileNameTotal(FoundBookAudioObservation file)
        {
            var name = Path.GetFileNameWithoutExtension(file.FileName);
            var of = OfPattern().Match(name);
            if (of.Success && Int(of.Groups[2]) >= Int(of.Groups[1]) && Int(of.Groups[2]) >= 2)
            {
                return Int(of.Groups[2]);
            }

            var dash = DashPattern().Match(name);
            if (dash.Success && Int(dash.Groups[2]) >= Int(dash.Groups[1]) && Int(dash.Groups[2]) >= 2)
            {
                return Int(dash.Groups[2]);
            }

            return null;
        }

        private static bool FileNameTotalCoversAll(IReadOnlyList<FoundBookAudioObservation> files, int total) =>
            files.All(f => FileNameTotal(f) == total);

        private static int Int(Group group) =>
            int.Parse(group.Value, NumberStyles.Integer, CultureInfo.InvariantCulture);

        private static string Ranges(IReadOnlyList<int> numbers)
        {
            var sb = new StringBuilder();
            var i = 0;
            while (i < numbers.Count)
            {
                var start = numbers[i];
                var end = start;
                while (i + 1 < numbers.Count && numbers[i + 1] == end + 1)
                {
                    end = numbers[++i];
                }

                if (sb.Length > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(start == end
                    ? start.ToString(CultureInfo.InvariantCulture)
                    : $"{start}–{end}");
                i++;
            }

            return sb.ToString();
        }

        public static string Clock(double seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}h {span.Minutes:00}m"
                : $"{span.Minutes}m";
        }
    }
}
