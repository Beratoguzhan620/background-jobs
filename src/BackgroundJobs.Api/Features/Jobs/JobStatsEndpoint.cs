using BackgroundJobs.Api.Features.Auth;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace BackgroundJobs.Api.Features.Jobs;

internal sealed record QueueStats(string Name, long Length);

internal sealed record ServerStats(string Name, int Workers, IReadOnlyList<string> Queues, DateTime? Heartbeat);

internal sealed record JobStats(
    IReadOnlyDictionary<JobStatus, int> RunsByStatus,
    long Enqueued,
    long Scheduled,
    long Processing,
    long Failed,
    IReadOnlyList<QueueStats> Queues,
    IReadOnlyList<ServerStats> Servers);

internal static class JobStatsEndpoint
{
    public static RouteGroupBuilder MapJobStats(this RouteGroupBuilder jobs)
    {
        jobs.MapGet("/stats", Get).WithSummary("Runs by status, queue lengths and connected workers")
            .RequireAuthorization(AuthSetup.ViewerPolicy);
        return jobs;
    }

    private static async Task<Ok<JobStats>> Get(JobsDbContext db, JobStorage storage, CancellationToken ct)
    {
        var byStatus = await db.JobRuns.AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        foreach (var status in Enum.GetValues<JobStatus>())
        {
            byStatus.TryAdd(status, 0);
        }

        var monitoring = storage.GetMonitoringApi();
        var stats = monitoring.GetStatistics();
        var queues = monitoring.Queues().Select(q => new QueueStats(q.Name, q.Length)).OrderBy(q => q.Name, StringComparer.Ordinal).ToList();
        var servers = monitoring.Servers()
            .Select(s => new ServerStats(s.Name, s.WorkersCount, s.Queues.ToList(), s.Heartbeat))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        return TypedResults.Ok(new JobStats(byStatus, stats.Enqueued, stats.Scheduled, stats.Processing, stats.Failed, queues, servers));
    }
}
