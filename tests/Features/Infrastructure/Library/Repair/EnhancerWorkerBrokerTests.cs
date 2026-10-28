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
using Listenarr.Infrastructure.Library.Repair;
using Listenarr.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;

namespace Listenarr.Tests.Features.Infrastructure.Library.Repair
{
    /// <summary>
    /// The hand-off to the GPU box: a chunk goes to a worker that asks, waits for one whose
    /// card is merely taken, and falls back to the local model only when nobody is there
    /// or the worker keeps failing it.
    /// </summary>
    [Trait("Name", "EnhancerWorkerBrokerTests")]
    [Trait("Category", "Repair")]
    public sealed class EnhancerWorkerBrokerTests : BaseTests
    {
        private readonly string _work = Path.Join(Path.GetTempPath(), $"gpu-broker-{Guid.NewGuid():N}");
        private readonly ManualClock _clock = new();
        private readonly EnhancerWorkerBroker _broker;

        public EnhancerWorkerBrokerTests()
        {
            Directory.CreateDirectory(_work);
            _broker = new EnhancerWorkerBroker(NullLogger<EnhancerWorkerBroker>.Instance, _clock)
            {
                PollInterval = TimeSpan.FromMilliseconds(5)
            };
        }

        private sealed class ManualClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 4, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow() => Now;
        }

        private string Input()
        {
            var path = Path.Combine(_work, "orig-0000.wav");
            File.WriteAllBytes(path, Wave(1));
            return path;
        }

        private string Output => Path.Combine(_work, "enh-0000.wav");

        private static byte[] Wave(byte fill)
        {
            var bytes = new byte[64];
            "RIFF"u8.CopyTo(bytes);
            "WAVE"u8.CopyTo(bytes.AsSpan(8));
            Array.Fill(bytes, fill, 12, 52);
            return bytes;
        }

        private static async Task<T> Within<T>(Task<T> task) =>
            await task.WaitAsync(TimeSpan.FromSeconds(10));

        [Fact]
        public async Task WithNoWorker_TheLocalModelTakesTheChunk()
        {
            Assert.False(await Within(_broker.TryEnhanceAsync(Input(), Output, TimeSpan.FromMinutes(5), CancellationToken.None)));
        }

        [Fact]
        public async Task AWorkerThatAsks_GetsTheChunk_AndItsUploadIsTheOutput()
        {
            await _broker.ClaimAsync("codegen", ready: true, TimeSpan.Zero, CancellationToken.None);
            var input = Input();
            var enhancing = _broker.TryEnhanceAsync(input, Output, TimeSpan.FromMinutes(5), CancellationToken.None);

            var task = await Within(_broker.ClaimAsync("codegen", ready: true, TimeSpan.FromSeconds(5), CancellationToken.None));
            Assert.NotNull(task);
            Assert.Equal(300, task.DurationSeconds);
            Assert.Equal(input, _broker.InputPathFor(task.Id, "codegen"));
            Assert.Null(_broker.InputPathFor(task.Id, "someone-else"));

            Assert.True(await _broker.CompleteAsync(task.Id, "codegen", new MemoryStream(Wave(7)), CancellationToken.None));
            Assert.True(await Within(enhancing));
            Assert.Equal(Wave(7), await File.ReadAllBytesAsync(Output));
            Assert.Equal(1, Assert.Single(_broker.Workers).Completed);
        }

        [Fact]
        public async Task AWorkerWhoseCardIsTaken_IsWaitedFor_NotFallenBackFrom()
        {
            await _broker.ClaimAsync("codegen", ready: false, TimeSpan.Zero, CancellationToken.None);
            var enhancing = _broker.TryEnhanceAsync(Input(), Output, TimeSpan.FromMinutes(5), CancellationToken.None);

            await Task.Delay(100);
            Assert.False(enhancing.IsCompleted);
            Assert.Null(await _broker.ClaimAsync("codegen", ready: false, TimeSpan.Zero, CancellationToken.None));

            var task = await Within(_broker.ClaimAsync("codegen", ready: true, TimeSpan.FromSeconds(5), CancellationToken.None));
            Assert.True(await _broker.CompleteAsync(task!.Id, "codegen", new MemoryStream(Wave(3)), CancellationToken.None));
            Assert.True(await Within(enhancing));
        }

