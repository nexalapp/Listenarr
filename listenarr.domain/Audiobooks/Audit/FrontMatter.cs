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

namespace Listenarr.Domain.Audiobooks.Audit
{
    /// <summary>
    /// Whether a stretch read at the start of a book is the book's own front matter, or the
    /// book proper beginning.
    ///
    /// <para>
    /// This is what tells the credits walk whether to carry on. A window that came back with
    /// words was taken as the story starting, so the walk stopped - and for a book that opens
    /// on a foreword that is wrong twice over. Front matter is not the story, and a recording
    /// that announces itself after its introduction was never given the chance to.
    /// </para>
    /// <para>
    /// A chapter heading is the other half: once the book proper has started there is nothing
    /// ahead to find, and saying so is what keeps the walk from spending its whole budget on
    /// every book that simply never names itself.
    /// </para>
    /// </summary>
    public static partial class FrontMatter
    {
        /// <summary>
        /// A heading that stands before the book: "Introduction.", "Foreword", "Author's
        /// Note". Dealing in Futures opens on "Introduction." and five and a half minutes of
        /// Joe Haldeman explaining how the collection is put together.
        /// </summary>
        [GeneratedRegex(@"^[\s""'‘’“”\-]*(?i:(?:an?\s+|the\s+)?(?:translator|author|editor|publisher)(?:'|’)?s?\s+(?:note|foreword|preface|introduction)|introduction|foreword|forward|preface|prologue|prelude|dedication|epigraph|acknowledgements?|acknowledgments?|a\s+note\s+(?:on|about|from)|about\s+the\s+author)\b")]
        private static partial Regex Heading();

        /// <summary>
        /// The book proper starting. Numbered or named, but explicitly a division of the
        /// work: a bare "One." is as likely to be a sentence as a heading.
        /// </summary>
        [GeneratedRegex(@"^[\s""'‘’“”\-]*(?i:(?:chapter|part|book|section|episode)\s+(?:\d{1,3}|one|two|three|i|ii|iii|iv|v)\b|chapter\s+the\s+first\b)")]
        private static partial Regex Division();

        /// <summary>Whether the first thing said here is a front-matter heading.</summary>
        public static bool IsHeading(string? heard) => Heading().IsMatch(FirstLine(heard));

        /// <summary>Whether the first thing said here is the book proper beginning.</summary>
        public static bool IsBookStarting(string? heard) => Division().IsMatch(FirstLine(heard));

        /// <summary>
        /// The walk only ever judges how a window opens. Whatever follows is the same prose
        /// either way, and a heading buried in the middle of a window belongs to the passage
        /// before it, not to the window.
        /// </summary>
        private static string FirstLine(string? heard)
        {
            if (string.IsNullOrWhiteSpace(heard))
            {
                return string.Empty;
            }

            var trimmed = heard.TrimStart();
            var end = trimmed.IndexOfAny(['\n', '\r']);
            return end < 0 ? trimmed : trimmed[..end];
        }
    }
}
