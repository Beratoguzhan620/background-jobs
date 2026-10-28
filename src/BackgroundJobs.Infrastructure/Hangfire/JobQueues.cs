namespace BackgroundJobs.Infrastructure.Hangfire;

/// <summary>
/// Separate queues so a pile of imports cannot hold up the nightly report or housekeeping.
/// Hangfire queue names must be lower case.
/// </summary>
public static class JobQueues
{
    public const string Imports = "imports";
    public const string Reports = "reports";
    public const string Maintenance = "maintenance";

    /// <summary>Order matters: a worker takes from the first queue that has work.</summary>
    public static readonly string[] All = [Reports, Maintenance, Imports, "default"];
}
