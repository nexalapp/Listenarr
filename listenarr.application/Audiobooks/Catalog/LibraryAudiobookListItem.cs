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

namespace Listenarr.Application.Audiobooks.Catalog
{
    public class LibraryAudiobookListItem
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string[]? Authors { get; set; }
        public string[]? Narrators { get; set; }
        public string? PublishYear { get; set; }
        public string? PublishedDate { get; set; }
        public string? Series { get; set; }
        public string? SeriesNumber { get; set; }
        public AudiobookSeriesMembershipDto[]? SeriesMemberships { get; set; }
        public string[]? Genres { get; set; }
        public string? Asin { get; set; }
        public string? OpenLibraryId { get; set; }
        public string? Publisher { get; set; }
        public string? Language { get; set; }
        public int? Runtime { get; set; }
        public string? Edition { get; set; }
        public string? ImageUrl { get; set; }
        public bool Monitored { get; set; }
        public string? BasePath { get; set; }
        public string? FilePath { get; set; }
        public long? FileSize { get; set; }
        public int FileCount { get; set; }

        /// <summary>
        /// Distinct container formats of the book's files, uppercase and sorted — for
        /// example ["MP3"] or ["M4B", "MP3"] for a book part-way through a conversion.
        ///
        /// A list rather than one value because a book can legitimately hold more than
        /// one, and collapsing that would make it invisible to a filter. Empty when the
        /// book has no registered files.
        /// </summary>
        public string[] Formats { get; set; } = [];
        public string? Quality { get; set; }
        public int? QualityProfileId { get; set; }
        public string[]? AuthorAsins { get; set; }
        public bool Wanted { get; set; }
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// The worst chapter verdict among the book's files, as a lowercase word the
        /// list filters on: <c>corrupt</c>, <c>oversegmented</c>, <c>generic-titles</c>,
        /// <c>none</c>, <c>healthy</c>. Null when no file has been judged yet.
        /// </summary>
        public string? ChapterHealth { get; set; }

        /// <summary>Whether a repair can do something about the worst file: amber when true, red when false.</summary>
        public bool ChapterRepairable { get; set; }

        /// <summary>
        /// How many of the book's tracked files a scan last failed to find at their paths.
        /// Their rows are kept, so the book still counts as having files; the list shows a
        /// badge and filters on it.
        /// </summary>
        public int NotFoundFiles { get; set; }

        /// <summary>
        /// Whether the audio introduces itself as this book, as a lowercase word:
        /// <c>match</c>, <c>narrator-mismatch</c>, <c>incomplete</c>, <c>mismatch</c>,
        /// <c>inconclusive</c>.
        /// Null until an audit has run.
        /// </summary>
        public string? AudioAudit { get; set; }

        /// <summary>One sentence for the badge's tooltip.</summary>
        public string? AudioAuditReason { get; set; }

        /// <summary>The narrator the audio named, which a repair would start from.</summary>
        public string? AudioAuditHeardNarrator { get; set; }

        /// <summary>Whether someone listened and overruled the verdict for these files.</summary>
        public bool AudioAuditAccepted { get; set; }

        /// <summary>
        /// What the recording sounds like, as a lowercase word the list filters on:
        /// <c>clean</c>, <c>hiss</c>, <c>muffled</c>, <c>hiss-and-muffled</c>,
        /// <c>codec-damaged</c>, <c>production-audio</c>. Null until a probe has run.
        /// </summary>
        public string? AudioQuality { get; set; }

        /// <summary>One sentence for the badge's tooltip.</summary>
        public string? AudioQualityReason { get; set; }

        /// <summary>
        /// The book summed up as a number, 0–100 over the checks that have run; null
        /// while none have. Computed at query time from the four stored category facts,
        /// never persisted, so reweighing it re-scores the whole library at once.
        /// </summary>
        public int? HealthScore { get; set; }

        /// <summary>Each category as <c>good</c>, <c>warn</c>, <c>bad</c> or <c>unknown</c>, with its sentence.</summary>
        public string? HealthTags { get; set; }
        public string? HealthTagsReason { get; set; }
        public string? HealthNaming { get; set; }
        public string? HealthNamingReason { get; set; }
        public string? HealthChapters { get; set; }
        public string? HealthChaptersReason { get; set; }
        public string? HealthAudio { get; set; }
        public string? HealthAudioReason { get; set; }

        /// <summary>
        /// The listener rating, so a card can carry it without fetching the book. Audible's
        /// overall score and how many rated it; <see cref="AudnexusRating"/> stands in when
        /// Audnexus answered instead, and has no count of its own.
        /// </summary>
        public double? AudibleRatingOverall { get; set; }

        public int? AudibleRatingOverallCount { get; set; }

        public double? AudnexusRating { get; set; }
    }
}
