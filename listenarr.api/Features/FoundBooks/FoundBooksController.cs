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
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Application.FoundBooks.Services;
using Listenarr.Domain.Common;
using Listenarr.Domain.FoundBooks;
using Microsoft.AspNetCore.Mvc;

namespace Listenarr.Api.Features.FoundBooks
{
    /// <summary>
    /// Books found in the watch folders that the library does not have: listing them,
    /// and the operator's decisions about each one.
    /// </summary>
    /// <remarks>
    /// Adding a book is client-driven, as Library Import is: the UI matches the book,
    /// adds it, and runs the manual import, bracketed by <c>begin-import</c> and
    /// <c>finish-import</c> here so a scan in the meantime leaves the row alone and the
    /// leftovers are cleared once the audio has moved.
    /// </remarks>
    [ApiController]
    [Route("api/v{version:apiVersion}/found")]
    [Tags("Library")]
    public sealed class FoundBooksController(
        IFoundBookRepository repository,
        IFoundBookScanProcessor scanProcessor,
        IFoundBookWatchFolderResolver watchFolderResolver,
        IFoundBookDecisionService decisions,
        IFoundBookMatchService matches,
        IFoundBookImportService imports,
        IFileSystem fileSystem) : ControllerBase
    {
        [HttpGet]
        public async Task<ActionResult<FoundBooksResponse>> List(
            [FromQuery] string? state = null,
            CancellationToken cancellationToken = default)
        {
            FoundBookState? filter = null;
            if (!string.IsNullOrWhiteSpace(state))
            {
                if (!Enum.TryParse<FoundBookState>(state, ignoreCase: true, out var parsed))
                {
                    return BadRequest(new { message = $"Unknown state '{state}'." });
                }

                filter = parsed;
            }

            var rows = await repository.GetAllAsync(cancellationToken);
            var shared = FoundBookFolderSharing.SharedRows(rows);
            var items = rows
                .Where(row => filter == null || row.State == filter)
                .Select(row => FoundBookDto.From(row, shared.Contains(row.Id)))
                .ToList();

            return Ok(new FoundBooksResponse(
                items,
                rows.Count(r => r.State == FoundBookState.Pending),
                rows.Count(r => r.State == FoundBookState.Blocked),
                scanProcessor.IsScanning,
                scanProcessor.LastScanCompletedAt));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<FoundBookDto>> Get(int id, CancellationToken cancellationToken = default)
        {
            var row = await repository.GetAsync(id, cancellationToken);
            return row == null ? NotFound() : Ok(FoundBookDto.From(row));
        }

        /// <summary>The folders a scan will look at, and why any configured one is skipped.</summary>
        [HttpGet("watch-folders")]
        public async Task<ActionResult<FoundBookWatchFolders>> WatchFolders(CancellationToken cancellationToken = default) =>
            Ok(await watchFolderResolver.ResolveAsync(cancellationToken));

        /// <summary>Start a scan now. 202 when started; 409 when one is already running.</summary>
        [HttpPost("scan")]
        public ActionResult<object> Scan() =>
            scanProcessor.TriggerScan()
                ? Accepted(new { scanning = true })
                : Conflict(new { scanning = true, message = "A scan is already running." });

        [HttpPost("{id:int}/ignore")]
        public async Task<ActionResult<FoundBookDecisionResponse>> Ignore(int id, CancellationToken cancellationToken = default) =>
            Respond(await decisions.IgnoreAsync(id, cancellationToken));

        [HttpPost("{id:int}/restore")]
        public async Task<ActionResult<FoundBookDecisionResponse>> Restore(int id, CancellationToken cancellationToken = default) =>
            Respond(await decisions.RestoreAsync(id, cancellationToken));

        /// <summary>
        /// Add the book to the library in one call: mark the row, add or reuse the
        /// record, move the files, finish the row. A failure at any step puts the row
        /// back here, server-side, so nothing depends on a follow-up request. 200 with
        /// the outcome either way; the row in the response is its state now.
        /// </summary>
        [HttpPost("{id:int}/import")]
        public async Task<ActionResult<FoundBookImportResponse>> Import(
            int id,
            [FromBody] ImportRequest? request,
            CancellationToken cancellationToken = default)
        {
            request ??= new ImportRequest(null, null, true, false);
            var result = await imports.ImportAsync(
                id,
                new FoundBookManualImportRequest(request.Asin, request.RootFolderPath, request.Monitored, request.SeparateBook),
                cancellationToken);

            if (result.Failure == FoundBookImportFailure.NotFound)
            {
                return NotFound(new { message = result.Error });
            }

            var book = result.Book ?? await repository.GetAsync(id, cancellationToken);
            return Ok(new FoundBookImportResponse(
                result.Success,
                result.AudiobookId,
                result.Failure == FoundBookImportFailure.None ? null : result.Failure.ToString(),
                result.Error,
                result.Skipped,
                book == null ? null : FoundBookDto.From(book)));
        }

        [HttpPost("{id:int}/begin-import")]
        public async Task<ActionResult<FoundBookDecisionResponse>> BeginImport(int id, CancellationToken cancellationToken = default) =>
            Respond(await decisions.BeginImportAsync(id, cancellationToken));

        [HttpPost("{id:int}/abort-import")]
        public async Task<ActionResult<FoundBookDecisionResponse>> AbortImport(int id, CancellationToken cancellationToken = default) =>
            Respond(await decisions.AbortImportAsync(id, cancellationToken));

        [HttpPost("{id:int}/finish-import")]
        public async Task<ActionResult<FoundBookDecisionResponse>> FinishImport(
            int id,
            [FromBody] FinishImportRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null || request.AudiobookId <= 0)
            {
                return BadRequest(new { message = "An audiobook id is required." });
            }

            return Respond(await decisions.FinishImportAsync(id, request.AudiobookId, autoAdded: false, cancellationToken));
        }

        /// <summary>Remember the catalogue match for a row so it survives a refresh and a scan.</summary>
        [HttpPut("{id:int}/match")]
        public async Task<ActionResult<FoundBookDto>> SetMatch(
            int id,
            [FromBody] SetMatchRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null || (string.IsNullOrWhiteSpace(request.Asin) && string.IsNullOrWhiteSpace(request.Title)))
            {
                return BadRequest(new { message = "A match needs at least an ASIN or a title." });
            }

            var row = await matches.SetMatchAsync(
                id,
                new FoundBookMatchChoice(request.Asin, request.Title, request.Author, request.Source, request.ImageUrl, request.Confidence),
                cancellationToken);
            return row == null ? NotFound() : Ok(FoundBookDto.From(row));
        }

