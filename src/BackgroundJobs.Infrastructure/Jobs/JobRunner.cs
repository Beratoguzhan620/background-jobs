using System.Text.Json;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Infrastructure.Hangfire;
using BackgroundJobs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackgroundJobs.Infrastructure.Jobs;

/// <summary>What a running job can do: report progress (which also proves it still owns the run).</summary>
public sealed class JobContext(Guid runId, int attempt, JobRunner runner, CancellationToken cancellation)
{
    public Guid RunId { get; } = runId;

    /// <summary>1 for the first attempt, 2 for the first retry, …</summary>
    public int Attempt { get; } = attempt;

    public CancellationToken Cancellation { get; } = cancellation;

    /// <summary>
    /// Saves progress and the heartbeat. Throws <see cref="RunCancelledException"/> if the run was cancelled
    /// or taken over meanwhile, so the job stops at a safe point.
    /// </summary>
    public Task ReportProgressAsync(int percent) => runner.ReportProgressAsync(RunId, percent, Cancellation);
}

/// <summary>The run is no longer ours to work on: it was cancelled, or another worker claimed it after a stall.</summary>
public sealed class RunCancelledException(Guid runId) : OperationCanceledException($"Run {runId} was cancelled.");

/// <summary>
/// Wraps every job. Hangfire delivers a job at least once, so before any work the runner <em>claims</em>
/// the run with one conditional UPDATE: only a queued or retrying run (or one whose worker went silent)
/// can move to Running. A second delivery finds nothing to claim and returns.
/// </summary>
public sealed class JobRunner(JobsDbContext db, TimeProvider clock, IOptions<JobsHangfireOptions> options, ILogger<JobRunner> logger)
{
    public async Task RunAsync(Guid runId, Func<JobContext, Task<object?>> work, CancellationToken ct)
    {
        var attempt = await TryClaimAsync(runId, ct);
        if (attempt is null)
        {
            logger.LogInformation("Run {RunId} was not claimed: it is finished, cancelled or running elsewhere", runId);
            return;
        }

        var context = new JobContext(runId, attempt.Value, this, ct);
        object? result;
        try
        {
            result = await work(context);
        }
        catch (RunCancelledException)
        {
            logger.LogInformation("Run {RunId} stopped: it was cancelled or taken over", runId);
            return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The worker is shutting down. Give the run back so the re-delivered job can claim it at once.
            await ReleaseAsync(runId);
            throw;
        }
        catch (Exception ex)
        {
            await RecordFailureAsync(runId, attempt.Value, ex);
            throw;
        }

        await CompleteAsync(runId, result);
    }

    internal async Task<int?> TryClaimAsync(Guid runId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var staleBefore = now - options.Value.HeartbeatTimeout;

        var claimed = await db.JobRuns
            .Where(r => r.Id == runId
                && (r.Status == JobStatus.Queued
                    || r.Status == JobStatus.Retrying
                    || (r.Status == JobStatus.Running && r.HeartbeatAt < staleBefore)))
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, JobStatus.Running)
                .SetProperty(r => r.Attempts, r => r.Attempts + 1)
                .SetProperty(r => r.StartedAt, r => r.StartedAt ?? now)
                .SetProperty(r => r.HeartbeatAt, now)
                .SetProperty(r => r.Error, (string?)null), ct);

        if (claimed == 0)
        {
            return null;
        }

        return await db.JobRuns.Where(r => r.Id == runId).Select(r => r.Attempts).SingleAsync(ct);
    }

    internal async Task ReportProgressAsync(Guid runId, int percent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var now = clock.GetUtcNow();
        var updated = await db.JobRuns
            .Where(r => r.Id == runId && r.Status == JobStatus.Running)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Progress, Math.Clamp(percent, 0, 99))
                .SetProperty(r => r.HeartbeatAt, now), ct);

        if (updated == 0)
        {
            throw new RunCancelledException(runId);
        }
    }

    private async Task CompleteAsync(Guid runId, object? result)
    {
        var now = clock.GetUtcNow();
        var json = result is null ? null : JsonSerializer.Serialize(result, JsonSerializerOptions.Web);

        // Not cancellable: the work is done, its outcome must be recorded.
        await db.JobRuns
            .Where(r => r.Id == runId && r.Status == JobStatus.Running)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, JobStatus.Succeeded)
                .SetProperty(r => r.Progress, 100)
                .SetProperty(r => r.FinishedAt, now)
                .SetProperty(r => r.HeartbeatAt, now)
                .SetProperty(r => r.Result, json), CancellationToken.None);

        logger.LogInformation("Run {RunId} succeeded", runId);
    }

    private async Task RecordFailureAsync(Guid runId, int attempt, Exception error)
    {
        var now = clock.GetUtcNow();
        var lastAttempt = attempt > options.Value.RetryAttempts;
        var message = error.Message.Length > JobRun.ErrorMaxLength ? error.Message[..JobRun.ErrorMaxLength] : error.Message;

        await db.JobRuns
            .Where(r => r.Id == runId && r.Status == JobStatus.Running)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, lastAttempt ? JobStatus.Failed : JobStatus.Retrying)
                .SetProperty(r => r.Error, message)
                .SetProperty(r => r.HeartbeatAt, now)
                .SetProperty(r => r.FinishedAt, lastAttempt ? now : (DateTimeOffset?)null), CancellationToken.None);

        logger.LogWarning(error, "Run {RunId} attempt {Attempt} failed; {Next}", runId, attempt, lastAttempt ? "giving up" : "will retry");
    }

    private Task ReleaseAsync(Guid runId) =>
        db.JobRuns
            .Where(r => r.Id == runId && r.Status == JobStatus.Running)
            .ExecuteUpdateAsync(set => set
                .SetProperty(r => r.Status, JobStatus.Queued)
                .SetProperty(r => r.Attempts, r => r.Attempts - 1)
                .SetProperty(r => r.HeartbeatAt, (DateTimeOffset?)null), CancellationToken.None);
}
