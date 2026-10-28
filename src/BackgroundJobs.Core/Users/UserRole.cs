namespace BackgroundJobs.Core.Users;

public enum UserRole
{
    /// <summary>Starts, cancels and retries jobs, manages users, opens the dashboard.</summary>
    Admin,

    /// <summary>Reads job status, results and statistics.</summary>
    Viewer,
}
