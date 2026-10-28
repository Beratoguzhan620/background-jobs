using System.Net;
using System.Text.RegularExpressions;
using BackgroundJobs.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BackgroundJobs.IntegrationTests;

[Collection(ApiCollection.Name)]
public partial class DashboardTests(ApiFactory factory)
{
    private HttpClient Browser() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    [Fact]
    public async Task The_dashboard_sends_strangers_to_the_sign_in_page()
    {
        var response = await Browser().GetAsync("/hangfire");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("http://localhost/login?ReturnUrl=%2Fhangfire", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task An_admin_signs_in_and_opens_the_dashboard()
    {
        var browser = Browser();

        var signIn = await SignInAsync(browser, ApiFactory.AdminEmail, ApiFactory.AdminPassword, "/hangfire/recurring");

        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        Assert.Equal("/hangfire/recurring", signIn.Headers.Location!.ToString());
        var dashboard = await browser.GetAsync("/hangfire");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        Assert.Contains("Background Jobs", await dashboard.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_viewer_cannot_sign_in_to_the_dashboard()
    {
        var (_, viewer) = await factory.UserClientAsync("Viewer");
        var browser = Browser();

        var signIn = await SignInAsync(browser, viewer.Email, ApiClient.Password);

        Assert.Equal(HttpStatusCode.Forbidden, signIn.StatusCode);
        Assert.Contains("Only admins can open the dashboard.", await signIn.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await browser.GetAsync("/hangfire")).StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_shows_an_error()
    {
        var signIn = await SignInAsync(Browser(), ApiFactory.AdminEmail, "wrong-password-12345");

        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
        Assert.Contains("Email or password is incorrect.", await signIn.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_sign_in_without_the_antiforgery_token_is_refused()
    {
        var response = await Browser().PostAsync("/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = ApiFactory.AdminEmail,
            ["password"] = ApiFactory.AdminPassword,
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("//evil.example.com")]
    [InlineData("https://evil.example.com/hangfire")]
    [InlineData("/\\evil.example.com")]
    public async Task The_return_address_cannot_leave_the_site(string returnUrl)
    {
        var signIn = await SignInAsync(Browser(), ApiFactory.AdminEmail, ApiFactory.AdminPassword, returnUrl);

        Assert.Equal("/hangfire", signIn.Headers.Location!.ToString());
    }

    [Fact]
    public async Task The_api_does_not_accept_the_dashboard_cookie()
    {
        var browser = Browser();
        await SignInAsync(browser, ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/jobs")).StatusCode);
    }

    private static async Task<HttpResponseMessage> SignInAsync(HttpClient browser, string email, string password, string? returnUrl = null)
    {
        var page = await (await browser.GetAsync("/login")).Content.ReadAsStringAsync();
        var token = TokenField().Match(page);
        var form = new Dictionary<string, string>
        {
            [token.Groups["name"].Value] = WebUtility.HtmlDecode(token.Groups["value"].Value),
            ["email"] = email,
            ["password"] = password,
        };
        if (returnUrl is not null)
        {
            form["returnUrl"] = returnUrl;
        }

        return await browser.PostAsync("/login", new FormUrlEncodedContent(form));
    }

    [GeneratedRegex("""<input type="hidden" name="(?<name>__RequestVerificationToken[^"]*)" value="(?<value>[^"]+)">""")]
    private static partial Regex TokenField();
}
