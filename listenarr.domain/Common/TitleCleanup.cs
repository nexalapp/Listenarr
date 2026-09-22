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
using System.Text.RegularExpressions;

namespace Listenarr.Domain.Common
{
    /// <summary>
    /// Taking the narrator back out of a title that carries one.
    /// </summary>
    /// <remarks>
    /// Audible sells two readings of the same book by putting the reader in the title -
    /// "Lock In (Narrated by Wil Wheaton)" beside "Lock In (Narrated by Amber Benson)" -
    /// and a book matched to one of them is shelved under that whole string. The library
    /// already writes the narrator in its own braces, so the parenthetical says it twice
    /// and does it in the one field that should name only the book.
    /// <para>
    /// The strip happens where a catalogue record becomes a <c>Audiobook</c>, not where
    /// search results are rendered: the picker still needs the two editions to read
    /// differently, and the narrator column there is what tells them apart.
    /// </para>
    /// </remarks>
    public static class TitleCleanup
    {
        // Trailing only. A title may legitimately open with a bracket, and a parenthetical
        // in the middle belongs to whatever it follows.
        private static readonly Regex NarratorSuffix = new(
            @"[\s ]*[\(\[](?:un)?(?:narrated|read|performed)\s+by\b[^\)\]]*[\)\]][\s ]*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// The title with a trailing "(Narrated by …)" removed, repeatedly, or as it came
        /// when there is none. A title that is nothing else is returned untouched: better a
        /// book called "Narrated by Wil Wheaton" than one called nothing at all.
        /// </summary>
        public static string? StripNarratorSuffix(string? title)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return title;
            }

            var result = title;
            while (true)
            {
                var stripped = NarratorSuffix.Replace(result, string.Empty);
                if (stripped.Length == result.Length)
                {
                    break;
                }

                result = stripped;
            }

            result = result.TrimEnd();
            return result.Length == 0 ? title : result;
        }
    }
}
