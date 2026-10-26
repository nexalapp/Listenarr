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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Conversion
{
    /// <summary>
    /// The cover an encode embeds. The encode builds its output from decoded audio, so
    /// nothing of the sources' containers survives into it unless it is asked for — and
    /// for a long time the cover never was: every converted and repaired book came out
    /// without one, while its original, archived beside it, still had it.
    ///
    /// <para>
    /// In order of preference: the art the book's own files carry, then an image in the
    /// book's folder, then the cover Listenarr has cached. The first is the book as it
    /// was; the other two only apply where embedding covers is switched on, the same
    /// setting a tag write obeys.
    /// </para>
    /// </summary>
    public sealed partial class ConversionJobProcessor
    {
        /// <summary>The sources asked for embedded art; a book carries the same art in every part.</summary>
        private const int CoverSourcesTried = 3;

        private static readonly string[] PreferredFolderCoverNames = ["cover", "folder"];

        /// <summary>
        /// A cover for the output, or null. Embedded art is extracted into
        /// <paramref name="scratchDirectory"/>; the caller deletes it once the encode is done.
        /// </summary>
        internal async Task<CoverForEncode?> ResolveCoverForEncodeAsync(
            Audiobook audiobook,
            IReadOnlyList<ConversionSource> originals,
            string scratchDirectory,
            Guid jobId,
            ApplicationSettings settings,
            IServiceProvider services,
            CancellationToken cancellationToken)
        {
            try
            {
                var ffmpeg = await services.GetRequiredService<IFfmpegService>().GetFfmpegPathAsync();
                if (!string.IsNullOrEmpty(ffmpeg))
                {
                    foreach (var source in originals.Take(CoverSourcesTried))
                    {
                        var extracted = await TryExtractEmbeddedCoverAsync(
                            ffmpeg, source.FullPath, scratchDirectory, jobId, cancellationToken);
                        if (extracted != null)
                        {
                            return new CoverForEncode(extracted, IsScratch: true);
                        }
                    }
                }

                if (!settings.EmbedCoverArtInTags)
                {
                    return null;
                }

                var folderCover = FindFolderCover(audiobook, originals);
                if (folderCover != null)
                {
                    return new CoverForEncode(folderCover, IsScratch: false);
                }

                var cached = await CachedCoverArt.ResolveAsync(audiobook, services);
                return cached == null ? null : new CoverForEncode(cached, IsScratch: false);
            }
            catch (Exception ex) when (IsNonFatal(ex))
            {
                // A missing cover is never worth failing an encode over.
                logger.LogWarning(ex, "Could not find a cover for audiobook {AudiobookId}; encoding without one", audiobook.Id);
                return null;
            }
        }

        /// <summary>
        /// Copy the first picture stream out byte for byte. The output's type is read from
        /// its first bytes rather than trusted from the container, and anything that is
        /// neither JPEG nor PNG is dropped: the mov muxer can carry nothing else.
        /// </summary>
        private async Task<string?> TryExtractEmbeddedCoverAsync(
            string ffmpeg,
            string sourcePath,
            string scratchDirectory,
            Guid jobId,
            CancellationToken cancellationToken)
        {
            var raw = Path.Combine(scratchDirectory, $"cover-{jobId:N}.img");
            var exitCode = await RunFfmpegAsync(
                ffmpeg,
                ["-hide_banner", "-nostdin", "-loglevel", "error", "-y",
                 "-i", sourcePath,
                 "-map", "0:v:0", "-c", "copy", "-frames:v", "1", "-f", "image2", raw],
                TimeSpan.FromMinutes(2),
                cancellationToken);

            if (exitCode != 0 || !File.Exists(raw))
            {
                TryDeleteScratch(raw);
                return null;
            }

            var extension = SniffImageExtension(raw);
            if (extension == null)
            {
                TryDeleteScratch(raw);
                return null;
            }

            var typed = Path.ChangeExtension(raw, extension);
            File.Move(raw, typed, overwrite: true);
            return typed;
        }

        internal static string? SniffImageExtension(string path)
        {
            Span<byte> head = stackalloc byte[4];
            using var stream = File.OpenRead(path);
            if (stream.Read(head) < 4)
            {
                return null;
            }

            if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            {
                return ".jpg";
            }

            return head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47 ? ".png" : null;
        }

        /// <summary>
        /// An image beside the sources: one named cover or folder if there is one, else the
        /// largest. Releases commonly ship their art this way and nowhere else.
        /// </summary>
        internal static string? FindFolderCover(Audiobook audiobook, IReadOnlyList<ConversionSource> originals)
        {
            var folder = originals.Count > 0 ? Path.GetDirectoryName(originals[0].FullPath) : audiobook.BasePath;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return null;
            }

            var images = Directory.EnumerateFiles(folder)
                .Where(CachedCoverArt.IsEmbeddableImage)
                .ToList();

            return images.FirstOrDefault(image => PreferredFolderCoverNames.Contains(
                       Path.GetFileNameWithoutExtension(image), StringComparer.OrdinalIgnoreCase))
                ?? images.OrderByDescending(image => new FileInfo(image).Length).FirstOrDefault();
        }

        private void DeleteScratchCover(CoverForEncode? cover)
        {
            if (cover is { IsScratch: true })
            {
                TryDeleteScratch(cover.Path);
            }
        }

        internal sealed record CoverForEncode(string Path, bool IsScratch);
    }
}
