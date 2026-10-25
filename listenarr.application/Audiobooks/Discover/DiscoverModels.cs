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
using Listenarr.Application.Audiobooks.Suggestions;

namespace Listenarr.Application.Audiobooks.Discover
{
    /// <summary>
    /// What a discovery tab shows, computed from rows already cached. Like the rest of
    /// the Suggested page, reading never goes to the network: a refresh does.
    /// </summary>
    /// <param name="Source">One of <see cref="Domain.Audiobooks.Discover.DiscoverSources"/>.</param>
    /// <param name="Sections">The sections the user follows, each with its books.</param>
    /// <param name="PagesPerSection">How deep the fetcher is told to go in each section.</param>
    /// <param name="Available">
    /// False when the source cannot be used at all — no abook.link indexer is configured,
    /// for instance — so the tab can say why rather than look empty.
    /// </param>
    /// <param name="Unavailable"></param>
    /// <param name="Status"></param>
    public sealed record DiscoverFeed(
        string Source,
        IReadOnlyList<DiscoverSectionFeed> Sections,
        int PagesPerSection,
        bool Available,
        string? Unavailable,
        DiscoverRefreshStatus Status);

    /// <summary>One section of a source, with the books it offered that the library lacks.</summary>
    /// <param name="SectionId"></param>
    /// <param name="SectionName"></param>
    /// <param name="PagesFetched"></param>
    /// <param name="TotalItems"></param>
    /// <param name="LastFetchedAt"></param>
    /// <param name="LastError"></param>
    /// <param name="Held">
    /// How many of the section's books are already on the shelf. They are not listed —
    /// this page is about what is missing — but the count says why a chart has gaps.
    /// </param>
    /// <param name="Books"></param>
    public sealed record DiscoverSectionFeed(
        string SectionId,
        string SectionName,
        int PagesFetched,
        int? TotalItems,
        DateTime? LastFetchedAt,
        string? LastError,
        int Held,
        IReadOnlyList<DiscoverEntry> Books);

    /// <summary>
    /// A book in a section: the catalog record the add flow wants, plus what the source
    /// said about it that a catalog cannot know.
    /// </summary>
    /// <param name="Rank">Place in the section as the source ordered it, 1-based.</param>
    /// <param name="PostedAt"></param>
    /// <param name="SourceUrl"></param>
    /// <param name="SourceNote"></param>
    /// <param name="Resolved">
    /// False when the book is still only what a forum title said: no cover, no ASIN.
    /// The background fetcher looks those up one at a time.
    /// </param>
    /// <param name="Book"></param>
    public sealed record DiscoverEntry(
        int Rank,
        DateTime? PostedAt,
        string? SourceUrl,
        string? SourceNote,
        bool Resolved,
        SuggestedBook Book);

    /// <summary>A section the user could follow, as the source lists it.</summary>
    /// <param name="Id"></param>
    /// <param name="Name"></param>
    /// <param name="Slug">Audible's URL segment for a category; null for a forum board.</param>
    /// <param name="Group">The heading the source files it under, when it has one.</param>
    /// <param name="Count">What the source says it holds — topics, for a board.</param>
    public sealed record DiscoverSectionOption(
        string Id,
        string Name,
        string? Slug = null,
        string? Group = null,
        int? Count = null);

    /// <summary>The sections a source offers, and which of them are followed.</summary>
    public sealed record DiscoverSectionCatalog(
        string Source,
        IReadOnlyList<DiscoverSectionOption> Options,
        IReadOnlyList<string> Selected,
        int PagesPerSection,
        bool Available,
        string? Unavailable);

    /// <summary>What the user chose to follow in one source. Stored in settings.</summary>
    public sealed record DiscoverSelection(
        IReadOnlyList<DiscoverSelectedSection> Sections,
        int PagesPerSection)
    {
        public static DiscoverSelection Empty(int pagesPerSection) => new([], pagesPerSection);
    }

    /// <summary>
    /// A followed section, carrying everything the fetcher needs to reach it. The name
    /// and slug are stored with the choice so a fetch never has to re-read the source's
    /// index first.
    /// </summary>
    public sealed record DiscoverSelectedSection(string Id, string Name, string? Slug = null);

    /// <summary>What a discovery fetch is doing, for the page to show.</summary>
    public sealed record DiscoverRefreshStatus(
        bool Running,
        int Completed,
        int Total,
        string? Current,
        DateTime? StartedAt,
        DateTime? FinishedAt,
        int Failed)
    {
        public static DiscoverRefreshStatus Idle => new(false, 0, 0, null, null, null, 0);
    }

    /// <summary>What the page sends to change which sections a source contributes.</summary>
    public sealed record SaveDiscoverSelectionRequest(
        IReadOnlyList<DiscoverSelectedSection>? Sections,
        int? PagesPerSection);
}
