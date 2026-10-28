using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace BackgroundJobs.IntegrationTests;

/// <summary>Boots the real API against a throwaway PostgreSQL container shared by the whole collection.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "admin-password-for-tests";

    private static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => _database.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Jobs", ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Bootstrap:AdminEmail", AdminEmail);
        builder.UseSetting("Bootstrap:AdminPassword", AdminPassword);
        builder.UseSetting("Hangfire:QueuePollInterval", "00:00:00.200");
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
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
