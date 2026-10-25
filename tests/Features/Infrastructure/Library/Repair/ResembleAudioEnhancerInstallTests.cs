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
using System.Text.Json;
using Listenarr.Application.Audiobooks.Repair;
using Listenarr.Infrastructure.Library.Repair;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Repair
{
    /// <summary>
    /// The install the repairs run from: it is only Ready when its interpreter can
    /// actually be reached, and finishing one points <c>current</c> at it without moving it.
    /// </summary>
    [Trait("Name", "ResembleAudioEnhancerInstallTests")]
    [Trait("Category", "Repair")]
    public sealed class ResembleAudioEnhancerInstallTests : BaseTests
    {
        private readonly string _config = Path.Join(Path.GetTempPath(), $"enhancer-install-{Guid.NewGuid():N}");

        private string EnhancerRoot => Path.Combine(_config, "enhancer");

        private ResembleAudioEnhancer BuildEnhancer()
        {
            var paths = new Mock<IApplicationPathService>();
            paths.Setup(p => p.ResolveFromConfig("enhancer")).Returns(EnhancerRoot);
            return new ResembleAudioEnhancer(paths.Object, new Mock<IProcessRunner>().Object, NullLogger<ResembleAudioEnhancer>.Instance);
        }

        /// <summary>A finished install: manifest, CLI script, and a venv interpreter linked into its Python.</summary>
        private string GivenInstall(string name)
        {
            var root = Path.Combine(EnhancerRoot, name);
            var bin = Path.Combine(root, "venv", "bin");
            var python = Path.Combine(root, "python", "bin");
            Directory.CreateDirectory(bin);
            Directory.CreateDirectory(python);
            File.WriteAllText(Path.Combine(python, "python3.11"), "");
            File.CreateSymbolicLink(Path.Combine(bin, "python"), Path.Combine(python, "python3.11"));
            File.WriteAllText(Path.Combine(bin, "resemble-enhance"), "#!" + Path.Combine(bin, "python"));
            File.WriteAllText(Path.Combine(root, "install.json"), JsonSerializer.Serialize(new
            {
                Schema = 1,
                ResembleAudioEnhancer.PipelineVersion,
                ResembleAudioEnhancer.UvVersion,
                ResembleAudioEnhancer.PythonVersion,
                ResembleAudioEnhancer.ModelRevision,
                CompletedAtUtc = DateTime.UtcNow,
                DiskBytes = 42L
            }));
            return root;
        }

        [Fact]
        public async Task AnInstallLinkedAsCurrent_IsReady()
        {
            var enhancer = BuildEnhancer();
            enhancer.PointCurrentAt(GivenInstall("install-a"));

            var status = await enhancer.GetStatusAsync();

            Assert.Equal(EnhancerState.Ready, status.State);
            // Pointed at, not moved: every absolute path inside it is still true.
            Assert.True(Directory.Exists(Path.Combine(EnhancerRoot, "install-a")));
        }

        [Fact]
        public async Task AnInstallMovedAfterItWasMade_IsNotReady()
        {
            // The first real install: renamed into place after its smoke test, every file
            // present, and its interpreter link naming a directory that no longer existed.
            // It reported Ready and failed all 114 repairs it was given.
            Directory.Move(GivenInstall("staging-old"), Path.Combine(EnhancerRoot, "current"));

            var status = await BuildEnhancer().GetStatusAsync();

            Assert.NotEqual(EnhancerState.Ready, status.State);
        }

        [Fact]
        public async Task ANewInstall_ReplacesTheOldOne_AndRemovesIt()
        {
            var enhancer = BuildEnhancer();
            enhancer.PointCurrentAt(GivenInstall("install-a"));

            enhancer.PointCurrentAt(GivenInstall("install-b"));

            Assert.Equal("install-b", new DirectoryInfo(Path.Combine(EnhancerRoot, "current")).LinkTarget);
            Assert.False(Directory.Exists(Path.Combine(EnhancerRoot, "install-a")));
            Assert.Equal(EnhancerState.Ready, (await enhancer.GetStatusAsync()).State);
        }

        [Fact]
        public void ANewInstall_ReplacesALegacyRenamedDirectory()
        {
            var enhancer = BuildEnhancer();
            Directory.Move(GivenInstall("staging-old"), Path.Combine(EnhancerRoot, "current"));

            enhancer.PointCurrentAt(GivenInstall("install-b"));

            Assert.Equal("install-b", new DirectoryInfo(Path.Combine(EnhancerRoot, "current")).LinkTarget);
        }

        public override async Task DisposeAsync()
        {
            try
            {
                Directory.Delete(_config, recursive: true);
            }
            catch (IOException)
            {
            }

            await base.DisposeAsync();
        }
    }
}
