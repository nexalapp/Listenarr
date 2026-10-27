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
using Listenarr.Application.Common;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Common
{
    /// <summary>
    /// The plain names the tasks page shows.
    ///
    /// <para>
    /// A worker registers under its class name, which is the right key and a poor label.
    /// What matters here is that a worker nobody has written a description for still gets
    /// a readable name rather than disappearing or showing raw C#.
    /// </para>
    /// </summary>
    [Trait("Name", "WorkerTaskCatalogueTests")]
    [Trait("Category", "Application")]
    public sealed class WorkerTaskCatalogueTests : BaseTests
    {
        [Fact]
        public void AKnownWorkerGetsItsWrittenNameAndDescription()
        {
            var described = WorkerTaskCatalogue.Describe("SuggestionReleaseCheckBackgroundService");

            Assert.Equal("Check what is available", described.Label);
            Assert.Contains("indexers", described.Description);
        }

        [Theory]
        [InlineData("ThingDoerBackgroundService", "Thing doer")]
        [InlineData("ImageCacheCleanupService", "Image cache cleanup")]
        [InlineData("move.scan.handoff.recovery", "Move scan handoff recovery")]
        [InlineData("quality-sweep", "Quality sweep")]
        public void AWorkerNobodyDescribedStillReadsAsEnglish(string name, string expected) =>
            Assert.Equal(expected, WorkerTaskCatalogue.Humanise(name));

        [Fact]
        public void AnUndescribedWorkerIsNamedButNotExplained()
        {
            var described = WorkerTaskCatalogue.Describe("SomeNewBackgroundService");

            Assert.Equal("Some new", described.Label);
            Assert.Empty(described.Description);
        }
    }
}
