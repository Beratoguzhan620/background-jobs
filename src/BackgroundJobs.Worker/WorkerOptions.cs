namespace BackgroundJobs.Worker;

internal sealed class WorkerOptions
{
    public const string Section = "Worker";

    /// <summary>Jobs this process runs at the same time.</summary>
    public int Concurrency { get; set; } = 4;
}
