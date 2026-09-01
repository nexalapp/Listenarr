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
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Features.Library
{
    /// <summary>
    /// Which fields an edit pins against a later metadata rescan.
    ///
    /// <para>
    /// Two rules, split on whether the caller sent a lock set. A caller that did is taken
    /// at its word — the edit form shows the padlocks it is submitting, and a lock it did
    /// not show would make the screen a lie. A caller that did not gets the inference:
    /// every field it changes is pinned.
    /// </para>
    /// <para>
    /// The inference is the one that is easy to break by accident. It has to fire on what
    /// an edit <em>changes</em>, not on what it mentions — a save that re-sends the whole
    /// record unchanged must lock nothing, or opening the form and pressing Save would
    /// freeze the book against every future rescan.
    /// </para>
    /// </summary>
    [Trait("Name", "AudiobookFieldLockTests")]
    [Trait("Category", "Library")]
    public sealed class AudiobookFieldLockTests : BaseTests
    {
        private static Audiobook Existing() => new()
        {
            Id = 7,
            Title = "Drive",
            Subtitle = "An Expanse Short Story",
            Description = "A novella set in the universe of The Expanse.",
            Authors = ["James S. A. Corey"],
            Narrators = ["Jefferson Mays"],
            Genres = ["Science Fiction"],
            Publisher = "Recorded Books",
            Language = "English",
            PublishYear = "2022",
            PublishedDate = "2022-05-03",
            Runtime = 45,
            ImageUrl = "/images/drive.jpg",
            Series = "The Expanse",
            SeriesNumber = "2.7"
        };

        private static List<string> Resolve(
            AudiobookUpdateRequest request,
            Audiobook? existing = null,
            bool suppressStaleImageUrl = false) =>
            LibraryUpdateWorkflow.ResolveLockedFields(
                existing ?? Existing(),
                request,
                suppressStaleImageUrl);

        [Fact]
        public void AnEditLocksTheFieldItChanges()
        {
            var locked = Resolve(new AudiobookUpdateRequest { Title = "Drive: An Expanse Novella" });

            Assert.Equal([LockableFields.Title], locked);
        }

        [Fact]
        public void ResendingAnUnchangedValueLocksNothing()
        {
            // The edit form sends only what changed, but the endpoint is public. A client
            // that PUTs the whole record must not freeze the book by saying nothing new.
            var locked = Resolve(new AudiobookUpdateRequest
            {
                Title = "Drive",
                Description = "A novella set in the universe of The Expanse.",
                Authors = ["James S. A. Corey"],
                Publisher = "Recorded Books"
            });

            Assert.Empty(locked);
        }

        [Fact]
        public void WhitespaceIsNotAChange()
        {
            var locked = Resolve(new AudiobookUpdateRequest { Title = "  Drive  " });

            Assert.Empty(locked);
        }

        [Fact]
        public void AnEditKeepsLocksTheBookAlreadyHad()
        {
            var existing = Existing();
            existing.LockedFields = [LockableFields.Description];

            var locked = Resolve(new AudiobookUpdateRequest { Title = "Drive Redux" }, existing);

            Assert.Equal([LockableFields.Title, LockableFields.Description], locked);
        }

        [Fact]
        public void ThePadlocksReplaceTheStoredSetWhenTheyAreSent()
        {
            var existing = Existing();
            existing.LockedFields = [LockableFields.Title, LockableFields.Description];

            var locked = Resolve(
                new AudiobookUpdateRequest { LockedFields = [LockableFields.Description] },
                existing);

            Assert.Equal([LockableFields.Description], locked);
        }

        [Fact]
        public void SentPadlocksAreTheWholeAnswer_NothingIsLockedOnTopOfThem()
        {
            // The form lights a padlock the moment a value changes, so what it submits is
            // what the operator was looking at. Adding to it here would put back a lock
            // they had just turned off — which is to say, unlocking a field you are also
            // correcting would be impossible, and that is exactly when it is wanted.
            var existing = Existing();
            existing.LockedFields = [LockableFields.Title];

            var locked = Resolve(
                new AudiobookUpdateRequest { Title = "Something Else", LockedFields = [] },
                existing);

            Assert.Empty(locked);
        }

        [Fact]
        public void AnEditWithNoPadlocksAtAllStillPinsWhatItChanged()
        {
            // The safety net for a caller that knows nothing about locks.
            var locked = Resolve(new AudiobookUpdateRequest { Description = "Rewritten." });

            Assert.Equal([LockableFields.Description], locked);
        }

        [Fact]
        public void LockingAFieldWithoutEditingItIsEnough()
        {
            var locked = Resolve(new AudiobookUpdateRequest
            {
                LockedFields = [LockableFields.Cover]
            });

            Assert.Equal([LockableFields.Cover], locked);
        }

        [Fact]
        public void AChangedListLocksItsField()
        {
            var locked = Resolve(new AudiobookUpdateRequest
            {
                Narrators = ["Jefferson Mays", "Sophie Aldred"]
            });

            Assert.Equal([LockableFields.Narrators], locked);
        }

        [Fact]
        public void ReorderingTheSameSeriesCountsAsAChange()
        {
            var existing = Existing();
            existing.SeriesMemberships =
            [
                new() { SeriesName = "The Expanse", SeriesNumber = "2.7", IsPrimary = true, SortOrder = 0 },
                new() { SeriesName = "Expanse Shorts", SeriesNumber = "3", SortOrder = 1 }
            ];

            var locked = Resolve(
                new AudiobookUpdateRequest
                {
                    SeriesMemberships =
                    [
                        new() { SeriesName = "Expanse Shorts", SeriesNumber = "3", IsPrimary = true, SortOrder = 0 },
                        new() { SeriesName = "The Expanse", SeriesNumber = "2.7", SortOrder = 1 }
                    ]
                },
                existing);

            Assert.Equal([LockableFields.Series], locked);
        }

        [Fact]
        public void ResendingTheSameSeriesDoesNot()
        {
            var existing = Existing();
            existing.SeriesMemberships =
            [
                new() { SeriesName = "The Expanse", SeriesNumber = "2.7", IsPrimary = true, SortOrder = 0 }
            ];

            var locked = Resolve(
                new AudiobookUpdateRequest
                {
                    SeriesMemberships =
                    [
                        new() { SeriesName = "The Expanse", SeriesNumber = "2.7", IsPrimary = true, SortOrder = 0 }
                    ]
                },
                existing);

            Assert.Empty(locked);
        }

        [Fact]
        public void ACoverUrlThatIsBeingDiscardedAsStaleDoesNotLockTheCover()
        {
            // Locking on the strength of a value that is not going to be written would pin
            // the book to the cover it already has, for a reason nobody chose.
            var locked = Resolve(
                new AudiobookUpdateRequest { ImageUrl = "https://example.invalid/stale.jpg" },
                suppressStaleImageUrl: true);

            Assert.Empty(locked);
        }

        [Fact]
        public void AnUnknownFieldNameIsDropped()
        {
            var locked = Resolve(new AudiobookUpdateRequest
            {
                LockedFields = [LockableFields.Title, "edition", "not_a_field"]
            });

            Assert.Equal([LockableFields.Title], locked);
        }

        [Fact]
        public void LocksAreStoredInTheCatalogsOrderRegardlessOfHowTheyArrive()
        {
            // Two lists holding the same locks are the same lock set. Normalising the order
            // is what stops a save re-serialising the column and looking like a change.
            var locked = Resolve(new AudiobookUpdateRequest
            {
                LockedFields = [LockableFields.Genres, "TITLE", LockableFields.Title]
            });

            Assert.Equal([LockableFields.Title, LockableFields.Genres], locked);
        }

        /// <summary>
        /// The catalog is duplicated in the edit form, which places a padlock beside each
        /// named field. Adding one here without adding it there ships a lock nothing can
        /// toggle, so this test is the reminder.
        /// </summary>
        [Fact]
        public void TheCatalogIsTheThirteenFieldsARescanOverwrites()
        {
            Assert.Equal(
                [
                    "title",
                    "subtitle",
                    "description",
                    "authors",
                    "narrators",
                    "series",
                    "publisher",
                    "publishYear",
                    "publishedDate",
                    "language",
                    "runtime",
                    "genres",
                    "cover"
                ],
                LockableFields.Definitions.Select(definition => definition.Field));
        }
    }
}
