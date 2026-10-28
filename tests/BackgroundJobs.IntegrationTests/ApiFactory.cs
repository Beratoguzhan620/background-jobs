using System.Security.Cryptography;
using BackgroundJobs.Infrastructure.Hangfire;
using Hangfire;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace BackgroundJobs.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL container shared by the whole collection, with a
/// Hangfire server in the same process so enqueued jobs really run.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "admin-password-for-tests";

    private static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _database.GetConnectionString();

    public string StorageRoot { get; } = Path.Combine(Path.GetTempPath(), $"background-jobs-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Jobs", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Bootstrap:AdminEmail", AdminEmail);
        builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
        builder.UseSetting("Hangfire:QueuePollInterval", "00:00:00.200");
        builder.UseSetting("Storage:Root", StorageRoot);
        // Fast retries so failure paths finish within a test.
        builder.UseSetting("Hangfire:RetryAttempts", "2");
        builder.UseSetting("Hangfire:RetryDelaysInSeconds:0", "1");
        builder.UseSetting("Hangfire:RetryDelaysInSeconds:1", "1");
        builder.UseSetting("Hangfire:RetryDelaysInSeconds:2", "1");
        builder.ConfigureServices(services => services.AddHangfireServer(options =>
        {
            options.ServerName = "test-server";
            options.Queues = JobQueues.All;
            options.WorkerCount = 4;
            options.SchedulePollingInterval = TimeSpan.FromMilliseconds(500);
        }));
    }

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        CreateClient().Dispose();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
        if (Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
