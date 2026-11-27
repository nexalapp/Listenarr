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
using Listenarr.Application.Downloads.Import;
using Listenarr.Application.FoundBooks.Contracts;
using Listenarr.Domain.FoundBooks;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Downloads.Import
{
    /// <summary>
    /// The download path never asked whether what arrived was the whole book, so a pack
    /// holding a series imported as one book and a release missing its last part imported
    /// as a complete one. These assert the gate refuses before any file moves - the whole
    /// point is that the files stay in the download directory, where the found-on-disk
    /// scanner can cluster them into the books they really are.
    /// </summary>
    [Trait("Name", "DownloadImportCompletenessGateTests")]
    [Trait("Category", "DownloadProcessingJob")]
    public sealed class DownloadImportCompletenessGateTests : BaseTests
    {
        public override async Task InitializeAsync()
        {
            Init();
            await AddAuthorizedRootAsync(FileService.GetTempPath());
        }

        [Fact]
        public async Task Overrun_IsRefusedAndLeavesEveryFileWhereItWas()
        {
            // Twenty Reacher novels in one folder against a record that runs eleven hours.
            var (service, audiobook, sources, probe) = await ArrangeAsync(
                runtimeMinutes: 11 * 60,
                perFileSeconds: 11 * 3600.0,
                fileCount: 3);

            var results = await service.ImportDownloadFilesAsync(audiobook, sources);

            AssertRefused(results, FoundBookCompleteness.Overrun);
            Assert.All(sources, source => Assert.True(File.Exists(source)));
            Assert.Equal(sources.Count, probe.Probed.Count);
        }

        [Fact]
        public async Task ShortOfTheRuntime_IsRefused()
        {
            // Harbinger: 2.5 hours on disk against a record that runs 10.
            var (service, audiobook, sources, _) = await ArrangeAsync(
                runtimeMinutes: 616,
                perFileSeconds: 150 * 60.0,
                fileCount: 1);

            var results = await service.ImportDownloadFilesAsync(audiobook, sources);

            AssertRefused(results, FoundBookCompleteness.Incomplete);
            Assert.All(sources, source => Assert.True(File.Exists(source)));
        }

        [Fact]
        public async Task UnreadableFile_IsRefused()
        {
            // Cyclops file 009: 27 MB of MPEG-4 video misnamed .mp3.
            var (service, audiobook, sources, _) = await ArrangeAsync(
                runtimeMinutes: 964,
                perFileSeconds: 964 * 60.0,
                fileCount: 1,
                probeFails: true);

            var results = await service.ImportDownloadFilesAsync(audiobook, sources);

            AssertRefused(results, FoundBookCompleteness.Corrupt);
            Assert.All(sources, source => Assert.True(File.Exists(source)));
        }

        [Fact]
        public async Task AudioMatchingTheRuntime_Imports()
        {
            var (service, audiobook, sources, _) = await ArrangeAsync(
                runtimeMinutes: 600,
                perFileSeconds: 600 * 60.0,
                fileCount: 1);

            var results = await service.ImportDownloadFilesAsync(audiobook, sources);

            Assert.All(results, result => Assert.True(result.Success));
        }

        [Fact]
        public async Task NoRuntimeAndNothingDeclared_Imports()
        {
            // Unknown is not a refusal: parts numbered from one with no total anywhere and
            // no runtime on the record is no evidence either way, and refusing on none of
            // it would strand ordinary imports.
            var (service, audiobook, sources, _) = await ArrangeAsync(
                runtimeMinutes: null,
                perFileSeconds: 30 * 60.0,
                fileCount: 3,
                declareTotal: false);

            var results = await service.ImportDownloadFilesAsync(audiobook, sources);

            Assert.All(results, result => Assert.True(result.Success));
        }

        [Fact]
        public async Task GateOff_ImportsTheOverrunAnyway()
        {
            var (service, audiobook, sources, probe) = await ArrangeAsync(
                runtimeMinutes: 11 * 60,
                perFileSeconds: 11 * 3600.0,
                fileCount: 3,
                gate: false);

            var results = await service.ImportDownloadFilesAsync(audiobook, sources);

            Assert.All(results, result => Assert.True(result.Success));
            Assert.Empty(probe.Probed);
        }

        [Fact]
        public async Task ProbeThatThrows_DoesNotFailTheImport()
        {
            // A broken ffprobe is not evidence the release is wrong, and must not read as
            // a bad download.
            var (service, audiobook, sources, _) = await ArrangeAsync(
                runtimeMinutes: 600,
                perFileSeconds: 600 * 60.0,
                fileCount: 1,
                probeThrows: true);

            var results = await service.ImportDownloadFilesAsync(audiobook, sources);

            Assert.All(results, result => Assert.True(result.Success));
        }

        private static void AssertRefused(
            IReadOnlyCollection<ImportResult> results,
            FoundBookCompleteness expected)
        {
            var refusal = Assert.Single(results);
            Assert.False(refusal.Success);
            Assert.Contains(expected.ToString(), refusal.Message);
            Assert.Contains("download directory", refusal.Message);
            // Nothing was planned, so nothing can have been published.
            Assert.Null(refusal.FinalPath);
        }

        private async Task<(DownloadImportService Service, Audiobook Audiobook, List<string> Sources, FakeProbe Probe)>
            ArrangeAsync(
                int? runtimeMinutes,
                double perFileSeconds,
                int fileCount,
                bool gate = true,
                bool probeFails = false,
                bool probeThrows = false,
                bool declareTotal = true)
        {
            var basePath = FileService.GetTempDirectory($"gate-{Guid.NewGuid():N}");
            var settings = new ApplicationSettingsBuilder()
                .WithMetadataProcessing()
                .WithMoveFileOnCompleted()
                .WithFileNamingPattern("{Title}")
                .WithMultiFileNamingPattern("{Title} {Chapter}")
                .Build();
            settings.GateImportOnCompleteness = gate;
            await _applicationSettingsRepository.SaveAsync(settings);

            var builder = new AudiobookBuilder()
                .WithTitle("Gated Book")
                .WithAuthor("Author")
                .WithBasePath(basePath);
            var audiobook = await _audiobookRepository.AddAsync(builder.Build());
            audiobook.Runtime = runtimeMinutes;
            await _audiobookRepository.UpdateAsync(audiobook);

            var probe = new FakeProbe { Throws = probeThrows };
            var sources = new List<string>();
            for (var index = 1; index <= fileCount; index++)
            {
                var name = declareTotal
                    ? $"gated-{index:00} of {fileCount:00}.mp3"
                    : $"gated-part-{index:00}.mp3";
                var source = await FileService.GetTempFileAsync(name);
                sources.Add(source);
                probe.Snapshots[source] = probeFails
                    ? FailedSnapshot()
                    : Snapshot(perFileSeconds);
            }

            var service = ActivatorUtilities.CreateInstance<DownloadImportService>(_provider, probe);
            return (service, audiobook, sources, probe);
        }

        private static FoundBookProbeSnapshot Snapshot(double seconds) =>
            new(true, null, seconds, 1, null, null, null, null, null, null, null, null, null, null, null, null);

        private static FoundBookProbeSnapshot FailedSnapshot() =>
            new(false, "could not read", 0, 0, null, null, null, null, null, null, null, null, null, null, null, null);

        private sealed class FakeProbe : IFoundBookProbe
        {
            public Dictionary<string, FoundBookProbeSnapshot> Snapshots { get; } = new(StringComparer.Ordinal);
            public List<string> Probed { get; } = [];
            public bool Throws { get; init; }

            public Task<FoundBookProbeSnapshot> ProbeAsync(
                string path,
                CancellationToken cancellationToken = default)
            {
                if (Throws)
                {
                    throw new InvalidOperationException("ffprobe is not on the path");
                }

                lock (Probed)
                {
                    Probed.Add(path);
                }

                return Task.FromResult(Snapshots.TryGetValue(path, out var snapshot)
                    ? snapshot
                    : FailedSnapshot());
            }
        }
    }
}
