using BackgroundJobs.Core;
using BackgroundJobs.Core.Jobs;

namespace BackgroundJobs.UnitTests;

public class JobRunTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_run_is_queued_with_no_progress()
    {
        var run = JobRun.Create(JobKind.Import, Guid.NewGuid(), "import:abc", Now);

        Assert.Equal(JobStatus.Queued, run.Status);
        Assert.Equal(0, run.Progress);
        Assert.Equal(0, run.Attempts);
        Assert.True(run.IsActive);
    }

    [Fact]
    public void An_active_run_can_be_cancelled_once()
    {
        var run = JobRun.Create(JobKind.Report, null, null, Now);

        run.Cancel(Now);

        Assert.Equal(JobStatus.Cancelled, run.Status);
        Assert.Equal(Now, run.FinishedAt);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(() => run.Cancel(Now)).Code);
    }

    [Fact]
    public void Only_failed_or_cancelled_runs_can_be_requeued()
    {
        var queued = JobRun.Create(JobKind.Import, null, null, Now);
        Assert.Equal(ErrorCodes.InvalidState, Assert.Throws<DomainException>(queued.Requeue).Code);

        var cancelled = JobRun.Create(JobKind.Import, null, null, Now);
        cancelled.Cancel(Now);
        cancelled.Requeue();

        Assert.Equal(JobStatus.Queued, cancelled.Status);
        Assert.Null(cancelled.FinishedAt);
        Assert.Equal(0, cancelled.Progress);
    }
}
