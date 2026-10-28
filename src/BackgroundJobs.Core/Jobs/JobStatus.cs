namespace BackgroundJobs.Core.Jobs;

public enum JobStatus
{
    /// <summary>Waiting for a worker, possibly scheduled for later.</summary>
    Queued,

    Running,

    /// <summary>An attempt failed; Hangfire will try again after a delay.</summary>
    Retrying,

    Succeeded,

    /// <summary>All attempts failed. An admin can retry it.</summary>
    Failed,

    Cancelled,
}
