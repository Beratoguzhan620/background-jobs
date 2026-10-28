using System.Security.Claims;
using BackgroundJobs.Api.Common;
using BackgroundJobs.Api.Features.Audit;
using BackgroundJobs.Api.Features.Auth;
using BackgroundJobs.Api.Features.Jobs;
using BackgroundJobs.Core;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Core.Reports;
using BackgroundJobs.Infrastructure.Files;
using BackgroundJobs.Infrastructure.Jobs;
using BackgroundJobs.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace BackgroundJobs.Api.Features.Reports;

/// <param name="From">First day, inclusive (yyyy-MM-dd, Türkiye time).</param>
/// <param name="To">Last day, inclusive.</param>
/// <param name="RunAt">Optional: build the report at this time instead of now (up to 30 days ahead).</param>
internal sealed record ReportRequest(DateOnly From, DateOnly To, DateTimeOffset? RunAt = null);

internal sealed record ReportResponse(JobRunResponse Run, DateOnly From, DateOnly To, bool Nightly, bool HasFile)
{
    public static ReportResponse Create(JobRun run, Report report) =>
        new(JobRunResponse.From(run), report.From, report.To, report.Nightly, report.StoredPath is not null);
}

internal static class ReportEndpoints
{
    private static readonly TimeSpan MaxScheduleAhead = TimeSpan.FromDays(30);

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports");
        group.MapPost("/", Request).WithSummary("Ask for a sales report now or at a later time")
            .RequireAuthorization(AuthSetup.AdminPolicy);
        group.MapGet("/{id:guid}", Get).WithSummary("A report request and its status").RequireAuthorization(AuthSetup.ViewerPolicy);
        group.MapGet("/{id:guid}/download", Download).WithSummary("The report as CSV").RequireAuthorization(AuthSetup.ViewerPolicy);
        return app;
    }

    private static async Task<Results<Accepted<ReportResponse>, ValidationProblem>> Request(
        ReportRequest request, ClaimsPrincipal user, JobsDbContext db, IBackgroundJobClient jobs, AuditTrail audit, TimeProvider clock, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var validation = new Validation()
            .Require(request.From != default && request.To != default, "from", "Both 'from' and 'to' are required (yyyy-MM-dd).")
            .Require(request.RunAt is null || (request.RunAt > now && request.RunAt <= now + MaxScheduleAhead), "runAt",
                "runAt must be in the future and at most 30 days ahead.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var run = JobRun.Create(JobKind.Report, user.UserId(), null, now, request.RunAt);
        var report = Report.Create(run.Id, request.From, request.To, nightly: false);
        db.JobRuns.Add(run);
        db.Reports.Add(report);
        audit.Record("report.requested", "job_run", run.Id, new { from = report.From, to = report.To, runAt = request.RunAt });
        await db.SaveChangesAsync(ct);

        var hangfireId = request.RunAt is { } runAt
            ? jobs.Schedule<ReportJob>(job => job.RunAsync(run.Id, CancellationToken.None), runAt)
            : jobs.Enqueue<ReportJob>(job => job.RunAsync(run.Id, CancellationToken.None));
        await JobRequests.AttachHangfireJobAsync(db, run.Id, hangfireId, ct);

        return TypedResults.Accepted($"/api/jobs/{run.Id}", ReportResponse.Create(run, report));
    }

    private static async Task<Ok<ReportResponse>> Get(Guid id, JobsDbContext db, CancellationToken ct)
    {
        var (run, report) = await LoadAsync(db, id, ct);
        return TypedResults.Ok(ReportResponse.Create(run, report));
    }

    private static async Task<IResult> Download(Guid id, JobsDbContext db, FileStore files, CancellationToken ct)
    {
        var (_, report) = await LoadAsync(db, id, ct);
        if (report.StoredPath is null || !files.Exists(report.StoredPath))
        {
            throw new DomainException(ErrorCodes.NotFound, "The report has not been built yet.");
        }

        return Results.File(files.OpenRead(report.StoredPath), "text/csv; charset=utf-8", $"sales-{report.From:yyyy-MM-dd}-{report.To:yyyy-MM-dd}.csv");
    }

    private static async Task<(JobRun Run, Report Report)> LoadAsync(JobsDbContext db, Guid id, CancellationToken ct)
    {
        var run = await db.JobRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.Kind == JobKind.Report, ct);
        var report = await db.Reports.AsNoTracking().SingleOrDefaultAsync(r => r.JobRunId == id, ct);
        return run is null || report is null ? throw new DomainException(ErrorCodes.NotFound, "Report not found.") : (run, report);
    }
}
