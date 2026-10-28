namespace BackgroundJobs.Core;

/// <summary>Stable error codes returned to API clients. Never rename a published value.</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string NotFound = "not_found";
    public const string InvalidCredentials = "invalid_credentials";
    public const string EmailTaken = "email_taken";
    public const string InvalidState = "invalid_state";
    public const string DuplicateJob = "duplicate_job";
    public const string InvalidFile = "invalid_file";
}
