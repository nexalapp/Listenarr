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

namespace Listenarr.Tests.Features.Domain.Audiobooks.Tagging
{
    /// <summary>
    /// Audible returns a blurb as HTML. Nothing that reads the MP4 desc atom renders
    /// markup, so writing it verbatim puts literal angle brackets into the summary Plex
    /// shows — which is what this stops.
    /// </summary>
    [Trait("Name", "TagTextTests")]
    [Trait("Category", "Tagging")]
    public class TagTextTests : BaseTests
    {
        [Fact]
        public void StripsTheMarkupAudibleWrapsABlurbIn()
        {
            var value = TagText.FromHtml(
                "<p><b>A novella</b> set in the universe of <i>The Expanse</i>.</p>");

            Assert.Equal("A novella set in the universe of The Expanse.", value);
        }

        [Fact]
        public void KeepsParagraphBreaks()
        {
            // InnerText -- what the repository's HtmlAgilityPack extractor returns --
            // runs these together as "OneTwo". The break is the part worth keeping.
            var value = TagText.FromHtml("<p>First paragraph.</p><p>Second paragraph.</p>");

            Assert.Equal("First paragraph.\n\nSecond paragraph.", value);
        }

        [Fact]
        public void TreatsALineBreakAsALineBreak()
        {
            Assert.Equal("One\nTwo", TagText.FromHtml("One<br/>Two"));
        }

        [Fact]
        public void DecodesEntitiesAfterTheTagsAreGone()
        {
            var value = TagText.FromHtml("<p>Bell &amp; Sons&#39; &quot;finest&quot;</p>");

            Assert.Equal("Bell & Sons' \"finest\"", value);
        }

        [Fact]
        public void CollapsesTheGapsMarkupLeavesBehind()
        {
            var value = TagText.FromHtml("<div><p>One.</p></div>\n\n\n<div><p>Two.</p></div>");

            Assert.Equal("One.\n\nTwo.", value);
        }

        [Fact]
        public void LeavesPlainTextExactlyAsItIs()
        {
            // A description that merely mentions "a < b" is not markup, and a stripper
            // let loose on it would eat the rest of the sentence.
            const string plain = "A short story of the Expanse.";

            Assert.Equal(plain, TagText.FromHtml(plain));
        }

        [Fact]
        public void HandlesNothingAtAll()
        {
            Assert.Equal(string.Empty, TagText.FromHtml(null));
            Assert.Equal(string.Empty, TagText.FromHtml("   "));
            Assert.Equal(string.Empty, TagText.FromHtml("<p></p>"));
        }
    }
}
