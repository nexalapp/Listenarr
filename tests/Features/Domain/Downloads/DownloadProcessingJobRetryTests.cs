using Listenarr.Tests.Builders;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Domain.Downloads
{
    [Trait("Name", "DownloadProcessingJobRetryTests")]
    [Trait("Category", "DownloadProcessingJob")]
    public class DownloadProcessingJobRetryTests : BaseTests
    {
        [Fact]
        public async Task Reopen_AfterRetriesExhausted_MakesTheJobRunnableAgain()
        {
            // Without this the retry endpoint clears the download's blocked flag while
            // the job stays Failed with its retries spent, so the import is never
            // attempted again and the download waits in ImportPending forever.
            var job = new DownloadProcessingJobBuilder().Build();
            job.RetryCount = job.MaxRetries;
            job.ScheduleRetry("No importable files found");

            Assert.Equal(ProcessingJobStatus.Failed, job.Status);

            job.Reopen();

            Assert.Equal(ProcessingJobStatus.Pending, job.Status);
            Assert.Equal(0, job.RetryCount);
            Assert.Null(job.ErrorMessage);
            Assert.Null(job.CompletedAt);
            Assert.Null(job.NextRetryAt);
            Assert.Contains(job.ProcessingLog, entry => entry.Contains("Retry requested", StringComparison.OrdinalIgnoreCase));

            await Task.CompletedTask;
        }

        [Fact]
        public async Task ScheduleRetry_WhenRetriesRunOut_KeepsTheCauseNotJustTheCount()
        {
            // "Max retries exceeded" says we stopped, not what went wrong, and it is
            // what the operator is shown on a blocked import.
            var job = new DownloadProcessingJobBuilder().Build();
            job.RetryCount = job.MaxRetries;

            job.ScheduleRetry("No importable files found");

            Assert.Equal(ProcessingJobStatus.Failed, job.Status);
            Assert.NotNull(job.ErrorMessage);
            Assert.Contains("No importable files found", job.ErrorMessage);
            Assert.Contains("max retries", job.ErrorMessage, StringComparison.OrdinalIgnoreCase);

            await Task.CompletedTask;
        }

        [Fact]
        public async Task ScheduleRetry_WhenRetriesRunOutWithNoCause_StillReportsTheLimit()
        {
            var job = new DownloadProcessingJobBuilder().Build();
            job.RetryCount = job.MaxRetries;

            job.ScheduleRetry();

            Assert.Equal(ProcessingJobStatus.Failed, job.Status);
            Assert.Equal($"Max retries ({job.MaxRetries}) exceeded", job.ErrorMessage);

            await Task.CompletedTask;
        }
    }
}
