using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task Update_rejects_blank_titles_without_changing_the_task(
        string? title)
    {
        await AssertInvalidUpdateLeavesTaskUnchangedAsync(
            title,
            version: 1,
            expectedErrorKey: "title");
    }

    [Fact]
    public async Task Update_rejects_an_overlong_title_without_changing_the_task()
    {
        await AssertInvalidUpdateLeavesTaskUnchangedAsync(
            new string('x', 201),
            version: 1,
            expectedErrorKey: "title");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Update_rejects_non_positive_versions_without_changing_the_task(
        long version)
    {
        await AssertInvalidUpdateLeavesTaskUnchangedAsync(
            "Should not be saved",
            version,
            expectedErrorKey: "version");
    }

    private async Task AssertInvalidUpdateLeavesTaskUnchangedAsync(
        string? title,
        long version,
        string expectedErrorKey)
    {
        using var client = _fixture.Factory.CreateClient();

        using var createResponse = await client.PostAsJsonAsync(
            "/api/tasks/",
            new CreateTaskRequest("Original"));

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content
            .ReadFromJsonAsync<TaskResponse>();

        Assert.NotNull(created);

        var uri = $"/api/tasks/{created.Id}";

        using var updateResponse = await client.PutAsJsonAsync(
            uri,
            new UpdateTaskRequest(
                title,
                true,
                version));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            updateResponse.StatusCode);

        Assert.Equal(
            "application/problem+json",
            updateResponse.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(
            await updateResponse.Content.ReadAsStringAsync());

        var root = problem.RootElement;

        Assert.Equal(
            (int)HttpStatusCode.BadRequest,
            root.GetProperty("status").GetInt32());

        var errors = root.GetProperty("errors");

        Assert.True(
            errors.TryGetProperty(expectedErrorKey, out var messages),
            $"Expected a validation error for '{expectedErrorKey}'.");

        Assert.Equal(JsonValueKind.Array, messages.ValueKind);
        Assert.True(messages.GetArrayLength() > 0);

        // Every rejected update asks for IsCompleted = true while the
        // stored task is incomplete, so this equality would catch a
        // partial write, not merely a preserved title.
        var persisted = await client.GetFromJsonAsync<TaskResponse>(uri);

        Assert.NotNull(persisted);
        Assert.Equal(created, persisted);
    }
}
