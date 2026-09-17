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
using System.Text;

namespace Listenarr.Domain.Audiobooks
{
    /// <summary>
    /// How a book's narrators are written into a folder, file or tag.
    ///
    /// A full-cast production credits fifteen readers, and a filename that names them
    /// all does not fit on a Linux filesystem: NAME_MAX is 255 bytes and the name is
    /// silently impossible to create, which surfaces as a failed publish. The list is
    /// capped by a setting (0 = every narrator) and, whatever the setting, cut down to
    /// what fits. A cut list ends in "et al." so the name says it is not the whole cast.
    /// </summary>
    public static class NarratorNameStyle
    {
        public const string Separator = ", ";
        public const string Overflow = " et al.";

        /// <summary>Linux NAME_MAX, in bytes of UTF-8; also the NTFS per-component limit in chars.</summary>
        public const int MaxComponentBytes = 255;

        public static string Render(string? narrators, int maxNarrators)
        {
            if (string.IsNullOrWhiteSpace(narrators))
            {
                return string.Empty;
            }

            var names = Split(narrators);
            if (maxNarrators <= 0 || names.Count <= maxNarrators)
            {
                return string.Join(Separator, names);
            }

            return string.Join(Separator, names.Take(maxNarrators)) + Overflow;
        }

        /// <summary>
        /// Shorten a path component that is over the byte limit by dropping narrators from
        /// the end of a brace-delimited list inside it, then, if that is not enough, by
        /// cutting the text. Returns the component unchanged when it already fits.
        /// </summary>
        public static string FitComponent(string component, string extension = "")
        {
            if (Encoding.UTF8.GetByteCount(component) <= MaxComponentBytes)
            {
                return component;
            }

            var open = component.IndexOf('{');
            var close = open >= 0 ? component.IndexOf('}', open) : -1;
            if (open >= 0 && close > open)
            {
                var inside = component[(open + 1)..close];
                var names = Split(inside.EndsWith(Overflow, StringComparison.Ordinal)
                    ? inside[..^Overflow.Length]
                    : inside);
                for (var keep = names.Count - 1; keep >= 1; keep--)
                {
                    var list = string.Join(Separator, names.Take(keep)) + Overflow;
                    var candidate = component[..(open + 1)] + list + component[close..];
                    if (Encoding.UTF8.GetByteCount(candidate) <= MaxComponentBytes)
                    {
                        return candidate;
                    }
                }
            }

            // Nothing to drop, or dropping was not enough: cut the text, keeping the extension.
            var stem = extension.Length > 0 && component.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                ? component[..^extension.Length]
                : component;
            var budget = MaxComponentBytes - Encoding.UTF8.GetByteCount(extension);
            while (stem.Length > 1 && Encoding.UTF8.GetByteCount(stem) > budget)
            {
                stem = stem[..^1];
            }

            return stem.TrimEnd() + extension;
        }

        private static List<string> Split(string value) =>
            [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}
