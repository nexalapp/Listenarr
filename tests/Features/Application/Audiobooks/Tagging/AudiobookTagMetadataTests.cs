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
using Listenarr.Application.Audiobooks.Tagging;
using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Audiobooks.Tagging
{
    /// <summary>
    /// Where the record is silent the file fills in - except for series, where silence
    /// means standalone. A file that still carried a series the record had dropped kept
    /// it forever: the write saw nothing to change while the tag table showed the
    /// mismatch it could never clear.
    /// </summary>
    [Trait("Name", "AudiobookTagMetadataTests")]
    [Trait("Category", "Tagging")]
    public sealed class AudiobookTagMetadataTests : BaseTests
    {
        private static Dictionary<string, string> Tags(params (string Key, string Value)[] tags) =>
            tags.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.OrdinalIgnoreCase);

        [Fact]
        public void Create_DoesNotTakeASeriesFromTheFileWhenTheRecordHasNone()
        {
            var audiobook = new AudiobookBuilder().WithId(1).WithTitle("Rescue Party").Build();
            audiobook.Series = null;
            audiobook.SeriesNumber = null;

            var metadata = AudiobookTagMetadata.Create(
                audiobook,
                Tags(("SERIES", "When the World Ends"), ("SERIESPOSITION", "1")));

            Assert.True(string.IsNullOrEmpty(metadata.Series));
            Assert.True(metadata.AllSeries == null || metadata.AllSeries.Count == 0);
        }

        [Fact]
        public void Create_StillTakesTheBlurbFromTheFileWhenTheRecordHasNone()
        {
            var audiobook = new AudiobookBuilder().WithId(1).WithTitle("Rescue Party").Build();
            audiobook.Description = null;

            var metadata = AudiobookTagMetadata.Create(
                audiobook,
                Tags(("description", "A ship comes for the last humans.")));

            Assert.Equal("A ship comes for the last humans.", metadata.Description);
        }

        [Fact]
        public void Create_KeepsTheRecordsSeriesOverTheFiles()
        {
            var audiobook = new AudiobookBuilder().WithId(1).WithTitle("Earthlight").Build();
            audiobook.Series = "Space Trilogy";
            audiobook.SeriesNumber = "2";

            var metadata = AudiobookTagMetadata.Create(
                audiobook,
                Tags(("SERIES", "Something Else"), ("SERIESPOSITION", "9")));

            Assert.Equal("Space Trilogy", metadata.Series);
        }
    }
}
