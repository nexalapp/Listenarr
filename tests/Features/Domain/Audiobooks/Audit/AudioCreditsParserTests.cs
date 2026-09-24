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

namespace Listenarr.Tests.Features.Domain.Audiobooks.Audit
{
    [Trait("Name", "AudioCreditsParserTests")]
    [Trait("Category", "Domain")]
    public sealed class AudioCreditsParserTests : BaseTests
    {
        [Theory]
        [InlineData("A War of Gifts, by Orson Scott Card. Read by Scott Brick.", "A War of Gifts", "Orson Scott Card", "Scott Brick")]
        [InlineData("Macmillan Audio presents A War of Gifts by Orson Scott Card, read by Scott Brick and Stefan Rudnicki.", "A War of Gifts", "Orson Scott Card", "Scott Brick and Stefan Rudnicki")]
        [InlineData("This is Audible.\nThe Forever War, by Joe Haldeman.\nNarrated by George Wilson.", "The Forever War", "Joe Haldeman", "George Wilson")]
        [InlineData("Drive, an Expanse short story by James S. A. Corey, performed by Jefferson Mays", "Drive, an Expanse short story", "James S. A. Corey", "Jefferson Mays")]
        [InlineData("[Music]\nAudio Renaissance presents A War of Gifts by Orson Scott Card.\nRead for you by Scott Brick and Stefan Ruttnicki.\n[Music]\n1. St. Nick.", "A War of Gifts", "Orson Scott Card", "Scott Brick and Stefan Ruttnicki")]
        [InlineData("This has been a Hachette Audio production of Drive.", "Drive", null, null)]
        [InlineData("You have been listening to The Forever War by Joe Haldeman, read by George Wilson.", "The Forever War", "Joe Haldeman", "George Wilson")]
        [InlineData("This has been A War of Gifts by Orson Scott Card. Read by Scott Brick.", "A War of Gifts", "Orson Scott Card", "Scott Brick")]
        [InlineData("then the\n\n[closing]\n[MUSIC] This has been a Hashet audio production of Drive, an expanse story, written by James S.A. Corey, read by Jefferson Mayes.", "Drive, an expanse story", "James S.A. Corey", "Jefferson Mayes")]
        [InlineData("It's alarms trigger in a strictly informational way by the way. [MUSIC] This has been a Hashet audio production of Drive, an expanse story, written by James S.A. Corey, read by Jefferson Mayes. Executive producer Michelle McGonigal.", "Drive, an expanse story", "James S.A. Corey", "Jefferson Mayes")]
        [InlineData("Read by Simon Vance.", null, null, "Simon Vance")]
        [InlineData("Dilation Sleep, written by Alastair Reynolds.", "Dilation Sleep", "Alastair Reynolds", null)]
        public void Parse_ReadsTheSpokenFormula(string heard, string? title, string? author, string? narrator)
        {
            var credits = AudioCreditsParser.Parse(heard);

            Assert.Equal(title, credits.Title);
            Assert.Equal(author, credits.Author);
            Assert.Equal(narrator, credits.Narrator);
        }

        [Theory]
        [InlineData("")]
        [InlineData("[Music]")]
        [InlineData("Zach Morgan sat attentively on the front row of the little sanctuary.")]
        public void Parse_FindsNothingInProse(string heard) =>
            Assert.True(AudioCreditsParser.Parse(heard).IsEmpty);

        [Fact]
        public void Parse_DoesNotTakeAWholeSentenceAsATitle()
        {
            // "by" inside prose must not drag a sentence in as the title.
            var credits = AudioCreditsParser.Parse(
                "He walked slowly down the long road past the church and the mill and the old house by the river where nobody lived by Tom.");

            Assert.Null(credits.Title);
        }
        [Fact]
        public void Parse_IgnoresASentenceTheDecoderGotStuckOn()
        {
            // Inhibitor Phase, verbatim: an Audible ident over music, and whisper looping
            // on an invented sentence. The book it names does not exist in this library.
            var credits = AudioCreditsParser.Parse(
                "This is Audible.\nThis is a book called The New World.\nThis is a book called The New World.\n"
                + "This is a book called The New World.\nThis is a book called The New World.");

            Assert.Null(credits.Title);
        }

        [Fact]
        public void Parse_KeepsAPhraseThatMerelyRecurs()
        {
            // Twice running is a refrain, not a stuck decoder.
            var credits = AudioCreditsParser.Parse(
                "Tantor Media presents.\nTantor Media presents.\nThe Scarlet Pimpernel by Baroness Orczy, read by Wanda McCaddon.");

            Assert.Equal("Wanda McCaddon", credits.Narrator);
        }
    }
}
