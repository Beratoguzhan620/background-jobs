namespace BackgroundJobs.Core;

/// <summary>
/// A business rule was broken. <see cref="Code"/> is a stable, machine-readable value that the API
/// returns to clients; the message is for humans and may change.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    /// <summary>Extra facts returned with the problem response, such as the id of an existing run.</summary>
    public IDictionary<string, object?> Details { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}
