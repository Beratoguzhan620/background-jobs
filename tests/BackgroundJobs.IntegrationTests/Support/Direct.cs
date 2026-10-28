using System.Text;
using BackgroundJobs.Core.Imports;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Infrastructure.Files;
using BackgroundJobs.Infrastructure.Jobs;
using BackgroundJobs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs.IntegrationTests.Support;

/// <summary>Reaches past the API to set up situations a client cannot create, such as a run Hangfire does not know about.</summary>
public static class Direct
{
    public static async Task<Guid> QueueImportWithoutHangfireAsync(this ApiFactory factory, string csv, DateTimeOffset? createdAt = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var files = services.GetRequiredService<FileStore>();
        var db = services.GetRequiredService<JobsDbContext>();

        await using var content = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var stored = await files.SaveAsync(content, "uploads", ".csv", long.MaxValue, CancellationToken.None);
        var now = createdAt ?? DateTimeOffset.UtcNow;
        var run = JobRun.Create(JobKind.Import, null, $"import:{stored.Sha256}", now);
        db.JobRuns.Add(run);
        db.ImportFiles.Add(ImportFile.Create(run.Id, "direct.csv", stored.Path, stored.Size, stored.Sha256, now));
        await db.SaveChangesAsync();
        return run.Id;
    }

    public static async Task RunImportAsync(this ApiFactory factory, Guid runId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ImportJob>().RunAsync(runId, CancellationToken.None);
    }

    public static async Task WithScopeAsync(this ApiFactory factory, Func<IServiceProvider, Task> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider);
    }
}
