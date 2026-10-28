using BackgroundJobs.Core;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackgroundJobs.Api.Features.Jobs;

internal static class JobRequests
{
    private const string DedupIndex = "ux_job_runs_dedup_key_active";

    /// <summary>
    /// Saves a new run. If the same work is already queued, running or done, the partial unique index
    /// refuses it, even when two identical requests arrive at the same moment, and the caller gets the
    /// existing run's id.
    /// </summary>
    public static async Task SaveOrRefuseDuplicateAsync(JobsDbContext db, JobRun run, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(DedupIndex))
        {
            db.ChangeTracker.Clear();
            var existing = await db.JobRuns.AsNoTracking()
                .Where(r => r.DedupKey == run.DedupKey && r.Status != JobStatus.Failed && r.Status != JobStatus.Cancelled)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(ct);

            throw new DomainException(ErrorCodes.DuplicateJob, "The same work is already queued, running or done.")
            {
                Details = { ["jobRunId"] = existing },
            };
        }
    }
}
