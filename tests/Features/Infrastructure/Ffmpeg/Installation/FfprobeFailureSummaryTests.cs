using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Ffmpeg.Installation
{
    [Trait("Name", "FfprobeFailureSummaryTests")]
    [Trait("Category", "Ffmpeg")]
    public class FfprobeFailureSummaryTests : BaseTests
    {
        [Fact]
        public async Task Summarise_UnimplementedCodec_KeepsTheReasonAndDropsFfmpegInternals()
        {
            // The real stderr from an xHE-AAC (USAC) audiobook. ffmpeg repeats the same
            // complaint per decode attempt and prefixes each with a component and a heap
            // pointer, none of which means anything to whoever has to act on it.
            const string stderr =
                "[mov,mp4,m4a,3gp,3g2,mj2 @ 0x78c2700] stream 0, timescale not set\n"
                + "[aac @ 0x78c3760] Audio object type 42 is not implemented.\n"
                + "[mov,mp4,m4a,3gp,3g2,mj2 @ 0x78c2700] Failed to open codec in avformat_find_stream_info\n"
                + "[aac @ 0x78c3760] Audio object type 42 is not implemented.\n";

            var summary = FfmpegService.SummariseFfprobeFailure(stderr);

            Assert.Contains("Audio object type 42 is not implemented.", summary);
            Assert.DoesNotContain("0x78c3760", summary);
            Assert.DoesNotContain("[aac @", summary);

            // Repeated once per decode attempt; saying it twice adds nothing.
            var occurrences = summary.Split("Audio object type 42").Length - 1;
            Assert.Equal(1, occurrences);

            await Task.CompletedTask;
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   \n  ")]
        public async Task Summarise_NoOutput_SaysSoRatherThanReturningNothing(string? stderr)
        {
            // An empty summary would render as a blank line in the import block detail,
            // which reads as though nothing went wrong.
            Assert.Equal("no diagnostic output", FfmpegService.SummariseFfprobeFailure(stderr));
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Summarise_VeryLongOutput_IsTruncatedForDisplay()
        {
            var stderr = string.Join('\n', Enumerable.Range(0, 50)
                .Select(i => $"[aac @ 0x{i:x}] distinct failure number {i} with plenty of padding text"));

            var summary = FfmpegService.SummariseFfprobeFailure(stderr);

            Assert.True(summary.Length <= 401, $"summary was {summary.Length} characters");
            await Task.CompletedTask;
        }
    }
}
