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
using Listenarr.Application.Search.AbookLink;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Search.AbookLink
{
    /// <summary>
    /// Pins the board index against the markup the forum serves, including the two
    /// rows it writes for a board that has children.
    /// </summary>
    [Trait("Name", "AbookBoardIndexParserTests")]
    [Trait("Category", "Application")]
    public sealed class AbookBoardIndexParserTests : BaseTests
    {
        /// <summary>
        /// Trimmed from the live index. Non-Fiction is written as two rows under one
        /// id — the board, then its children — which is the shape that produced a
        /// nameless duplicate board the first time around.
        /// </summary>
        private const string BoardIndex = """
            <div id="boardindex_table"><table class="table_list"><tbody class="content" id="category_3_boards">
              <tr class="windowbg2" id="board_7">
                <td class="icon windowbg"><a href="https://abook.link/book/index.php?board=7.0"><img/></a></td>
                <td class="info">
                  <a class="subject" name="b7" href="https://abook.link/book/index.php?board=7.0">Science Fiction</a>
                  <p class="moderators">Moderator: <a href="#">Doghouse Riley</a></p>
                </td>
                <td class="stats windowbg"><p>13,233 Posts <br /> 12,326 Topics </p></td>
              </tr>
              <tr class="windowbg2" id="board_14">
                <td class="icon windowbg"><a href="https://abook.link/book/index.php?board=14.0"><img/></a></td>
                <td class="info">
                  <a class="subject" name="b14" href="https://abook.link/book/index.php?board=14.0">Non-Fiction</a>
                </td>
                <td class="stats windowbg"><p>11,881 Posts <br /> 11,356 Topics </p></td>
              </tr>
              <tr class="windowbg2" id="board_14">
                <td colspan="4" class="children">
                  Child Boards: <a href="https://abook.link/book/index.php?board=55.0">Religion</a>,
                  <a href="https://abook.link/book/index.php?board=56.0">Biography / Memoir</a>
                </td>
              </tr>
              <tr class="windowbg2" id="board_64">
                <td class="info">
                  <a class="subject" href="https://abook.link/book/index.php?board=64.0">Pseudoscience/Metaphysical/Conspiracy/Unexplained</a>
                </td>
                <td class="windowbg stats">54</td>
                <td class="windowbg stats">46</td>
              </tr>
            </tbody></table></div>
            """;

        [Fact]
        public void Parse_ReadsEveryBoardWithItsName()
        {
            var boards = AbookBoardIndexParser.Parse(BoardIndex);

            Assert.Equal(["7", "14", "64"], boards.Select(board => board.Id));
            Assert.Equal("Science Fiction", boards[0].Name);
            Assert.Equal("Pseudoscience/Metaphysical/Conspiracy/Unexplained", boards[2].Name);
        }

        [Fact]
        public void Parse_DoesNotRepeatABoardThatListsChildren()
        {
            var boards = AbookBoardIndexParser.Parse(BoardIndex);

            Assert.Single(boards, board => board.Id == "14");
        }

        [Fact]
        public void Parse_ReadsTheTopicCountRatherThanThePostCount()
        {
            var boards = AbookBoardIndexParser.Parse(BoardIndex);

            // Labelled, both numbers in one cell, posts first.
            Assert.Equal(12326, boards[0].Topics);
        }

        [Fact]
        public void Parse_ReadsTheTopicCountFromTheSecondStatsColumn()
        {
            var boards = AbookBoardIndexParser.Parse(BoardIndex);

            // This forum's theme labels the columns in its header and leaves the cells
            // bare: posts, then topics.
            Assert.Equal(46, boards[2].Topics);
        }

        [Fact]
        public void Parse_WithOneUnlabelledNumber_SaysNothingAboutTopics()
        {
            const string html =
                "<div id=\"boardindex_table\"><tr id=\"board_3\">"
                + "<td class=\"info\"><a class=\"subject\" href=\"?board=3.0\">Odd theme</a></td>"
                + "<td class=\"windowbg stats\">1,234</td>"
                + "</tr></div>";

            // A lone number is the post count, and reading it as the topic count would
            // put a wrong figure beside the board in the picker.
            Assert.Null(Assert.Single(AbookBoardIndexParser.Parse(html)).Topics);
        }

        [Fact]
        public void Parse_OfSomethingThatIsNotTheIndex_FindsNothing()
        {
            Assert.Empty(AbookBoardIndexParser.Parse("<html><body>Login required</body></html>"));
            Assert.Empty(AbookBoardIndexParser.Parse(null));
        }
    }

    /// <summary>
    /// Pins a board's topic listing: what was posted, when, and which rows are not a
    /// release at all.
    /// </summary>
    [Trait("Name", "AbookTopicListParserTests")]
    [Trait("Category", "Application")]
    public sealed class AbookTopicListParserTests : BaseTests
    {
        private static readonly DateTime Now = new(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc);

        /// <summary>Trimmed from the live board page: three rows as the forum writes them.</summary>
        private const string BoardPage = """
            <table class="table_grid"><tbody>
              <tr>
                <td class="icon1 windowbg"><img src="normal_post.gif" alt="" /></td>
                <td class="subject windowbg2">
                  <div>
                    <span id="msg_145619"><a href="https://abook.link/book/index.php?topic=123463.0">Jack Campbell - Lost Fleet 04 - Valiant (2022)</a></span>
                    <a href="#" id="newicon145619"><img src="new.gif" alt="New" /></a>
                    <p>Started by <a href="#" title="View the profile of Dutchman">Dutchman</a></p>
                  </div>
                </td>
                <td class="stats windowbg">0 Replies <br /> 17 Views</td>
                <td class="lastpost windowbg2"><a href="#"><img src="last_post.gif" /></a> Today at 12:16:52 PM <br /> by <a href="#">Dutchman</a></td>
              </tr>
              <tr>
                <td class="icon1 windowbg"><img src="normal_post.gif" alt="" /></td>
                <td class="subject windowbg">
                  <div>
                    <span id="msg_145601"><a href="https://abook.link/book/index.php?topic=123457.0">[REQUEST] Anything by Adrian Tchaikovsky</a></span>
                    <p>Started by <a href="#">Someone</a></p>
                  </div>
                </td>
                <td class="stats windowbg2">3 Replies <br /> 90 Views</td>
                <td class="lastpost windowbg"><a href="#"><img src="last_post.gif" /></a> October 22, 2026, 10:52:16 PM <br /> by <a href="#">Someone</a></td>
              </tr>
              <tr>
                <td class="subject windowbg2">
                  <div>
                    <span id="msg_145588"><a href="https://abook.link/book/index.php?topic=123455.0">William R. Forstchen - The Lost Regiment 08 - Men of War (2010)</a></span>
                    <p>Started by <a href="#">Dutchman</a></p>
                  </div>
                </td>
                <td class="stats windowbg">0 Replies <br /> 18 Views</td>
                <td class="lastpost windowbg2">October 22, 2026, 10:45:38 PM <br /> by <a href="#">Dutchman</a></td>
              </tr>
            </tbody></table>
            """;

        [Fact]
        public void ParseBoard_ReadsEveryTopicWithItsSubject()
        {
            var topics = AbookTopicListParser.ParseBoard(BoardPage, "7", Now);

            Assert.Equal([123463, 123457, 123455], topics.Select(topic => topic.TopicId));
            Assert.Equal("Jack Campbell - Lost Fleet 04 - Valiant (2022)", topics[0].Subject);
            Assert.All(topics, topic => Assert.Equal("7", topic.BoardId));
        }

        [Fact]
        public void ParseBoard_KeepsEachRowsOwnTimeAndReplyCount()
        {
            var topics = AbookTopicListParser.ParseBoard(BoardPage, "7", Now);

            // The cells carry no id tying them to their subject, so a row read as a
            // whole is the only thing keeping these together.
            Assert.Equal(new DateTime(2026, 10, 23, 12, 16, 52, DateTimeKind.Unspecified), topics[0].PostedAt);
            Assert.Equal(0, topics[0].Replies);
            Assert.Equal(new DateTime(2026, 10, 22, 22, 52, 16, DateTimeKind.Unspecified), topics[1].PostedAt);
            Assert.Equal(3, topics[1].Replies);
        }

        [Fact]
        public void ParseBoard_LeavesTheNonReleasePostsForTheCallerToRecognise()
        {
            var topics = AbookTopicListParser.ParseBoard(BoardPage, "7", Now);

            // The parser reads what is there; a [REQUEST] is dropped later, by the
            // title parser that knows what the prefixes mean.
            Assert.Contains(topics, topic => topic.Subject.StartsWith("[REQUEST]", StringComparison.Ordinal));
        }

        [Fact]
        public void ParseBoard_OfALoggedOutPage_FindsNothing()
        {
            Assert.Empty(AbookTopicListParser.ParseBoard("<html><body>Login</body></html>", "7", Now));
            Assert.Empty(AbookTopicListParser.ParseBoard(null, "7", Now));
        }
    }
}
