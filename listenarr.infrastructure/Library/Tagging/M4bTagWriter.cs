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
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Library.Tagging
{
    /// <summary>
    /// Writes metadata atoms into a copy of an M4B, then proves the copy carries them.
    ///
    /// <para>
    /// The copy is a byte copy and the edit touches only the metadata box, so the audio,
    /// the chapter tracks and the cover art are preserved by construction rather than by
    /// being carefully re-mapped. That is why this does not remux: ffmpeg's mov muxer
    /// will write arbitrary freeform atoms (<c>-movflags +use_metadata_tags</c>) or
    /// cover art, but not both — verified against ffmpeg 6.1, where turning the flag on
    /// drops the attached picture without a word. The library's files need both.
    /// </para>
    /// <para>
    /// Only the tags the plan asks for are set. Everything else the file carries is left
    /// exactly as it is, which is both safer than rewriting it and the reason a second
    /// run is free: when the plan has nothing to write, nothing is opened at all.
    /// </para>
    /// </summary>
    public sealed class M4bTagWriter(
        FfprobeTagReader reader,
        ILogger<M4bTagWriter> logger) : IAudiobookTagWriter
    {
        /// <summary>
        /// How far the written file's duration may sit from the original's. Nothing here
        /// re-encodes, so this is slack for a container rounding a duration rather than a
        /// tolerance for drift: anything larger means the file was damaged.
        /// </summary>
        private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(1);

        /// <summary>
        /// The MP4 atoms ffmpeg's demuxer reports under these names.
        ///
        /// A key in this table is written as its standard atom, which is what a player
        /// reads; anything else becomes an iTunes freeform atom named after the key,
        /// which is how <c>SERIES</c>, <c>ASIN</c> and their neighbours already exist in
        /// this library's files. Both round-trip through ffprobe under the same name,
        /// which is what makes verification possible either way.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, TagLib.ReadOnlyByteVector> StandardAtoms =
            new Dictionary<string, TagLib.ReadOnlyByteVector>(StringComparer.OrdinalIgnoreCase)
            {
                [TagCatalog.Title] = Atom("©nam"),
                [TagCatalog.Album] = Atom("©alb"),
                [TagCatalog.Artist] = Atom("©ART"),
                [TagCatalog.AlbumArtist] = Atom("aART"),
                [TagCatalog.Composer] = Atom("©wrt"),
                [TagCatalog.Genre] = Atom("©gen"),
                [TagCatalog.Date] = Atom("©day"),
                [TagCatalog.Comment] = Atom("©cmt"),
                [TagCatalog.Copyright] = Atom("cprt"),
                [TagCatalog.SortAlbum] = Atom("soal"),
                // The one that matters. Plex populates an album summary from desc and
                // from nothing else.
                [TagCatalog.Description] = Atom("desc")
            };

        /// <summary>
        /// An MP4 atom name as its four raw bytes.
        ///
        /// Built byte by byte rather than from a string: an atom name is exactly four
        /// bytes, and the copyright sign that begins half of them is the single byte
        /// 0xA9. Letting it be encoded as text turns it into the two bytes of UTF-8 'Â©'
        /// and produces a five-byte name that names nothing.
        /// </summary>
        private static TagLib.ReadOnlyByteVector Atom(string name)
        {
            var bytes = new byte[name.Length];
            for (var i = 0; i < name.Length; i++)
            {
                bytes[i] = (byte)name[i];
            }

            return new TagLib.ReadOnlyByteVector(bytes);
        }

        /// <summary>The mean string iTunes freeform atoms are namespaced under.</summary>
        private const string FreeformMean = "com.apple.iTunes";

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            // Only the verifier is an external binary. Writing needs nothing installed,
            // but a write that cannot be read back is not one worth starting.
            reader.IsAvailableAsync();

        public async Task<AudiobookFileTags> ReadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            var probe = await reader.ProbeAsync(filePath, cancellationToken);
            return probe.Tags
                ?? throw new FfmpegException(
                    $"Could not read the tags of {LogRedaction.SanitizeFilePath(filePath)}: {probe.Error}");
        }

        public async Task<TagWriteResult> WriteAsync(
            TagWriteRequest request,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (!await IsAvailableAsync(cancellationToken))
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.WriterUnavailable,
                    "ffprobe is unavailable, so a written file could not be checked. Nothing has been changed.");
            }

            if (!File.Exists(request.SourcePath))
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.SourceUnreadable,
                    $"The file is missing: {LogRedaction.SanitizeFilePath(request.SourcePath)}");
            }

            if (request.Tags.Count == 0)
            {
                // Nothing to set. Copying a book-sized file to change nothing is pure
                // cost, and publishing the result would replace a file for no reason.
                return TagWriteResult.Ok(0);
            }

            try
            {
                progress?.Report(0);
                await CopyAsync(request.SourcePath, request.ScratchOutputPath, progress, cancellationToken);

                ApplyTags(request.ScratchOutputPath, request.Tags, request.CoverArtPath);
                progress?.Report(1);

                return await VerifyAsync(request, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryDelete(request.ScratchOutputPath);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "A tag write could not write its working file");
                TryDelete(request.ScratchOutputPath);
                return TagWriteResult.Fail(
                    TagWriteFailureKind.Transient,
                    $"Could not write the tagged copy: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogError(ex, "Unexpected failure writing tags");
                TryDelete(request.ScratchOutputPath);
                return TagWriteResult.Fail(TagWriteFailureKind.Unknown, ex.Message);
            }
        }

        public async Task<TagWriteResult> ApplyAsync(
            string path,
            IReadOnlyDictionary<string, string> tags,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(tags);

            if (tags.Count == 0)
            {
                return TagWriteResult.Ok(0);
            }

            // Read first: the verification compares the result against what the file
            // carried going in, so a lost chapter or cover is detected even though this
            // never made a copy to fall back to.
            var before = await reader.ProbeAsync(path, cancellationToken);
            if (before.Tags == null)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.SourceUnreadable,
                    $"The file could not be read before tagging: {before.Error}");
            }

            try
            {
                ApplyTags(path, tags, coverArtPath: null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                logger.LogWarning(ex, "Could not apply tags to a working file");
                return TagWriteResult.Fail(TagWriteFailureKind.WriteFailed, ex.Message);
            }

            return await VerifyAsync(
                new TagWriteRequest(path, path, tags, before.Tags),
                cancellationToken);
        }

        /// <summary>
        /// Copy the book to the scratch path, reporting progress as it goes.
        ///
        /// A plain <c>File.Copy</c> would be simpler but silent, and this is minutes of
        /// work for a real book over a share — an operator watching a bar that does not
        /// move assumes it has hung.
        /// </summary>
        private static async Task CopyAsync(
            string sourcePath,
            string destinationPath,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                useAsync: true);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true);

            var total = source.Length;
            var buffer = new byte[1024 * 1024];
            long copied = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;

                if (total > 0)
                {
                    // Nine tenths: the atom edit and the verification that follow are the
                    // last of the work, and a bar that sat at 100% through them would be
                    // lying about being finished.
                    progress?.Report(0.9 * copied / total);
                }
            }
        }

        /// <summary>
        /// Set the requested atoms on a file, leaving every other atom untouched.
        ///
        /// Setting replaces rather than appends, which is what collapses the duplicate
        /// <c>SERIES</c> atoms several files in this library already carry and what stops
        /// a second run from adding a third.
        /// </summary>
        private void ApplyTags(
            string path,
            IReadOnlyDictionary<string, string> tags,
            string? coverArtPath)
        {
            using var file = TagLib.File.Create(path);
            if (file.GetTag(TagLib.TagTypes.Apple, create: true) is not TagLib.Mpeg4.AppleTag apple)
            {
                throw new InvalidOperationException(
                    "This file does not carry an MP4 metadata box, so tags cannot be written into it.");
            }

            foreach (var (key, value) in tags)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (StandardAtoms.TryGetValue(key, out var atom))
                {
                    apple.SetText(atom, value);
                    continue;
                }

                // Anything the container has no standard atom for. ffprobe reads a
                // freeform atom back under its name string, so SERIES written here is
                // SERIES when it is read.
                apple.SetDashBox(FreeformMean, key, value);
            }

            // Only ever called for a file that carries none: replacing existing art is
            // never automatic, because the file's own may be the better one and nothing
            // here can tell.
            if (!string.IsNullOrWhiteSpace(coverArtPath) && File.Exists(coverArtPath))
            {
                apple.Pictures =
                [
                    new TagLib.Picture(coverArtPath) { Type = TagLib.PictureType.FrontCover }
                ];
            }

            file.Save();

            logger.LogDebug(
                "Set {Count} tag(s) on {Path}",
                tags.Count,
                LogRedaction.SanitizeFilePath(path));
        }

        /// <summary>
        /// Prove the written copy carries what was asked for and lost nothing.
        ///
        /// Every one of these has a silent failure behind it: an atom the container could
        /// not represent is simply absent, and a metadata edit that shifted the media data
        /// without fixing the chunk offsets leaves a file that still parses and no longer
        /// plays. Neither shows up as an error from the write itself.
        /// </summary>
        private async Task<TagWriteResult> VerifyAsync(
            TagWriteRequest request,
            CancellationToken cancellationToken)
        {
            var output = request.ScratchOutputPath;
            if (!File.Exists(output))
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    "The tag write reported success but produced no file.");
            }

            if (new FileInfo(output).Length == 0)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    "The tag write reported success but the file it produced is empty.");
            }

            var probe = await reader.ProbeAsync(output, cancellationToken);
            if (probe.Tags == null)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    $"The tagged copy could not be read back: {probe.Error} The original file has been left alone.");
            }

            var written = probe.Tags;

            if (written.ChapterCount != request.Existing.ChapterCount)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    $"The tagged copy has {written.ChapterCount} chapter(s) but the original has {request.Existing.ChapterCount}. The original file has been left alone.");
            }

            if (request.Existing.HasCoverArt && !written.HasCoverArt)
            {
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    "The tagged copy lost its cover art. The original file has been left alone.");
            }

            if (request.Existing.Duration > TimeSpan.Zero)
            {
                var drift = (written.Duration - request.Existing.Duration).Duration();
                if (drift > DurationTolerance)
                {
                    return TagWriteResult.Fail(
                        TagWriteFailureKind.OutputRejected,
                        $"The tagged copy runs {written.Duration:hh\\:mm\\:ss} but the original runs {request.Existing.Duration:hh\\:mm\\:ss}. The original file has been left alone.");
                }
            }

            var missing = new List<string>();
            foreach (var (key, expected) in request.Tags)
            {
                if (!written.Tags.TryGetValue(key, out var actual)
                    || !TagValue.AreEquivalent(actual, expected))
                {
                    missing.Add(key);
                }
            }

            if (missing.Count > 0)
            {
                // Naming the tags matters: the usual cause is a value this container
                // cannot carry, and the operator's fix is to stop writing that one tag.
                return TagWriteResult.Fail(
                    TagWriteFailureKind.OutputRejected,
                    $"The tagged copy did not keep {missing.Count} tag(s) that were written ({string.Join(", ", missing.Take(6))}). The original file has been left alone.");
            }

            logger.LogInformation(
                "Tag write verified: {Tags} tag(s), {Chapters} chapter(s), {Duration}",
                request.Tags.Count,
                written.ChapterCount,
                written.Duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));

            return TagWriteResult.Ok(request.Tags.Count);
        }

        private void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(
                    ex,
                    "Could not remove the tag-write scratch file {Path}",
                    LogRedaction.SanitizeFilePath(path));
            }
        }
    }
}
