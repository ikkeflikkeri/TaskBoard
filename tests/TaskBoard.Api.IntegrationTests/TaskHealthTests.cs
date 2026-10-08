using System.Net;
using Xunit;

namespace TaskBoard.Api.IntegrationTests;

public sealed class TaskHealthTests(
    TaskBoardFixture fixture)
    : IClassFixture<TaskBoardFixture>
{
    // Both endpoints return the default plain-text status. The
    // database failure path is covered separately; these cases only
    // establish the healthy responses.
    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoint_returns_healthy_with_database_available(
        string uri)
    {
        using var client = fixture.Factory.CreateClient();

        using var response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Healthy",
            await response.Content.ReadAsStringAsync());
    }
}