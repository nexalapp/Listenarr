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
namespace Listenarr.Application.Search.AbookLink
{
    /// <summary>
    /// How much of a post we managed to understand.
    ///
    /// abook.link posts are written by hand, so a parse is rarely all-or-nothing. What
    /// matters is whether we got enough to act: a book we can identify and a string we
    /// can resolve. Everything else is display detail.
    /// </summary>
    public enum AbookParseOutcome
    {
        /// <summary>Identified the book and found a search string.</summary>
        Complete,

        /// <summary>Identified the book, but no search string — the grab cannot proceed unaided.</summary>
        MissingSearchString,

        /// <summary>Found a search string but could not identify the book confidently.</summary>
        MissingIdentity,

        /// <summary>An archive import from the old forum. Payload references a dead domain.</summary>
        ArchiveSpot,

        /// <summary>Not a release at all — a request, a filled request or a reading order.</summary>
        NotARelease,

        /// <summary>Nothing usable.</summary>
        Unusable
    }

    /// <summary>
    /// What a parsed abook.link topic yielded.
    ///
    /// Fields are nullable on purpose: posters omit them freely, and a missing field must
    /// read as "the poster did not say" rather than a default that later looks like fact.
    /// <see cref="RawNfo"/> and <see cref="RawPayload"/> are always kept so the UI can show
    /// the original text when parsing disagrees with what a human can plainly see.
    /// </summary>
    public sealed class AbookPost
    {
        public AbookParseOutcome Outcome { get; set; } = AbookParseOutcome.Unusable;

        // --- identity ---
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Narrator { get; set; }
        public string? SeriesName { get; set; }
        public string? SeriesPosition { get; set; }
        public int? Year { get; set; }
        public string? Publisher { get; set; }

        /// <summary>Audible ASIN, when the post links one. The strongest match key available.</summary>
        public string? Asin { get; set; }

        // --- quality ---
        public string? Format { get; set; }
        public int? FileCount { get; set; }
        public long? SizeBytes { get; set; }
        public TimeSpan? Duration { get; set; }
        public string? Bitrate { get; set; }
        public bool? Abridged { get; set; }

        /// <summary>Archive tool named by the poster, e.g. Winrar. Explains why a password exists.</summary>
        public string? CompressedWith { get; set; }

        // --- payload ---
        public string? SearchString { get; set; }
        public string? Password { get; set; }

        /// <summary>Newsgroup named in trailing prose, e.g. "a.b.misc".</summary>
        public string? NewsgroupHint { get; set; }

        /// <summary>
        /// True when the poster says the release is spread over parts that must be combined
        /// rather than fetched as a single NZB.
        /// </summary>
        public bool MultiPart { get; set; }

        /// <summary>Free prose from the payload block, shown verbatim rather than interpreted.</summary>
        public string? PayloadNotes { get; set; }

        // --- provenance ---
        public string? RawNfo { get; set; }
        public string? RawPayload { get; set; }

        /// <summary>Field labels seen but not understood. Drives improvement of the synonym sets.</summary>
        public List<string> UnrecognisedLabels { get; set; } = new();

        public bool CanGrab => SearchString is { Length: > 0 };

        public bool HasIdentity => Title is { Length: > 0 } && Author is { Length: > 0 };
    }
}
