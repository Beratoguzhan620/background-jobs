using BackgroundJobs.Core.Users;

namespace BackgroundJobs.UnitTests;

public class UserTests
{
    [Fact]
    public void Emails_are_stored_trimmed_and_lower_cased()
    {
        var user = User.Create("  Ayse.Yilmaz@Example.COM ", " Ayşe ", UserRole.Viewer, DateTimeOffset.UnixEpoch);

        Assert.Equal("ayse.yilmaz@example.com", user.Email);
        Assert.Equal("Ayşe", user.DisplayName);
    }
}
