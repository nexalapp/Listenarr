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
    /// <summary>One writable tag: what it is called, what it means, and what it defaults to.</summary>
    public sealed record TagDefinition(
        string Tag,
        string Label,
        string Description,
        string DefaultPattern,
        TagWriteMode DefaultMode,
        bool IsLongText = false);

    /// <summary>
    /// The tags Listenarr will write, and the defaults it writes them with.
    ///
    /// <para>
    /// The defaults are taken from the fork's own library rather than from a generic
    /// tagging convention. Every series book in it carries <c>album</c> in brackets
    /// mirroring the folder name and <c>sort_album</c> bracketless and sortable, and a
    /// default that wrote a plain title into <c>album</c> would disagree with several
    /// hundred already-correct files.
    /// </para>
    /// <para>
    /// Tags with no field behind them (<c>copyright</c>) and tags MP4 does not carry
    /// globally (<c>language</c>) default to <see cref="TagWriteMode.Never"/>: they are
    /// offered because an operator may have a use for them, not because Listenarr has
    /// anything to put there.
    /// </para>
    /// </summary>
    public static class TagCatalog
    {
        /// <summary>The MP4 atom Plex reads an album summary from, and the reason this exists.</summary>
        public const string Description = "description";

        public const string Title = "title";
        public const string Album = "album";
        public const string SortAlbum = "sort_album";
        public const string Artist = "artist";
        public const string AlbumArtist = "album_artist";
        public const string Composer = "composer";
        public const string Genre = "genre";
        public const string Date = "date";
        public const string Comment = "comment";
        public const string Copyright = "copyright";
        public const string Publisher = "publisher";
        public const string Language = "language";
        public const string Asin = "ASIN";
        public const string Series = "SERIES";
        public const string SeriesPosition = "SERIESPOSITION";
        public const string Subtitle = "SUBTITLE";
        public const string AudioFileUrl = "WWWAUDIOFILE";

        public static IReadOnlyList<TagDefinition> Definitions { get; } =
        [
            new(
                Title,
                "Title",
                "The book's own title. Players show this as the track or book name.",
                "{Title}",
                TagWriteMode.Always),

            new(
                Album,
                "Album",
                "What a player shows as the book. The library's convention brackets the series so the tag mirrors the folder name; the brackets disappear for a standalone book.",
                "{SeriesBrackets} {Title}",
                TagWriteMode.Always),

            new(
                SortAlbum,
                "Sort Album",
                "How the book sorts within its series. Bracketless, so a series reads in order rather than clustering under '['.",
                "{Series} {SeriesNumber} - {Title}",
                TagWriteMode.Always),

            new(
                Artist,
                "Artist",
                "The author. Audiobook players treat artist as the author, not the narrator.",
                "{Author}",
                TagWriteMode.Always),

            new(
                AlbumArtist,
                "Album Artist",
                "The author again. Plex groups a library by album artist, so leaving it empty scatters an author's books.",
                "{Author}",
                TagWriteMode.Always),

            new(
                Composer,
                "Composer (Narrator)",
                "The narrator. MP4 has no narrator atom, and every audiobook tagger uses composer for the reader.",
                "{Narrator}",
                TagWriteMode.Always),

            new(
                Genre,
                "Genre",
                "The book's genre.",
                "{Genre}",
                TagWriteMode.Always),

            new(
                Date,
                "Year",
                "Publication year.",
                "{Year}",
                TagWriteMode.Always),

            new(
                Description,
                "Description",
                "The blurb. This becomes the MP4 'desc' atom, which is the only place Plex reads an album summary from — the whole reason this fork exists.",
                "{Description}",
                TagWriteMode.Always,
                IsLongText: true),

            new(
                Subtitle,
                "Subtitle",
                "The book's subtitle, where it has one.",
                "{Subtitle}",
                TagWriteMode.Always),

            new(
                Series,
                "Series",
                "The series name on its own, for players that group by it.",
                "{Series}",
                TagWriteMode.Always),

            new(
                SeriesPosition,
                "Series Position",
                "The book's position in its series, exactly as the metadata source gave it — an omnibus at '1-4' and a novella at '1.5' are both real positions.",
                "{SeriesNumber}",
                TagWriteMode.Always),

            new(
                Asin,
                "ASIN",
                "The Audible identifier. A later rescan uses it to find the book again without matching on title.",
                "{Asin}",
                TagWriteMode.Always),

            new(
                Publisher,
                "Publisher",
                "The publisher.",
                "{Publisher}",
                TagWriteMode.Always),

            new(
                AudioFileUrl,
                "Audible URL",
                "A link back to the book on Audible. Written only when the book has an ASIN.",
                "https://www.audible.com/pd/{Asin}",
                TagWriteMode.WhenEmpty),

            new(
                Comment,
                "Comment",
                "Free text. Off by default: the library's files use it for a one-line series note that no single pattern reconstructs well, and overwriting that loses it.",
                "{Description}",
                TagWriteMode.Never,
                IsLongText: true),

            new(
                Copyright,
                "Copyright",
                "Off by default: Listenarr holds no copyright field, so anything written here would have to come from a pattern the operator writes themselves.",
                string.Empty,
                TagWriteMode.Never),

            new(
                Language,
                "Language",
                "Off by default: MP4 carries language on the audio stream rather than as a book-level tag, so a written value does not always read back.",
                "{Language}",
                TagWriteMode.Never)
        ];

        /// <summary>
        /// Keys ffprobe reports under a file's tags that are not tags at all.
        ///
        /// <c>major_brand</c> and its neighbours describe the container; <c>encoder</c>
        /// and <c>creation_time</c> belong to whoever wrote the file last. Carrying them
        /// forward as <c>-metadata</c> arguments would turn container facts into iTunes
        /// freeform atoms, so a rewrite drops them and lets the muxer write its own.
        /// </summary>
        public static readonly IReadOnlySet<string> ContainerTags =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "major_brand",
                "minor_version",
                "compatible_brands",
                "encoder",
                "creation_time",
                "handler_name",
                "vendor_id",
                "media_type"
            };

        private static readonly Dictionary<string, TagDefinition> ByTag =
            Definitions.ToDictionary(definition => definition.Tag, StringComparer.OrdinalIgnoreCase);

        public static bool IsKnown(string? tag) =>
            !string.IsNullOrWhiteSpace(tag) && ByTag.ContainsKey(tag);

        public static TagDefinition? Find(string? tag) =>
            !string.IsNullOrWhiteSpace(tag) && ByTag.TryGetValue(tag, out var definition)
                ? definition
                : null;

        /// <summary>The shipped mapping: every tag at its documented default.</summary>
        public static List<TagMapping> CreateDefaultMappings() =>
            [.. Definitions.Select(definition =>
                new TagMapping(definition.Tag, definition.DefaultPattern, definition.DefaultMode))];

        /// <summary>
        /// Reconcile a stored mapping with the catalog: unknown tags are dropped, tags
        /// added to the catalog since the settings were saved appear at their default,
        /// and the order is the catalog's rather than the database's.
        ///
        /// A settings row written before this feature existed holds nothing, and that has
        /// to come back as the defaults rather than as "write no tags at all".
        /// </summary>
        public static List<TagMapping> Reconcile(IEnumerable<TagMapping>? stored)
        {
            var storedByTag = new Dictionary<string, TagMapping>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in stored ?? [])
            {
                if (mapping == null || !IsKnown(mapping.Tag))
                {
                    continue;
                }

                storedByTag[mapping.Tag] = mapping;
            }

            return [.. Definitions.Select(definition =>
                storedByTag.TryGetValue(definition.Tag, out var mapping)
                    ? new TagMapping(definition.Tag, mapping.Pattern ?? string.Empty, mapping.Mode)
                    : new TagMapping(definition.Tag, definition.DefaultPattern, definition.DefaultMode))];
        }
    }
}
