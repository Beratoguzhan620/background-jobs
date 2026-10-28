using System.Globalization;
using System.Text;
using BackgroundJobs.Api.Common;
using BackgroundJobs.Api.Features.Auth;
using BackgroundJobs.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BackgroundJobs.Api.Features.Audit;

internal sealed record AuditEntryResponse(long Id, DateTimeOffset OccurredAt, Guid? ActorId, string Action, string TargetType,
    string? TargetId, string? Details, string? CorrelationId);

internal sealed record AuditPage(IReadOnlyList<AuditEntryResponse> Items, string? NextCursor);

internal static class AuditEndpoints
{
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit", Search).WithTags("Audit").WithSummary("Search the audit trail, newest first")
            .RequireAuthorization(AuthSetup.AdminPolicy);
        return app;
    }

    private static async Task<Results<Ok<AuditPage>, ValidationProblem>> Search(JobsDbContext db, CancellationToken ct,
        Guid? actorId = null, string? targetId = null, string? action = null, int? limit = null, string? cursor = null)
    {
        long cursorId = 0;
        var validation = new Validation()
            .Require(limit is null or (>= 1 and <= MaxPageSize), "limit", $"Limit must be between 1 and {MaxPageSize}.")
            .Require(cursor is null || TryDecode(cursor, out cursorId), "cursor", "The cursor is not valid.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var query = db.AuditEntries.AsNoTracking();
        if (actorId is { } actor)
        {
            query = query.Where(e => e.ActorId == actor);
        }

        if (!string.IsNullOrWhiteSpace(targetId))
        {
            query = query.Where(e => e.TargetId == targetId);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(e => e.Action == action);
        }

        if (cursor is not null)
        {
            query = query.Where(e => e.Id < cursorId);
        }

        var size = limit ?? 50;
        var rows = await query.OrderByDescending(e => e.Id).Take(size + 1).ToListAsync(ct);
        var page = rows.Take(size).ToList();
        var next = rows.Count > size
            ? WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(page[^1].Id.ToString(CultureInfo.InvariantCulture)))
            : null;

        return TypedResults.Ok(new AuditPage(
            page.Select(e => new AuditEntryResponse(e.Id, e.OccurredAt, e.ActorId, e.Action, e.TargetType, e.TargetId, e.Details, e.CorrelationId)).ToList(),
            next));
    }

    private static bool TryDecode(string cursor, out long id)
    {
        id = 0;
        try
        {
            return long.TryParse(Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor)), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
