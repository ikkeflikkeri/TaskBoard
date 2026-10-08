using System.Net;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace TaskBoard.Api.IntegrationTests;

public sealed class TaskHealthOutageTests
{
    [Fact]
    public async Task Database_outage_fails_readiness_but_not_liveness()
    {
        // This test owns this container. It does not stop a fixture's
        // database or affect other test classes.
        await using var postgres =
            new PostgreSqlBuilder("postgres:18").Build();

        await postgres.StartAsync();

        var connectionString = new NpgsqlConnectionStringBuilder(
            postgres.GetConnectionString())
        {
            // Require a fresh connection for each readiness check,
            // avoiding previously pooled connections during the outage.
            Pooling = false,
            Timeout = 2,
            CommandTimeout = 2
        }.ConnectionString;

        await using var factory = TaskBoardFactory.Start(connectionString);

        using var client = factory.CreateClient();

        client.Timeout = TimeSpan.FromSeconds(15);

        // Establish the healthy baseline before causing the outage.
        await AssertHealthAsync(
            client,
            "/health/ready",
            HttpStatusCode.OK,
            "Healthy");

        await AssertHealthAsync(
            client,
            "/health/live",
            HttpStatusCode.OK,
            "Healthy");

        await postgres.StopAsync();

        // The database is now unavailable to the same application.
        await AssertHealthAsync(
            client,
            "/health/ready",
            HttpStatusCode.ServiceUnavailable,
            "Unhealthy");

        await AssertHealthAsync(
            client,
            "/health/live",
            HttpStatusCode.OK,
            "Healthy");
    }

    private static async Task AssertHealthAsync(
        HttpClient client,
        string uri,
        HttpStatusCode expectedStatus,
        string expectedBody)
    {
        using var response = await client.GetAsync(uri);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(
            expectedBody,
            await response.Content.ReadAsStringAsync());
    }
}