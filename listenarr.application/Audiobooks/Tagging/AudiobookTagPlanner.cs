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

namespace Listenarr.Application.Audiobooks.Tagging
{
    /// <summary>
    /// Turns a book, a tag mapping and a file's current tags into the exact set of tags
    /// that file should end up with.
    ///
    /// <para>
    /// This is the single place a tag value is decided. Conversion writes tags too, and
    /// two independent renderings would mean converting a book and enriching it produced
    /// different files — so conversion resolves its tags here as well, against an empty
    /// set of existing ones. There is one answer to "what does this book's album tag
    /// say", and this is it.
    /// </para>
    /// <para>
    /// Deliberately free of IO: it is handed what the file currently has rather than
    /// reading it, which is what lets the preview and the write share one implementation
    /// and lets both be tested without an encoder.
    /// </para>
    /// </summary>
    public sealed class AudiobookTagPlanner(IFileNamingService namingService)
    {
        /// <summary>
        /// Resolve the tags for one file.
        /// </summary>
        /// <param name="metadata">The book as Listenarr knows it — the corrected record, not the file's own tags.</param>
        /// <param name="mappings">What goes in each tag and whether it may be overwritten.</param>
        /// <param name="existingTags">The tags the file carries now, keyed case-insensitively.</param>
        /// <param name="selectedTags">
        /// The tags the operator chose for this run, or null for every tag the mapping
        /// allows. A choice made in a preview applies to that run only; it is not a
        /// silent edit of the settings that would change every later book.
        /// </param>
        public TagPlan Plan(
            AudioMetadata metadata,
            IReadOnlyList<TagMapping> mappings,
            IReadOnlyDictionary<string, string>? existingTags,
            IReadOnlySet<string>? selectedTags = null)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(mappings);

            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in existingTags ?? new Dictionary<string, string>())
            {
                if (string.IsNullOrWhiteSpace(key) || TagCatalog.ContainerTags.Contains(key))
                {
                    continue;
                }

                // First value wins. A file carrying the same key twice — which several in
                // this library do — has one of them read back, and rebuilding from this
                // dictionary is what collapses the pair on the next write.
                current.TryAdd(key.Trim(), value ?? string.Empty);
            }

            // Start from what the file already has so tags nothing here manages — a
            // release's own totaltracks, a tagger's custom key — survive the rewrite.
            var finalTags = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase);
            var changes = new List<TagChange>(mappings.Count);

            foreach (var mapping in TagCatalog.Reconcile(mappings))
            {
                var definition = TagCatalog.Find(mapping.Tag);
                if (definition == null)
                {
                    continue;
                }

                current.TryGetValue(mapping.Tag, out var existing);
                var change = Resolve(metadata, mapping, definition, existing, selectedTags);
                changes.Add(change);

                if (!change.IsWrite)
                {
                    continue;
                }

                // The canonical casing replaces whatever casing the file used, so a file
                // holding both "SERIES" and "series" ends up with exactly one.
                finalTags.Remove(mapping.Tag);
                finalTags[definition.Tag] = change.Proposed ?? string.Empty;
            }

            return new TagPlan(changes, finalTags);
        }

        private TagChange Resolve(
            AudioMetadata metadata,
            TagMapping mapping,
            TagDefinition definition,
            string? existing,
            IReadOnlySet<string>? selectedTags)
        {
            TagChange Skip(TagChangeAction action, string reason, string? proposed = null) =>
                new(definition.Tag, definition.Label, existing, proposed, action, reason);

            if (mapping.Mode == TagWriteMode.Never)
            {
                return Skip(
                    TagChangeAction.NotConfigured,
                    "Left as it is: this tag is set never to be written.");
            }

            if (selectedTags != null && !selectedTags.Contains(definition.Tag))
            {
                return Skip(
                    TagChangeAction.Deselected,
                    "Left as it is: not selected for this run.");
            }

            string proposed;
            try
            {
                proposed = namingService.RenderTagValue(mapping.Pattern ?? string.Empty, metadata);
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException
                && ex is not OutOfMemoryException
                && ex is not StackOverflowException)
            {
                // A pattern an operator typed is untrusted input. One that cannot render
                // must not take the whole book's tagging down with it.
                return Skip(
                    TagChangeAction.NoValue,
                    $"Skipped: this tag's pattern could not be rendered ({ex.Message}).");
            }

            if (string.IsNullOrWhiteSpace(proposed))
            {
                return Skip(
                    TagChangeAction.NoValue,
                    "Nothing to write: this book has no value for this tag.");
            }

            if (mapping.Mode == TagWriteMode.WhenEmpty && !string.IsNullOrWhiteSpace(existing))
            {
                return Skip(
                    TagChangeAction.Preserved,
                    "Kept: the file already has a value, and this tag is only written when empty.",
                    proposed);
            }

            if (TagValue.AreEquivalent(existing, proposed))
            {
                return new TagChange(
                    definition.Tag,
                    definition.Label,
                    existing,
                    proposed,
                    TagChangeAction.Unchanged,
                    "Already correct.");
            }

            return new TagChange(
                definition.Tag,
                definition.Label,
                existing,
                proposed,
                TagChangeAction.Write,
                string.IsNullOrWhiteSpace(existing) ? "Will be added." : "Will be replaced.");
        }

    }
}
