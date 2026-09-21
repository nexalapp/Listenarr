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
using Listenarr.Api.Features.FoundBooks;
using Listenarr.Application.FoundBooks.Services;
using Listenarr.Domain.FoundBooks;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Api.Features.FoundBooks
{
    /// <summary>
    /// A retry after a late failure finds the audio already moved. The adapter must
    /// not ask the workflow for files that are gone - it reports a missing source as
    /// a failure - and with nothing left it must say the import is done.
    /// </summary>
    [Trait("Name", "FoundBookManualImportAdapterTests")]
    [Trait("Category", "FoundBooks")]
    public sealed class FoundBookManualImportAdapterTests : BaseTests
    {
        [Fact]
        public async Task EveryAudioFileAlreadyMoved_IsDoneWithoutAskingTheWorkflow()
        {
            var fileSystem = new Mock<IFileSystem>();
            fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
            var adapter = new FoundBookManualImportAdapter(_provider.GetRequiredService<ManualImportWorkflow>(), fileSystem.Object);
            var row = new FoundBook
            {
                Id = 1,
                BookFolder = "/downloads/Wool",
                FilesJson = FoundBookFilesJson.Serialize(
                [
                    new FoundBookFileEntry("/downloads/Wool/01.mp3", 1, DateTime.UtcNow, true, null),
                    new FoundBookFileEntry("/downloads/Wool/02.mp3", 1, DateTime.UtcNow, true, null)
                ])
            };

            var outcome = await adapter.ImportAsync(row, 42, includeCompanions: true);

            Assert.True(outcome.Success, outcome.Error);
            Assert.Equal(0, outcome.ImportedCount);
            Assert.Equal(2, outcome.TotalCount);
        }

        [Fact]
        public async Task NoAudioAtAll_IsARefusal()
        {
            var adapter = new FoundBookManualImportAdapter(_provider.GetRequiredService<ManualImportWorkflow>(), Mock.Of<IFileSystem>());
            var row = new FoundBook { Id = 1, BookFolder = "/downloads/Wool", FilesJson = "[]" };

            var outcome = await adapter.ImportAsync(row, 42, includeCompanions: true);

            Assert.False(outcome.Success);
            Assert.Contains("no audio", outcome.Error);
        }
    }
}
