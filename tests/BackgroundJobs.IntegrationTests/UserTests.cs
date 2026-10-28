using System.Net;
using System.Net.Http.Json;
using BackgroundJobs.IntegrationTests.Support;

namespace BackgroundJobs.IntegrationTests;

[Collection(ApiCollection.Name)]
public class UserTests(ApiFactory factory)
{
    [Fact]
    public async Task An_admin_adds_a_viewer_who_can_read_but_not_manage()
    {
        var (viewer, user) = await factory.UserClientAsync("Viewer");

        Assert.Equal("Viewer", user.Role);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.PostAsJsonAsync("/api/users", new { email = ApiClient.UniqueEmail(), displayName = "X", role = "Admin", password = ApiClient.Password })).StatusCode);
    }

    [Fact]
    public async Task The_same_email_cannot_be_added_twice()
    {
        var admin = await factory.AdminClientAsync();
        var body = new { email = ApiClient.UniqueEmail(), displayName = "Twice", role = "Viewer", password = ApiClient.Password };

        (await admin.PostAsJsonAsync("/api/users", body)).EnsureSuccessStatusCode();
        var second = await admin.PostAsJsonAsync("/api/users", body);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("email_taken", await second.ProblemCodeAsync());
    }

    [Fact]
    public async Task Invalid_users_list_every_problem()
    {
        var admin = await factory.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/users", new { email = "nope", displayName = "", role = "Admin", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"email\"", body);
        Assert.Contains("\"displayName\"", body);
        Assert.Contains("\"password\"", body);
    }
}
