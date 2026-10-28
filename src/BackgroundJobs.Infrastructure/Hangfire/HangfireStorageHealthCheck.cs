using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BackgroundJobs.Infrastructure.Hangfire;

/// <summary>Healthy when the job storage answers; reports how many workers are connected.</summary>
public sealed class HangfireStorageHealthCheck(JobStorage storage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var monitoring = storage.GetMonitoringApi();
            var stats = monitoring.GetStatistics();
            return Task.FromResult(HealthCheckResult.Healthy(data: new Dictionary<string, object>
            {
                ["servers"] = stats.Servers,
                ["enqueued"] = stats.Enqueued,
                ["failed"] = stats.Failed,
            }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Job storage is not reachable.", ex));
        }
    }
}
