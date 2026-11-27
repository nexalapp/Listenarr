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
namespace Listenarr.Domain.Configuration
{
    /// <summary>
    /// Settings governing what a completed download is allowed to become.
    /// </summary>
    /// <remarks>
    /// Their own file because ApplicationSettings sits at the 500-line focus rule, and a
    /// setting worth a paragraph should not have to be squeezed into one line to fit.
    /// </remarks>
    public partial class ApplicationSettings
    {
        /// <summary>
        /// Whether a completed download is judged against the book's runtime before any
        /// file is moved, and refused when the files are unreadable, short of the runtime,
        /// or far longer than it.
        /// </summary>
        /// <remarks>
        /// On by default. Without it a release holding a whole series imports as one book,
        /// and a release missing its last part imports as a complete one — both measured on
        /// a real library. A refusal leaves everything in the download directory, where the
        /// found-on-disk scanner clusters it by tag into the books it really holds.
        /// </remarks>
        public bool GateImportOnCompleteness { get; set; } = true;
    }
}
