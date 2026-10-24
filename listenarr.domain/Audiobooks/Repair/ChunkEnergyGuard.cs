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
    public enum ChunkOutcome
    {
        UseEnhanced,

        /// <summary>The model destroyed the passage; the original chunk is spliced back.</summary>
        UseOriginal
    }

    /// <summary>
    /// The music guard. The enhancement model is trained on speech and treats an
    /// orchestral score as noise around a voice that is not there: measured on a full-cast
    /// production's opening, it removed most of the passage. Removing something <b>loud</b>
    /// is the signature of that failure — real cleaning only ever removes what was quiet —
    /// so a chunk whose output lost serious level from a loud input keeps its original.
    /// </summary>
    public static class ChunkEnergyGuard
    {
        /// <summary>
        /// A chunk at least this loud is carrying programme — a score, a song, loud
        /// narration — rather than hiss and room tone. Narration RMS sits near −20 dBFS;
        /// backgrounds worth removing sit under −35.
        /// </summary>
        public const double LoudChunkRmsDb = -35;

        /// <summary>
        /// Cleaning a loud chunk moves its level by a couple of dB; deleting its music
        /// moves it by ten or more. The line sits where the measured cases separate.
        /// </summary>
        public const double DestroyedDropDb = 6;

        public static ChunkOutcome Decide(double originalRmsDb, double enhancedRmsDb)
        {
            var loud = originalRmsDb > LoudChunkRmsDb;
            var dropped = originalRmsDb - enhancedRmsDb > DestroyedDropDb;
            return loud && dropped ? ChunkOutcome.UseOriginal : ChunkOutcome.UseEnhanced;
        }
    }
}
