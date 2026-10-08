using System.Net;
using System.Net.Http.Json;
using TaskBoard.Api.Features.Tasks;
using Xunit;

namespace TaskBoard.Api.IntegrationTests;

public sealed class TaskEndpointsTests
    : IClassFixture<TaskBoardFixture>
{
    private readonly TaskBoardFixture _fixture;

    public TaskEndpointsTests(TaskBoardFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_persists_a_trimmed_title()
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/tasks/",
            new CreateTaskRequest("  Integration test  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content
            .ReadFromJsonAsync<TaskResponse>();

        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Integration test", created.Title);
        Assert.False(created.IsCompleted);
        Assert.Equal(1L, created.Version);

        var location = response.Headers.Location;
        Assert.NotNull(location);

        var persisted = await client.GetFromJsonAsync<TaskResponse>(
            location.ToString());

        Assert.Equal(created, persisted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_blank_titles(string? title)
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/tasks/",
            new CreateTaskRequest(title));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_rejects_a_stale_version()
    {
        using var client = _fixture.Factory.CreateClient();

        using var createResponse = await client.PostAsJsonAsync(
            "/api/tasks/",
            new CreateTaskRequest("Original"));

        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content
            .ReadFromJsonAsync<TaskResponse>();

        Assert.NotNull(created);

        var uri = $"/api/tasks/{created.Id}";

        using var updateResponse = await client.PutAsJsonAsync(
            uri,
            new UpdateTaskRequest(
                "Accepted edit",
                true,
                created.Version));

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var updated = await updateResponse.Content
            .ReadFromJsonAsync<TaskResponse>();

        Assert.NotNull(updated);
        Assert.Equal(2L, updated.Version);

        using var staleResponse = await client.PutAsJsonAsync(
            uri,
            new UpdateTaskRequest(
                "Stale edit",
                false,
                created.Version));

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        var persisted = await client.GetFromJsonAsync<TaskResponse>(uri);

        Assert.NotNull(persisted);
        Assert.Equal("Accepted edit", persisted.Title);
        Assert.True(persisted.IsCompleted);
        Assert.Equal(2L, persisted.Version);
    }
}
