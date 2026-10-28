namespace BackgroundJobs.Infrastructure.Hangfire;

public sealed class JobsHangfireOptions
{
    public const string Section = "Hangfire";

    /// <summary>How often idle workers look for new work. Lower in tests, about a second in production.</summary>
    public TimeSpan QueuePollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Retries after the first attempt fails.</summary>
    public int RetryAttempts { get; set; } = 3;

    /// <summary>Delay before each retry; the last value repeats if there are more attempts than delays.</summary>
    public int[] RetryDelaysInSeconds { get; set; } = [15, 60, 300];

    /// <summary>A running job that has not reported for this long is considered abandoned.</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(5);
}
