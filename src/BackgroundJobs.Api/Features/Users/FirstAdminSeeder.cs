using BackgroundJobs.Api.Features.Auth;
using BackgroundJobs.Core.Users;
using BackgroundJobs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BackgroundJobs.Api.Features.Users;

internal sealed class BootstrapOptions
{
    public const string Section = "Bootstrap";

    public string? AdminEmail { get; set; }

    /// <summary>From user secrets or an environment variable; never from the repository.</summary>
    public string? AdminPassword { get; set; }
}

/// <summary>
/// Creates the first admin from configuration when there are no users yet, so a fresh install can be
/// signed into. After that, admins add users through the API and the setting is ignored.
/// </summary>
internal static class FirstAdminSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var options = provider.GetRequiredService<IConfiguration>().GetSection(BootstrapOptions.Section).Get<BootstrapOptions>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(FirstAdminSeeder));
        var db = provider.GetRequiredService<JobsDbContext>();

        if (string.IsNullOrWhiteSpace(options?.AdminEmail) || string.IsNullOrEmpty(options.AdminPassword) || await db.Users.AnyAsync(ct))
        {
            return;
        }

        if (options.AdminPassword.Length < UserEndpoints.PasswordMinLength)
        {
            throw new InvalidOperationException($"Bootstrap:AdminPassword must be at least {UserEndpoints.PasswordMinLength} characters.");
        }

        await UserCreation.CreateAsync(db, provider.GetRequiredService<PasswordVerifier>(), options.AdminEmail, "Administrator",
            UserRole.Admin, options.AdminPassword, provider.GetRequiredService<TimeProvider>(), ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created the first admin {Email}", options.AdminEmail);
    }
}
