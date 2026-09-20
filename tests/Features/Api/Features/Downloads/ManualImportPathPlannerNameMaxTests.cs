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
using System.Text;
using Listenarr.Api.Dtos.ManualImport;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Api.Features.Downloads
{
    /// <summary>
    /// A full-cast book's name is fitted to the 255-byte limit when the pattern is
    /// rendered, but the extension (and, without a number token, the sequence suffix)
    /// goes on afterwards. The finished path is what the filesystem sees, so it is
    /// the finished path that must fit.
    /// </summary>
    [Trait("Name", "ManualImportPathPlannerNameMaxTests")]
    [Trait("Category", "Api")]
    public sealed class ManualImportPathPlannerNameMaxTests : BaseTests
    {
        [Fact]
        public async Task GeneratePathAsync_MultiFileSuffix_KeepsEveryComponentUnderNameMax()
        {
            var cast = string.Join(", ", Enumerable.Range(1, 16).Select(i => $"Narrator Number {i:00} Surname"));
            var audiobook = new Audiobook
            {
                Id = 1,
                Title = "Fearless [Dramatized Adaptation]",
                Authors = ["Jack Campbell"],
                Narrators = [.. cast.Split(", ")],
                Series = "Lost Fleet",
                SeriesNumber = "2",
                PublishYear = "2026",
            };
            var metadata = audiobook.CreateBasicAudioMetadata();
            var root = Path.Join(Path.GetTempPath(), "listenarr-name-max");
            var settings = new ApplicationSettings
            {
                OutputPath = root,
                FolderNamingPattern = "{Author}/[{Series} {SeriesNumber}] {Title} {{Narrator}} ({Year})",
                FileNamingPattern = "{Author} - [{Series} {SeriesNumber}] {Title} {{Narrator}} ({Year})",
                // A number token makes the pattern a path, not a filename, so the fit
                // it gets holds back no room for the extension appended afterwards.
                MultiFileNamingPattern = "{Author} - [{Series} {SeriesNumber}] {Title} {{Narrator}} ({Year}) - {ChapterNumber:000}",
            };
            var item = new ManualImportItemDto
            {
                FullPath = Path.Join(root, "incoming", "Fearless - Part 2.mp3"),
                MatchedAudiobookId = 1,
                ChapterNumberHint = 2,
            };
            var planner = new ManualImportPathPlanner(new FileNamingService(
                Mock.Of<IConfigurationService>(),
                NullLogger<FileNamingService>.Instance));

            var plan = await planner.GeneratePathAsync(
                audiobook,
                metadata,
                item,
                root,
                [new RootFolder { Id = 1, Name = "Library", Path = root }],
                settings,
                new FileSystemPathSemantics(FileSystemPathSyntax.Unix, FileSystemCaseSensitivity.Sensitive),
                isMultiFile: true);

            var fileName = Path.GetFileName(plan.DestinationPath);
            Assert.EndsWith(" - 002.mp3", fileName);
            Assert.Contains(" et al.}", fileName);
            foreach (var component in plan.DestinationPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.True(
                    Encoding.UTF8.GetByteCount(component) <= NarratorNameStyle.MaxComponentBytes,
                    $"{Encoding.UTF8.GetByteCount(component)} bytes: {component}");
            }
        }
    }
}
