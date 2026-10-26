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

using System.Text.RegularExpressions;

namespace Listenarr.Application.Audiobooks.Suggestions
{
    /// <summary>
    /// Whether an indexer's release is, with high confidence, a suggested book.
    ///
    /// An abook.link post that declares an ASIN is held to it and nothing else: a matching
    /// title is not enough when the post says which edition it is. Most posts, the older
    /// ones especially, declare none, but their title and author are parsed fields rather
    /// than free text, so the title must equal the book's and the author must carry its
    /// first author's surname. A Newznab release only has a free-text title, so it must
    /// carry the book's title as a phrase and the surname as a word - the two things a
    /// release of some other book is least likely to share.
    /// </summary>
    public static partial class SuggestionReleaseMatch
    {
        [GeneratedRegex(@"[^\p{L}\p{N}]+")]
        private static partial Regex Separators();

        public static bool AsinMatches(string? releaseAsin, string? bookAsin) =>
            !string.IsNullOrWhiteSpace(releaseAsin)
            && !string.IsNullOrWhiteSpace(bookAsin)
            && string.Equals(releaseAsin.Trim(), bookAsin.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether an abook.link post, as its parsed fields describe it, is this book.</summary>
        public static bool AbookPostMatches(
            string? postAsin,
            string? postTitle,
            string? postAuthor,
            string? bookAsin,
            string? bookTitle,
            IReadOnlyList<string>? authors,
            string? postSeries = null,
            string? postSeriesPosition = null,
            string? bookSeriesNumber = null)
        {
            if (!string.IsNullOrWhiteSpace(postAsin))
            {
                return AsinMatches(postAsin, bookAsin);
            }

            var surname = Surname(authors?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)));
            return surname.Length > 0
                && TitlesMatch(postTitle, bookTitle)
                && (" " + Normalize(postAuthor) + " ").Contains(" " + surname + " ", StringComparison.Ordinal)
                && !TitleIsOnlyTheSeries(postTitle, postSeries, postSeriesPosition, bookSeriesNumber);
        }

        /// <summary>
        /// True when the post's title is just its series name and its position is not the
        /// book's. Such a title names the series, not a book in it: "Aer-ki Jyr - Gauntlet
        /// Wars 04 - Gauntlet Wars" is book 4, and matched Gauntlet Wars book 1 on title and
        /// author alone. Only then are positions compared - elsewhere they disagree for
        /// reasons that are no part of identity, a second numbering scheme or a parse of
        /// "1 633 - 1633" as 633 - and comparing them would turn away the right book.
        /// </summary>
        private static bool TitleIsOnlyTheSeries(
            string? postTitle,
            string? postSeries,
            string? postSeriesPosition,
            string? bookSeriesNumber)
        {
            if (string.IsNullOrWhiteSpace(postSeries) || Normalize(postTitle) != Normalize(postSeries))
            {
                return false;
            }

            var post = Position(postSeriesPosition);
            var book = Position(bookSeriesNumber);
            return post is null || book is null || post != book;
        }

        private static decimal? Position(string? value)
        {
            var match = PositionNumber().Match(value ?? string.Empty);
            return match.Success && decimal.TryParse(
                match.Value,
                System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture,
                out var number)
                ? number
                : null;
        }

        [GeneratedRegex(@"\d+(?:\.\d+)?")]
        private static partial Regex PositionNumber();

