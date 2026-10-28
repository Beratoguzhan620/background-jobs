using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace BackgroundJobs.IntegrationTests.Support;

public sealed record ImportDto(JobRunDto Run, string FileName, long SizeBytes, int RowsRead, int RowsImported, int RowsDuplicate, int RowsRejected, bool HasErrorFile);

public static class Imports
{
    public const string Header = "order_no,ordered_at,customer_email,sku,quantity,unit_price";

    /// <summary>A valid order file with <paramref name="count"/> rows and order numbers unique to this call.</summary>
    public static string OrderCsv(int count, string? prefix = null)
    {
        prefix ??= Guid.NewGuid().ToString("N")[..8];
        var csv = new StringBuilder(Header).Append('\n');
        for (var i = 1; i <= count; i++)
        {
            var day = 1 + (i % 28);
            csv.Append(CultureInfo.InvariantCulture, $"{prefix}-{i},2026-10-{day:00},customer{i % 50}@example.com,SKU-{i % 7},{1 + (i % 3)},{(i % 90) + 9.99m}\n");
        }

        return csv.ToString();
    }

    public static Task<HttpResponseMessage> UploadAsync(this HttpClient client, string csv, string fileName = "orders.csv")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", fileName);
        return client.PostAsync("/api/imports", content);
    }

    /// <summary>Polls the run until it reaches one of the given statuses.</summary>
    public static async Task<JobRunDto> WaitForAsync(this HttpClient client, Guid runId, params string[] statuses)
    {
        statuses = statuses.Length == 0 ? ["Succeeded", "Failed", "Cancelled"] : statuses;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        JobRunDto run;
        do
        {
            run = await (await client.GetAsync($"/api/jobs/{runId}")).ReadAsync<JobRunDto>();
            if (statuses.Contains(run.Status))
            {
                return run;
            }

            await Task.Delay(200);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException($"Run {runId} stayed {run.Status}; expected {string.Join(" or ", statuses)}.");
    }
}
