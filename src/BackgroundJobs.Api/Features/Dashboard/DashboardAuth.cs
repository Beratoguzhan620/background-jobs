using BackgroundJobs.Core.Users;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BackgroundJobs.Api.Features.Dashboard;

internal static class DashboardAuth
{
    /// <summary>Cookie sign-in used only by the dashboard and its sign-in page; the API keeps using JWT.</summary>
    public const string Scheme = "Dashboard";
    public const string Policy = "DashboardAdmin";
    public const string Path = "/hangfire";

    public static IServiceCollection AddDashboardAuth(this IServiceCollection services)
    {
        services.AddAuthentication().AddCookie(Scheme, options =>
        {
            options.LoginPath = "/login";
            options.LogoutPath = "/logout";
            options.AccessDeniedPath = "/login?denied=true";
            options.Cookie.Name = "bj.dashboard";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
        });

        services.AddAuthorizationBuilder().AddPolicy(Policy, policy => policy
            .AddAuthenticationSchemes(Scheme)
            .RequireRole(nameof(UserRole.Admin)));

        services.AddAntiforgery();
        return services;
    }
}

/// <summary>
/// Hangfire's own check, run after the endpoint's authorization policy. Hangfire allows only local
/// requests by default; here the signed-in user's role decides instead.
/// </summary>
internal sealed class DashboardAdminFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        context.GetHttpContext().User.IsInRole(nameof(UserRole.Admin));
}
