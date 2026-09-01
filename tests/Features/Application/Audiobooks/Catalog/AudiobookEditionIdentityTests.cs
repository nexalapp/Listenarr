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

namespace Listenarr.Tests.Features.Application.Audiobooks.Catalog
{
    [Trait("Name", "AudiobookEditionIdentityTests")]
    [Trait("Category", "LibraryAdd")]
    public sealed class AudiobookEditionIdentityTests : BaseTests
    {
        private const string CollectionAsin = "B002V8MRS2";

        private static Audiobook Held(string title, params string[] narrators) => new()
        {
            Title = title,
            Asin = CollectionAsin,
            Narrators = narrators.ToList()
        };

        private static AudibleBookMetadata Incoming(string title, params string[] narrators) => new()
        {
            Title = title,
            Asin = CollectionAsin,
            Narrators = narrators.ToList()
        };

        [Fact]
        public void RepresentsSameEdition_SameAsinDifferentTitle_IsNotTheSameBook()
        {
            // Audible files every novella in a collection under one ASIN, so the first
            // one imported must not lock the rest of the collection out.
            var held = Held("Dilation Sleep", "John Lee");

            Assert.False(AudiobookEditionIdentity.RepresentsSameEdition(
                held, Incoming("Nightingale", "John Lee")));
            Assert.False(AudiobookEditionIdentity.RepresentsSameEdition(
                held, Incoming("Grafenwalder's Bestiary", "John Lee")));
        }

        [Fact]
        public void RepresentsSameEdition_SameTitleDifferentNarrator_IsNotTheSameBook()
        {
            var held = Held("Project Hail Mary", "Ray Porter");

            Assert.False(AudiobookEditionIdentity.RepresentsSameEdition(
                held, Incoming("Project Hail Mary", "Andy Weir")));
        }

        [Fact]
        public void RepresentsSameEdition_AsinTitleAndNarratorAllAgree_IsTheSameBook()
        {
            var held = Held("Century Rain", "John Lee");

            Assert.True(AudiobookEditionIdentity.RepresentsSameEdition(
                held, Incoming("Century Rain", "John Lee")));
        }

        [Fact]
        public void RepresentsSameEdition_NarratorOrderDiffers_IsStillTheSameBook()
        {
            var held = Held("A War of Gifts", "Scott Brick", "Stefan Rudnicki");

            Assert.True(AudiobookEditionIdentity.RepresentsSameEdition(
                held, Incoming("A War of Gifts", "Stefan Rudnicki", "Scott Brick")));
        }

        [Fact]
        public void RepresentsSameEdition_NoNarratorOnEitherSide_LeavesTheTitleToDecide()
        {
            // Missing data on both sides is not a difference; inventing one would let a
            // genuine re-import through whenever the source omitted the narrator.
            var held = Held("Radicalized");

            Assert.True(AudiobookEditionIdentity.RepresentsSameEdition(held, Incoming("Radicalized")));
            Assert.False(AudiobookEditionIdentity.RepresentsSameEdition(held, Incoming("Unauthorized Bread")));
        }

        [Fact]
        public void RepresentsSameEdition_TakesTheLegacySingleNarratorField()
        {
            var held = Held("Permafrost", "John Lee");
            var incoming = new AudibleBookMetadata
            {
                Title = "Permafrost",
                Asin = CollectionAsin,
                Narrator = "John Lee"
            };

            Assert.True(AudiobookEditionIdentity.RepresentsSameEdition(held, incoming));
        }

        [Fact]
        public async Task FindExistingEditionAsync_ChecksEveryBookSharingTheAsin()
        {
            // The database returns namesakes in no particular order, so the match cannot
            // depend on which one comes back first.
            var repo = new Mock<IAudiobookRepository>();
            repo.Setup(r => r.GetAllByAsinAsync(CollectionAsin))
                .ReturnsAsync(new List<Audiobook>
                {
                    Held("Dilation Sleep", "John Lee"),
                    Held("Nightingale", "John Lee"),
                    Held("Grafenwalder's Bestiary", "John Lee")
                });

            var match = await AudiobookEditionIdentity.FindExistingEditionAsync(
                repo.Object, Incoming("Nightingale", "John Lee"));

            Assert.NotNull(match);
            Assert.Equal("Nightingale", match!.Title);
        }

        [Fact]
        public async Task FindExistingEditionAsync_NoNamesakeMatches_AllowsTheAdd()
        {
            var repo = new Mock<IAudiobookRepository>();
            repo.Setup(r => r.GetAllByAsinAsync(CollectionAsin))
                .ReturnsAsync(new List<Audiobook> { Held("Dilation Sleep", "John Lee") });

            var match = await AudiobookEditionIdentity.FindExistingEditionAsync(
                repo.Object, Incoming("Nightingale", "John Lee"));

            Assert.Null(match);
        }
    }
}
