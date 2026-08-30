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
using System.Text;
using System.Text.RegularExpressions;

namespace Listenarr.Application.Search.AbookLink
{
    /// <summary>
    /// Reads an abook.link topic into <see cref="AbookPost"/>.
    ///
    /// Posts are written by hand, assisted by the site's own formatter, and eight sampled
    /// posts produced ten different layouts: six spellings for the copyright date, five
    /// duration formats, three mutually exclusive file-information sections, and a series
    /// that may be two fields, one combined field, or only present inside the title.
    ///
    /// So nothing is matched on an exact string. Labels are normalised and looked up in
    /// synonym sets, unknown labels are collected rather than dropped, and the raw text is
    /// always carried through for a human to read when the parse disagrees with them.
    ///
    /// See tests/Fixtures/AbookLink for the corpus this is written against.
    /// </summary>
    public static partial class AbookPostParser
    {
        // Labels are normalised before lookup, so "Genre (but may be multi):" and "Genre:"
        // are the same key. Parenthetical asides in labels are a real pattern, not a typo.
        private static readonly string[] TitleLabels = ["title"];
        private static readonly string[] AuthorLabels = ["author"];
        private static readonly string[] NarratorLabels = ["read by", "narrator", "narrated by"];
        private static readonly string[] SeriesLabels = ["series name", "series"];
        private static readonly string[] PositionLabels = ["series position", "book number", "position"];
        private static readonly string[] CombinedSeriesLabels = ["series & position", "series and position"];
        private static readonly string[] PublisherLabels = ["publisher"];
        private static readonly string[] AbridgedLabels = ["abridged"];
        private static readonly string[] CompressedLabels = ["compressed with", "archive"];
        private static readonly string[] TagLabels = ["tags", "id3 tags"];

        private static readonly string[] YearLabels =
        [
            "copyright", "audio copyright", "audiobook copyright",
            "audiobook release", "release date", "publication date"
        ];

        private static readonly string[] FormatLabels = ["media format", "file type", "source format", "encoded codec"];
        private static readonly string[] FileCountLabels = ["total files", "number of files"];
        private static readonly string[] SizeLabels = ["total size", "size"];
        private static readonly string[] DurationLabels = ["duration", "total duration", "length"];
        private static readonly string[] BitrateLabels = ["encoded at", "encoded bitrate", "bitrate", "source bitrate"];

        [GeneratedRegex(@"^\s*([A-Za-z][A-Za-z0-9 &/'\-]{0,40}?)\s*(\([^)]*\))?\s*:\s*(.*)$")]
        private static partial Regex LabelledLine();

        [GeneratedRegex(@"\bin\s+((?:a\.b\.|alt\.binaries\.)[A-Za-z0-9.\-]+)", RegexOptions.IgnoreCase)]
        private static partial Regex NewsgroupMention();

        [GeneratedRegex(@"\b(\d{1,4})\s*(?:x\s*)?(?:mp3s?|m4bs?|files?)\b", RegexOptions.IgnoreCase)]
        private static partial Regex CountInFormat();

        [GeneratedRegex(@"(select all|create nzb|not show up as one collection|individual download links)",
            RegexOptions.IgnoreCase)]
        private static partial Regex MultiPartHint();

        /// <summary>
        /// Parses a topic. <paramref name="topicTitle"/> is used to fill identity the NFO
        /// omits — abook.link topic titles follow a far more consistent shape than the
        /// bodies do, so they are a dependable fallback.
        /// </summary>
        public static AbookPost Parse(string body, string? topicTitle = null)
        {
            var post = new AbookPost { RawNfo = body };

            if (string.IsNullOrWhiteSpace(body))
            {
                post.Outcome = AbookParseOutcome.Unusable;
                return post;
            }

            var fromTitle = AbookTopicTitle.Parse(topicTitle);
            if (fromTitle.IsNotARelease)
            {
                post.Outcome = AbookParseOutcome.NotARelease;
                return post;
            }

            var isArchive = body.Contains("[SPOT] posting from the old forum", StringComparison.OrdinalIgnoreCase)
                || (topicTitle?.Contains("[SPOT]", StringComparison.OrdinalIgnoreCase) ?? false);

            ReadLabelledFields(body, post);
            ReadPayload(body, post);

            post.Title ??= fromTitle.Title;
            post.Author ??= fromTitle.Author;
            post.SeriesName ??= fromTitle.SeriesName;
            post.SeriesPosition ??= fromTitle.SeriesPosition;
            post.Year ??= fromTitle.Year;
            post.Asin ??= AbookValues.ParseAsin(body);

            post.Outcome = Decide(post, isArchive);
            return post;
        }

        private static AbookParseOutcome Decide(AbookPost post, bool isArchive)
        {
            if (isArchive)
            {
                // Archive imports carry a payload that still names the old abook.ws domain
                // and the site warns their links may be dead, so they are never treated as
                // a normal release even when a code is present.
                return AbookParseOutcome.ArchiveSpot;
            }

            if (post.HasIdentity && post.CanGrab) return AbookParseOutcome.Complete;
            if (post.HasIdentity) return AbookParseOutcome.MissingSearchString;
            if (post.CanGrab) return AbookParseOutcome.MissingIdentity;
            return AbookParseOutcome.Unusable;
        }

