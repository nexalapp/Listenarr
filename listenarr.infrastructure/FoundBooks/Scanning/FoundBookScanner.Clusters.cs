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
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Listenarr.Application.FoundBooks.Models;
using Listenarr.Domain.Common;
using Listenarr.Domain.FoundBooks;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.FoundBooks.Scanning
{
    public sealed partial class FoundBookScanner
    {
        private static readonly HashSet<string> DeclaredLengthExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".nfo", ".txt"
        };

        private const long MaxDeclaredLengthFileBytes = 256 * 1024;

        // "(u255~55-64.44-m-chap)" and the like: a release group's encoding note.
        private static readonly Regex EncodingNote = new(@"\s*\((?:[^()]*~[^()]*)\)\s*", RegexOptions.Compiled);
        private static readonly Regex YearNote = new(@"\s*[\(\[](\d{4})[\)\]]\s*", RegexOptions.Compiled);
        private static readonly Regex BracketSeries = new(@"^\[(?<series>.+?)\s+(?<pos>\d+(?:\.\d+)?)\]\s*-?\s*(?<title>.+)$", RegexOptions.Compiled);
        private static readonly Regex TrailingNumber = new(@"^(?<series>.+?)\s+(?:book\s+|bk\.?\s*|#)?(?<pos>\d+(?:\.\d+)?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private sealed class Cluster(IReadOnlyList<ScannedFile> audio, IReadOnlyDictionary<string, FoundBookProbeSnapshot> probes)
        {
            public List<ScannedFile> Audio { get; } = audio.ToList();
            public List<ScannedFile> Companions { get; } = [];
            public IReadOnlyDictionary<string, FoundBookProbeSnapshot> Probes { get; } = probes;

            public string TitleKey => FileUtils.NormalizeComparisonValue(First(p => p.Album));
            public string AuthorKey => FileUtils.NormalizeComparisonValue(First(p => p.AlbumArtist ?? p.Artist));

            public string? First(Func<FoundBookProbeSnapshot, string?> pick) =>
                Audio.Select(f => Probes.TryGetValue(f.Path, out var p) ? pick(p) : null)
                    .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            public IEnumerable<string> Directories => Audio.Select(f => f.Directory).Distinct(StringComparer.Ordinal);
        }

        /// <summary>
        /// Join clusters in the same or neighbouring directories that carry the same
        /// album and a compatible artist. The unmatched-scan grouping only looks within
        /// one directory; a pack's loose "Part 1" beside a "Part 2" folder needs this.
        /// </summary>
        private static void Merge(List<Cluster> clusters, FileSystemPathSemantics semantics)
        {
            for (var i = 0; i < clusters.Count; i++)
            {
                var left = clusters[i];
                if (left.TitleKey.Length == 0)
                {
                    continue;
                }

                for (var j = clusters.Count - 1; j > i; j--)
                {
                    var right = clusters[j];
                    if (right.TitleKey.Length == 0
                        || !string.Equals(left.TitleKey, right.TitleKey, StringComparison.Ordinal)
                        || !AuthorsCompatible(left.AuthorKey, right.AuthorKey)
                        || !Neighbours(left, right, semantics))
                    {
                        continue;
                    }

                    left.Audio.AddRange(right.Audio);
                    clusters.RemoveAt(j);
                }
            }
        }

        /// <summary>
        /// Untagged files in one directory that share a name once the part numbering is
        /// stripped are one book. The Library Import stem only strips the common
        /// "N of M" and leading-track forms; rips also number as "01--33", "_001_C000"
        /// and "(01)", and each of those would otherwise be a book of its own.
        /// </summary>
        private static void MergeUntagged(List<Cluster> clusters, FileSystemPathSemantics semantics)
        {
            for (var i = 0; i < clusters.Count; i++)
            {
                var left = clusters[i];
                if (left.TitleKey.Length > 0)
                {
                    continue;
                }

                var leftStem = LooseStem(left.Audio[0].Path);
                for (var j = clusters.Count - 1; j > i; j--)
                {
                    var right = clusters[j];
                    if (right.TitleKey.Length > 0
                        || !semantics.Comparer.Equals(left.Audio[0].Directory, right.Audio[0].Directory)
                        || !string.Equals(leftStem, LooseStem(right.Audio[0].Path), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    left.Audio.AddRange(right.Audio);
                    clusters.RemoveAt(j);
                }
            }
        }

        internal static string LooseStem(string path)
        {
            var stem = FileUtils.NormalizeComparisonValue(StemText(path));
            return stem.Length > 0
                ? stem
                : FileUtils.NormalizeComparisonValue(Path.GetFileName(Path.GetDirectoryName(path)));
        }

        private static bool AuthorsCompatible(string left, string right) =>
            left.Length == 0 || right.Length == 0 || FileUtils.ValuesOverlap(left, right);

        private static readonly Regex DiscFolder = new(@"^(?:cd|disc|disk|part|pt|vol|volume)?\s*\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Same directory, parent and child, or two sibling directories that are plainly
        /// discs of one book ("CD1", "Disc 2", "1"). Two ordinary sibling folders with the
        /// same album tag are more likely two books whose tags were never filled in.
        /// </summary>
        private static bool Neighbours(Cluster left, Cluster right, FileSystemPathSemantics semantics) =>
            left.Directories.Any(a => right.Directories.Any(b =>
                semantics.Comparer.Equals(a, b)
                || semantics.Comparer.Equals(Path.GetDirectoryName(a), b)
                || semantics.Comparer.Equals(a, Path.GetDirectoryName(b))
                || (semantics.Comparer.Equals(Path.GetDirectoryName(a), Path.GetDirectoryName(b))
                    && DiscFolder.IsMatch(Path.GetFileName(a))
                    && DiscFolder.IsMatch(Path.GetFileName(b)))));

        /// <summary>
        /// Non-audio files ride with the book they sit beside. A directory holding one
        /// book gives it everything; one holding several gives each only the files whose
        /// name overlaps its title or one of its audio stems.
        /// </summary>
        private static void AttachCompanions(List<Cluster> clusters, IReadOnlyList<ScannedFile> files, FileSystemPathSemantics semantics)
        {
            foreach (var directory in files.Where(f => !f.IsAudio && !f.IsInProgress).GroupBy(f => f.Directory, semantics.Comparer))
            {
                var here = clusters.Where(c => c.Directories.Contains(directory.Key, semantics.Comparer)).ToList();
                if (here.Count == 0)
                {
                    continue;
                }

                foreach (var companion in directory)
                {
                    if (here.Count == 1)
                    {
                        here[0].Companions.Add(companion);
                        continue;
                    }

                    var stem = FileUtils.NormalizeComparisonValue(Path.GetFileNameWithoutExtension(companion.Path));
                    var owner = here.FirstOrDefault(c =>
                        (c.TitleKey.Length > 0 && FileUtils.ValuesOverlap(stem, c.TitleKey))
                        || c.Audio.Any(a => FileUtils.ValuesOverlap(stem, FileUtils.ExtractComparableAudioStem(a.Path))));
                    owner?.Companions.Add(companion);
                }
            }
        }

        private FoundBookCandidate Build(
            Cluster cluster,
            string root,
            FileSystemPathSemantics semantics,
            IReadOnlySet<string> inProgressDirectories)
        {
            var bookFolder = CommonAncestor(cluster.Audio.Select(f => f.Directory).ToList(), root, semantics);
            var plans = MultiFileImportPlanner.BuildPlans(
                cluster.Audio.Select(f => (f.Path, (string?)Path.GetRelativePath(bookFolder, f.Path))),
                semantics.Comparer);
            var ordered = plans
                .Select(plan => (File: cluster.Audio.First(f => semantics.Comparer.Equals(f.Path, plan.FullPath)), plan.ChapterNumberHint))
                .ToList();

            var entries = ordered
                .Select(o => new FoundBookFileEntry(
                    o.File.Path,
                    o.File.Length,
                    o.File.LastWriteUtc,
                    true,
                    cluster.Probes.GetValueOrDefault(o.File.Path)))
                .Concat(cluster.Companions
                    .OrderBy(c => c.Path, StringComparer.Ordinal)
                    .Select(c => new FoundBookFileEntry(c.Path, c.Length, c.LastWriteUtc, false, null)))
                .ToList();

            var hint = FolderHint(bookFolder, root, semantics, ordered.FirstOrDefault().File?.Path);
            var title = TidyTitle(cluster.First(p => p.Album)) ?? hint.Title;
            var author = cluster.First(p => p.AlbumArtist) ?? cluster.First(p => p.Artist) ?? hint.Author;
            var series = cluster.First(p => p.Series) ?? hint.Series;
            var position = cluster.First(p => p.SeriesPosition) ?? hint.Position;
            var year = cluster.First(p => p.Year) ?? hint.Year;

            var declared = cluster.Audio
                .Select(f => cluster.Probes.GetValueOrDefault(f.Path)?.DeclaredDurationSeconds)
                .FirstOrDefault(d => d.HasValue)
                ?? DeclaredLengthFromCompanions(cluster.Companions);

            var observations = ordered
                .Select(o =>
                {
                    var p = cluster.Probes.GetValueOrDefault(o.File.Path);
                    return new FoundBookAudioObservation(
                        Path.GetFileName(o.File.Path),
                        p == null || !p.Succeeded,
                        p?.DurationSeconds ?? 0,
                        p?.ChapterCount ?? 0,
                        p?.TrackNumber,
                        p?.TrackTotal,
                        o.ChapterNumberHint);
                })
                .ToList();
            var verdict = FoundBookCompletenessAnalyzer.Analyze(observations, declared);

            var inProgress = cluster.Directories.Any(inProgressDirectories.Contains);

            return new FoundBookCandidate(
                root,
                bookFolder,
                ClusterKey(root, ordered.Select(o => o.File.Path)),
                Signature(root, entries),
                entries,
                title,
                author,
                series,
                position,
                cluster.First(p => p.Narrator),
                year,
                cluster.First(p => p.Asin),
                verdict.Completeness,
                verdict.Reason,
                entries.Max(e => e.LastWriteUtc),
                inProgress);
        }

        private double? DeclaredLengthFromCompanions(IEnumerable<ScannedFile> companions)
        {
            foreach (var companion in companions.Where(c => DeclaredLengthExtensions.Contains(Path.GetExtension(c.Path)) && c.Length <= MaxDeclaredLengthFileBytes))
            {
                try
                {
                    var parsed = DeclaredLengthParser.Parse(fileSystem.ReadAllText(companion.Path));
                    if (parsed.HasValue)
                    {
                        return parsed;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Could not read {Path} for a declared length", companion.Path);
                }
            }

            return null;
        }

        private static string CommonAncestor(IReadOnlyList<string> directories, string root, FileSystemPathSemantics semantics)
        {
            var common = directories[0];
            foreach (var dir in directories.Skip(1))
            {
                while (!FileSystemPathIdentity.IsSameOrInside(dir, common, semantics))
                {
                    common = Path.GetDirectoryName(common) ?? root;
                    if (common.Length <= root.Length)
                    {
                        return root;
                    }
                }
            }

            return common;
        }

        private static string ClusterKey(string root, IEnumerable<string> audioPaths)
        {
            var relative = audioPaths.Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal);
            return Hash(string.Join("\n", relative));
        }

        private static string Signature(string root, IEnumerable<FoundBookFileEntry> entries)
        {
            var parts = entries
                .Select(e => $"{Path.GetRelativePath(root, e.Path).Replace('\\', '/')}|{e.Length}|{e.LastWriteUtc.Ticks}")
                .OrderBy(p => p, StringComparer.Ordinal);
            return Hash(string.Join("\n", parts));
        }

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private static string? TidyTitle(string? album)
        {
            if (string.IsNullOrWhiteSpace(album))
            {
                return null;
            }

            var tidy = Regex.Replace(album, @"\s*[\(\[]\s*unabridged\s*[\)\]]", string.Empty, RegexOptions.IgnoreCase).Trim();
            return tidy.Length > 0 ? tidy : album;
        }

        private sealed record FolderHintResult(string? Author, string? Title, string? Series, string? Position, string? Year);

        /// <summary>
        /// What the folder name says when the tags say nothing: "Author - Title",
        /// "Author - Series 03 - Title", "Author - [Series 03] - Title (2016)".
        /// </summary>
        private static readonly FolderHintResult NoHint = new(null, null, null, null, null);

        // A download client's job id, a torrent hash: a folder named by a machine says
        // nothing about the book, and the filenames inside usually do.
        private static readonly Regex OpaqueName = new(@"^[0-9a-fA-F]{16,}$|^[A-Za-z0-9]{12,}$", RegexOptions.Compiled);
        private static readonly Regex EditionWord = new(@"[\s\-_]*\b(?:unabridged|abridged)\b[\s\-_]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TrailingNumbering = new(
            @"(?:[\s\-_\.]+(?:\d+|of|part|pt|ch|chapter|cd|disc|disk|track|(?:part|pt|ch|chapter|cd|disc|disk|track|c)\s*\d+|\d+\s*(?:of|/)\s*\d+)|[\s\-_\.]*(?:\(\s*\d+\s*(?:(?:of|/)\s*\d+)?\s*\)|\[\s*\d+\s*(?:(?:of|/)\s*\d+)?\s*\]))$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// What the names say when the tags say nothing. The folder is asked first;
        /// when it is a machine's name — a hash, a job id — or says less than the files
        /// do, the shared stem of the filenames is read the same way.
        /// </summary>
        private static FolderHintResult FolderHint(string bookFolder, string root, FileSystemPathSemantics semantics, string? firstAudioPath = null)
        {
            var folderHint = FolderNameHint(bookFolder, root, semantics);
            if (firstAudioPath == null)
            {
                return folderHint;
            }

            var folderName = Path.GetFileName(bookFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? string.Empty;
            var fileHint = NameHint(StemText(firstAudioPath));
            var folderIsOpaque = OpaqueName.IsMatch(folderName) || semantics.Comparer.Equals(bookFolder, root);
            if (folderIsOpaque || Score(fileHint) > Score(folderHint))
            {
                return fileHint with { Year = fileHint.Year ?? folderHint.Year };
            }

            return folderHint;
        }

        private static int Score(FolderHintResult hint) =>
            (hint.Author != null ? 2 : 0) + (hint.Series != null ? 1 : 0) + (hint.Title != null ? 1 : 0);

        /// <summary>
        /// The filename without its extension, part numbering, and edition word:
        /// "Jack Campbell - The Lost Fleet 02 - Fearless - Unabridged - Part 1" becomes
        /// "Jack Campbell - The Lost Fleet 02 - Fearless".
        /// </summary>
        internal static string StemText(string path)
        {
            var text = Path.GetFileNameWithoutExtension(path);
            string previous;
            do
            {
                previous = text;
                text = TrailingNumbering.Replace(text, string.Empty);
                text = EditionWord.Replace(text, string.Empty);
                text = text.TrimEnd(' ', '-', '_', '.');
            }
            while (text != previous && text.Length > 0);
            return text;
        }

        private static FolderHintResult FolderNameHint(string bookFolder, string root, FileSystemPathSemantics semantics)
        {
            var name = Path.GetFileName(bookFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name) || semantics.Comparer.Equals(bookFolder, root))
            {
                return NoHint;
            }

            return NameHint(name);
        }

        /// <summary>"Author - Title", "Author - Series 03 - Title", "Author - [Series 03] - Title (2016)".</summary>
        private static FolderHintResult NameHint(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return NoHint;
            }

            string? year = null;
            name = EncodingNote.Replace(name, " ");
            name = YearNote.Replace(name, m => { year ??= m.Groups[1].Value; return " "; }).Trim();

            var parts = name.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1)
            {
                // "Isaac Asimov, Foundation and Earth": a name of two or three words, a
                // comma, then the title.
                var comma = parts[0].Split(", ", 2, StringSplitOptions.TrimEntries);
                if (comma.Length == 2 && comma[0].Split(' ').Length is >= 2 and <= 3 && !comma[0].Any(char.IsDigit))
                {
                    return new FolderHintResult(comma[0], comma[1], null, null, year);
                }

                var bracket = BracketSeries.Match(parts[0]);
                return bracket.Success
                    ? new FolderHintResult(null, bracket.Groups["title"].Value, bracket.Groups["series"].Value, bracket.Groups["pos"].Value, year)
                    : new FolderHintResult(null, parts[0], null, null, year);
            }

            var author = parts[0];
            var title = parts[^1];
            string? series = null;
            string? position = null;
            foreach (var middle in parts.Skip(1).Take(parts.Length - 2).Select(m => m.Trim('[', ']', ' ')))
            {
                var trailing = TrailingNumber.Match(middle);
                if (trailing.Success)
                {
                    series = trailing.Groups["series"].Value;
                    position = trailing.Groups["pos"].Value;
                }
                else
                {
                    series ??= middle;
                }
            }

            var titleBracket = BracketSeries.Match(title);
            if (titleBracket.Success)
            {
                series ??= titleBracket.Groups["series"].Value;
                position ??= titleBracket.Groups["pos"].Value;
                title = titleBracket.Groups["title"].Value;
            }

            return new FolderHintResult(author, title, series, position, year);
        }
    }
}
