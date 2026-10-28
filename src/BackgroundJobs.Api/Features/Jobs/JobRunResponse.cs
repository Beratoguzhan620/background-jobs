using System.Text.Json;
using BackgroundJobs.Core.Jobs;

namespace BackgroundJobs.Api.Features.Jobs;

internal sealed record JobRunResponse(
    Guid Id,
    long Number,
    JobKind Kind,
    JobStatus Status,
    int Progress,
    int Attempts,
    Guid? RequestedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ScheduledFor,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error,
    JsonElement? Result)
{
    public static JobRunResponse From(JobRun run) => new(
        run.Id, run.Number, run.Kind, run.Status, run.Progress, run.Attempts, run.RequestedBy, run.CreatedAt, run.ScheduledFor,
        run.StartedAt, run.FinishedAt, run.Error,
        run.Result is null ? null : JsonDocument.Parse(run.Result).RootElement.Clone());
}

internal sealed record JobRunPage(IReadOnlyList<JobRunResponse> Items, long? NextCursor);
