using System.Net.Http.Json;

namespace BackgroundJobs.IntegrationTests.Support;

public sealed record ReportDto(JobRunDto Run, DateOnly From, DateOnly To, bool Nightly, bool HasFile);

public static class Reports
{
    public static Task<HttpResponseMessage> RequestReportAsync(this HttpClient client, string from, string to, DateTimeOffset? runAt = null) =>
        client.PostAsJsonAsync("/api/reports", new { from, to, runAt });
}
