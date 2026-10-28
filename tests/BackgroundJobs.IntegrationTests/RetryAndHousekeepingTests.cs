using System.Net;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Core.Reports;
using BackgroundJobs.Infrastructure.Jobs;
using BackgroundJobs.Infrastructure.Persistence;
using BackgroundJobs.IntegrationTests.Support;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs.IntegrationTests;

[Collection(ApiCollection.Name)]
public class RetryAndHousekeepingTests(ApiFactory factory)
{
    [Fact]
    public async Task A_failing_job_is_retried_then_marked_failed_and_can_be_retried_by_hand()
    {
        var admin = await factory.AdminClientAsync();
        var csv = Imports.OrderCsv(25);
        var runId = await factory.QueueImportWithoutHangfireAsync(csv);

        // Take the uploaded file away so every attempt fails.
        var storedPath = await StoredPathAsync(runId);
        var fullPath = Path.Combine(factory.StorageRoot, storedPath);
        var original = await File.ReadAllBytesAsync(fullPath);
        File.Delete(fullPath);
        await factory.WithScopeAsync(services =>
        {
            services.GetRequiredService<IBackgroundJobClient>().Enqueue<ImportJob>(job => job.RunAsync(runId, CancellationToken.None));
            return Task.CompletedTask;
        });

        var failed = await admin.WaitForAsync(runId, "Failed");
        Assert.Equal(3, failed.Attempts); // the first attempt and two retries
        Assert.Contains("no longer in storage", failed.Error);
        Assert.NotNull(failed.FinishedAt);

        // Put the file back and retry by hand.
        await File.WriteAllBytesAsync(fullPath, original);
        var retried = await admin.PostAsync($"/api/jobs/{runId}/retry", null);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        Assert.Equal("Queued", (await retried.ReadAsync<JobRunDto>()).Status);

        var succeeded = await admin.WaitForAsync(runId, "Succeeded");
        Assert.Equal(1, succeeded.Attempts);
        Assert.Null(succeeded.Error);
        Assert.Equal(25, succeeded.Result!.Value.GetProperty("rowsImported").GetInt32());

        var audit = (await (await admin.GetAsync($"/api/audit?targetId={runId}&action=job.retried")).ReadAsync<AuditPageDto>()).Items;
        using var details = System.Text.Json.JsonDocument.Parse(Assert.Single(audit).Details!);
        Assert.Equal(3, details.RootElement.GetProperty("previousAttempts").GetInt32());
    }

    [Fact]
    public async Task Only_failed_or_cancelled_runs_can_be_retried()
    {
        var admin = await factory.AdminClientAsync();
        var import = await (await admin.UploadAsync(Imports.OrderCsv(2))).ReadAsync<ImportDto>();
        await admin.WaitForAsync(import.Run.Id, "Succeeded");

        var retry = await admin.PostAsync($"/api/jobs/{import.Run.Id}/retry", null);
        var cancel = await admin.PostAsync($"/api/jobs/{import.Run.Id}/cancel", null);

        Assert.Equal("invalid_state", await retry.ProblemCodeAsync());
        Assert.Equal("invalid_state", await cancel.ProblemCodeAsync());
    }

    [Fact]
    public async Task The_nightly_report_is_made_once_even_if_the_schedule_fires_twice()
    {
        await Task.WhenAll(RunNightlyAsync(), RunNightlyAsync(), RunNightlyAsync());

        var yesterday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(ReportJob.TimeZone)).DateTime).AddDays(-1);
        await factory.WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<JobsDbContext>();
            var runs = await db.JobRuns.AsNoTracking().Where(r => r.DedupKey == $"report:nightly:{yesterday:yyyy-MM-dd}").ToListAsync();
            var run = Assert.Single(runs);
            Assert.Null(run.RequestedBy);
            Assert.True((await db.Reports.SingleAsync(r => r.JobRunId == run.Id)).Nightly);
        });
    }

    [Fact]
    public async Task Housekeeping_deletes_old_uploads_and_rescues_orphaned_runs()
    {
        var admin = await factory.AdminClientAsync();

        // A finished import whose upload is past the retention period.
        var oldImport = await factory.QueueImportWithoutHangfireAsync(Imports.OrderCsv(3), DateTimeOffset.UtcNow.AddDays(-30));
        await factory.RunImportAsync(oldImport);
        var oldPath = await StoredPathAsync(oldImport);

        // A report run saved but never handed to Hangfire.
        Guid orphanId = default;
        await factory.WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<JobsDbContext>();
            var orphan = JobRun.Create(JobKind.Report, null, null, DateTimeOffset.UtcNow.AddMinutes(-10));
            db.JobRuns.Add(orphan);
            db.Reports.Add(Report.Create(orphan.Id, new DateOnly(2001, 1, 1), new DateOnly(2001, 1, 2), nightly: false));
            await db.SaveChangesAsync();
            orphanId = orphan.Id;
        });

        await factory.WithScopeAsync(services => services.GetRequiredService<RecurringJobs>().CleanupAsync(CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(factory.StorageRoot, oldPath)));
        await factory.WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<JobsDbContext>();
            Assert.Null((await db.ImportFiles.AsNoTracking().SingleAsync(f => f.JobRunId == oldImport)).StoredPath);
            var cleanup = await db.JobRuns.AsNoTracking().Where(r => r.Kind == JobKind.Cleanup).OrderByDescending(r => r.Number).FirstAsync();
            Assert.Equal(JobStatus.Succeeded, cleanup.Status);
        });

        Assert.Equal("Succeeded", (await admin.WaitForAsync(orphanId)).Status);
    }

    private async Task RunNightlyAsync() =>
        await factory.WithScopeAsync(services => services.GetRequiredService<RecurringJobs>().NightlyReportAsync(CancellationToken.None));

    private async Task<string> StoredPathAsync(Guid runId)
    {
        string path = string.Empty;
        await factory.WithScopeAsync(async services =>
            path = (await services.GetRequiredService<JobsDbContext>().ImportFiles.AsNoTracking().SingleAsync(f => f.JobRunId == runId)).StoredPath!);
        return path;
    }
}
