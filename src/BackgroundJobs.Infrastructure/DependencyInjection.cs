using BackgroundJobs.Infrastructure.Hangfire;
using BackgroundJobs.Infrastructure.Persistence;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Database, Hangfire storage and the job classes. Used by both the API and the worker.</summary>
    public static IServiceCollection AddJobsInfrastructure(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<JobsDbContext>((provider, options) => ConfigureDbContext(options, connectionString)
            .AddInterceptors(provider.GetServices<IInterceptor>()));

        services.Configure<JobsHangfireOptions>(configuration.GetSection(JobsHangfireOptions.Section));
        var hangfire = configuration.GetSection(JobsHangfireOptions.Section).Get<JobsHangfireOptions>() ?? new JobsHangfireOptions();

        services.AddHangfire(config =>
        {
            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UsePostgreSqlStorage(
                    storage => storage.UseNpgsqlConnection(connectionString),
                    new PostgreSqlStorageOptions
                    {
                        SchemaName = "hangfire",
                        QueuePollInterval = hangfire.QueuePollInterval,
                        PrepareSchemaIfNecessary = true,
                    });

            // Replace Hangfire's default (10 retries) with our own policy.
            GlobalJobFilters.Filters.Remove(typeof(AutomaticRetryAttribute));
            config.UseFilter(new AutomaticRetryAttribute
            {
                Attempts = hangfire.RetryAttempts,
                DelaysInSeconds = hangfire.RetryDelaysInSeconds,
                OnAttemptsExceeded = AttemptsExceededAction.Fail,
            });
        });

        return services;
    }

    internal static DbContextOptionsBuilder ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
