using BackgroundJobs.Infrastructure.Hangfire;
using BackgroundJobs.IntegrationTests.Support;
using Hangfire;
using Hangfire.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs.IntegrationTests;

[Collection(ApiCollection.Name)]
public class WorkersTests(ApiFactory factory)
{
    [Fact]
    public async Task Two_servers_share_the_queue_and_every_run_happens_once()
    {
        using var second = new BackgroundJobServer(
            new BackgroundJobServerOptions
            {
                ServerName = "test-server-2",
                Queues = JobQueues.All,
                WorkerCount = 4,
                Activator = new AspNetCoreJobActivator(factory.Services.GetRequiredService<IServiceScopeFactory>()),
            },
            factory.Services.GetRequiredService<JobStorage>());

        var admin = await factory.AdminClientAsync();
        var uploads = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => admin.UploadAsync(Imports.OrderCsv(300))));
        var runs = await Task.WhenAll(uploads.Select(async u => await admin.WaitForAsync((await u.ReadAsync<ImportDto>()).Run.Id)));

        Assert.All(runs, run =>
        {
            Assert.Equal("Succeeded", run.Status);
            Assert.Equal(1, run.Attempts);
            Assert.Equal(300, run.Result!.Value.GetProperty("rowsImported").GetInt32());
        });

        var stats = await (await admin.GetAsync("/api/jobs/stats")).ReadAsync<JobStatsDto>();
        Assert.True(stats.Servers.Count >= 2, $"expected two servers, saw {stats.Servers.Count}");
        Assert.True(stats.RunsByStatus["Succeeded"] >= 8);

        second.SendStop();
    }
}
