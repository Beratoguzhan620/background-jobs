using System.Net;
using System.Text.Json;
using BackgroundJobs.Core.Imports;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Infrastructure.Jobs;
using BackgroundJobs.Infrastructure.Persistence;
using BackgroundJobs.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ImportTests(ApiFactory factory)
{
    [Fact]
    public async Task An_order_file_is_imported_in_the_background()
    {
        var admin = await factory.AdminClientAsync();

        var response = await admin.UploadAsync(Imports.OrderCsv(3), "october.csv");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var accepted = await response.ReadAsync<ImportDto>();
        Assert.Equal("Queued", accepted.Run.Status);
        Assert.Equal($"/api/jobs/{accepted.Run.Id}", response.Headers.Location?.ToString());

        var run = await admin.WaitForAsync(accepted.Run.Id);
        Assert.Equal("Succeeded", run.Status);
        Assert.Equal(100, run.Progress);
        Assert.Equal(1, run.Attempts);
        Assert.Equal(3, run.Result!.Value.GetProperty("rowsImported").GetInt32());

        var import = await (await admin.GetAsync($"/api/imports/{run.Id}")).ReadAsync<ImportDto>();
        Assert.Equal("october.csv", import.FileName);
        Assert.Equal((3, 3, 0, 0), (import.RowsRead, import.RowsImported, import.RowsDuplicate, import.RowsRejected));
        Assert.False(import.HasErrorFile);
    }

    [Fact]
    public async Task Rejected_rows_are_listed_with_their_line_and_reason()
    {
        var admin = await factory.AdminClientAsync();
        var csv = Imports.Header + "\n"
            + "OK-1,2026-10-01,a@example.com,SKU-1,1,10.00\n"
            + "BAD-1,2026-10-01,a@example.com,SKU-1,0,10.00\n"
            + "OK-2,2026-10-02T10:30:00+03:00,b@example.com,SKU-2,2,5.5\n"
            + "BAD-2,yesterday,b@example.com,SKU-2,2,5.50\n"
            + "\"BAD,3\",2026-10-03,not-an-email,SKU-3,1,1.00\n";
        var accepted = await (await admin.UploadAsync(csv.Replace("OK-", $"{Guid.NewGuid():N}"[..6] + "-", StringComparison.Ordinal))).ReadAsync<ImportDto>();

        await admin.WaitForAsync(accepted.Run.Id, "Succeeded");
        var import = await (await admin.GetAsync($"/api/imports/{accepted.Run.Id}")).ReadAsync<ImportDto>();
        var errors = await (await admin.GetAsync($"/api/imports/{accepted.Run.Id}/errors")).Content.ReadAsStringAsync();

        Assert.Equal((5, 2, 3), (import.RowsRead, import.RowsImported, import.RowsRejected));
        Assert.True(import.HasErrorFile);
        Assert.Contains("line,order_no,reason", errors);
        Assert.Contains("3,\"BAD-1\",\"quantity must be", errors);
        Assert.Contains("5,\"BAD-2\",\"ordered_at is not", errors);
        Assert.Contains("6,\"BAD,3\",\"order_no must be", errors);
    }

    [Fact]
    public async Task The_same_file_twice_is_refused_with_the_first_run_id()
    {
        var admin = await factory.AdminClientAsync();
        var csv = Imports.OrderCsv(5);

        var first = await (await admin.UploadAsync(csv)).ReadAsync<ImportDto>();
        var second = await admin.UploadAsync(csv, "renamed.csv");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("duplicate_job", await second.ProblemCodeAsync());
        using var problem = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(first.Run.Id, problem.RootElement.GetProperty("jobRunId").GetGuid());
    }

    [Fact]
    public async Task Orders_already_imported_by_another_file_are_counted_as_duplicates()
    {
        var admin = await factory.AdminClientAsync();
        var prefix = Guid.NewGuid().ToString("N")[..8];
        var first = await (await admin.UploadAsync(Imports.OrderCsv(10, prefix))).ReadAsync<ImportDto>();
        await admin.WaitForAsync(first.Run.Id, "Succeeded");

        var overlapping = await (await admin.UploadAsync(Imports.OrderCsv(15, prefix))).ReadAsync<ImportDto>();
        await admin.WaitForAsync(overlapping.Run.Id, "Succeeded");

        var import = await (await admin.GetAsync($"/api/imports/{overlapping.Run.Id}")).ReadAsync<ImportDto>();
        Assert.Equal((15, 5, 10), (import.RowsRead, import.RowsImported, import.RowsDuplicate));
    }

    [Theory]
    [InlineData("order_no,sku\nA-1,SKU-1\n", "orders.csv", "invalid_file")]
    [InlineData("", "orders.csv", "validation_failed")]
    [InlineData("order_no\n", "orders.txt", "validation_failed")]
    public async Task Unusable_files_are_refused_before_a_run_is_created(string content, string name, string code)
    {
        var admin = await factory.AdminClientAsync();

        var response = await admin.UploadAsync(content, name);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Viewers_can_follow_imports_but_not_start_them()
    {
        var admin = await factory.AdminClientAsync();
        var (viewer, _) = await factory.UserClientAsync("Viewer");
        var accepted = await (await admin.UploadAsync(Imports.OrderCsv(1))).ReadAsync<ImportDto>();

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.UploadAsync(Imports.OrderCsv(1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/imports/{accepted.Run.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_large_file_is_imported_in_chunks()
    {
        var admin = await factory.AdminClientAsync();

        var accepted = await (await admin.UploadAsync(Imports.OrderCsv(20_000))).ReadAsync<ImportDto>();
        var run = await admin.WaitForAsync(accepted.Run.Id);

        Assert.Equal("Succeeded", run.Status);
        Assert.Equal(20_000, run.Result!.Value.GetProperty("rowsImported").GetInt32());
    }

    [Fact]
    public async Task A_run_delivered_to_two_workers_at_once_is_done_once()
    {
        // A queued run that Hangfire does not know about, so only the two calls below can pick it up.
        var runId = await QueueImportWithoutHangfireAsync(Imports.OrderCsv(1_200));

        await Task.WhenAll(RunImportAsync(runId), RunImportAsync(runId));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        var run = await db.JobRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        Assert.Equal(JobStatus.Succeeded, run.Status);
        Assert.Equal(1, run.Attempts);
        Assert.Equal(1_200, await db.Orders.CountAsync(o => o.ImportFileId == runId));
    }

    private async Task RunImportAsync(Guid runId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ImportJob>().RunAsync(runId, CancellationToken.None);
    }

    private async Task<Guid> QueueImportWithoutHangfireAsync(string csv)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var files = services.GetRequiredService<BackgroundJobs.Infrastructure.Files.FileStore>();
        var db = services.GetRequiredService<JobsDbContext>();

        await using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var stored = await files.SaveAsync(content, "uploads", ".csv", long.MaxValue, CancellationToken.None);
        var run = JobRun.Create(JobKind.Import, null, $"import:{stored.Sha256}", DateTimeOffset.UtcNow);
        db.JobRuns.Add(run);
        db.ImportFiles.Add(ImportFile.Create(run.Id, "direct.csv", stored.Path, stored.Size, stored.Sha256, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return run.Id;
    }
}
