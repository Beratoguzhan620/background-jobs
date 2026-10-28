using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BackgroundJobs.IntegrationTests.Support;

namespace BackgroundJobs.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task The_first_admin_from_configuration_can_sign_in()
    {
        var client = await factory.AdminClientAsync();

        var me = await (await client.GetAsync("/api/me")).ReadAsync<UserDto>();

        Assert.Equal(ApiFactory.AdminEmail, me.Email);
        Assert.Equal("Admin", me.Role);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Wrong_password_and_unknown_email_get_the_same_answer(bool knownEmail)
    {
        var client = factory.CreateClient();
        var email = knownEmail ? ApiFactory.AdminEmail : ApiClient.UniqueEmail();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "not-the-password-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credentials", await response.ProblemCodeAsync());
    }

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/jobs")]
    [InlineData("/api/users")]
    [InlineData("/api/audit")]
    public async Task Endpoints_need_a_token(string path)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var client = await factory.AdminClientAsync();
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^4] + "AAAA");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }
}
