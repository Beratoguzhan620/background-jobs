using System.Security.Claims;
using BackgroundJobs.Api.Common;
using BackgroundJobs.Api.Features.Audit;
using BackgroundJobs.Api.Logging;
using BackgroundJobs.Core;
using BackgroundJobs.Core.Users;
using BackgroundJobs.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace BackgroundJobs.Api.Features.Auth;

internal sealed record LoginRequest(string Email, string Password);

internal sealed record UserResponse(Guid Id, string Email, string DisplayName, UserRole Role, DateTimeOffset CreatedAt)
{
    public static UserResponse From(User user) => new(user.Id, user.Email, user.DisplayName, user.Role, user.CreatedAt);
}

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", Login).WithTags("Auth").WithSummary("Exchange email and password for an access token");
        app.MapGet("/api/me", Me).WithTags("Auth").WithSummary("The signed-in user").RequireAuthorization(AuthSetup.ViewerPolicy);
        return app;
    }

    private static async Task<Results<Ok<AccessTokenResponse>, ValidationProblem>> Login(
        LoginRequest request, UserSignIn signIn, TokenService tokens, CancellationToken ct)
    {
        var validation = new Validation()
            .Require(!string.IsNullOrWhiteSpace(request.Email), "email", "Email is required.")
            .Require(!string.IsNullOrEmpty(request.Password), "password", "Password is required.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var user = await signIn.VerifyAsync(request.Email, request.Password, "api", ct);
        return TypedResults.Ok(tokens.Issue(user));
    }

    private static async Task<Ok<UserResponse>> Me(ClaimsPrincipal principal, JobsDbContext db, CancellationToken ct)
    {
        var id = principal.UserId();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "User not found.");
        return TypedResults.Ok(UserResponse.From(user));
    }
}

/// <summary>Checks credentials for both the API and the dashboard, and records every attempt.</summary>
internal sealed class UserSignIn(JobsDbContext db, PasswordVerifier passwords, AuditTrail audit, ILogger<UserSignIn> logger)
{
    public async Task<User> VerifyAsync(string email, string password, string channel, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == normalized, ct);

        if (!passwords.Verify(user, password, out var rehash) || user is null)
        {
            audit.Record("auth.login_failed", "user", user?.Id, new { email = Masking.Email(normalized), channel }, user?.Id);
            await db.SaveChangesAsync(ct);
            logger.LogWarning("Failed {Channel} login for {Email}", channel, normalized);
            throw new DomainException(ErrorCodes.InvalidCredentials, "Email or password is incorrect.");
        }

        if (rehash is not null)
        {
            user.SetPasswordHash(rehash);
        }

        audit.Record("auth.login_succeeded", "user", user.Id, new { channel }, user.Id);
        await db.SaveChangesAsync(ct);
        return user;
    }
}
