using BackgroundJobs.Api.Common;
using BackgroundJobs.Api.Features.Auth;
using BackgroundJobs.Core;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Infrastructure.Persistence;
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

    private static async Task<Ok<JobRunResponse>> Get(Guid id, JobsDbContext db, CancellationToken ct) =>
        TypedResults.Ok(JobRunResponse.From(await db.JobRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "Job run not found.")));
}
