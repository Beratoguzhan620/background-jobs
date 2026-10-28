using System.Net;
using BackgroundJobs.IntegrationTests.Support;

namespace BackgroundJobs.IntegrationTests;

[Collection(ApiCollection.Name)]
public class JobListTests(ApiFactory factory)
{
    [Theory]
    [InlineData("?status=Sleeping")]
    [InlineData("?kind=Email")]
    [InlineData("?limit=0")]
    [InlineData("?limit=101")]
    public async Task Bad_filters_are_validation_errors(string query)
    {
        var client = await factory.AdminClientAsync();

        var response = await client.GetAsync($"/api/jobs{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        var client = await factory.AdminClientAsync();

        var response = await client.GetAsync($"/api/jobs/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
