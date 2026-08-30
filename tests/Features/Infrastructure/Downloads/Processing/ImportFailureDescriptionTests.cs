using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Infrastructure.Downloads.Processing
{
    [Trait("Name", "ImportFailureDescriptionTests")]
    [Trait("Category", "DownloadProcessingJob")]
    public class ImportFailureDescriptionTests : BaseTests
    {
        [Fact]
        public async Task Describe_FailedResults_NamesTheReasonRatherThanPointingAtTheLog()
        {
            var results = new List<ImportResult>
            {
                new() { Success = false, Message = "The newly created file changed beneath its pinned parent." },
                new() { Success = true, Message = "Imported" }
            };

            var description = DownloadProcessingJobProcessor.DescribeFailedImports(results);

            Assert.Contains("changed beneath its pinned parent", description);
            Assert.Contains("1 of 2", description);
            Assert.DoesNotContain("see the log", description, StringComparison.OrdinalIgnoreCase);

            await Task.CompletedTask;
        }

        [Fact]
        public async Task Describe_RepeatedReason_IsNotRepeatedBack()
        {
            // A multi-file release usually fails the same way on every file.
            var results = new List<ImportResult>
            {
                new() { Success = false, Message = "Destination is read-only" },
                new() { Success = false, Message = "Destination is read-only" },
                new() { Success = false, Message = "Destination is read-only" }
            };

            var description = DownloadProcessingJobProcessor.DescribeFailedImports(results);

            Assert.Equal(1, description.Split("Destination is read-only").Length - 1);
            Assert.Contains("3 of 3", description);

            await Task.CompletedTask;
        }

        [Fact]
        public async Task Describe_FailureWithNoMessage_StillSaysHowManyFailed()
        {
            // Silence must not render as an empty reason, which reads as no problem at all.
            var results = new List<ImportResult>
            {
                new() { Success = false, Message = null },
                new() { Success = false, Message = "   " }
            };

            var description = DownloadProcessingJobProcessor.DescribeFailedImports(results);

            Assert.Contains("2 of 2", description);
            Assert.Contains("no reason", description);

            await Task.CompletedTask;
        }
    }
}
