using System.Net;
using BackgroundJobs.IntegrationTests.Support;

namespace BackgroundJobs.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ReportTests(ApiFactory factory)
{
    [Fact]
    public async Task A_report_sums_the_imported_orders_per_day()
    {
        var admin = await factory.AdminClientAsync();
        var id = Guid.NewGuid().ToString("N")[..6];
        // Dates no other test uses, so the totals belong to this test alone. Türkiye has been on UTC+3 all year since 2016.
        var csv = Imports.Header + "\n"
            + $"R{id}-1,2024-02-01,a@example.com,PEN,2,10.00\n"
            + $"R{id}-2,2024-02-01T23:30:00+03:00,b@example.com,INK,1,5.50\n"
            + $"R{id}-3,2024-02-02,c@example.com,PEN,3,1.00\n"
            + $"R{id}-4,2024-02-03,d@example.com,PEN,9,1.00\n";
        var import = await (await admin.UploadAsync(csv)).ReadAsync<ImportDto>();
        await admin.WaitForAsync(import.Run.Id, "Succeeded");

        var response = await admin.RequestReportAsync("2024-02-01", "2024-02-02");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var report = await response.ReadAsync<ReportDto>();
        var run = await admin.WaitForAsync(report.Run.Id);

        Assert.Equal("Succeeded", run.Status);
        var summary = run.Result!.Value;
        Assert.Equal(3, summary.GetProperty("orders").GetInt64());
        Assert.Equal(6, summary.GetProperty("items").GetInt64());
        Assert.Equal(28.50m, summary.GetProperty("revenue").GetDecimal());
        Assert.Equal("PEN", summary.GetProperty("topSkus")[0].GetProperty("sku").GetString());

        var csvReport = await (await admin.GetAsync($"/api/reports/{report.Run.Id}/download")).Content.ReadAsStringAsync();
        Assert.Contains("2024-02-01,2,3,25.50", csvReport);
        Assert.Contains("2024-02-02,1,3,3.00", csvReport);
        Assert.Contains("total,3,6,28.50", csvReport);
    }

    [Fact]
    public async Task A_scheduled_report_waits_and_can_be_cancelled()
    {
        var admin = await factory.AdminClientAsync();
        var runAt = DateTimeOffset.UtcNow.AddHours(1);

        var report = await (await admin.RequestReportAsync("2026-10-01", "2026-10-31", runAt)).ReadAsync<ReportDto>();
        Assert.Equal("Queued", report.Run.Status);
        Assert.Equal(runAt.ToUnixTimeSeconds(), report.Run.ScheduledFor!.Value.ToUnixTimeSeconds());

        var cancelled = await admin.PostAsync($"/api/jobs/{report.Run.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal("Cancelled", (await cancelled.ReadAsync<JobRunDto>()).Status);

        var again = await admin.PostAsync($"/api/jobs/{report.Run.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("invalid_state", await again.ProblemCodeAsync());
    }

    [Theory]
    [InlineData("2026-10-31", "2026-10-01", null)]
    [InlineData("2020-01-01", "2026-01-01", null)]
    [InlineData("2026-10-01", "2026-10-02", -1)]
    [InlineData("2026-10-01", "2026-10-02", 24 * 40)]
    public async Task Impossible_reports_are_refused(string from, string to, int? hoursFromNow)
    {
        var admin = await factory.AdminClientAsync();
        var runAt = hoursFromNow is { } hours ? DateTimeOffset.UtcNow.AddHours(hours) : (DateTimeOffset?)null;

        var response = await admin.RequestReportAsync(from, to, runAt);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Viewers_cannot_ask_for_reports_or_cancel_runs()
    {
        var (viewer, _) = await factory.UserClientAsync("Viewer");
        var admin = await factory.AdminClientAsync();
        var report = await (await admin.RequestReportAsync("2026-10-01", "2026-10-02", DateTimeOffset.UtcNow.AddHours(2))).ReadAsync<ReportDto>();

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.RequestReportAsync("2026-10-01", "2026-10-02")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"/api/jobs/{report.Run.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/reports/{report.Run.Id}")).StatusCode);
    }
}
