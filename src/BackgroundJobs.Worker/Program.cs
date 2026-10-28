using BackgroundJobs.Infrastructure;
using BackgroundJobs.Infrastructure.Hangfire;
using BackgroundJobs.Infrastructure.Jobs;
using BackgroundJobs.Infrastructure.Persistence;
using BackgroundJobs.Worker;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

// A Hangfire server. It exposes only health endpoints, so Compose and orchestrators can tell whether it is up.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("Jobs");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Jobs is not configured.");
}

builder.Services.AddJobsInfrastructure(builder.Configuration, connectionString);
builder.Services.AddSingleton(TimeProvider.System);

var worker = builder.Configuration.GetSection(WorkerOptions.Section).Get<WorkerOptions>() ?? new WorkerOptions();
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = $"{Environment.MachineName}:{Environment.ProcessId}";
    options.WorkerCount = worker.Concurrency;
    options.Queues = JobQueues.All;
    // Give running jobs time to notice the shutdown and stop at a safe point.
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<JobsDbContext>("database", tags: ["ready"])
    .AddCheck<HangfireStorageHealthCheck>("job-storage", tags: ["ready"]);

var app = builder.Build();

// Every worker registers the schedules; AddOrUpdate is idempotent and Hangfire fires each occurrence once.
var schedules = app.Services.GetRequiredService<IRecurringJobManager>();
var istanbul = TimeZoneInfo.FindSystemTimeZoneById(ReportJob.TimeZone);
schedules.AddOrUpdate<RecurringJobs>(RecurringJobs.NightlyReportId, jobs => jobs.NightlyReportAsync(CancellationToken.None),
    "15 2 * * *", new RecurringJobOptions { TimeZone = istanbul });
schedules.AddOrUpdate<RecurringJobs>(RecurringJobs.CleanupId, jobs => jobs.CleanupAsync(CancellationToken.None),
    Cron.Hourly(5), new RecurringJobOptions { TimeZone = istanbul });

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

await app.RunAsync();
