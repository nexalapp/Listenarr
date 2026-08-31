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
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Conversion
{
    public sealed partial class ConversionJobProcessor
    {
        /// <summary>
        /// Read every source file and build the plan.
        ///
        /// Probing is a real cost — one ffprobe per file over a NAS share — but the
        /// chapter marks are computed from the decoded durations, and a duration guessed
        /// from the bitrate would put every mark after the first in the wrong place.
        /// </summary>
        private async Task<PlanningOutcome> BuildPlanAsync(
            Audiobook audiobook,
            IReadOnlyList<AudiobookFile> sourceFiles,
            IFfmpegService ffmpegService,
            StringComparer pathComparer,
            CancellationToken cancellationToken)
        {
            var sources = new List<ConversionSource>(sourceFiles.Count);
            AudioMetadata? bookTags = null;

            foreach (var file in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var path = ResolveFullPath(audiobook, file);
                if (path == null || !File.Exists(path))
                {
                    return PlanningOutcome.Failed(
                        ConversionFailureKind.SourceUnreadable,
                        $"Source file is missing: {LogRedaction.SanitizeFilePath(path ?? file.Path)}");
                }

                AudioMetadata metadata;
                try
                {
                    metadata = await ffmpegService.RunFfprobeAsync(path);
                }
                catch (FfmpegException ex)
                {
                    // ffprobe already reduces its own diagnostics to the one line worth
                    // showing, so carry that through rather than replacing it.
                    return PlanningOutcome.Failed(
                        ConversionFailureKind.SourceUnreadable,
                        ex.Message);
                }

                // The first file that carries real tags stands in for the book, which is
                // the same convention the importer uses.
                bookTags ??= HasUsableTags(metadata) ? metadata : null;

                // A file may already carry chapter marks — a book previously merged into
                // one chaptered MP3 keeps them in ID3 CHAP frames. Reading them is what
                // stops the conversion from flattening that book into one chapter.
                var embeddedChapters = await ffmpegService.ReadChaptersAsync(path, cancellationToken);

                sources.Add(new ConversionSource(
                    path,
                    BuildRelativePath(audiobook, path),
                    metadata.Duration,
                    metadata.BitRate,
                    metadata.SampleRate,
                    metadata.Channels,
                    metadata.Title,
                    embeddedChapters));
            }

            if (sources.Count == 0)
            {
                return PlanningOutcome.Failed(
                    ConversionFailureKind.SourceUnreadable,
                    "No readable source files were found for this book.");
            }

            var plan = ConversionPlanner.BuildPlan(sources, pathComparer);
            var tags = BuildBookTags(audiobook, bookTags);

            logger.LogInformation(
                "Planned conversion of audiobook {AudiobookId}: {Files} file(s), {Duration}, {BitRate}bps {SampleRate}Hz {Channels}ch",
                audiobook.Id,
                plan.OrderedSources.Count,
                plan.TotalDuration,
                plan.TargetBitRate,
                plan.TargetSampleRate,
                plan.TargetChannels);

            return PlanningOutcome.Planned(plan, tags);
        }

        /// <summary>
        /// Tags for the output, preferring what the library knows about the book over
        /// what its files happen to carry. The library record is the corrected one; the
        /// file tags are whatever the source release shipped with.
        /// </summary>
        private static AudioMetadata BuildBookTags(Audiobook audiobook, AudioMetadata? fromFiles)
        {
            var tags = audiobook.CreateBasicAudioMetadata();

            // The album is what a player shows as the book, and CreateBasicAudioMetadata
            // leaves it empty because it describes a single imported file.
            tags.Album = FirstNonEmpty(audiobook.Title, fromFiles?.Album, tags.Title) ?? string.Empty;

            // The description is the reason this conversion exists, so it is worth
            // falling back to the file tags when the library record has none.
            tags.Description = FirstNonEmpty(audiobook.Description, fromFiles?.Description);

            tags.Genre = FirstNonEmpty(fromFiles?.Genre, "Audiobook") ?? "Audiobook";
            tags.Narrator ??= fromFiles?.Narrator;
            tags.Publisher ??= fromFiles?.Publisher;
            tags.Language ??= fromFiles?.Language;
            tags.Year ??= fromFiles?.Year;

            if (string.IsNullOrWhiteSpace(tags.Artist) && fromFiles != null)
            {
                tags.Artist = fromFiles.Artist;
                tags.AlbumArtist = fromFiles.AlbumArtist;
            }

            return tags;
        }

        private static string? FirstNonEmpty(params string?[] candidates) =>
            candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

        /// <summary>
        /// Whether a probe found tags worth borrowing. A file whose only tag is a track
        /// number tells us nothing about the book.
        /// </summary>
        private static bool HasUsableTags(AudioMetadata metadata) =>
            !string.IsNullOrWhiteSpace(metadata.Album)
            || !string.IsNullOrWhiteSpace(metadata.Artist)
            || !string.IsNullOrWhiteSpace(metadata.Description);

        /// <summary>
        /// Path of a source relative to the book's directory. Ordering uses it so a book
        /// split across "Disc 1"/"Disc 2" subdirectories sorts by disc and then by track.
        /// </summary>
        private static string? BuildRelativePath(Audiobook audiobook, string fullPath)
        {
            var basePath = audiobook.BasePath;
            if (string.IsNullOrWhiteSpace(basePath))
            {
                return Path.GetFileName(fullPath);
            }

            try
            {
                var relative = Path.GetRelativePath(basePath, fullPath);
                // GetRelativePath returns a ".." walk when the file is outside the base,
                // which would sort worse than the filename alone.
                return relative.StartsWith("..", StringComparison.Ordinal)
                    ? Path.GetFileName(fullPath)
                    : relative;
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
            {
                return Path.GetFileName(fullPath);
            }
        }

        /// <summary>
        /// Absolute path of a registered file. Stored paths may be relative to the
        /// owning book's base path, so a bare join is not enough.
        /// </summary>
        private static string? ResolveFullPath(Audiobook audiobook, AudiobookFile file)
        {
            if (string.IsNullOrWhiteSpace(file.Path))
            {
                return null;
            }

            try
            {
                if (Path.IsPathRooted(file.Path))
                {
                    return Path.GetFullPath(file.Path);
                }

                return string.IsNullOrWhiteSpace(audiobook.BasePath)
                    ? null
                    : Path.GetFullPath(Path.Combine(audiobook.BasePath, file.Path));
            }
            catch (Exception ex) when (
                ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        private sealed record PlanningOutcome(
            ConversionPlan? Plan,
            AudioMetadata? Tags,
            ConversionFailureKind FailureKind,
            string? Error)
        {
            public bool Success => Plan != null && Tags != null;

            public static PlanningOutcome Planned(ConversionPlan plan, AudioMetadata tags) =>
                new(plan, tags, ConversionFailureKind.None, null);

            public static PlanningOutcome Failed(ConversionFailureKind kind, string error) =>
                new(null, null, kind, error);
        }
    }
}
