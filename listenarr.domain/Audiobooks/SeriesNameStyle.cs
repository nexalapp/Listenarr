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
using System.Text.Json;

namespace Listenarr.Domain.Audiobooks
{
    /// <summary>
    /// How a series name is written into paths and tags: with the words that only say
    /// "this is a series" taken off the end.
    ///
    /// <para>
    /// Audible names most series "X Series", "X Trilogy", "X Saga", and a library that
    /// takes those names as they come files its books under
    /// <c>[Space Odyssey Series 2]</c>. Of one library's 196 series, 37 ended in such
    /// a word; two stacked them ("Sprawl Trilogy Series"). The words are a setting,
    /// shipped with the ones that were found, because the same survey found names the
    /// rule must leave alone: "Wizarding World", "The Sixth World", "Forward Collection".
    /// </para>
    /// <para>
    /// Only the rendered name changes. The stored series name is what Audible and the
    /// Series page know the series by, and stripping it there would break both.
    /// </para>
    /// </summary>
    public static class SeriesNameStyle
    {
        /// <summary>The words a fresh install drops: every one seen in the survey, plus the siblings of Trilogy.</summary>
        public static readonly IReadOnlyList<string> DefaultDropWords =
        [
            "Series",
            "Trilogy",
            "Saga",
            "Cycle",
            "Sequence",
            "Duology",
            "Quartet",
            "Quintet",
            "Tetralogy"
        ];

        public static string DefaultDropWordsJson => JsonSerializer.Serialize(DefaultDropWords);

        /// <summary>The list as stored, or the default when the stored value is unreadable.</summary>
        public static IReadOnlyList<string> ParseDropWords(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            try
            {
                var words = JsonSerializer.Deserialize<List<string>>(json) ?? [];
                return words
                    .Select(word => word?.Trim() ?? string.Empty)
                    .Where(word => word.Length > 0)
                    .ToList();
            }
            catch (JsonException)
            {
                return DefaultDropWords;
            }
        }

        /// <summary>
        /// The name with any trailing drop words removed, repeatedly, case-insensitively.
        /// A name that is nothing but drop words is returned as it was: better a folder
        /// called "Series" than one called nothing.
        /// </summary>
        public static string Render(string? name, IReadOnlyList<string>? dropWords)
        {
            var trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0 || dropWords == null || dropWords.Count == 0)
            {
                return trimmed;
            }

            var current = trimmed;
            while (true)
            {
                var lastSpace = current.LastIndexOf(' ');
                if (lastSpace <= 0)
                {
                    break;
                }

                var lastWord = current[(lastSpace + 1)..];
                if (!dropWords.Any(word => string.Equals(word, lastWord, StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }

                current = current[..lastSpace].TrimEnd();
            }

            return current.Length == 0 ? trimmed : current;
        }
    }
}
