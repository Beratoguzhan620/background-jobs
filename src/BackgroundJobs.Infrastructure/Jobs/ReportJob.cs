using System.Data;
using System.Globalization;
using System.Text;
using BackgroundJobs.Core.Reports;
using BackgroundJobs.Infrastructure.Files;
using BackgroundJobs.Infrastructure.Hangfire;
using BackgroundJobs.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BackgroundJobs.Infrastructure.Jobs;

public sealed record DailySales(DateOnly Day, long Orders, long Items, decimal Revenue);

public sealed record SkuSales(string Sku, long Items, decimal Revenue);

public sealed record ReportSummary(DateOnly From, DateOnly To, long Orders, long Items, decimal Revenue, IReadOnlyList<SkuSales> TopSkus);

/// <summary>Sales per day and the best-selling SKUs for a period, as a CSV file and a JSON summary.</summary>
public sealed class ReportJob(JobRunner runner, JobsDbContext db, FileStore files)
{
    /// <summary>Days are counted in Türkiye time, where the shop is.</summary>
    public const string TimeZone = "Europe/Istanbul";

    [Queue(JobQueues.Reports)]
    [JobDisplayName("Sales report (run {0})")]
    public Task RunAsync(Guid runId, CancellationToken cancellationToken) =>
        runner.RunAsync(runId, context => BuildAsync(context), cancellationToken);

    private async Task<object?> BuildAsync(JobContext context)
    {
        var ct = context.Cancellation;
        var report = await db.Reports.SingleAsync(r => r.JobRunId == context.RunId, ct);

        var daily = await QueryAsync(
            $"""
            SELECT (ordered_at AT TIME ZONE '{TimeZone}')::date, count(*), sum(quantity)::bigint, sum(quantity::bigint * unit_price_minor)::bigint
            FROM orders
            WHERE (ordered_at AT TIME ZONE '{TimeZone}')::date BETWEEN @from AND @to
            GROUP BY 1 ORDER BY 1
            """,
            report,
            r => new DailySales(r.GetFieldValue<DateOnly>(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3) / 100m),
            ct);

        await context.ReportProgressAsync(50);

        var top = await QueryAsync(
            $"""
            SELECT sku, sum(quantity)::bigint, sum(quantity::bigint * unit_price_minor)::bigint AS revenue
            FROM orders
            WHERE (ordered_at AT TIME ZONE '{TimeZone}')::date BETWEEN @from AND @to
            GROUP BY sku ORDER BY revenue DESC, sku LIMIT 5
            """,
            report,
            r => new SkuSales(r.GetString(0), r.GetInt64(1), r.GetInt64(2) / 100m),
            ct);

        var (path, stream) = files.Create("reports", ".csv");
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
        {
            await writer.WriteLineAsync("date,orders,items,revenue");
            foreach (var day in daily)
            {
                await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"{day.Day:yyyy-MM-dd},{day.Orders},{day.Items},{day.Revenue:0.00}"));
            }

            await writer.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                $"total,{daily.Sum(d => d.Orders)},{daily.Sum(d => d.Items)},{daily.Sum(d => d.Revenue):0.00}"));
        }

        files.Delete(report.StoredPath);
        report.Store(path);
        await db.SaveChangesAsync(ct);

        return new ReportSummary(report.From, report.To, daily.Sum(d => d.Orders), daily.Sum(d => d.Items), daily.Sum(d => d.Revenue), top);
    }

    private async Task<List<T>> QueryAsync<T>(string sql, Report report, Func<NpgsqlDataReader, T> map, CancellationToken ct)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(ct);
        }

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("from", report.From);
        command.Parameters.AddWithValue("to", report.To);

        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}
