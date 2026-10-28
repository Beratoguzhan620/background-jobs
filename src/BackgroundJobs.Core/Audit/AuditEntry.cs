namespace BackgroundJobs.Core.Audit;

/// <summary>Who did what to which resource. Rows are only ever inserted.</summary>
public sealed class AuditEntry
{
    private AuditEntry()
    {
        Action = string.Empty;
        TargetType = string.Empty;
    }

    public long Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary><c>null</c> for the system itself (recurring jobs) or an unknown caller.</summary>
    public Guid? ActorId { get; private set; }

    public string Action { get; private set; }

    public string TargetType { get; private set; }

    public string? TargetId { get; private set; }

    /// <summary>JSON object with extra facts; never secrets.</summary>
    public string? Details { get; private set; }

    public string? CorrelationId { get; private set; }

    public static AuditEntry Create(DateTimeOffset occurredAt, Guid? actorId, string action, string targetType, string? targetId,
        string? details, string? correlationId) => new()
    {
        OccurredAt = occurredAt,
        ActorId = actorId,
        Action = action,
        TargetType = targetType,
        TargetId = targetId,
        Details = details,
        CorrelationId = correlationId,
    };
}
