namespace BackgroundJobs.Core.Jobs;

/// <summary>
/// One requested piece of background work as people see it: what it is, who asked, how far it got and
/// how it ended. Hangfire's own job (linked by <see cref="HangfireJobId"/>) handles queues and retries.
/// </summary>
public sealed class JobRun
{
    public const int DedupKeyMaxLength = 200;
    public const int ErrorMaxLength = 500;

    private JobRun()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Short, increasing number assigned by the database ("run #42"); also the paging order.</summary>
    public long Number { get; private set; }

    public JobKind Kind { get; private set; }

    public JobStatus Status { get; private set; }

    /// <summary>Identifies "the same work" (a file hash, a report period). Unique among active and successful runs.</summary>
    public string? DedupKey { get; private set; }

    public string? HangfireJobId { get; private set; }

    /// <summary>The user who asked for it; <c>null</c> for recurring runs.</summary>
    public Guid? RequestedBy { get; private set; }

    /// <summary>0 to 100.</summary>
    public int Progress { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the run should start; <c>null</c> means as soon as possible.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>Last sign of life from the worker running it.</summary>
    public DateTimeOffset? HeartbeatAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public string? Error { get; private set; }

    /// <summary>JSON summary of the outcome, shaped by the kind of job.</summary>
    public string? Result { get; private set; }

    /// <summary>Concurrency token (PostgreSQL xmin): an admin's cancel cannot silently overwrite a worker's update.</summary>
    public uint Version { get; private set; }

    public static JobRun Create(JobKind kind, Guid? requestedBy, string? dedupKey, DateTimeOffset now, DateTimeOffset? scheduledFor = null) => new()
    {
        Id = Guid.CreateVersion7(now),
        Kind = kind,
        Status = JobStatus.Queued,
        DedupKey = dedupKey,
        RequestedBy = requestedBy,
        CreatedAt = now,
        ScheduledFor = scheduledFor,
    };

    public bool IsActive => Status is JobStatus.Queued or JobStatus.Running or JobStatus.Retrying;

    public void Cancel(DateTimeOffset now)
    {
        if (!IsActive)
        {
            throw new DomainException(ErrorCodes.InvalidState, $"A {Status.ToString().ToLowerInvariant()} run cannot be cancelled.");
        }

        Status = JobStatus.Cancelled;
        FinishedAt = now;
    }

    /// <summary>Puts a failed or cancelled run back in the queue under a new Hangfire job.</summary>
    public void Requeue()
    {
        if (Status is not (JobStatus.Failed or JobStatus.Cancelled))
        {
            throw new DomainException(ErrorCodes.InvalidState, "Only failed or cancelled runs can be retried.");
        }

        Status = JobStatus.Queued;
        Progress = 0;
        Attempts = 0;
        Error = null;
        FinishedAt = null;
        HeartbeatAt = null;
        ScheduledFor = null;
    }
}
