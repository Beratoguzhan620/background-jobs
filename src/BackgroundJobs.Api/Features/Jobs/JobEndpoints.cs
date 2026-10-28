using BackgroundJobs.Api.Common;
using BackgroundJobs.Api.Features.Audit;
using BackgroundJobs.Api.Features.Auth;
using BackgroundJobs.Core;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Infrastructure.Jobs;
using BackgroundJobs.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace BackgroundJobs.Api.Features.Jobs;

internal static class JobEndpoints
{
    private const int MaxPageSize = 100;

    public static RouteGroupBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("Jobs");
        group.MapGet("/", List).WithSummary("Job runs, newest first, filtered by status and kind").RequireAuthorization(AuthSetup.ViewerPolicy);
        group.MapGet("/{id:guid}", Get).WithSummary("One job run with its progress and result").RequireAuthorization(AuthSetup.ViewerPolicy);
        group.MapPost("/{id:guid}/cancel", Cancel).WithSummary("Cancel a queued, scheduled or running run")
            .RequireAuthorization(AuthSetup.AdminPolicy).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{id:guid}/retry", Retry).WithSummary("Put a failed or cancelled import or report back in the queue")
            .RequireAuthorization(AuthSetup.AdminPolicy).ProducesProblem(StatusCodes.Status409Conflict);
        return group;
    }

    private static async Task<Results<Ok<JobRunPage>, ValidationProblem>> List(JobsDbContext db, CancellationToken ct,
        string? status = null, string? kind = null, int? limit = null, long? cursor = null)
    {
        JobStatus parsedStatus = default;
        JobKind parsedKind = default;
        var validation = new Validation()
            .Require(status is null || Enum.TryParse(status, ignoreCase: true, out parsedStatus), "status",
                $"Status must be one of: {string.Join(", ", Enum.GetNames<JobStatus>())}.")
            .Require(kind is null || Enum.TryParse(kind, ignoreCase: true, out parsedKind), "kind",
                $"Kind must be one of: {string.Join(", ", Enum.GetNames<JobKind>())}.")
            .Require(limit is null or (>= 1 and <= MaxPageSize), "limit", $"Limit must be between 1 and {MaxPageSize}.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var query = db.JobRuns.AsNoTracking();
        if (status is not null)
        {
            query = query.Where(r => r.Status == parsedStatus);
        }

        if (kind is not null)
        {
            query = query.Where(r => r.Kind == parsedKind);
        }

        // The cursor is the last run number seen; numbers only grow, so paging is stable while new runs arrive.
        if (cursor is { } after)
        {
            query = query.Where(r => r.Number < after);
        }

        var size = limit ?? 20;
        var rows = await query.OrderByDescending(r => r.Number).Take(size + 1).ToListAsync(ct);
        var page = rows.Take(size).ToList();
        return TypedResults.Ok(new JobRunPage(page.Select(JobRunResponse.From).ToList(), rows.Count > size ? page[^1].Number : null));
    }

    private static async Task<Ok<JobRunResponse>> Cancel(Guid id, JobsDbContext db, IBackgroundJobClient jobs, AuditTrail audit,
        TimeProvider clock, CancellationToken ct)
    {
        var run = await FindAsync(db, id, ct);
        run.Cancel(clock.GetUtcNow());
        audit.Record("job.cancelled", "job_run", run.Id, new { kind = run.Kind.ToString() });
        // xmin: if a worker updated the run in the meantime this fails with 409 instead of overwriting it.
        await db.SaveChangesAsync(ct);

        // Removes it from the queue or the schedule; a running job notices at its next progress update.
        if (run.HangfireJobId is not null)
        {
            jobs.Delete(run.HangfireJobId);
        }

        return TypedResults.Ok(JobRunResponse.From(run));
    }

    private static async Task<Ok<JobRunResponse>> Retry(Guid id, JobsDbContext db, IBackgroundJobClient jobs, AuditTrail audit, CancellationToken ct)
    {
        var run = await FindAsync(db, id, ct);
        if (run.Kind is not (JobKind.Import or JobKind.Report))
        {
            throw new DomainException(ErrorCodes.InvalidState, "Housekeeping runs again on its schedule; it cannot be retried by hand.");
        }

        var previousAttempts = run.Attempts;
        run.Requeue();
        audit.Record("job.retried", "job_run", run.Id, new { kind = run.Kind.ToString(), previousAttempts });
        await JobRequests.SaveOrRefuseDuplicateAsync(db, run, ct);

        var hangfireId = run.Kind == JobKind.Import
            ? jobs.Enqueue<ImportJob>(job => job.RunAsync(run.Id, CancellationToken.None))
            : jobs.Enqueue<ReportJob>(job => job.RunAsync(run.Id, CancellationToken.None));
        await JobRequests.AttachHangfireJobAsync(db, run.Id, hangfireId, ct);

        return TypedResults.Ok(JobRunResponse.From(run));
    }

    private static async Task<JobRun> FindAsync(JobsDbContext db, Guid id, CancellationToken ct) =>
        await db.JobRuns.SingleOrDefaultAsync(r => r.Id == id, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "Job run not found.");

    private static async Task<Ok<JobRunResponse>> Get(Guid id, JobsDbContext db, CancellationToken ct) =>
        TypedResults.Ok(JobRunResponse.From(await db.JobRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "Job run not found.")));
}
