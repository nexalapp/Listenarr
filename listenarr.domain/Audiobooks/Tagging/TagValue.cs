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

namespace Listenarr.Domain.Audiobooks.Tagging
{
    /// <summary>
    /// Comparing tag values the way a reader would, not byte for byte.
    ///
    /// <para>
    /// One rule, used in both places it matters. The planner uses it to decide whether a
    /// tag needs writing at all, and the verifier uses it to decide whether what came
    /// back is what went in. If the two disagreed, a value the planner called "already
    /// correct" could be one the verifier called missing, and every run would rewrite
    /// every file without ever converging.
    /// </para>
    /// <para>
    /// A file written on one platform carries CRLF where the source had LF, and some
    /// taggers add trailing whitespace on a round trip. Neither is a change anyone made.
    /// </para>
    /// </summary>
    public static class TagValue
    {
        public static string Normalize(string? value) =>
            value == null
                ? string.Empty
                : value.Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Trim();

        public static bool AreEquivalent(string? left, string? right) =>
            string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

        /// <summary>
        /// The longest value that may be written into one tag.
        ///
        /// Generous — a blurb runs to a few thousand characters — but not unbounded: an
        /// operator-typed value arrives from a form, and a tag is not the place to
        /// discover how large a metadata box a player will tolerate.
        /// </summary>
        public const int MaxLength = 16_384;

        /// <summary>
        /// Make an operator-typed value fit to write.
        ///
        /// A value rendered from a pattern has already been through this; one typed into
        /// the preview has not, and a stray control character would corrupt the atom
        /// carrying it. Newlines survive, because a blurb has paragraphs.
        /// </summary>
        public static string Sanitize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new System.Text.StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (c == '\r')
                {
                    continue;
                }

                if (c == '\n' || !char.IsControl(c))
                {
                    builder.Append(c);
                }
            }

            var sanitized = builder.ToString().Trim();
            return sanitized.Length > MaxLength ? sanitized[..MaxLength].TrimEnd() : sanitized;
        }
    }
}
