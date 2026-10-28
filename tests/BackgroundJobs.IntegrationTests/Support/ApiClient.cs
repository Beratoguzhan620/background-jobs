using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackgroundJobs.IntegrationTests.Support;

public sealed record TokenDto(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public sealed record UserDto(Guid Id, string Email, string DisplayName, string Role, DateTimeOffset CreatedAt);

public sealed record JobRunDto(Guid Id, long Number, string Kind, string Status, int Progress, int Attempts, Guid? RequestedBy,
    DateTimeOffset CreatedAt, DateTimeOffset? ScheduledFor, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? Error, JsonElement? Result);

public sealed record JobRunPageDto(List<JobRunDto> Items, long? NextCursor);

public sealed record AuditEntryDto(long Id, DateTimeOffset OccurredAt, Guid? ActorId, string Action, string TargetType, string? TargetId, string? Details, string? CorrelationId);

public sealed record AuditPageDto(List<AuditEntryDto> Items, string? NextCursor);

public static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public const string Password = "a-long-test-password";

    public static string UniqueEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@example.com";

    public static async Task<HttpClient> AdminClientAsync(this ApiFactory factory)
    {
        var client = factory.CreateClient();
        await client.SignInAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        return client;
    }

    /// <summary>Creates a user with the given role through the admin API and returns a client signed in as them.</summary>
    public static async Task<(HttpClient Client, UserDto User)> UserClientAsync(this ApiFactory factory, string role)
    {
        var admin = await factory.AdminClientAsync();
        var email = UniqueEmail(role.ToLowerInvariant());
        var created = await admin.PostAsJsonAsync("/api/users", new { email, displayName = $"Test {role}", role, password = Password });
        created.EnsureSuccessStatusCode();
        var user = await created.ReadAsync<UserDto>();

        var client = factory.CreateClient();
        await client.SignInAsync(email, Password);
        return (client, user);
    }

    public static async Task SignInAsync(this HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var token = await response.ReadAsync<TokenDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}

public sealed record QueueStatsDto(string Name, long Length);

public sealed record ServerStatsDto(string Name, int Workers, List<string> Queues, DateTime? Heartbeat);

public sealed record JobStatsDto(Dictionary<string, int> RunsByStatus, long Enqueued, long Scheduled, long Processing, long Failed,
    List<QueueStatsDto> Queues, List<ServerStatsDto> Servers);
