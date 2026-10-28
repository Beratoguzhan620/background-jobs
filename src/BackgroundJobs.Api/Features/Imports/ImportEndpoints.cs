using System.Security.Claims;
using System.Text;
using BackgroundJobs.Api.Features.Audit;
using BackgroundJobs.Api.Features.Auth;
using BackgroundJobs.Api.Features.Jobs;
using BackgroundJobs.Api.Common;
using BackgroundJobs.Core;
using BackgroundJobs.Core.Imports;
using BackgroundJobs.Core.Jobs;
using BackgroundJobs.Infrastructure.Files;
using BackgroundJobs.Infrastructure.Jobs;
using BackgroundJobs.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BackgroundJobs.Api.Features.Imports;

internal sealed record ImportResponse(
    JobRunResponse Run,
    string FileName,
    long SizeBytes,
    int RowsRead,
    int RowsImported,
    int RowsDuplicate,
    int RowsRejected,
    bool HasErrorFile)
{
    public static ImportResponse From(JobRun run, ImportFile file) => new(
        JobRunResponse.From(run), file.OriginalName, file.SizeBytes, file.RowsRead, file.RowsImported, file.RowsDuplicate,
        file.RowsRejected, file.ErrorFilePath is not null);
}

internal static class ImportEndpoints
{
    public const long MaxFileBytes = 50L * 1024 * 1024;

    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/imports").WithTags("Imports");

        group.MapPost("/", Upload)
            .WithSummary("Upload an order file (CSV, up to 50 MB); it is imported in the background")
            .RequireAuthorization(AuthSetup.AdminPolicy)
            // Called with a bearer token, never a cookie, so there is no cross-site form to protect against.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxFileBytes + (1024 * 1024)))
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", Get).WithSummary("An import with its row counts").RequireAuthorization(AuthSetup.ViewerPolicy);
        group.MapGet("/{id:guid}/errors", Errors).WithSummary("The rejected rows as CSV, with line numbers and reasons")
            .RequireAuthorization(AuthSetup.ViewerPolicy);
        return app;
    }

    private static async Task<Results<Accepted<ImportResponse>, ValidationProblem>> Upload(
        IFormFile? file, ClaimsPrincipal user, JobsDbContext db, FileStore files, IBackgroundJobClient jobs, AuditTrail audit,
        TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(file is { Length: > 0 }, "file", "Attach a non-empty CSV file in the 'file' field.")
            .Require(file is null || Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase),
                "file", "Only .csv files are accepted.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        StoredFile stored;
        await using (var upload = file!.OpenReadStream())
        {
            try
            {
                stored = await files.SaveAsync(upload, "uploads", ".csv", MaxFileBytes, ct);
            }
            catch (FileTooLargeException ex)
            {
                throw new DomainException(ErrorCodes.InvalidFile, ex.Message);
            }
        }

        try
        {
            await CheckHeaderAsync(files, stored.Path, ct);

            var now = clock.GetUtcNow();
            var run = JobRun.Create(JobKind.Import, user.UserId(), $"import:{stored.Sha256}", now);
            var import = ImportFile.Create(run.Id, Path.GetFileName(file.FileName), stored.Path, stored.Size, stored.Sha256, now);
            db.JobRuns.Add(run);
            db.ImportFiles.Add(import);
            audit.Record("import.requested", "job_run", run.Id, new { file = import.OriginalName, sizeBytes = stored.Size });

            await JobRequests.SaveOrRefuseDuplicateAsync(db, run, ct);

            var hangfireId = jobs.Enqueue<ImportJob>(job => job.RunAsync(run.Id, CancellationToken.None));
            await JobRequests.AttachHangfireJobAsync(db, run.Id, hangfireId, ct);

            return TypedResults.Accepted($"/api/jobs/{run.Id}", ImportResponse.From(run, import));
        }
        catch
        {
            files.Delete(stored.Path);
            throw;
        }
    }

    private static async Task<Ok<ImportResponse>> Get(Guid id, JobsDbContext db, CancellationToken ct)
    {
        var (run, file) = await LoadAsync(db, id, ct);
        return TypedResults.Ok(ImportResponse.From(run, file));
    }

    private static async Task<IResult> Errors(Guid id, JobsDbContext db, FileStore files, CancellationToken ct)
    {
        var (_, file) = await LoadAsync(db, id, ct);
        if (file.ErrorFilePath is null || !files.Exists(file.ErrorFilePath))
        {
            throw new DomainException(ErrorCodes.NotFound, "This import has no rejected rows.");
        }

        var name = $"{Path.GetFileNameWithoutExtension(file.OriginalName)}-errors.csv";
        return Results.File(files.OpenRead(file.ErrorFilePath), "text/csv; charset=utf-8", name);
    }

    private static async Task<(JobRun Run, ImportFile File)> LoadAsync(JobsDbContext db, Guid id, CancellationToken ct)
    {
        var run = await db.JobRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.Kind == JobKind.Import, ct);
        var file = await db.ImportFiles.AsNoTracking().SingleOrDefaultAsync(f => f.JobRunId == id, ct);
        return run is null || file is null ? throw new DomainException(ErrorCodes.NotFound, "Import not found.") : (run, file);
    }

    /// <summary>Refuses a file whose header is wrong straight away, instead of failing later in the worker.</summary>
    private static async Task CheckHeaderAsync(FileStore files, string path, CancellationToken ct)
    {
        await using var stream = files.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = await new CsvReader(reader).ReadRecordAsync(ct)
            ?? throw new DomainException(ErrorCodes.InvalidFile, "The file is empty.");
        if (!OrderFile.TryReadHeader(header, out _, out var problem))
        {
            throw new DomainException(ErrorCodes.InvalidFile, problem!);
        }
    }
}
