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
using Listenarr.Api.Dtos.ManualImport;
using Listenarr.Application.Common.Exceptions;
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Application.FoundBooks.Services;
using Listenarr.Domain.FoundBooks;

namespace Listenarr.Api.Features.FoundBooks
{
    /// <summary>
    /// Runs the manual-import workflow for a found book: the same request the Found
    /// tab's Add sends, built here from the row instead of from the browser.
    /// </summary>
    public sealed class FoundBookManualImportAdapter(ManualImportWorkflow workflow, IFileSystem fileSystem) : IFoundBookImporter
    {
        public bool IsAvailable => true;

        public async Task<FoundBookImportOutcome> ImportAsync(
            FoundBook row,
            int audiobookId,
            bool includeCompanions,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(row);
            var all = FoundBookFilesJson.Deserialize(row.FilesJson).Where(f => f.IsAudio).ToList();
            if (all.Count == 0)
            {
                return new FoundBookImportOutcome(false, 0, 0, "The row has no audio files.");
            }

            // A retry after a failure that came late - the database refusing to record
            // the finish, say - finds some or all of the audio already moved. Only what
            // is still here is asked for; the workflow reports a missing source as a
            // failure, and asking for an empty folder throws. With nothing left to move
            // the import is done and only the finish remains.
            var audio = all.Where(f => fileSystem.FileExists(f.Path)).ToList();
            if (audio.Count == 0)
            {
                return new FoundBookImportOutcome(true, 0, all.Count, null);
            }

            var request = new ManualImportRequestDto
            {
                Path = row.BookFolder,
                Mode = "interactive",
                Action = FileAction.Move,
                IncludeCompanionFiles = includeCompanions,
                CleanupEmptySourceFolders = true,
                Items = audio.Select(f => new ManualImportItemDto
                {
                    FullPath = f.Path,
                    MatchedAudiobookId = audiobookId
                }).ToList()
            };

            try
            {
                var batch = await workflow.StartAsync(request, cancellationToken);
                var failed = batch.Results.FirstOrDefault(r => !r.Success);
                var success = failed == null && batch.ImportedCount == audio.Count;
                return new FoundBookImportOutcome(
                    success,
                    batch.ImportedCount,
                    all.Count,
                    success ? null : failed?.Error ?? failed?.SkipReason ?? $"{batch.ImportedCount} of {audio.Count} files imported");
            }
            catch (ApplicationConflictException ex)
            {
                return new FoundBookImportOutcome(false, 0, audio.Count, ex.SafeDetail);
            }
            catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException)
            {
                return new FoundBookImportOutcome(false, 0, audio.Count, ex.Message);
            }
        }
    }
}