        [Fact]
        public async Task AWorkerThatGoesAway_HandsTheChunkBackToTheLocalModel()
        {
            await _broker.ClaimAsync("codegen", ready: false, TimeSpan.Zero, CancellationToken.None);
            var enhancing = _broker.TryEnhanceAsync(Input(), Output, TimeSpan.FromMinutes(5), CancellationToken.None);
            await Task.Delay(50);

            _clock.Now += EnhancerWorkerBroker.AliveWindow + TimeSpan.FromSeconds(1);
            Assert.False(await Within(enhancing));
            Assert.False(_broker.HasLiveWorker);
        }

        [Fact]
        public async Task AChunkFailedThreeTimes_GoesToTheLocalModel()
        {
            await _broker.ClaimAsync("codegen", ready: true, TimeSpan.Zero, CancellationToken.None);
            var enhancing = _broker.TryEnhanceAsync(Input(), Output, TimeSpan.FromMinutes(5), CancellationToken.None);

            for (var attempt = 0; attempt < EnhancerWorkerBroker.RemoteAttempts; attempt++)
            {
                var task = await Within(_broker.ClaimAsync("codegen", ready: true, TimeSpan.FromSeconds(5), CancellationToken.None));
                Assert.True(_broker.Fail(task!.Id, "codegen", "CUDA out of memory"));
            }

            Assert.False(await Within(enhancing));
            Assert.Equal(3, Assert.Single(_broker.Workers).Failed);
        }

        [Fact]
        public async Task AClaimedChunkPastItsDeadline_IsOfferedAgain_AndTheLateUploadIsRefused()
        {
            await _broker.ClaimAsync("codegen", ready: true, TimeSpan.Zero, CancellationToken.None);
            var enhancing = _broker.TryEnhanceAsync(Input(), Output, TimeSpan.FromMinutes(5), CancellationToken.None);
            var lost = await Within(_broker.ClaimAsync("codegen", ready: true, TimeSpan.FromSeconds(5), CancellationToken.None));

            // Another worker keeps the pool alive while the first sits on its chunk too long.
            var deadline = _clock.Now + EnhancerWorkerBroker.ClaimDeadline(TimeSpan.FromMinutes(5));
            while (_clock.Now <= deadline)
            {
                await _broker.ClaimAsync("codegen-2", ready: false, TimeSpan.Zero, CancellationToken.None);
                _clock.Now += TimeSpan.FromSeconds(60);
            }

            await _broker.ClaimAsync("codegen-2", ready: false, TimeSpan.Zero, CancellationToken.None);

            var retry = await Within(_broker.ClaimAsync("codegen-2", ready: true, TimeSpan.FromSeconds(5), CancellationToken.None));
            Assert.NotEqual(lost!.Id, retry!.Id);
            Assert.False(await _broker.CompleteAsync(lost.Id, "codegen", new MemoryStream(Wave(9)), CancellationToken.None));

            Assert.True(await _broker.CompleteAsync(retry.Id, "codegen-2", new MemoryStream(Wave(4)), CancellationToken.None));
            Assert.True(await Within(enhancing));
            Assert.Equal(Wave(4), await File.ReadAllBytesAsync(Output));
        }

        [Fact]
        public async Task AnUploadThatIsNotAWave_IsRefused_AndTheChunkOfferedAgain()
        {
            await _broker.ClaimAsync("codegen", ready: true, TimeSpan.Zero, CancellationToken.None);
            var enhancing = _broker.TryEnhanceAsync(Input(), Output, TimeSpan.FromMinutes(5), CancellationToken.None);
            var task = await Within(_broker.ClaimAsync("codegen", ready: true, TimeSpan.FromSeconds(5), CancellationToken.None));

            Assert.False(await _broker.CompleteAsync(task!.Id, "codegen", new MemoryStream("<html>"u8.ToArray()), CancellationToken.None));
            Assert.False(File.Exists(Output));

            var again = await Within(_broker.ClaimAsync("codegen", ready: true, TimeSpan.FromSeconds(5), CancellationToken.None));
            Assert.True(await _broker.CompleteAsync(again!.Id, "codegen", new MemoryStream(Wave(5)), CancellationToken.None));
            Assert.True(await Within(enhancing));
        }

        public override async Task DisposeAsync()
        {
            try
            {
                Directory.Delete(_work, recursive: true);
            }
            catch (IOException)
            {
            }

            await base.DisposeAsync();
        }
    }
}
