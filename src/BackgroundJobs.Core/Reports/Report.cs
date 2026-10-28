namespace BackgroundJobs.Core.Reports;

/// <summary>A sales report for a period of whole days (Türkiye time). Its id is the id of the job run that builds it.</summary>
public sealed class Report
{
    public const int MaxDays = 366;

    private Report()
    {
    }

    public Guid JobRunId { get; private set; }

    public DateOnly From { get; private set; }

    /// <summary>Inclusive.</summary>
    public DateOnly To { get; private set; }

    /// <summary>The nightly report rather than one an admin asked for.</summary>
    public bool Nightly { get; private set; }

    public string? StoredPath { get; private set; }

    public static Report Create(Guid jobRunId, DateOnly from, DateOnly to, bool nightly)
    {
        if (to < from)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "'to' must not be before 'from'.");
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxDays)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A report covers at most {MaxDays} days.");
        }

        return new Report { JobRunId = jobRunId, From = from, To = to, Nightly = nightly };
    }

    public void Store(string path) => StoredPath = path;
}
