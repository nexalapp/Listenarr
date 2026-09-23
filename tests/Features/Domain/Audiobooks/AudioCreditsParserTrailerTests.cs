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
using Listenarr.Domain.Audiobooks.Audit;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks
{
    [Trait("Name", "AudioCreditsParserTrailerTests")]
    [Trait("Category", "Domain")]
    public sealed class AudioCreditsParserTrailerTests : BaseTests
    {
        /// <summary>
        /// The tape that started this: the opening credits Victor Garber, the closing
        /// advertises five other Crichton titles with five other readers, and the parser
        /// used to hand back the first of those as this book's narrator.
        /// </summary>
        [Fact]
        public void Parse_TakesTheNarratorFromTheOpening_NotTheClosingAdvertisement()
        {
            var heard =
                "Random House Audiobooks Presents \"Eaters of the Dead\" by Michael Crichton. "
                + "Red for you by Victor Garber, with commentary provided by Michael Crichton."
                + AudioAuditTranscript.ClosingMarker
                + "Among the many other titles by Michael Crichton, available on audio from Random House, "
                + "are Airframe, read by Blair Brown, The Lost World, read by Anthony Heald, "
                + "Jurassic Park, read by John Hurd, Sphere, read by Edward Asner, and Disclosure, "
                + "read by John Lithgow. This has been a Random House audio books presentation.";

            var credits = AudioCreditsParser.Parse(heard);

            Assert.Equal("Victor Garber", credits.Narrator);
            Assert.Equal("Michael Crichton", credits.Author);
        }

        /// <summary>A closing with no advertisement still credits the book it ends.</summary>
        [Fact]
        public void Parse_StillReadsAClosingWhenTheOpeningSaidNothing()
        {
            var heard =
                "Chapter forty. The last of the fires burned down to nothing."
                + AudioAuditTranscript.ClosingMarker
                + "This has been Drive by Daniel Pink, read by Roger Wayne.";

            var credits = AudioCreditsParser.Parse(heard);

            Assert.Equal("Roger Wayne", credits.Narrator);
        }

        [Theory]
        [InlineData("also available, read by Blair Brown")]
        [InlineData("Other titles by this author, read by Blair Brown")]
        [InlineData("available on audio from Random House, read by Blair Brown")]
        [InlineData("Look for The Lost World, read by Blair Brown")]
        public void Parse_NeverTakesANarratorFromAnAdvertisement(string trailer)
        {
            var credits = AudioCreditsParser.Parse(
                "Sphere by Michael Crichton, read by Edward Asner. " + trailer);

            Assert.Equal("Edward Asner", credits.Narrator);
        }

        /// <summary>
        /// "Read for you by" is the phrase whisper mangles; a bare "Red" is left alone,
        /// because "Red" opens far more titles than it does credits.
        /// </summary>
        [Theory]
        [InlineData("Eaters of the Dead by Michael Crichton. Red for you by Victor Garber.", "Victor Garber")]
        [InlineData("Eaters of the Dead by Michael Crichton. Reed for you by Victor Garber.", "Victor Garber")]
        [InlineData("Eaters of the Dead by Michael Crichton. Read for you by Victor Garber.", "Victor Garber")]
        public void Parse_HearsReadForYouByThroughWhispersMishearing(string heard, string expected)
        {
            Assert.Equal(expected, AudioCreditsParser.Parse(heard).Narrator);
        }

        [Fact]
        public void Parse_DoesNotReadABareRedAsACredit()
        {
            var credits = AudioCreditsParser.Parse("Red Rising by Pierce Brown, narrated by Tim Gerard Reynolds.");

            Assert.Equal("Tim Gerard Reynolds", credits.Narrator);
            Assert.Equal("Pierce Brown", credits.Author);
        }
    }
}
