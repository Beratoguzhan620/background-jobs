namespace BackgroundJobs.Core.Users;

public sealed class User
{
    public const int EmailMaxLength = 254;
    public const int DisplayNameMaxLength = 80;

    private User()
    {
        Email = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
    }

    public Guid Id { get; private set; }

    /// <summary>Lower-cased and trimmed, so uniqueness does not depend on how it was typed.</summary>
    public string Email { get; private set; }

    public string DisplayName { get; private set; }

    public UserRole Role { get; private set; }

    public string PasswordHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static User Create(string email, string displayName, UserRole role, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now),
        Email = NormalizeEmail(email),
        DisplayName = displayName.Trim(),
        Role = role,
        CreatedAt = now,
    };

    public void SetPasswordHash(string hash) => PasswordHash = hash;
}
