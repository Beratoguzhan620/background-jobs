using BackgroundJobs.Core;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Core.Reports;
using BackgroundJobs.Infrastructure.Files;
using BackgroundJobs.Infrastructure.Hangfire;
using BackgroundJobs.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackgroundJobs.Infrastructure.Jobs;

public sealed record CleanupSummary(int DeletedUploads, int RequeuedOrphans);

/// <summary>Work that Hangfire starts on a schedule. Each one records a job run so it shows up next to the rest.</summary>
public sealed class RecurringJobs(
    JobsDbContext db,
    JobRunner runner,
    FileStore files,
    IBackgroundJobClient jobs,
    TimeProvider clock,
    IOptions<JobsHangfireOptions> options,
    ILogger<RecurringJobs> logger)
{
    public const string NightlyReportId = "nightly-sales-report";
    public const string CleanupId = "hourly-cleanup";

    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById(ReportJob.TimeZone);

    /// <summary>
    /// Yesterday's sales. The dedup key is the date, so even if the schedule fires twice (two servers, a
    /// restart, a manual trigger from the dashboard) only one report per day is made.
    /// </summary>
    [Queue(JobQueues.Reports)]
    [JobDisplayName("Nightly sales report")]
    [AutomaticRetry(Attempts = 0)]
    public async Task NightlyReportAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Istanbul).DateTime);
        var day = today.AddDays(-1);

        var run = JobRun.Create(JobKind.Report, null, $"report:nightly:{day:yyyy-MM-dd}", clock.GetUtcNow());
        db.JobRuns.Add(run);
        db.Reports.Add(Report.Create(run.Id, day, day, nightly: true));
        try
        {
            await JobRequests.SaveOrRefuseDuplicateAsync(db, run, cancellationToken);
        }
        catch (DomainException ex) when (ex.Code == ErrorCodes.DuplicateJob)
        {
            logger.LogInformation("The nightly report for {Day} already exists", day);
            return;
        }

        // Retries happen through the report job itself, not by creating another nightly run.
        var hangfireId = jobs.Enqueue<ReportJob>(job => job.RunAsync(run.Id, CancellationToken.None));
        await JobRequests.AttachHangfireJobAsync(db, run.Id, hangfireId, cancellationToken);
    }

    /// <summary>Deletes old uploads and re-enqueues runs whose Hangfire job was never created.</summary>
    [Queue(JobQueues.Maintenance)]
    [JobDisplayName("Hourly cleanup")]
    [DisableConcurrentExecution(timeoutInSeconds: 60)]
    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        var run = JobRun.Create(JobKind.Cleanup, null, null, clock.GetUtcNow());
        db.JobRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        await runner.RunAsync(run.Id, context => CleanUpAsync(context), cancellationToken);
    }

    private async Task<object?> CleanUpAsync(JobContext context)
    {
        var ct = context.Cancellation;
        var now = clock.GetUtcNow();

        // Uploads of finished imports past their retention period.
        var keepAfter = now - options.Value.UploadRetention;
        var expired = await db.ImportFiles
            .Where(f => f.StoredPath != null && f.UploadedAt < keepAfter)
            .Join(db.JobRuns.Where(r => r.Status == JobStatus.Succeeded || r.Status == JobStatus.Failed || r.Status == JobStatus.Cancelled),
                f => f.JobRunId, r => r.Id, (f, _) => f)
            .ToListAsync(ct);

        foreach (var file in expired)
        {
            files.Delete(file.StoredPath);
            file.ForgetStoredFile();
        }

        await db.SaveChangesAsync(ct);
        await context.ReportProgressAsync(50);

        // A run saved but never handed to Hangfire (the API stopped in between) would wait forever.
        var orphanBefore = now - options.Value.OrphanAfter;
        var orphans = await db.JobRuns.AsNoTracking()
            .Where(r => r.Status == JobStatus.Queued && r.HangfireJobId == null && r.ScheduledFor == null && r.CreatedAt < orphanBefore
                && (r.Kind == JobKind.Import || r.Kind == JobKind.Report))
            .Select(r => new { r.Id, r.Kind })
            .ToListAsync(ct);

        foreach (var orphan in orphans)
        {
            var hangfireId = orphan.Kind == JobKind.Import
                ? jobs.Enqueue<ImportJob>(job => job.RunAsync(orphan.Id, CancellationToken.None))
                : jobs.Enqueue<ReportJob>(job => job.RunAsync(orphan.Id, CancellationToken.None));
            await JobRequests.AttachHangfireJobAsync(db, orphan.Id, hangfireId, ct);
            logger.LogWarning("Re-enqueued orphaned run {RunId}", orphan.Id);
        }

        return new CleanupSummary(expired.Count, orphans.Count);
    }
}