        [HttpDelete("{id:int}/match")]
        public async Task<ActionResult<FoundBookDto>> ClearMatch(int id, CancellationToken cancellationToken = default)
        {
            var row = await matches.SetMatchAsync(id, null, cancellationToken);
            return row == null ? NotFound() : Ok(FoundBookDto.From(row));
        }

        /// <summary>
        /// Hear the opening credits of the book's first file and read title, author and
        /// narrator out of them. 409 when transcription is off or the model is still
        /// downloading.
        /// </summary>
        [HttpPost("{id:int}/listen")]
        public async Task<ActionResult<FoundBookListenResponse>> Listen(int id, CancellationToken cancellationToken = default)
        {
            try
            {
                var heard = await matches.ListenAsync(id, cancellationToken);
                if (heard == null)
                {
                    return NotFound();
                }

                var row = await repository.GetAsync(id, cancellationToken);
                return Ok(new FoundBookListenResponse(
                    heard.Credits.Title,
                    heard.Credits.Author,
                    heard.Credits.Narrator,
                    heard.Transcript,
                    row == null ? null : FoundBookDto.From(row)));
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Stream one of the row's audio files for a preview. The file comes from the
        /// row, never from the request, so nothing outside the watch folder can be
        /// asked for.
        /// </summary>
        [HttpGet("{id:int}/audio")]
        public async Task<IActionResult> Audio(int id, [FromQuery] int index = 0, CancellationToken cancellationToken = default)
        {
            var row = await repository.GetAsync(id, cancellationToken);
            if (row == null)
            {
                return NotFound();
            }

            var audio = FoundBookFilesJson.Deserialize(row.FilesJson).Where(f => f.IsAudio).ToList();
            if (index < 0 || index >= audio.Count)
            {
                return NotFound(new { message = "No such file on this row." });
            }

            var path = audio[index].Path;
            if (!fileSystem.TryValidateMutationTarget(path, [row.WatchFolder], out var safePath, out _)
                || !FileUtils.IsAudioFile(safePath)
                || !fileSystem.FileExists(safePath))
            {
                return NotFound(new { message = "File not found" });
            }

            return new PhysicalFileResult(safePath, AudioContentTypes.ForFile(safePath))
            {
                EnableRangeProcessing = true
            };
        }

        /// <summary>Delete the book's files. The UI confirms first; nothing here asks again.</summary>
        [HttpPost("{id:int}/discard")]
        public async Task<ActionResult<FoundBookDecisionResponse>> Discard(int id, CancellationToken cancellationToken = default) =>
            Respond(await decisions.DiscardAsync(id, cancellationToken));

        private ActionResult<FoundBookDecisionResponse> Respond(FoundBookDecisionResult result) => result.Failure switch
        {
            FoundBookDecisionFailure.None => Ok(new FoundBookDecisionResponse(FoundBookDto.From(result.Book!), result.Skipped)),
            FoundBookDecisionFailure.NotFound => NotFound(new { message = result.Error }),
            FoundBookDecisionFailure.WrongState or FoundBookDecisionFailure.FilesRemain => Conflict(new { message = result.Error }),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = result.Error })
        };
    }

    public sealed record FinishImportRequest(int AudiobookId);

    public sealed record ImportRequest(string? Asin, string? RootFolderPath, bool Monitored = true, bool SeparateBook = false);

    public sealed record FoundBookImportResponse(
        bool Success,
        int? AudiobookId,
        string? Failure,
        string? Error,
        IReadOnlyList<string> Skipped,
        FoundBookDto? Book);

    public sealed record SetMatchRequest(
        string? Asin,
        string? Title,
        string? Author,
        string? Source,
        string? ImageUrl,
        double? Confidence);

    public sealed record FoundBookListenResponse(
        string? Title,
        string? Author,
        string? Narrator,
        string Transcript,
        FoundBookDto? Book);

    public sealed record FoundBookDecisionResponse(FoundBookDto Book, IReadOnlyList<string> Skipped);

    public sealed record FoundBooksResponse(
        IReadOnlyList<FoundBookDto> Items,
        int Pending,
        int Blocked,
        bool Scanning,
        DateTime? LastScanCompletedAt);

    public sealed record FoundBookFileDto(string Path, long Length, bool IsAudio, double? DurationSeconds, string? Error);

    public sealed record FoundBookDto(
        int Id,
        string WatchFolder,
        string BookFolder,
        IReadOnlyList<FoundBookFileDto> Files,
        int AudioFileCount,
        long TotalBytes,
        double TotalDurationSeconds,
        string? Format,
        string? Title,
        string? Author,
        string? Series,
        string? SeriesPosition,
        string? Narrator,
        string? Year,
        string? Asin,
        string Completeness,
        string? CompletenessReason,
        string LibraryStatus,
        int? MatchedAudiobookId,
        string State,
        string BlockedKind,
        string? BlockedReason,
        DateTime FirstSeenAt,
        DateTime LastSeenAt,
        bool AutoAdded,
        bool SharesFolder,
        string? MatchAsin,
        string? MatchTitle,
        string? MatchAuthor,
        string? MatchSource,
        string? MatchImageUrl,
        double? MatchConfidence,
        string? HeardTitle,
        string? HeardAuthor,
        string? HeardNarrator,
        DateTime? HeardAt)
    {
        public static FoundBookDto From(FoundBook row, bool sharesFolder = false) => new(
            row.Id,
            row.WatchFolder,
            row.BookFolder,
            FoundBookFilesJson.Deserialize(row.FilesJson)
                .Select(f => new FoundBookFileDto(
                    f.Path,
                    f.Length,
                    f.IsAudio,
                    f.Probe?.Succeeded == true ? f.Probe.DurationSeconds : null,
                    f.Probe?.Error))
                .ToList(),
            row.AudioFileCount,
            row.TotalBytes,
            row.TotalDurationSeconds,
            row.Format,
            row.DetectedTitle,
            row.DetectedAuthor,
            row.DetectedSeries,
            row.DetectedSeriesPosition,
            row.DetectedNarrator,
            row.DetectedYear,
            row.DetectedAsin,
            row.Completeness.ToString(),
            row.CompletenessReason,
            row.LibraryStatus.ToString(),
            row.MatchedAudiobookId,
            row.State.ToString(),
            row.BlockedKind.ToString(),
            row.BlockedReason,
            row.FirstSeenAt,
            row.LastSeenAt,
            row.AutoAdded,
            sharesFolder,
            row.MatchAsin,
            row.MatchTitle,
            row.MatchAuthor,
            row.MatchSource,
            row.MatchImageUrl,
            row.MatchConfidence,
            row.HeardTitle,
            row.HeardAuthor,
            row.HeardNarrator,
            row.HeardAt);
    }
}
