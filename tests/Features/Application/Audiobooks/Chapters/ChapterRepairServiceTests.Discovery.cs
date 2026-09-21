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
using Listenarr.Application.Audiobooks.Transcription;
using Listenarr.Domain.Audiobooks.Chapters;

namespace Listenarr.Tests.Features.Application.Audiobooks.Chapters
{
    /// <summary>A file with no marks: pauses, the edition over them, and the narrator after each.</summary>
    public sealed partial class ChapterRepairServiceTests
    {
        private static TimeSpan Min(double minutes) => TimeSpan.FromMinutes(minutes);

        /// <summary>A 100-minute file with no marks and nothing in its atoms.</summary>
        private void GivenAnUnchapteredFile() =>
            GivenFileReads([], new ChapterAtomState(false, null, 0, false));

        /// <summary>Three-second pauses at the given minutes, and half-second breaths every minute besides.</summary>
        private void GivenPausesAt(params double[] minutes)
        {
            var pauses = minutes.Select(m => new SilenceSpan(Min(m) - TimeSpan.FromSeconds(3), Min(m))).ToList();
            pauses.AddRange(Enumerable.Range(1, 99).Where(i => !minutes.Contains(i)).Select(i => new SilenceSpan(Min(i), Min(i) + TimeSpan.FromSeconds(0.5))));
            _silences
                .Setup(s => s.DetectAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(pauses);
        }

        /// <summary>What the narrator says in the ten seconds from each minute; silence elsewhere.</summary>
        private void GivenHeardAtMinutes(IReadOnlyDictionary<double, string> announcements)
        {
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, TimeSpan start, TimeSpan _, CancellationToken _) =>
                {
                    var heard = announcements.FirstOrDefault(pair => (Min(pair.Key) - start).Duration() <= TimeSpan.FromSeconds(1));
                    return heard.Value != null ? new Transcript(heard.Value) : new Transcript("the rain kept falling");
                });
        }

