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
    [Trait("Name", "AudioCreditsParserBoundaryTests")]
    [Trait("Category", "Domain")]
    public sealed class AudioCreditsParserBoundaryTests : BaseTests
    {
        /// <summary>
        /// Whisper breaks a line where the reader pauses, so the story begins on the line
        /// after the credits. Flattened to a space, its first capitalised word joined the
        /// narrator: this record was credited to "Garrick Hagon They".
        /// </summary>
        [Fact]
        public void Parse_DoesNotRunANameIntoTheStoryOnTheNextLine()
        {
            var credits = AudioCreditsParser.Parse(
                "Isis audio books presents an unabridged recording of\n"
                + "\"Three Thousand and One of Final Odyssey\"\n"
                + "Written by Arthur C. Clarke\n"
                + "Read by Garrick Hagon\n"
                + "They looked out across the plain, and the towers of Diaspar were burning.");

            Assert.Equal("Garrick Hagon", credits.Narrator);
        }

        [Fact]
        public void Parse_DoesNotRunAnAuthorIntoTheNextLine()
        {
            var credits = AudioCreditsParser.Parse(
                "The Hammer of God by Arthur C. Clarke\nChapter one. The asteroid was named Kali.");

            Assert.Equal("Arthur C. Clarke", credits.Author);
        }

        /// <summary>
        /// "The unabridged recording of" is an announcement, not part of the title. Only
        /// "a" and "an" were stripped, so a whole publisher's preamble became the title:
        /// "Brilliant Audio Presents The Unabridged Recording of Stirred".
        /// </summary>
        [Theory]
        [InlineData("Brilliance Audio presents the unabridged recording of Stirred by J. A. Konrath.", "Stirred")]
        [InlineData("Recorded Books presents an unabridged recording of The Hammer of God by Arthur C. Clarke.", "The Hammer of God")]
        [InlineData("Isis Audio Books presents the unabridged edition of Childhood's End by Arthur C. Clarke.", "Childhood's End")]
        [InlineData("Tantor Media presents the audiobook production of Drive by Daniel Pink.", "Drive")]
        public void Parse_StripsThePublishersAnnouncementFromTheTitle(string heard, string expected)
        {
            Assert.Equal(expected, AudioCreditsParser.Parse(heard).Title);
        }

        /// <summary>A title that merely begins with "The" keeps it.</summary>
        [Fact]
        public void Parse_KeepsATitlesOwnLeadingThe()
        {
            var credits = AudioCreditsParser.Parse("The Final Odyssey by Arthur C. Clarke, read by Garrick Hagon.");

            Assert.Equal("The Final Odyssey", credits.Title);
            Assert.Equal("Garrick Hagon", credits.Narrator);
        }

        /// <summary>Two readers are one credit; splitting them is the caller's business.</summary>
        [Fact]
        public void Parse_KeepsBothReadersOfATwoHanderTogether()
        {
            var credits = AudioCreditsParser.Parse(
                "Stirred by J. A. Konrath and Blake Crouch, performed by Angela Dawe and Phil Gigante.\nPart one.");

            Assert.Equal("Angela Dawe and Phil Gigante", credits.Narrator);
        }
    }
}
