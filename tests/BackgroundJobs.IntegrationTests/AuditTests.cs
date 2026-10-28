using System.Net.Http.Json;
using BackgroundJobs.IntegrationTests.Support;

namespace BackgroundJobs.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuditTests(ApiFactory factory)
{
    [Fact]
    public async Task Sign_ins_and_new_users_are_audited_without_secrets()
    {
        var admin = await factory.AdminClientAsync();
        var (_, viewer) = await factory.UserClientAsync("Viewer");
        await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = viewer.Email, password = "wrong-password-99" });

        var entries = (await (await admin.GetAsync($"/api/audit?targetId={viewer.Id}")).ReadAsync<AuditPageDto>()).Items;

        Assert.Contains(entries, e => e.Action == "user.created");
        Assert.Contains(entries, e => e.Action == "auth.login_succeeded");
        var failed = Assert.Single(entries, e => e.Action == "auth.login_failed");
        Assert.DoesNotContain("wrong-password-99", failed.Details);
        Assert.DoesNotContain(viewer.Email, failed.Details);
    }
}
