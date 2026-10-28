using System.Text;
using BackgroundJobs.Core.Imports;
using BackgroundJobs.Infrastructure.Files;
using BackgroundJobs.Infrastructure.Hangfire;
using BackgroundJobs.Infrastructure.Persistence;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace BackgroundJobs.Infrastructure.Jobs;

public sealed record ImportSummary(int RowsRead, int RowsImported, int RowsDuplicate, int RowsRejected);

/// <summary>
/// Reads an uploaded order file in chunks and inserts the valid rows. Safe to run again: orders are inserted
/// with ON CONFLICT DO NOTHING and the counts are taken from the database at the end, so a retry after a
/// half-finished attempt neither duplicates orders nor miscounts them.
/// </summary>
public sealed class ImportJob(JobRunner runner, JobsDbContext db, FileStore files)
{
    public const int BatchSize = 500;

    [Queue(JobQueues.Imports)]
    [JobDisplayName("Import order file (run {0})")]
    public Task RunAsync(Guid runId, CancellationToken cancellationToken) =>
        runner.RunAsync(runId, context => ImportAsync(context), cancellationToken);

    private async Task<object?> ImportAsync(JobContext context)
    {
        var ct = context.Cancellation;
        var file = await db.ImportFiles.SingleAsync(f => f.JobRunId == context.RunId, ct);
        if (file.StoredPath is null || !files.Exists(file.StoredPath))
        {
            throw new InvalidOperationException("The uploaded file is no longer in storage.");
        }

        await using var stream = files.OpenRead(file.StoredPath);
        using var text = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var csv = new CsvReader(text);

        var header = await csv.ReadRecordAsync(ct) ?? throw new InvalidOperationException("The file is empty.");
        if (!OrderFile.TryReadHeader(header, out var positions, out var problem))
        {
            throw new InvalidOperationException(problem);
        }

        var errors = new ErrorFile(files);
        var batch = new List<OrderRow>(BatchSize);
        int read = 0, valid = 0, rejected = 0;

        try
        {
            while (await csv.ReadRecordAsync(ct) is { } fields)
            {
                if (OrderFile.IsBlank(fields))
                {
                    continue;
                }

                read++;
                var row = OrderFile.Parse(fields, positions, out var reason);
                if (row is null)
                {
                    rejected++;
                    await errors.AddAsync(csv.LineNumber, fields, positions, reason!);
                    continue;
                }

                valid++;
                batch.Add(row);
                if (batch.Count == BatchSize)
                {
                    await InsertAsync(batch, file.JobRunId, ct);
                    batch.Clear();
                    await context.ReportProgressAsync((int)(stream.Position * 100 / Math.Max(1, stream.Length)));
                }
            }

            if (batch.Count > 0)
            {
                await InsertAsync(batch, file.JobRunId, ct);
            }
        }
        finally
        {
            await errors.DisposeAsync();
        }

        // Counted from the database so a retried import reports the same numbers as a clean one.
        var imported = await db.Orders.CountAsync(o => o.ImportFileId == file.JobRunId, ct);
        files.Delete(file.ErrorFilePath);
        file.RecordOutcome(read, imported, valid - imported, rejected, rejected > 0 ? errors.Path : null);
        await db.SaveChangesAsync(ct);

        return new ImportSummary(read, imported, valid - imported, rejected);
    }

    private Task<int> InsertAsync(List<OrderRow> rows, Guid importFileId, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO orders (order_no, ordered_at, customer_email, sku, quantity, unit_price_minor, import_file_id)
            SELECT o.order_no, o.ordered_at, o.customer_email, o.sku, o.quantity, o.unit_price_minor, @import_file_id
            FROM unnest(@order_nos, @ordered_ats, @emails, @skus, @quantities, @prices)
                 AS o(order_no, ordered_at, customer_email, sku, quantity, unit_price_minor)
            ON CONFLICT (order_no) DO NOTHING
            """,
            [
                new NpgsqlParameter("import_file_id", importFileId),
                new NpgsqlParameter("order_nos", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = rows.Select(r => r.OrderNo).ToArray() },
                new NpgsqlParameter("ordered_ats", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = rows.Select(r => r.OrderedAt.ToUniversalTime()).ToArray() },
                new NpgsqlParameter("emails", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = rows.Select(r => r.CustomerEmail).ToArray() },
                new NpgsqlParameter("skus", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = rows.Select(r => r.Sku).ToArray() },
                new NpgsqlParameter("quantities", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = rows.Select(r => r.Quantity).ToArray() },
                new NpgsqlParameter("prices", NpgsqlDbType.Array | NpgsqlDbType.Bigint) { Value = rows.Select(r => r.UnitPriceMinor).ToArray() },
            ],
            ct);

    /// <summary>Rejected rows with their line number and reason, written only if there are any.</summary>
    private sealed class ErrorFile(FileStore files) : IAsyncDisposable
    {
        private StreamWriter? _writer;

        public string? Path { get; private set; }

        public async Task AddAsync(int line, string[] fields, IReadOnlyDictionary<string, int> positions, string reason)
        {
            if (_writer is null)
            {
                var (path, stream) = files.Create("import-errors", ".csv");
                Path = path;
                _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                await _writer.WriteLineAsync("line,order_no,reason");
            }

            var orderNo = positions["order_no"] < fields.Length ? fields[positions["order_no"]] : string.Empty;
            await _writer.WriteLineAsync($"{line},{Quote(orderNo)},{Quote(reason)}");
        }

        public async ValueTask DisposeAsync()
        {
            if (_writer is not null)
            {
                await _writer.DisposeAsync();
            }
        }

        // Quotes the value and neutralises a leading formula character so the file is safe to open in Excel.
        private static string Quote(string value)
        {
            if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
            {
                value = "'" + value;
            }

            return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
        }
    }
}
