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
using Listenarr.Application.Audiobooks.Chapters;
using Listenarr.Domain.Audiobooks.Chapters;
using Listenarr.Infrastructure.Ffmpeg.Tagging;
using Listenarr.Infrastructure.Library.Tagging;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Tagging
{
    /// <summary>
    /// The chapter rewrite against real files: a remux that must keep everything but
    /// the chapters, and must be caught when it does not.
    /// </summary>
    [Trait("Name", "FfmpegChapterRewriterTests")]
    [Trait("Category", "Tagging")]
    public sealed class FfmpegChapterRewriterTests : BaseTests, IDisposable
    {
        private readonly string _workingDirectory = Path.Combine(
            Path.GetTempPath(),
            "listenarr-chapter-rewrite-" + Guid.NewGuid().ToString("N"));

        private string OutputPath => Path.Combine(_workingDirectory, "out.m4b");

        private static (FfmpegChapterRewriter Rewriter, M4bTagWriter Writer) Build()
        {
            var runner = new SystemProcessRunner(NullLogger<SystemProcessRunner>.Instance);
            var reader = new FfprobeTagReader(M4bFixtures.PathResolvedFfmpeg(), runner);
            var writer = new M4bTagWriter(reader, NullLogger<M4bTagWriter>.Instance);
            var rewriter = new FfmpegChapterRewriter(
                M4bFixtures.PathResolvedFfmpeg(),
                runner,
                writer,
                reader,
                NullLogger<FfmpegChapterRewriter>.Instance);
            return (rewriter, writer);
        }

        private static ChapterPlan PlanOf(params (string Title, double Start, double End)[] chapters) =>
            new(
                ChapterSource.Played,
                chapters.Select(c => new EmbeddedChapter(c.Title, TimeSpan.FromSeconds(c.Start), TimeSpan.FromSeconds(c.End))).ToList(),
                Partial: false,
                "test");

        [EncoderFact]
        public async Task RewriteAsync_WritesThePlanAndKeepsEverythingElse()
        {
            // A cover, freeform tags and a description: the things a remux is known to drop.
            var source = await M4bFixtures.WriteBookAsync(
                _workingDirectory,
                seconds: 8,
                chapters: 4,
                coverArt: true,
                extraMetadataDocument: "title=Drive\ndescription=A short story.\n");
            var (rewriter, writer) = Build();
            var applied = await writer.ApplyAsync(source, new Dictionary<string, string> { ["SERIES"] = "The Expanse", ["ASIN"] = "B00TEST" });
            Assert.True(applied.Success, applied.Message);
            var existing = await writer.ReadAsync(source);
            Assert.Equal(4, existing.ChapterCount);

            var result = await rewriter.RewriteAsync(new ChapterRewriteRequest(
                source,
                OutputPath,
                PlanOf(("One", 0, 3), ("Two", 3, 8)),
                existing));

            Assert.True(result.Success, result.Message);
            var written = await writer.ReadAsync(OutputPath);
            Assert.Equal(2, written.ChapterCount);
            Assert.Equal(["One", "Two"], written.Chapters!.Select(c => c.Title));
            Assert.True(written.HasCoverArt);
            Assert.Equal("Drive", written.Tags["title"]);
            Assert.Equal("A short story.", written.Tags["description"]);
            Assert.Equal("The Expanse", written.Tags["SERIES"]);
            Assert.Equal("B00TEST", written.Tags["ASIN"]);
            Assert.InRange((written.Duration - existing.Duration).Duration().TotalSeconds, 0, 1);

            // Both chapter forms, and the atom parses: this is what the whole repair is for.
            var atoms = ChapterAtomInspector.Inspect(OutputPath);
            Assert.NotNull(atoms);
            Assert.True(atoms.NeroAtomValid);
            Assert.Equal(2, atoms.NeroChapterCount);
            Assert.True(atoms.HasChapterTrack);
        }

        [EncoderFact]
        public async Task RewriteAsync_RepairsAShiftedAtom()
        {
            var source = await M4bFixtures.WriteBookAsync(_workingDirectory, seconds: 8, chapters: 4);
            M4bFixtures.ShiftChapterAtom(source);
            var (rewriter, writer) = Build();
            var existing = await writer.ReadAsync(source);
            Assert.NotNull(existing.Atoms?.NeroAtomError);

            // The QuickTime track survives the bug, so the played list is the plan.
            var (plan, rejection) = ChapterPlanner.Plan(existing.Chapters, existing.Duration, null, null);
            Assert.Null(rejection);

            var result = await rewriter.RewriteAsync(new ChapterRewriteRequest(source, OutputPath, plan!, existing));

            Assert.True(result.Success, result.Message);
            var atoms = ChapterAtomInspector.Inspect(OutputPath);
            Assert.True(atoms!.NeroAtomValid);
            Assert.Equal(4, atoms.NeroChapterCount);
        }

        [EncoderFact]
        public async Task RewriteAsync_RefusesAnEmptyPlan()
        {
            var source = await M4bFixtures.WriteBookAsync(_workingDirectory, seconds: 4);
            var (rewriter, writer) = Build();
            var existing = await writer.ReadAsync(source);

            var result = await rewriter.RewriteAsync(new ChapterRewriteRequest(
                source,
                OutputPath,
                new ChapterPlan(ChapterSource.Played, [], false, ""),
                existing));

            Assert.False(result.Success);
            Assert.False(File.Exists(OutputPath));
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_workingDirectory))
                {
                    Directory.Delete(_workingDirectory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
