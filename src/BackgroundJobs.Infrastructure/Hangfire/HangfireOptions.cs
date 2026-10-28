namespace BackgroundJobs.Infrastructure.Hangfire;

public sealed class JobsHangfireOptions
{
    public const string Section = "Hangfire";

    /// <summary>How often idle workers look for new work. Lower in tests, about a second in production.</summary>
    public TimeSpan QueuePollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Retries after the first attempt fails.</summary>
    public int RetryAttempts { get; set; } = 3;

    /// <summary>Delay before each retry; the last value repeats if there are more attempts than delays.</summary>
    public int[]? RetryDelaysInSeconds { get; set; }

    /// <summary>Configured delays, or 15 s, 1 min, 5 min. Kept separate because configuration binding appends to arrays.</summary>
    public int[] EffectiveRetryDelays => RetryDelaysInSeconds is { Length: > 0 } delays ? delays : [15, 60, 300];

    /// <summary>Uploaded files are deleted this long after their import finished.</summary>
    public TimeSpan UploadRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>A queued run with no Hangfire job after this long is re-enqueued by housekeeping.</summary>
    public TimeSpan OrphanAfter { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>A running job that has not reported for this long is considered abandoned.</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
