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
namespace Listenarr.Application.Audiobooks
{
    /// <summary>
    /// Why an operator's request to stop or clear a queued job did not happen. A refusal
    /// is reported with its reason rather than swallowed, because the operator is looking
    /// at the row they asked about and needs to know why it is still there.
    /// </summary>
    public enum JobControlOutcome
    {
        Done,

        /// <summary>No such job. A row dismissed in another tab reads as this.</summary>
        NotFound,

        /// <summary>Cancel was asked of a job that had already finished.</summary>
        AlreadyTerminal,

        /// <summary>Dismiss was asked of a job that is still running.</summary>
        StillActive,

        /// <summary>
        /// The job is the only thing that knows where a library file is. Clearing it
        /// would leave the file to the scratch sweeper, and the book would lose its
        /// only copy.
        /// </summary>
        HoldsOnlyCopy
    }

    public sealed record JobControlResult(JobControlOutcome Outcome, string? Reason = null)
    {
        public bool Succeeded => Outcome == JobControlOutcome.Done;

        public static JobControlResult Done() => new(JobControlOutcome.Done);
    }
}
