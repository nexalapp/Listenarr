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
            AudiobookTagPlanner tagPlanner,
            IReadOnlyList<TagMapping> tagMappings,
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

            // The output is brand new, so there are no existing tags to preserve or
            // protect: every mapping that resolves to something is written. Routing
            // through the same planner a tag write uses is what makes a converted book
            // and an enriched one carry identical tags.
            var bookMetadata = AudiobookTagMetadata.Create(audiobook, bookTags);
            var tags = tagPlanner.Plan(bookMetadata, tagMappings, existingTags: null).FinalTags;

            logger.LogInformation(
                "Planned conversion of audiobook {AudiobookId}: {Files} file(s), {Duration}, {BitRate}bps {SampleRate}Hz {Channels}ch",
                audiobook.Id,
                plan.OrderedSources.Count,
                plan.TotalDuration,
                plan.TargetBitRate,
                plan.TargetSampleRate,
                plan.TargetChannels);

            return PlanningOutcome.Planned(plan, bookMetadata, tags);
        }

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

        /// <summary>Absolute path of a registered file, resolved the same way everywhere.</summary>
        private static string? ResolveFullPath(Audiobook audiobook, AudiobookFile file) =>
            AudiobookFilePaths.ResolveFullPath(audiobook, file);

        /// <summary>
        /// A planned conversion. <paramref name="Metadata"/> is what the naming pattern
        /// resolves the destination filename from; <paramref name="Tags"/> is the
        /// resolved tag set the output will carry. They are separate because a filename
        /// and a tag sanitise their values differently.
        /// </summary>
        private sealed record PlanningOutcome(
            ConversionPlan? Plan,
            AudioMetadata? Metadata,
            IReadOnlyDictionary<string, string>? Tags,
            ConversionFailureKind FailureKind,
            string? Error)
        {
            public bool Success => Plan != null && Metadata != null && Tags != null;

            public static PlanningOutcome Planned(
                ConversionPlan plan,
                AudioMetadata metadata,
                IReadOnlyDictionary<string, string> tags) =>
                new(plan, metadata, tags, ConversionFailureKind.None, null);

            public static PlanningOutcome Failed(ConversionFailureKind kind, string error) =>
                new(null, null, null, kind, error);
        }
    }
}
