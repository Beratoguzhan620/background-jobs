using System.Net.Mail;
using BackgroundJobs.Api.Common;
using BackgroundJobs.Api.Features.Audit;
using BackgroundJobs.Api.Features.Auth;
using BackgroundJobs.Core;
using BackgroundJobs.Core.Users;
using BackgroundJobs.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace BackgroundJobs.Api.Features.Users;

/// <param name="Role">Admin or Viewer.</param>
internal sealed record CreateUserRequest(string Email, string DisplayName, UserRole Role, string Password);

internal static class UserEndpoints
{
    public const int PasswordMinLength = 12;
    private const int PasswordMaxLength = 128;

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(AuthSetup.AdminPolicy);
        group.MapGet("/", List).WithSummary("List users");
        group.MapPost("/", Create).WithSummary("Add an admin or a viewer");
        return app;
    }

    private static async Task<Ok<List<UserResponse>>> List(JobsDbContext db, CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.CreatedAt).ToListAsync(ct);
        return TypedResults.Ok(users.Select(UserResponse.From).ToList());
    }

    private static async Task<Results<Created<UserResponse>, ValidationProblem>> Create(
        CreateUserRequest request, JobsDbContext db, PasswordVerifier passwords, AuditTrail audit, TimeProvider clock, CancellationToken ct)
    {
        var validation = Validate(request);
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var user = await UserCreation.CreateAsync(db, passwords, request.Email, request.DisplayName, request.Role, request.Password, clock, ct);
        audit.Record("user.created", "user", user.Id, new { role = user.Role.ToString() });
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/users/{user.Id}", UserResponse.From(user));
    }

    internal static Validation Validate(CreateUserRequest request) => new Validation()
        .Require(IsEmail(request.Email), "email", "A valid email address is required.")
        .Require(!string.IsNullOrWhiteSpace(request.DisplayName) && request.DisplayName.Trim().Length <= User.DisplayNameMaxLength,
            "displayName", $"Display name is required and can be at most {User.DisplayNameMaxLength} characters.")
        .Require(Enum.IsDefined(request.Role), "role", "Role must be Admin or Viewer.")
        .Require(request.Password is { Length: >= PasswordMinLength and <= PasswordMaxLength },
            "password", $"Password must be {PasswordMinLength} to {PasswordMaxLength} characters.");

    private static bool IsEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= User.EmailMaxLength
        && MailAddress.TryCreate(value.Trim(), out var address)
        && string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase)
        && address.Host.Contains('.', StringComparison.Ordinal);
}

internal static class UserCreation
{
    /// <summary>Adds the user to the context; the caller saves. A duplicate email is a 409.</summary>
    public static async Task<User> CreateAsync(JobsDbContext db, PasswordVerifier passwords, string email, string displayName,
        UserRole role, string password, TimeProvider clock, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(email);
        if (await db.Users.AnyAsync(u => u.Email == normalized, ct))
        {
            throw new DomainException(ErrorCodes.EmailTaken, "A user with this email already exists.");
        }

        var user = User.Create(normalized, displayName, role, clock.GetUtcNow());
        user.SetPasswordHash(passwords.Hash(user, password));
        db.Users.Add(user);
        return user;
    }
}
