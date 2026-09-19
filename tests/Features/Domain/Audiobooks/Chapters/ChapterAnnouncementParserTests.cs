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
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Audiobooks.Chapters
{
    [Trait("Name", "ChapterAnnouncementParserTests")]
    [Trait("Category", "Domain")]
    public sealed class ChapterAnnouncementParserTests : BaseTests
    {
        [Theory]
        [InlineData("1. Saint Nick\nZach Morgan sat attentively on the front row.", "Chapter", 1, "Chapter 1: Saint Nick")]
        [InlineData("2. Ender's stocking\nPeter Wiggin was supposed to spend the day at the library.", "Chapter", 2, "Chapter 2: Ender's stocking")]
        [InlineData("One.\nSaint Nick\nZach Morgan sat attentively.", "Chapter", 1, "Chapter 1: Saint Nick")]
        [InlineData("Twenty-three.\nThe Drop", "Chapter", 23, "Chapter 23: The Drop")]
        [InlineData("Chapter 4: The Long Night\nThe morning came grey and cold.", "Chapter", 4, "Chapter 4: The Long Night")]
        [InlineData("7. Stockings.\nRat Army was only a small percentage of the population.", "Chapter", 7, "Chapter 7: Stockings")]
        [InlineData("Chapter four.\nThe morning came grey and cold and nobody spoke.", "Chapter", 4, "Chapter 4")]
        [InlineData("Chapter four. The morning came grey and cold.", "Chapter", 4, "Chapter 4")]
        [InlineData("chapter 4 the morning came", "Chapter", 4, "Chapter 4")]
        [InlineData("Chapter twenty-three.", "Chapter", 23, "Chapter 23")]
        [InlineData("Chapter one hundred and three", "Chapter", 103, "Chapter 103")]
        [InlineData("Chapter the fourth.", "Chapter", 4, "Chapter 4")]
        [InlineData("Part two. Sergeant Mandella.", "Part", 2, "Part 2: Sergeant Mandella")]
        [InlineData("Book three, chapter one.", "Chapter", 1, "Chapter 1")]
        [InlineData("Book three.\nThe Long Night", "Book", 3, "Book 3: The Long Night")]
        [InlineData("Prologue. It was the year 1997.", "Section", null, "Prologue")]
        [InlineData("Epilogue", "Section", null, "Epilogue")]
        [InlineData("Author's note.", "Section", null, "Author's Note")]
        [InlineData("[music] Chapter seven.", "Chapter", 7, "Chapter 7")]
        public void Parse_HearsTheUsualShapes(string heard, string kind, int? number, string title)
        {
            var announcement = ChapterAnnouncementParser.Parse(heard);

            Assert.NotNull(announcement);
            Assert.Equal(kind, announcement.Kind);
            Assert.Equal(number, announcement.Number);
            Assert.Equal(title, announcement.Title);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("The morning came grey and cold and nobody spoke.")]
        [InlineData("He had read the chapter twice and understood none of it.")]   // too far in
        [InlineData("This is the end of chapter three.")]                          // the previous chapter's tail
        [InlineData("and that was the end of the part where we")]
        [InlineData("Chapter")]                                                    // no number
        [InlineData("Chapter zero")]
        [InlineData("Two men walked into the bar and sat down without a word.")]   // a sentence that starts with a number
        [InlineData("2. He had waited a long time for this and now it was here.")]  // a number, then prose
        [InlineData("1984 was the year it all changed.")]
        public void Parse_StaysQuietOtherwise(string heard) =>
            Assert.Null(ChapterAnnouncementParser.Parse(heard));

        [Theory]
        [InlineData("four", 4, 1)]
        [InlineData("twenty four", 24, 2)]
        [InlineData("forty", 40, 1)]
        [InlineData("the ninth", 9, 2)]
        [InlineData("two hundred", 200, 2)]
        [InlineData("one hundred and twelve", 112, 4)]
        [InlineData("12", 12, 1)]
        public void ReadNumber_ReadsWordsAndDigits(string words, int expected, int consumed)
        {
            var (number, span) = ChapterAnnouncementParser.ReadNumber(words.Split(' '), 0);
            Assert.Equal(expected, number);
            Assert.Equal(consumed, span);
        }
    }
}
