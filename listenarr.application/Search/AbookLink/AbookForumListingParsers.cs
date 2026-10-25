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
using System.Net;
using System.Text.RegularExpressions;

namespace Listenarr.Application.Search.AbookLink
{
    /// <summary>A board on the forum's front page.</summary>
    /// <param name="Id"></param>
    /// <param name="Name"></param>
    /// <param name="Topics">What the index says the board holds, when the column is shown.</param>
    public sealed record AbookBoard(string Id, string Name, int? Topics);

    /// <summary>
    /// Reads the forum's board index.
    ///
    /// <para>
    /// SMF gives every board row an id of the form <c>board_&lt;id&gt;</c> and puts the
    /// board's name in the one link in that row carrying the <c>subject</c> class. A
    /// board with children is written as two rows under the same id — the board, then a
    /// row listing its children — so the second one has no subject link and is skipped
    /// rather than read as a nameless board.
    /// </para>
    /// </summary>
    public static partial class AbookBoardIndexParser
    {
        [GeneratedRegex(@"<tr\b[^>]*\sid=""board_(\d+)""[^>]*>(.*?)</tr>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex BoardRow();

        [GeneratedRegex(@"<a\b[^>]*class=""[^""]*\bsubject\b[^""]*""[^>]*>(.*?)</a>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex SubjectLink();

        [GeneratedRegex(@"<td\b[^>]*class=""[^""]*\bstats\b[^""]*""[^>]*>(.*?)</td>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex StatsCell();

        [GeneratedRegex(@"([\d,]+)\s*(Posts?|Topics?)", RegexOptions.IgnoreCase)]
        private static partial Regex StatsCount();

        public static IReadOnlyList<AbookBoard> Parse(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return [];
            }

            var boards = new List<AbookBoard>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (Match row in BoardRow().Matches(html))
            {
                var id = row.Groups[1].Value;
                var body = row.Groups[2].Value;

                var subject = SubjectLink().Match(body);
                if (!subject.Success)
                {
                    continue;
                }

                var name = AbookMarkup.Text(subject.Groups[1].Value);
                if (name.Length == 0 || !seen.Add(id))
                {
                    continue;
                }

                boards.Add(new AbookBoard(id, name, Topics(body)));
            }

            return boards;
        }

        /// <summary>
        /// How many topics the board holds, as the index states it.
        ///
        /// <para>
        /// Two shapes. SMF's own themes label both numbers in one cell — "13,233 Posts
        /// 12,326 Topics" — while this forum's theme gives each its own stats cell with
        /// a bare number in it, posts first. A row with one unlabelled number is
        /// therefore a post count, and says nothing about topics.
        /// </para>
        /// </summary>
        private static int? Topics(string row)
        {
            var cells = StatsCell().Matches(row)
                .Select(cell => AbookMarkup.Text(cell.Groups[1].Value))
                .ToList();
            if (cells.Count == 0)
            {
                return null;
            }

            foreach (var cell in cells)
            {
                foreach (Match count in StatsCount().Matches(cell))
                {
                    if (count.Groups[2].Value.StartsWith("Topic", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(count.Groups[1].Value.Replace(",", string.Empty), out var labelled))
                    {
                        return labelled;
                    }
                }
            }

            return cells.Count >= 2 && int.TryParse(
                cells[1].Replace(",", string.Empty),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var topics)
                ? topics
                : null;
        }
    }

    /// <summary>A topic in a listing, before its post has been read.</summary>
    /// <param name="TopicId"></param>
    /// <param name="Subject"></param>
    /// <param name="BoardId">Named by the recent-topics page; null on a board's own listing.</param>
    /// <param name="BoardName"></param>
    /// <param name="Replies">
    /// How many replies it has. A release post normally has none, which is what makes
    /// the last-post time the time it was posted.
    /// </param>
    /// <param name="PostedAt"></param>
    public sealed record AbookTopicListing(
        int TopicId,
        string Subject,
        string? BoardId,
        string? BoardName,
        int? Replies,
        DateTime? PostedAt);

    /// <summary>
    /// Reads a board's topic listing — the page the "recently posted" tab is built from,
    /// twenty topics at a time, newest first.
    ///
    /// <para>
    /// The listing orders by last post, not by when the topic started, and prints only
    /// the last post's time. For a release topic, which is normally one post and no
    /// replies, those are the same moment; where there are replies the time shown is the
    /// last one, and the board's own order already follows it. It is a "what is new"
    /// list, not an archive.
    /// </para>
    /// </summary>
    public static partial class AbookTopicListParser
    {
        [GeneratedRegex(@"index\.php\?topic=(\d+)", RegexOptions.IgnoreCase)]
        private static partial Regex TopicHref();

        [GeneratedRegex(@"<span\b[^>]*\sid=""msg_(\d+)""[^>]*>(.*?)</span>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex MessageAnchor();

        [GeneratedRegex(@"<td\b[^>]*class=""[^""]*\bsubject\b[^""]*""[^>]*>(.*?)</td>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex SubjectCell();

        [GeneratedRegex(@"<td\b[^>]*class=""[^""]*\blastpost\b[^""]*""[^>]*>(.*?)</td>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex LastPostCell();

        [GeneratedRegex(@"<td\b[^>]*class=""[^""]*\bstats\b[^""]*""[^>]*>(.*?)</td>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex StatsCell();

        [GeneratedRegex(@"(\d[\d,]*)\s+Repl", RegexOptions.IgnoreCase)]
        private static partial Regex ReplyCount();

        [GeneratedRegex(@"<a\b[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
        private static partial Regex AnyLink();

        /// <summary>One page of a single board's topic listing.</summary>
        public static IReadOnlyList<AbookTopicListing> ParseBoard(string? html, string boardId, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                return [];
            }

            var topics = new List<AbookTopicListing>();
            var seen = new HashSet<int>();

            // Rows are taken apart one at a time so a subject keeps the dates and reply
            // count printed beside it; the cells carry no ids tying them together.
            foreach (var row in Rows(html))
            {
                var subjectCell = SubjectCell().Match(row);
                if (!subjectCell.Success)
                {
                    continue;
                }

                var anchor = MessageAnchor().Match(subjectCell.Groups[1].Value);
                var linkSource = anchor.Success ? anchor.Groups[2].Value : subjectCell.Groups[1].Value;
                var href = TopicHref().Match(linkSource);
                if (!href.Success || !int.TryParse(href.Groups[1].Value, out var topicId) || !seen.Add(topicId))
                {
                    continue;
                }

                var subject = AbookMarkup.Text(AnyLink().Match(linkSource) is { Success: true } link
                    ? link.Groups[1].Value
                    : linkSource);
                if (subject.Length == 0)
                {
                    continue;
                }

                var lastPost = LastPostCell().Match(row);
                var stats = StatsCell().Match(row);

                topics.Add(new AbookTopicListing(
                    topicId,
                    subject,
                    boardId,
                    null,
                    stats.Success ? Replies(stats.Groups[1].Value) : null,
                    lastPost.Success ? SmfTimestamp.Parse(AbookMarkup.Text(lastPost.Groups[1].Value), now) : null));
            }

            return topics;
        }

        /// <summary>
        /// The page split into table rows. Splitting rather than matching a balanced
        /// <c>&lt;tr&gt;</c> keeps this honest about what a regex can do: every row here
        /// is flat, and a row that is not simply yields no subject cell and is skipped.
        /// </summary>
        private static IEnumerable<string> Rows(string html)
        {
            var parts = Regex.Split(html, @"<tr\b", RegexOptions.IgnoreCase);
            for (var index = 1; index < parts.Length; index++)
            {
                yield return parts[index];
            }
        }

        private static int? Replies(string statsCell)
        {
            var match = ReplyCount().Match(AbookMarkup.Text(statsCell));
            return match.Success && int.TryParse(match.Groups[1].Value.Replace(",", string.Empty), out var replies)
                ? replies
                : null;
        }
    }

    /// <summary>Markup to text, the way every forum listing parser here needs it.</summary>
    internal static partial class AbookMarkup
    {
        [GeneratedRegex(@"<[^>]+>")]
        private static partial Regex Tag();

        [GeneratedRegex(@"\s+")]
        private static partial Regex Whitespace();

        public static string Text(string? html) =>
            string.IsNullOrEmpty(html)
                ? string.Empty
                : Whitespace().Replace(WebUtility.HtmlDecode(Tag().Replace(html, " ")), " ").Trim();
    }
}
