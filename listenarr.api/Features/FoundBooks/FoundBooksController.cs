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
        IFoundBookDecisionService decisions) : ControllerBase
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
        bool SharesFolder)
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
            sharesFolder);
    }
}