        private void GivenAudnexusChapters(string asin, int count, TimeSpan each, TimeSpan runtime, TimeSpan? shift = null) =>
            _audnexus
                .Setup(a => a.LookupChaptersAsync(asin, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AudnexusChapterLookup(new AudnexusChapterResponse
                {
                    RuntimeLengthMs = (int)runtime.TotalMilliseconds,
                    Chapters = Enumerable.Range(0, count).Select(i => new AudnexusChapter
                    {
                        Title = $"Chapter {i + 1}: Title {i + 1}",
                        StartOffsetMs = (int)(each * i + (i == 0 ? TimeSpan.Zero : shift ?? TimeSpan.Zero)).TotalMilliseconds,
                        LengthMs = (int)each.TotalMilliseconds
                    }).ToList()
                }, Unavailable: false));

        [Fact]
        public async Task PlanAsync_FindsChaptersAfterThePausesWhereTheNarratorAnnouncesThem()
        {
            GivenBook();
            GivenTranscription(enabled: true);
            GivenAnUnchapteredFile();
            GivenPausesAt(12, 27, 40, 55, 71, 88);
            GivenHeardAtMinutes(new Dictionary<double, string>
            {
                [0] = "Chapter one.",
                [27] = "Chapter two. The drop.",
                [55] = "Chapter three.",
                [88] = "Chapter four."
            });

            var preview = await BuildService().PlanAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.Equal(ChapterHealth.None, file.Health);
            Assert.True(file.Repairable);
            Assert.Equal(ChapterSource.Announcements, file.Plan!.Source);
            Assert.Equal(["Chapter 1", "Chapter 2: The drop", "Chapter 3", "Chapter 4"], file.Plan.Chapters.Select(c => c.Title));
            Assert.Equal(Min(27) - SilenceCandidates.Lead, file.Plan.Chapters[1].Start);
            Assert.Equal(Duration, file.Plan.Chapters[^1].End);
            // The start of the file and every long pause were listened at; the breaths were not.
            _transcriber.Verify(
                t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), ChapterRepairService.ListenWindow, It.IsAny<CancellationToken>()),
                Times.Exactly(7));
            // Kept on the file, so the enqueue writes exactly this.
            var stored = ChapterPlanStorage.Deserialize(_book!.Files![0].ChapterPlanJson!)!.Plan;
            Assert.Equal(file.Plan.Chapters.Select(c => (c.Title, c.Start)), stored!.Chapters.Select(c => (c.Title, c.Start)));
        }

        [Fact]
        public async Task PlanAsync_LaysTheEditionOverThePausesAndListensOnlyAtItsMarks()
        {
            GivenBook(asin: "B00X");
            GivenTranscription(enabled: true);
            GivenAnUnchapteredFile();
            // The file runs twenty seconds late on the edition: every pause is 20s after an edition mark.
            var shift = TimeSpan.FromSeconds(20);
            GivenAudnexusChapters("B00X", 5, Min(20), Duration);
            var pauses = Enumerable.Range(1, 4).Select(i => new SilenceSpan(Min(20 * i) + shift - TimeSpan.FromSeconds(2), Min(20 * i) + shift)).ToList();
            pauses.AddRange([new SilenceSpan(Min(7), Min(7) + TimeSpan.FromSeconds(3)), new SilenceSpan(Min(33), Min(33) + TimeSpan.FromSeconds(3))]);
            _silences.Setup(s => s.DetectAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(pauses);
            GivenHeardAtMinutes(new Dictionary<double, string> { [20 + shift.TotalMinutes] = "Chapter two.", [60 + shift.TotalMinutes] = "Chapter four." });

            var preview = await BuildService().PlanAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.True(file.Repairable);
            Assert.Equal(ChapterSource.Audnexus, file.Plan!.Source);
            Assert.Equal(5, file.Plan.Chapters.Count);
            Assert.Equal("Chapter 2: Title 2", file.Plan.Chapters[1].Title);
            // On the file's own pause, not the edition's rounded mark.
            Assert.Equal(Min(20) + shift - SilenceCandidates.Lead, file.Plan.Chapters[1].Start);
            Assert.Contains("+0:19 offset", file.Plan.Note);
            Assert.Contains("4 of them on the file's own pauses; 2 confirmed by the narrator", file.Plan.Note);
            Assert.False(file.Plan.Partial);
            // Five edition marks were listened at; the two scene breaks were not.
            _transcriber.Verify(
                t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
                Times.Exactly(5));
        }

        [Fact]
        public async Task PlanAsync_PrefersTheNarratorWhenTheEditionsMarksAreNotWhereTheChaptersAre()
        {
            GivenBook(asin: "B00X");
            GivenTranscription(enabled: true);
            GivenAnUnchapteredFile();
            // A different edition: same runtime within tolerance, chapters that are not where this file's are.
            GivenAudnexusChapters("B00X", 4, Min(25), Duration);
            GivenPausesAt(12, 27, 40, 55, 71, 88);
            GivenHeardAtMinutes(new Dictionary<double, string>
            {
                [0] = "Chapter one.",
                [27] = "Chapter two.",
                [55] = "Chapter three.",
                [88] = "Chapter four."
            });

            var preview = await BuildService().PlanAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.Equal(ChapterSource.Announcements, file.Plan!.Source);
            Assert.Equal([TimeSpan.Zero, Min(27) - SilenceCandidates.Lead, Min(55) - SilenceCandidates.Lead, Min(88) - SilenceCandidates.Lead], file.Plan.Chapters.Select(c => c.Start));
            // Four chapters heard, four in the edition: the edition's titles on the narrator's marks.
            Assert.Equal(["Chapter 1: Title 1", "Chapter 2: Title 2", "Chapter 3: Title 3", "Chapter 4: Title 4"], file.Plan.Chapters.Select(c => c.Title));
        }

        [Fact]
        public async Task PlanAsync_OffersAnAlignedEditionWhenTheNarratorAnnouncesNothing()
        {
            GivenBook(asin: "B00X");
            GivenTranscription(enabled: true);
            GivenAnUnchapteredFile();
            GivenAudnexusChapters("B00X", 5, Min(20), Duration);
            GivenPausesAt(20, 40, 60, 80);
            GivenHeardAtMinutes(new Dictionary<double, string>());

            var preview = await BuildService().PlanAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.True(file.Repairable);
            Assert.Equal(ChapterSource.Audnexus, file.Plan!.Source);
            Assert.True(file.Plan.Partial);
            Assert.Contains("0 confirmed by the narrator", file.Plan.Note);
        }

        [Fact]
        public async Task PlanAsync_UsesTheEditionWithoutListeningWhenTranscriptionIsOff()
        {
            GivenBook(asin: "B00X");
            GivenAnUnchapteredFile();
            GivenAudnexusChapters("B00X", 5, Min(20), Duration);
            GivenPausesAt(20, 40, 60, 80);

            var preview = await BuildService().PlanAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.True(file.Repairable);
            Assert.Equal(ChapterSource.Audnexus, file.Plan!.Source);
            Assert.Null(file.Plan.Heard);
            _transcriber.Verify(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PlanAsync_TellsTheOperatorToTurnTranscriptionOnForAnUnchapteredFile()
        {
            GivenBook();
            GivenAnUnchapteredFile();
            GivenPausesAt(20, 40, 60, 80);

            var preview = await BuildService().PlanAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.Equal(ChapterHealth.None, file.Health);
            Assert.False(file.Repairable);
            Assert.Contains("no ASIN", file.Rejection);
            Assert.Contains("Turn on transcription", file.Rejection);
            // Not worth decoding the whole file to say so.
            _silences.Verify(s => s.DetectAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PlanAsync_RejectsAnUnchapteredFileWithoutPauses()
        {
            GivenBook();
            GivenTranscription(enabled: true);
            GivenAnUnchapteredFile();
            _silences.Setup(s => s.DetectAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);

            var preview = await BuildService().PlanAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.False(file.Repairable);
            Assert.Contains("No pause long enough", file.Rejection);
            _transcriber.Verify(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task PlanAsync_RejectsAnUnchapteredFileThatCannotBeDecoded()
        {
            GivenBook();
            GivenTranscription(enabled: true);
            GivenAnUnchapteredFile();
            _silences
                .Setup(s => s.DetectAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SilenceDetectionException("ffmpeg could not decode Book.m4b (exit 1): Invalid data found"));

            var preview = await BuildService().PlanAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.False(file.Repairable);
            Assert.Contains("Invalid data found", file.Rejection);
        }

        [Fact]
        public async Task PlanAsync_FindsTheChaptersOfACdRipWhoseTracksAreNotWhereTheChaptersAre()
        {
            GivenBook();
            GivenTranscription(enabled: true);
            // Twenty-five four-minute tracks cut by length, not at the chapters; the chapters begin at 12, 27 and 55 minutes.
            var tracks = Tracks(25, Min(4));
            GivenFileReads(tracks, new ChapterAtomState(true, null, 25, true));
            GivenPausesAt(12, 27, 55, 71);
            GivenHeardAtMinutes(new Dictionary<double, string> { [0] = "Chapter one.", [12] = "Chapter two.", [27] = "Chapter three.", [55] = "Chapter four." });

            var preview = await BuildService().PlanAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.Equal(ChapterHealth.Oversegmented, file.Health);
            Assert.True(file.Repairable);
            Assert.Equal(ChapterSource.Announcements, file.Plan!.Source);
            Assert.Equal(["Chapter 1", "Chapter 2", "Chapter 3", "Chapter 4"], file.Plan.Chapters.Select(c => c.Title));
            // Chapter 2 falls on a track boundary, which is sample-accurate and stands in for the pause beside it.
            Assert.Equal([TimeSpan.Zero, Min(12), Min(27) - SilenceCandidates.Lead, Min(55) - SilenceCandidates.Lead], file.Plan.Chapters.Select(c => c.Start));
        }

        [Fact]
        public async Task PlanAsync_SaysWhatTheAudioGaveWhenNeitherTheTracksNorThePausesAreAnnounced()
        {
            GivenBook();
            GivenTranscription(enabled: true);
            var tracks = Tracks(25, Min(4));
            GivenFileReads(tracks, new ChapterAtomState(true, null, 25, true));
            GivenPausesAt(12, 27, 55, 71);
            GivenHeardAtMinutes(new Dictionary<double, string>());

            var preview = await BuildService().PlanAsync(7);

            var file = Assert.Single(preview!.Files);
            Assert.False(file.Repairable);
            Assert.Contains("No chapter announcements were heard at any mark", file.Rejection);
            Assert.Contains("Listening after the pauses in the audio instead: no chapter announcement was heard after any of the", file.Rejection);
        }

        [Fact]
        public async Task PlanAsync_KeepsNothingWhenAPauseCouldNotBeHeard()
        {
            GivenBook();
            GivenTranscription(enabled: true);
            GivenAnUnchapteredFile();
            GivenPausesAt(20, 40, 60, 80);
            _transcriber
                .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("the model file is truncated"));

            await Assert.ThrowsAsync<ChapterSourceUnavailableException>(() => BuildService().PlanAsync(7));

            Assert.Null(_book!.Files![0].ChapterPlanJson);
        }
    }
}
