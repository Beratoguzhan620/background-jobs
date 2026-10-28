using System.Net;
using System.Security.Claims;
using BackgroundJobs.Api.Features.Auth;
using BackgroundJobs.Core;
using BackgroundJobs.Core.Users;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BackgroundJobs.Api.Features.Dashboard;

/// <summary>The dashboard's sign-in page: a plain form, protected against cross-site posts by an antiforgery token.</summary>
internal static class SignInPage
{
    public static IEndpointRouteBuilder MapSignInPage(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", Show).ExcludeFromDescription();
        app.MapPost("/login", SignIn).ExcludeFromDescription();
        app.MapPost("/logout", SignOut).ExcludeFromDescription();
        return app;
    }

    private static IResult Show(HttpContext context, IAntiforgery antiforgery, string? returnUrl = null, bool denied = false) =>
        Page(context, antiforgery, returnUrl, denied ? "Only admins can open the dashboard." : null);

    private static async Task<IResult> SignIn(
        HttpContext context, IAntiforgery antiforgery, UserSignIn signIn,
        [FromForm] string? email, [FromForm] string? password, [FromForm] string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return Page(context, antiforgery, returnUrl, "Enter your email and password.", email, StatusCodes.Status400BadRequest);
        }

        User user;
        try
        {
            user = await signIn.VerifyAsync(email, password, "dashboard", context.RequestAborted);
        }
        catch (DomainException ex) when (ex.Code == ErrorCodes.InvalidCredentials)
        {
            return Page(context, antiforgery, returnUrl, "Email or password is incorrect.", email, StatusCodes.Status401Unauthorized);
        }

        if (user.Role != UserRole.Admin)
        {
            return Page(context, antiforgery, returnUrl, "Only admins can open the dashboard.", email, StatusCodes.Status403Forbidden);
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Name, user.DisplayName),
                new Claim(AuthSetup.RoleClaim, user.Role.ToString()),
            ],
            DashboardAuth.Scheme, JwtRegisteredClaimNames.Name, AuthSetup.RoleClaim);
        await context.SignInAsync(DashboardAuth.Scheme, new ClaimsPrincipal(identity));

        return Results.LocalRedirect(SafeReturnUrl(returnUrl));
    }

    private static async Task SignOut(HttpContext context)
    {
        await context.SignOutAsync(DashboardAuth.Scheme);
        context.Response.Redirect("/login");
    }

    /// <summary>Only paths inside this site, so the form cannot be used to send people elsewhere.</summary>
    private static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 1 } url && url[0] == '/' && url[1] is not ('/' or '\\') ? url : DashboardAuth.Path;

    private static IResult Page(HttpContext context, IAntiforgery antiforgery, string? returnUrl, string? error,
        string? email = null, int status = StatusCodes.Status200OK)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        var html = Template
            .Replace("{{error}}", error is null ? string.Empty : $"""<p class="error" role="alert">{WebUtility.HtmlEncode(error)}</p>""", StringComparison.Ordinal)
            .Replace("{{tokenName}}", WebUtility.HtmlEncode(tokens.FormFieldName), StringComparison.Ordinal)
            .Replace("{{token}}", WebUtility.HtmlEncode(tokens.RequestToken), StringComparison.Ordinal)
            .Replace("{{returnUrl}}", WebUtility.HtmlEncode(SafeReturnUrl(returnUrl)), StringComparison.Ordinal)
            .Replace("{{email}}", WebUtility.HtmlEncode(email ?? string.Empty), StringComparison.Ordinal);

        context.Response.Headers["X-Frame-Options"] = "DENY";
        return Results.Content(html, "text/html; charset=utf-8", statusCode: status);
    }

    private const string Template = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>Sign in · Background Jobs</title>
          <link rel="preconnect" href="https://fonts.googleapis.com">
          <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
          <link href="https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@500&family=IBM+Plex+Sans:wght@400;500;600&display=swap" rel="stylesheet">
          <style>
            :root { --bg: #F7F6F3; --surface: #FFFFFF; --line: #E2DFD8; --text: #1A1A1A; --muted: #6B6862; --primary: #1F2A37; --accent: #9A6417; --danger: #A3402B; }
            * { box-sizing: border-box; }
            body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 16px; background: var(--bg); color: var(--text); font: 400 15px/1.5 'IBM Plex Sans', system-ui, sans-serif; }
            main { width: 100%; max-width: 380px; background: var(--surface); border: 1px solid var(--line); border-radius: 10px; padding: 28px; }
            .eyebrow { margin: 0 0 6px; font: 500 12px 'IBM Plex Mono', ui-monospace, monospace; letter-spacing: .04em; color: var(--accent); text-transform: uppercase; }
            h1 { margin: 0 0 4px; font-size: 22px; font-weight: 600; }
            .lead { margin: 0 0 22px; color: var(--muted); }
            label { display: block; margin: 0 0 6px; font-weight: 500; }
            input { width: 100%; margin: 0 0 16px; padding: 10px 12px; border: 1px solid var(--line); border-radius: 6px; font: inherit; color: var(--text); background: var(--surface); }
            input:focus { outline: 2px solid var(--accent); outline-offset: 1px; border-color: var(--accent); }
            button { width: 100%; padding: 11px 14px; border: 0; border-radius: 6px; background: var(--primary); color: #FFFFFF; font: 600 15px 'IBM Plex Sans', system-ui, sans-serif; cursor: pointer; }
            button:hover { background: #2B3A4C; }
            .error { margin: 0 0 16px; padding: 10px 12px; border: 1px solid #E8C5BC; border-radius: 6px; background: #FBF1EE; color: var(--danger); }
            .note { margin: 18px 0 0; color: var(--muted); font-size: 13px; }
          </style>
        </head>
        <body>
          <main>
            <p class="eyebrow">Background Jobs</p>
            <h1>Sign in to the dashboard</h1>
            <p class="lead">Queues, retries and recurring jobs, for admins.</p>
            {{error}}
            <form method="post" action="/login">
              <input type="hidden" name="{{tokenName}}" value="{{token}}">
              <input type="hidden" name="returnUrl" value="{{returnUrl}}">
              <label for="email">Email</label>
              <input id="email" name="email" type="email" autocomplete="username" required value="{{email}}">
              <label for="password">Password</label>
              <input id="password" name="password" type="password" autocomplete="current-password" required>
              <button type="submit">Sign in</button>
            </form>
            <p class="note">Viewers can follow jobs through the API; the dashboard can change them, so it is for admins only.</p>
          </main>
        </body>
        </html>
        """;
}