        private static void ReadLabelledFields(string body, AbookPost post)
        {
            foreach (var rawLine in body.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');

                // The payload block is read separately; its "Code:" lines would otherwise
                // look like ordinary labelled fields.
                if (line.TrimStart().StartsWith("Code:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var match = LabelledLine().Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var label = Normalise(match.Groups[1].Value);
                var value = match.Groups[3].Value.Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                if (!Assign(post, label, value))
                {
                    post.UnrecognisedLabels.Add(match.Groups[1].Value.Trim());
                }
            }
        }

        private static bool Assign(AbookPost post, string label, string value)
        {
            if (In(label, TitleLabels)) { post.Title ??= value; return true; }
            if (In(label, AuthorLabels)) { post.Author ??= value; return true; }
            if (In(label, NarratorLabels)) { post.Narrator ??= value; return true; }
            if (In(label, PublisherLabels)) { post.Publisher ??= value; return true; }

            if (In(label, CombinedSeriesLabels))
            {
                var (name, position) = AbookValues.SplitSeriesAndPosition(value);
                post.SeriesName ??= name;
                post.SeriesPosition ??= position;
                return true;
            }

            if (In(label, SeriesLabels)) { post.SeriesName ??= value; return true; }
            if (In(label, PositionLabels)) { post.SeriesPosition ??= AbookValues.ParsePosition(value); return true; }
            if (In(label, YearLabels)) { post.Year ??= AbookValues.ParseYear(value); return true; }
            if (In(label, CompressedLabels)) { post.CompressedWith ??= value; return true; }

            // Recognised so it stops appearing as an unknown label, but not surfaced:
            // ID3 tag state says nothing about which book this is or its quality.
            if (In(label, TagLabels)) { return true; }

            if (In(label, AbridgedLabels))
            {
                post.Abridged ??= value.StartsWith("y", StringComparison.OrdinalIgnoreCase);
                return true;
            }

            if (In(label, FormatLabels))
            {
                post.Format ??= value;
                // "49 MP3s" states the file count inside the format string.
                var count = CountInFormat().Match(value);
                if (count.Success && int.TryParse(count.Groups[1].Value, out var parsed))
                {
                    post.FileCount ??= parsed;
                }
                return true;
            }

            if (In(label, FileCountLabels))
            {
                if (int.TryParse(value.Trim(), out var files)) post.FileCount ??= files;
                return true;
            }

            if (In(label, SizeLabels)) { post.SizeBytes ??= AbookValues.ParseSize(value, out _); return true; }
            if (In(label, DurationLabels)) { post.Duration ??= AbookValues.ParseDuration(value); return true; }
            if (In(label, BitrateLabels)) { post.Bitrate ??= value; return true; }

            return false;
        }

        /// <summary>
        /// Reads the block revealed by thanking. Three labellings occur: <c>Search:</c>,
        /// <c>Search for:</c>, and an unlabelled <c>Code:</c>. A second code block, when
        /// present, is the password.
        /// </summary>
        private static void ReadPayload(string body, AbookPost post)
        {
            var index = body.IndexOf("Hidden content:", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return;
            }

            var payload = body[index..];
            post.RawPayload = payload.Trim();

            var lines = payload.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
            string? pending = null;
            var notes = new StringBuilder();

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.Equals("Hidden content:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsPasswordLabel(line)) { pending = "password"; continue; }
                if (IsSearchLabel(line)) { pending = "search"; continue; }

                if (line.StartsWith("Code:", StringComparison.OrdinalIgnoreCase))
                {
                    var inline = line[5..].Trim();
                    var value = inline.Length > 0 ? inline : NextValue(lines, i);

                    if (value is { Length: > 0 })
                    {
                        // An unlabelled code is the search string: no sampled post has ever
                        // led with a password.
                        if (pending == "password") post.Password ??= value;
                        else post.SearchString ??= value;
                    }

                    pending = null;
                    continue;
                }

                if (!IsStructural(line))
                {
                    notes.AppendLine(line);
                }
            }

            var prose = notes.ToString().Trim();
            if (prose.Length > 0)
            {
                post.PayloadNotes = prose;
                post.NewsgroupHint = NewsgroupMention().Match(prose) is { Success: true } n ? n.Groups[1].Value : null;
                post.MultiPart = MultiPartHint().IsMatch(prose);
            }
        }

        private static string? NextValue(string[] lines, int from)
        {
            for (var i = from + 1; i < lines.Length; i++)
            {
                var candidate = lines[i].Trim();
                if (candidate.Length == 0) continue;
                if (candidate.Equals("[Copy]", StringComparison.OrdinalIgnoreCase)) continue;
                if (IsSearchLabel(candidate) || IsPasswordLabel(candidate)) return null;
                return candidate;
            }

            return null;
        }

        private static bool IsSearchLabel(string line) =>
            line.Equals("Search:", StringComparison.OrdinalIgnoreCase)
            || line.Equals("Search for:", StringComparison.OrdinalIgnoreCase);

        private static bool IsPasswordLabel(string line) =>
            line.Equals("Password:", StringComparison.OrdinalIgnoreCase)
            || line.Equals("Pass:", StringComparison.OrdinalIgnoreCase);

        private static bool IsStructural(string line) =>
            line.Equals("[Copy]", StringComparison.OrdinalIgnoreCase)
            || line.All(c => c is '=' or '-' or '_');

        private static bool In(string label, string[] set) => set.Contains(label, StringComparer.Ordinal);

        /// <summary>
        /// Lowercases, drops any parenthetical aside and collapses whitespace, so
        /// "Genre (but may be multi)" and "Genre" resolve to the same key.
        /// </summary>
        private static string Normalise(string label)
        {
            var cleaned = new StringBuilder();
            var depth = 0;

            foreach (var c in label)
            {
                if (c == '(') { depth++; continue; }
                if (c == ')') { if (depth > 0) depth--; continue; }
                if (depth > 0) continue;
                cleaned.Append(char.ToLowerInvariant(c));
            }

            return string.Join(' ', cleaned.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
