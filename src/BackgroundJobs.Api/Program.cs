using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using BackgroundJobs.Api.Common;
using BackgroundJobs.Api.Features.Audit;
using BackgroundJobs.Api.Features.Auth;
using BackgroundJobs.Api.Features.Dashboard;
using BackgroundJobs.Api.Features.Imports;
using BackgroundJobs.Api.Features.Jobs;
using BackgroundJobs.Api.Features.Reports;
using BackgroundJobs.Api.Features.Users;
using BackgroundJobs.Api.Logging;
using BackgroundJobs.Infrastructure;
using BackgroundJobs.Infrastructure.Hangfire;
using BackgroundJobs.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ILogEventEnricher, SensitiveDataEnricher>();
builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("Jobs");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:Jobs is not configured. Use 'dotnet user-secrets' or the ConnectionStrings__Jobs environment variable.");
}

// The API only writes jobs to storage; the worker processes run them.
builder.Services.AddJobsInfrastructure(builder.Configuration, connectionString);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditTrail>();
builder.Services.AddScoped<UserSignIn>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddJobsAuth(builder.Configuration);
builder.Services.AddDashboardAuth();

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

builder.Services.AddOpenApi(options => options
    .AddDocumentTransformer<DocumentInfoTransformer>()
    .AddDocumentTransformer<SecuritySchemesTransformer>()
    .AddOperationTransformer<SecuritySchemesTransformer>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<JobsDbContext>("database", tags: ["ready"])
    .AddCheck<HangfireStorageHealthCheck>("job-storage", tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseWhen(context => context.Request.Path.StartsWithSegments(DashboardAuth.Path),
    dashboard => dashboard.UseMiddleware<DashboardAuditMiddleware>());
app.UseAuthorization();
app.UseAntiforgery();

app.MapOpenApi();
app.MapScalarApiReference(options => options
    .WithTitle("Background Jobs")
    .WithTheme(ScalarTheme.Default)
    .ForceLightMode()
    .HideDarkModeToggle()
    .DisableDefaultFonts()
    .WithCustomCss(ScalarStyle.Css)
    .HideDeveloperTools()
    .HideClientButton()
    .DisableAgent()
    .DisableMcp()
    .DisableTelemetry());
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();

app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapJobEndpoints().MapJobStats();
app.MapImportEndpoints();
app.MapReportEndpoints();
app.MapAuditEndpoints();
app.MapSignInPage();
app.MapHangfireDashboard(DashboardAuth.Path, new DashboardOptions
{
    DashboardTitle = "Background Jobs",
    AppPath = "/scalar",
    Authorization = [new DashboardAdminFilter()],
    DisplayStorageConnectionString = false,
}).RequireAuthorization(DashboardAuth.Policy);

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<JobsDbContext>().Database.MigrateAsync();
}

await FirstAdminSeeder.SeedAsync(app.Services);

await app.RunAsync();

public partial class Program;
