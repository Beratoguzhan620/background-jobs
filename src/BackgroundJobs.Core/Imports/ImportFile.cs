namespace BackgroundJobs.Core.Imports;

/// <summary>An uploaded order file and what came of it. Its id is the id of the job run that imports it.</summary>
public sealed class ImportFile
{
    public const int NameMaxLength = 200;

    private ImportFile()
    {
        OriginalName = string.Empty;
        Sha256 = string.Empty;
    }

    public Guid JobRunId { get; private set; }

    public string OriginalName { get; private set; }

    /// <summary>Relative path in file storage; cleared when housekeeping deletes the upload.</summary>
    public string? StoredPath { get; private set; }

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; }

    public int RowsRead { get; private set; }

    public int RowsImported { get; private set; }

    /// <summary>Valid rows whose order number was already in the database.</summary>
    public int RowsDuplicate { get; private set; }

    public int RowsRejected { get; private set; }

    public string? ErrorFilePath { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public static ImportFile Create(Guid jobRunId, string originalName, string storedPath, long size, string sha256, DateTimeOffset now) => new()
    {
        JobRunId = jobRunId,
        OriginalName = originalName.Length > NameMaxLength ? originalName[..NameMaxLength] : originalName,
        StoredPath = storedPath,
        SizeBytes = size,
        Sha256 = sha256,
        UploadedAt = now,
    };

    public void RecordOutcome(int read, int imported, int duplicate, int rejected, string? errorFilePath)
    {
        RowsRead = read;
        RowsImported = imported;
        RowsDuplicate = duplicate;
        RowsRejected = rejected;
        ErrorFilePath = errorFilePath;
    }

    public void ForgetStoredFile() => StoredPath = null;
}
