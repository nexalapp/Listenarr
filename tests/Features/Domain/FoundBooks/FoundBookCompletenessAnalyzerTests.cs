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
using Listenarr.Domain.FoundBooks;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.FoundBooks
{
    [Trait("Name", "FoundBookCompletenessAnalyzerTests")]
    [Trait("Category", "Domain")]
    public sealed class FoundBookCompletenessAnalyzerTests : BaseTests
    {
        private static FoundBookAudioObservation File(
            string name,
            double seconds = 600,
            int? track = null,
            int? total = null,
            int? hint = null,
            int chapters = 0,
            bool failed = false) =>
            new(name, failed, seconds, chapters, track, total, hint);

        [Fact]
        public void NumberedRunThatCloses_IsComplete()
        {
            var files = Enumerable.Range(1, 20)
                .Select(i => File($"Hugh Howey - Wool {i:00}-20.mp3"))
                .ToList();

            var verdict = FoundBookCompletenessAnalyzer.Analyze(files, null);

            Assert.Equal(FoundBookCompleteness.Complete, verdict.Completeness);
            Assert.Contains("Parts 1–20 of 20", verdict.Reason);
        }

        [Fact]
        public void NumberedRunWithAGap_IsIncompleteAndNamesTheGap()
        {
            var files = Enumerable.Range(1, 20)
                .Where(i => i != 7 && i != 8 && i != 15)
                .Select(i => File($"Book {i:00} of 20.mp3"))
                .ToList();

            var verdict = FoundBookCompletenessAnalyzer.Analyze(files, null);

            Assert.Equal(FoundBookCompleteness.Incomplete, verdict.Completeness);
            Assert.Contains("missing 7–8, 15", verdict.Reason);
        }

        [Fact]
        public void RunThatStartsLate_IsIncomplete()
        {
            // Triumphant arrived as parts 4–16 with nothing saying how many there were.
            var files = Enumerable.Range(4, 13)
                .Select(i => File($"{i:000} Jack Campbell (2019) Triumphant.mp4", hint: i))
                .ToList();

            var verdict = FoundBookCompletenessAnalyzer.Analyze(files, null);

            Assert.Equal(FoundBookCompleteness.Incomplete, verdict.Completeness);
            Assert.Contains("missing 1–3", verdict.Reason);
        }

        [Fact]
        public void RunFromOneWithNoTotal_IsUnknown()
        {
            // The last part could be missing and nothing would say so.
            var files = Enumerable.Range(1, 12)
                .Select(i => File($"{i:00} - Swamp Spirits.mp3", hint: i))
                .ToList();

            var verdict = FoundBookCompletenessAnalyzer.Analyze(files, null);

            Assert.Equal(FoundBookCompleteness.Unknown, verdict.Completeness);
            Assert.Contains("no total declared", verdict.Reason);
        }

        [Fact]
        public void TrackTotalTag_CountsAsATotal()
        {
            var files = Enumerable.Range(1, 5)
                .Select(i => File($"Chapter {i}.mp3", track: i, total: 5))
                .ToList();

            Assert.Equal(FoundBookCompleteness.Complete, FoundBookCompletenessAnalyzer.Analyze(files, null).Completeness);
        }

        [Fact]
        public void DiscTrackPairs_AreNotMistakenForATotal()
        {
            // "1-01" .. "1-12": the second number differs per file, so it is a track, not a count.
            var files = Enumerable.Range(1, 12)
                .Select(i => File($"Red_Dwarf_1-{i:00}.mp3", hint: i))
                .ToList();

            var verdict = FoundBookCompletenessAnalyzer.Analyze(files, null);

            Assert.NotEqual(FoundBookCompleteness.Incomplete, verdict.Completeness);
        }

        [Fact]
        public void DeclaredLengthMatched_IsComplete()
        {
            var files = Enumerable.Range(1, 3).Select(i => File($"part{i}.mp3", seconds: 3600, hint: i)).ToList();

            var verdict = FoundBookCompletenessAnalyzer.Analyze(files, declaredDurationSeconds: 3 * 3600 + 60);

            Assert.Equal(FoundBookCompleteness.Complete, verdict.Completeness);
            Assert.Contains("declared 3h 01m", verdict.Reason);
        }

        [Fact]
        public void DeclaredLengthShortfall_IsIncomplete()
        {
            var files = Enumerable.Range(1, 3).Select(i => File($"part{i}.mp3", seconds: 3600, hint: i)).ToList();

            var verdict = FoundBookCompletenessAnalyzer.Analyze(files, declaredDurationSeconds: 5 * 3600);

            Assert.Equal(FoundBookCompleteness.Incomplete, verdict.Completeness);
            Assert.Contains("3h 00m of the declared 5h 00m", verdict.Reason);
        }

        [Fact]
        public void UnreadableFile_IsCorrupt_WhateverElseIsTrue()
        {
            var files = Enumerable.Range(1, 3)
                .Select(i => File($"part {i} of 3.mp3", failed: i == 2))
                .ToList();

            var verdict = FoundBookCompletenessAnalyzer.Analyze(files, null);

            Assert.Equal(FoundBookCompleteness.Corrupt, verdict.Completeness);
            Assert.Contains("part 2 of 3.mp3", verdict.Reason);
        }

        [Fact]
        public void SingleFileWithChapters_IsComplete()
        {
            var verdict = FoundBookCompletenessAnalyzer.Analyze([File("book.m4b", seconds: 30000, chapters: 24)], null);

            Assert.Equal(FoundBookCompleteness.Complete, verdict.Completeness);
        }

        [Fact]
        public void SingleLongFileWithoutChapters_IsComplete()
        {
            var verdict = FoundBookCompletenessAnalyzer.Analyze([File("book.mp3", seconds: 2.9 * 3600)], null);

            Assert.Equal(FoundBookCompleteness.Complete, verdict.Completeness);
        }

        [Fact]
        public void SingleShortFile_IsUnknown()
        {
            var verdict = FoundBookCompletenessAnalyzer.Analyze([File("sample.mp3", seconds: 8 * 60)], null);

            Assert.Equal(FoundBookCompleteness.Unknown, verdict.Completeness);
        }

        [Fact]
        public void DuplicatePartNumber_DoesNotBreakTheRun()
        {
            var files = Enumerable.Range(1, 33).Select(i => File($"Birthright  {i:00}--33.mp3")).ToList();
            files.Add(File("Birthright  33--33(1).mp3"));

            Assert.Equal(FoundBookCompleteness.Complete, FoundBookCompletenessAnalyzer.Analyze(files, null).Completeness);
        }
    }

    [Trait("Name", "DeclaredLengthParserTests")]
    [Trait("Category", "Domain")]
    public sealed class DeclaredLengthParserTests : BaseTests
    {
        [Theory]
        [InlineData("Length: 11 hrs and 58 mins", 11 * 3600 + 58 * 60)]
        [InlineData("6h,2m. Daniel Stillman's Life", 6 * 3600 + 2 * 60)]
        [InlineData("Carson Mach book 1, unabridged 07:36, read by", null)]
        [InlineData("Runtime: 09:14:02", 9 * 3600 + 14 * 60 + 2)]
        [InlineData("Duration 1:02:03", 3600 + 123)]
        [InlineData("Read by Anna Stone", null)]
        [InlineData("45 mins", 45 * 60)]
        [InlineData("5 mins", null)]
        [InlineData("", null)]
        public void Parse_ReadsTheFormsRipsUse(string text, int? expected) =>
            Assert.Equal(expected, DeclaredLengthParser.Parse(text));
    }
}
