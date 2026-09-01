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

namespace Listenarr.Domain.Audiobooks.Metadata
{
    /// <summary>One field an operator can pin against a metadata rescan.</summary>
    public sealed record LockableField(string Field, string Label);

    /// <summary>
    /// The fields a metadata rescan is allowed to overwrite, and which an operator can
    /// therefore pin.
    ///
    /// <para>
    /// Deliberately exactly that set. A padlock on a field nothing overwrites would be a
    /// lie, so <c>Edition</c> — which the edit form has but no rescan touches — is absent,
    /// and so are the identifiers: a rescan is <em>keyed</em> on the ASIN and writes the
    /// same one back, which makes locking it protection against nothing.
    /// </para>
    /// <para>
    /// A lock is absolute. There is no rescan that ignores locks, because a "just this
    /// once" override is how a protection stops being one — the operator reaches for it
    /// whenever the rescan looks disappointing, and the locks then mean nothing.
    /// Unlocking the field is the way past it, and it leaves a visible trace.
    /// </para>
    /// </summary>
    public static class LockableFields
    {
        public const string Title = "title";
        public const string Subtitle = "subtitle";
        public const string Description = "description";
        public const string Authors = "authors";
        public const string Narrators = "narrators";
        public const string Series = "series";
        public const string Publisher = "publisher";
        public const string PublishYear = "publishYear";
        public const string PublishedDate = "publishedDate";
        public const string Language = "language";
        public const string Runtime = "runtime";
        public const string Genres = "genres";
        public const string Cover = "cover";

        public static IReadOnlyList<LockableField> Definitions { get; } =
        [
            new(Title, "Title"),
            new(Subtitle, "Subtitle"),
            new(Description, "Description"),
            new(Authors, "Authors"),
            new(Narrators, "Narrators"),

            // One lock for the whole membership list rather than one per series: a book's
            // series and its position are edited together and are overwritten together.
            new(Series, "Series"),

            new(Publisher, "Publisher"),
            new(PublishYear, "Publish Year"),
            new(PublishedDate, "Release Date"),
            new(Language, "Language"),
            new(Runtime, "Runtime"),
            new(Genres, "Genres"),
            new(Cover, "Cover Image")
        ];

        private static readonly Dictionary<string, LockableField> ByField =
            Definitions.ToDictionary(definition => definition.Field, StringComparer.OrdinalIgnoreCase);

        public static bool IsKnown(string? field) =>
            !string.IsNullOrWhiteSpace(field) && ByField.ContainsKey(field);

        public static string? LabelFor(string? field) =>
            !string.IsNullOrWhiteSpace(field) && ByField.TryGetValue(field, out var definition)
                ? definition.Label
                : null;

        /// <summary>
        /// A stored or submitted lock list, reduced to known fields in the catalog's order
        /// with duplicates and casing collapsed.
        /// </summary>
        /// <remarks>
        /// Normalising on the way in is what keeps the column comparable: two lists holding
        /// the same locks in a different order are the same lock set, and a rescan that
        /// re-serialised them differently would look like a change to EF.
        /// </remarks>
        public static List<string> Normalize(IEnumerable<string?>? fields)
        {
            if (fields == null)
            {
                return [];
            }

            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
            {
                if (IsKnown(field))
                {
                    wanted.Add(field!.Trim());
                }
            }

            return [.. Definitions
                .Where(definition => wanted.Contains(definition.Field))
                .Select(definition => definition.Field)];
        }

        /// <summary>The locks as a set, for a caller asking "may I write this field?".</summary>
        public static IReadOnlySet<string> AsSet(IEnumerable<string?>? fields) =>
            new HashSet<string>(Normalize(fields), StringComparer.OrdinalIgnoreCase);
    }
}
