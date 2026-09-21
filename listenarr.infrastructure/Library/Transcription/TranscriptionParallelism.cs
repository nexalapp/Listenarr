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
namespace Listenarr.Infrastructure.Library.Transcription
{
    /// <summary>
    /// How many whisper runs share the CPU, and how many threads each gets.
    ///
    /// <para>
    /// whisper.cpp scales poorly past eight or so threads on a small model, so one run
    /// given every core leaves most of them idle. Splitting the cores into a few slots
    /// of eight-ish threads each, and letting that many chapter-planning jobs run at
    /// once, gets more books through per hour on a many-core box and changes nothing
    /// on a four-core one, which still gets a single slot.
    /// </para>
    /// </summary>
    public static class TranscriptionParallelism
    {
        /// <summary>Threads a slot wants before another slot is worth opening.</summary>
        public const int ThreadsPerSlotTarget = 8;

        /// <summary>Never more than this many runs at once: memory per model instance, and the disk they all read.</summary>
        public const int MaximumSlots = 4;

        /// <summary>How many transcriptions, and so how many planning jobs, may run at once.</summary>
        public static int Slots { get; } = SlotsFor(Environment.ProcessorCount);

        /// <summary>Threads for one run, leaving a core free for everything else.</summary>
        public static int ThreadsPerSlot { get; } = ThreadsFor(Environment.ProcessorCount, Slots);

        public static int SlotsFor(int processorCount) =>
            Math.Clamp(processorCount / ThreadsPerSlotTarget, 1, MaximumSlots);

        public static int ThreadsFor(int processorCount, int slots) =>
            Math.Max(1, (processorCount - 1) / Math.Max(1, slots));
    }
}
