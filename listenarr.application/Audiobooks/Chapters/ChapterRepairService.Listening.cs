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
using Microsoft.Extensions.Logging;

namespace Listenarr.Application.Audiobooks.Chapters
{
    /// <summary>
    /// Listening at a run of marks, and what to do about the ones that cannot be heard.
    ///
    /// <para>
    /// A window that will not decode is not silence — silence is an answer the planner
    /// acts on — so it cannot simply pass for one. But a book of two hundred marks with
    /// one bad spot in it is a book whose chapters are perfectly findable from the other
    /// hundred and ninety-nine, and refusing the lot was how three long books spent a
    /// day being planned, failing at the same offset, and being planned again. So a few
    /// unreadable marks are recorded as unheard and the plan goes on; past a few, the
    /// file itself is the problem and the plan is refused with a count.
    /// </para>
    /// </summary>
    public sealed partial class ChapterRepairService
    {
        /// <summary>Always allow this many bad windows, however short the book.</summary>
        public const int AlwaysToleratedUnreadable = 3;

        /// <summary>Beyond the flat allowance, this share of a book's marks may be unreadable.</summary>
        public const double ToleratedUnreadableShare = 0.1;

        /// <summary>How many unreadable marks this file may have before its plan is refused.</summary>
        public static int ToleratedUnreadable(int markCount) =>
            Math.Max(AlwaysToleratedUnreadable, (int)(markCount * ToleratedUnreadableShare));

        /// <summary>
        /// What was heard at each of these starts, in order. A mark that cannot be heard
        /// is null, and counted; too many and the caller is told rather than given a list
        /// with holes in it.
        /// </summary>
        private async Task<HeardMarks> HearEveryAsync(
            string fullPath,
            IReadOnlyList<TimeSpan> starts,
            string? model,
            int markCount,
            Action<int>? progress,
            CancellationToken cancellationToken)
        {
            var heard = new List<string?>(starts.Count);
            var unreadable = 0;
            var tolerated = ToleratedUnreadable(markCount);

            foreach (var start in starts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    heard.Add(await HearAsync(fullPath, start, model, cancellationToken));
                }
                catch (TranscriptionFailedException ex)
                {
                    unreadable++;
                    if (unreadable > tolerated)
                    {
                        throw new TranscriptionFailedException(
                            $"{unreadable} of {starts.Count} mark(s) could not be heard, which is more than this file is allowed ({tolerated}). The last said: {ex.Message}",
                            ex);
                    }

                    logger.LogWarning(
                        "Mark {Index} of {Count} in {Path} could not be heard ({Unreadable} so far, {Tolerated} allowed); carrying on",
                        heard.Count + 1,
                        starts.Count,
                        LogRedaction.SanitizeFilePath(fullPath),
                        unreadable,
                        tolerated);
                    heard.Add(null);
                }

                progress?.Invoke(heard.Count);
            }

            return new HeardMarks(heard, unreadable);
        }

        /// <summary>What was heard, and how much of it could not be.</summary>
        private sealed record HeardMarks(List<string?> Heard, int Unreadable);
    }
}
