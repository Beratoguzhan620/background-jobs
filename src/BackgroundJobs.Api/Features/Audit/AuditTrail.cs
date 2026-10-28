using System.Text.Json;
using BackgroundJobs.Api.Common;
using BackgroundJobs.Core.Audit;
using BackgroundJobs.Infrastructure.Persistence;

namespace BackgroundJobs.Api.Features.Audit;

/// <summary>Adds audit entries to the current unit of work, so they are saved together with the change they describe.</summary>
internal sealed class AuditTrail(JobsDbContext db, IHttpContextAccessor http, TimeProvider clock)
{
    public void Record(string action, string targetType, object? targetId, object? details = null, Guid? actorId = null)
    {
        var context = http.HttpContext;
        db.AuditEntries.Add(AuditEntry.Create(
            clock.GetUtcNow(),
            actorId ?? context?.User.UserIdOrNull(),
            action,
            targetType,
            targetId?.ToString(),
            details is null ? null : JsonSerializer.Serialize(details, JsonSerializerOptions.Web),
            context?.TraceIdentifier));
    }
}
