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
using System.ComponentModel.DataAnnotations;

namespace Listenarr.Domain.Audiobooks.Discover
{
    /// <summary>
    /// Where a discovered book came from.
    ///
    /// These are stored values, not an enum: a row names its source in a TEXT column, and
    /// renaming a member of a persisted enum orphans every row holding the old name.
    /// Adding a source is safe; changing one of these strings is a data repair.
    /// </summary>
    public static class DiscoverSources
    {
        /// <summary>Audible's bestseller charts, one section per category.</summary>
        public const string AudibleCharts = "audible-charts";

        /// <summary>abook.link's boards, one section per board.</summary>
        public const string AbookLink = "abook-link";

        public static bool IsKnown(string? source) =>
            source is AudibleCharts or AbookLink;
    }

    /// <summary>
    /// One slice of a source the user has chosen to follow — an Audible chart category or
    /// an abook.link board — and how far through it the fetcher has got.
    ///
    /// The row exists only for sections that have been fetched at least once; the
    /// selection itself lives in settings, so unselecting a section leaves its cached
    /// books in place and reselecting it costs nothing.
    /// </summary>
    public class DiscoverSection
    {
        [Key]
        public int Id { get; set; }

        /// <summary>One of <see cref="DiscoverSources"/>.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>Audible browse-node id, or abook.link board id.</summary>
        public string SectionId { get; set; } = string.Empty;

        public string SectionName { get; set; } = string.Empty;

        /// <summary>How many pages of this section have been read.</summary>
        public int PagesFetched { get; set; }

        /// <summary>
        /// How many items the source says the section holds, when it says. Audible's
        /// chart reports "1 - 20 of 100"; a forum board reports a page count instead.
        /// </summary>
        public int? TotalItems { get; set; }

        /// <summary>
        /// Whether the source has run out: a chart that has given up its hundredth book,
        /// or a board page that came back empty. Without it the fetcher would keep
        /// asking for a page past the end, which Audible answers by repeating the last
        /// one for ever.
        /// </summary>
        public bool Exhausted { get; set; }

        public DateTime? LastFetchedAt { get; set; }

        /// <summary>Why the last fetch failed, for the page to show. Null once one succeeds.</summary>
        public string? LastError { get; set; }
    }

    /// <summary>
    /// A book a source offered, cached so the page never waits on the network.
    ///
    /// <para>
    /// The same book can be in two sections — a chart category and a board — and is one
    /// row in each: the rank, the note and the link all belong to the section it was
    /// found in, and the page shows it under that section.
    /// </para>
    /// </summary>
    public class DiscoverItem
    {
        [Key]
        public int Id { get; set; }

        public string Source { get; set; } = string.Empty;

        public string SectionId { get; set; } = string.Empty;

        /// <summary>ASIN for a chart entry, topic id for a forum post.</summary>
        public string ExternalId { get; set; } = string.Empty;

        /// <summary>
        /// Position in the section as the source ordered it: the chart place, or the
        /// position in the board's recency order at the time it was read.
        /// </summary>
        public int Rank { get; set; }

        /// <summary>
        /// Which page of the section it was read from. A chart page is re-read in place
        /// — what has dropped off it is gone — so the page is what identifies the rows a
        /// re-read replaces.
        /// </summary>
        public int Page { get; set; }

        public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>When the source published it, where the source says. Forum posts do.</summary>
        public DateTime? PostedAt { get; set; }

        /// <summary>The page this came from, so a card can link back to it.</summary>
        public string? SourceUrl { get; set; }

        /// <summary>
        /// What the source said that the book record does not carry — a forum post's
        /// format and size, for instance. Shown verbatim under the card.
        /// </summary>
        public string? SourceNote { get; set; }

        /// <summary>
        /// Whether metadata has been looked up for this item. Chart entries arrive
        /// identified by ASIN and are enriched in the same step; a forum topic is only a
        /// title until someone looks it up, which happens once, in the background.
        /// </summary>
        public bool Enriched { get; set; }

        public DiscoverBook Book { get; set; } = new();
    }

    /// <summary>
    /// A book as a discovery source described it, carrying the same fields as a cached
    /// catalog entry so the page can hand it to the add flow unchanged.
    /// </summary>
    public class DiscoverBook
    {
        public string? Asin { get; set; }

        public string Title { get; set; } = string.Empty;

        public string? Subtitle { get; set; }

        public List<string> Authors { get; set; } = new();

        public List<string> Narrators { get; set; } = new();

        public string? ImageUrl { get; set; }

        public int? Runtime { get; set; }

        public string? Language { get; set; }

        public string? Publisher { get; set; }

        public List<string> Genres { get; set; } = new();

        public string? Series { get; set; }

        public string? SeriesNumber { get; set; }

        public string? PublishedDate { get; set; }

        public string? Isbn { get; set; }

        public string? Link { get; set; }

        public string? MetadataSource { get; set; }

        public double? RatingOverall { get; set; }

        public int? RatingCount { get; set; }

        public double? RatingStory { get; set; }

        public string? Description { get; set; }
    }
}
