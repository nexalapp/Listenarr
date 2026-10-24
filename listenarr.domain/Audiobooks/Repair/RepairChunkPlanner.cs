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
namespace Listenarr.Domain.Audiobooks.Repair
{
    /// <summary>One stretch of a file for the enhancer: where it starts and how long it runs.</summary>
    public sealed record RepairChunk(int Index, TimeSpan Start, TimeSpan Length);

    /// <summary>
    /// Cuts a file into the stretches the enhancer processes one at a time.
    ///
    /// <para>
    /// Chunks exist for two reasons: the model holds five to ten gigabytes while it
    /// works, so hours of audio cannot go through in one piece; and a chunk is the unit
    /// of loss on a crash — a ten-hour book that dies on hour nine must redo twenty
    /// minutes, not nine hours.
    /// </para>
    /// <para>
    /// Chunks butt against each other exactly, with no overlap, so the reassembled file
    /// is sample-count identical to the plan and the chapter marks cannot drift. The
    /// model operates locally enough that the seam is inaudible against the damage being
    /// repaired; an overlap-and-crossfade would sound marginally safer and would break
    /// the exact duration accounting everything downstream leans on.
    /// </para>
    /// </summary>
    public static class RepairChunkPlanner
    {
        /// <summary>
        /// Twenty minutes: far inside the model's memory ceiling, small enough that a
        /// crash costs little, big enough that per-process model loading stays noise.
        /// </summary>
        public static readonly TimeSpan ChunkLength = TimeSpan.FromMinutes(20);

        /// <summary>
        /// A remainder shorter than this is absorbed into the previous chunk instead of
        /// becoming its own: the model's quality drops on very short inputs, and a
        /// three-second final chunk is all seam and no audio.
        /// </summary>
        public static readonly TimeSpan ShortestTail = TimeSpan.FromSeconds(30);

        public static IReadOnlyList<RepairChunk> Plan(TimeSpan fileDuration)
        {
            if (fileDuration <= TimeSpan.Zero)
            {
                return [];
            }

            if (fileDuration <= ChunkLength + ShortestTail)
            {
                return [new RepairChunk(0, TimeSpan.Zero, fileDuration)];
            }

            var chunks = new List<RepairChunk>();
            var cursor = TimeSpan.Zero;
            while (fileDuration - cursor > ChunkLength + ShortestTail)
            {
                chunks.Add(new RepairChunk(chunks.Count, cursor, ChunkLength));
                cursor += ChunkLength;
            }

            chunks.Add(new RepairChunk(chunks.Count, cursor, fileDuration - cursor));
            return chunks;
        }
    }
}