        public static bool TitleAndAuthorMatch(
            string? releaseTitle,
            string? bookTitle,
            IReadOnlyList<string>? authors,
            string? bookSeriesNumber = null)
        {
            var release = " " + Normalize(releaseTitle) + " ";
            var title = Normalize(DistinctiveTitle(bookTitle));
            var surname = Surname(authors?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)));

            return title.Length > 0
                && surname.Length > 0
                && release.Contains(" " + title + " ", StringComparison.Ordinal)
                && release.Contains(" " + surname + " ", StringComparison.Ordinal)
                && !NamesAnotherBookInTheSeries(release, title, bookSeriesNumber);
        }

        /// <summary>
        /// True when the book's title is being used as the series' name and the number
        /// after it is some other book's. A free-text release title is all a Newznab
        /// indexer gives, so "Allan Cole and Chris Bunch - Sten 08 - Empires End" carries
        /// the title "Sten" and the surname "Bunch" and matched Sten book 1 - and book 3,
        /// whose distinctive title is also "Sten". The abook.link side has this rule
        /// already, on parsed fields; this is the same rule read out of the free text.
        ///
        /// Only a number directly after the title counts, so "The Wolf Worlds" matching
        /// "Sten, Bk 2 - The Wolf Worlds" is untouched, and a book with no position of its
        /// own is never turned away - there is nothing to disagree with.
        /// </summary>
        private static bool NamesAnotherBookInTheSeries(
            string normalizedRelease, string normalizedTitle, string? bookSeriesNumber)
        {
            var book = Position(bookSeriesNumber);
            if (book is null)
            {
                return false;
            }

            foreach (Match match in TitleThenNumber(normalizedTitle).Matches(normalizedRelease))
            {
                if (Position(match.Groups[1].Value) is { } inRelease && inRelease != book)
                {
                    return true;
                }
            }

            return false;
        }

        private static Regex TitleThenNumber(string normalizedTitle) =>
            new($@"(?<= ){Regex.Escape(normalizedTitle)} (\d+(?:\.\d+)?)(?= )", RegexOptions.None);

        /// <summary>
        /// Whether a release of this size could be the recording at all. An audiobook is
        /// speech, and the thinnest encodings in this library still run well above 8 kbps;
        /// 34.8 MB against a stated ten and a half hours is under one, which is an ebook
        /// posted under the book's name, not a reading of it.
        ///
        /// The floor is set that low on purpose. A record's runtime and a release's
        /// edition disagree often and for honest reasons - abridgements, a different
        /// reading - so this may only reject what no amount of such disagreement could
        /// explain. A book with no runtime, or a release with no size, is not judged.
        /// </summary>
        public static bool CouldBeTheRecording(long sizeBytes, int? runtimeMinutes) =>
            sizeBytes <= 0
            || runtimeMinutes is not > 0
            || sizeBytes / (double)runtimeMinutes.Value >= MinimumBytesPerMinute;

        /// <summary>8 kbps, in bytes per minute of runtime.</summary>
        private const double MinimumBytesPerMinute = 8000 / 8.0 * 60;

        /// <summary>What to search for: the book's distinctive title and its first author.</summary>
        public static string QueryFor(string title, IReadOnlyList<string>? authors) =>
            $"{DistinctiveTitle(title)} {CleanAuthor(authors?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)))}".Trim();

        /// <summary>
        /// Two parsed titles are one book when they agree in full, or when one has no
        /// subtitle and their main titles agree - but only if that main title tells books
        /// apart. "Solaris" is "Solaris: The Definitive Edition"; "1636: The Barbie
        /// Consortium" is not "1636: The Saxon Uprising", and a bare "1636" is neither.
        /// </summary>
        private static bool TitlesMatch(string? postTitle, string? bookTitle)
        {
            var post = Normalize(postTitle);
            var book = Normalize(bookTitle);
            if (book.Length == 0)
            {
                return false;
            }

            if (post == book)
            {
                return true;
            }

            var bookMain = MainTitle(bookTitle);
            var postMain = MainTitle(postTitle);
            var oneHasNoSubtitle = !HasSubtitle(postTitle) || !HasSubtitle(bookTitle);
            return oneHasNoSubtitle
                && IsDistinctive(bookMain)
                && Normalize(postMain) == Normalize(bookMain);
        }

        /// <summary>
        /// The title to look for: the main title when it identifies the book, otherwise the
        /// whole of it. A main title that is one bare word or a number - "1636", "Sten" - is
        /// shared by every book in its series, so the subtitle has to come too.
        /// </summary>
        private static string DistinctiveTitle(string? title) =>
            IsDistinctive(MainTitle(title)) ? MainTitle(title) : (title ?? string.Empty).Trim();

        private static bool IsDistinctive(string mainTitle)
        {
            var words = Normalize(mainTitle).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length >= 2 || (words.Length == 1 && words[0].Length > 4 && !words[0].All(char.IsDigit));
        }

        private static bool HasSubtitle(string? title)
        {
            var value = (title ?? string.Empty).Trim();
            var colon = value.IndexOf(':');
            return colon > 0 && colon < value.Length - 1;
        }

        /// <summary>
        /// The author's name without a role: "Eric Flint - editor", "Eric Flint (Editor)"
        /// and "Eric Flint [editor]" are all Eric Flint, and taking the last word as the
        /// surname would otherwise make it "editor".
        /// </summary>
        private static string CleanAuthor(string? author)
        {
            var value = (author ?? string.Empty).Trim();
            var dash = value.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0)
            {
                value = value[..dash];
            }

            return RoleInBrackets().Replace(value, " ").Trim();
        }

        [GeneratedRegex(@"[\(\[][^\)\]]*[\)\]]")]
        private static partial Regex RoleInBrackets();

        /// <summary>
        /// The title before any subtitle: "Solaris: The Definitive Edition" is posted as
        /// "Solaris" as often as not, and a subtitle is no part of which book it is.
        /// </summary>
        private static string MainTitle(string? title)
        {
            var value = (title ?? string.Empty).Trim();
            var colon = value.IndexOf(':');
            return colon > 0 ? value[..colon].Trim() : value;
        }

        private static string Surname(string? author)
        {
            var words = Normalize(CleanAuthor(author)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 0 ? string.Empty : words[^1];
        }

        private static string Normalize(string? value) =>
            string.Join(' ', Separators()
                .Split((value ?? string.Empty).ToLowerInvariant())
                .Where(word => word.Length > 0));
    }
}
