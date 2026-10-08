using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskBoard.Api.Data;
using TaskBoard.Api.Features.Tasks;
using Xunit;

namespace TaskBoard.Api.IntegrationTests;

public sealed class TaskPaginationTests
    : IClassFixture<TaskBoardFixture>, IAsyncLifetime
{
    private static readonly DateTime Epoch =
        new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly TaskBoardFixture _fixture;

    public TaskPaginationTests(TaskBoardFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // This class has its own fixture/container.
        // Start each test with an empty tasks table.
        await using var scope =
            _fixture.Factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<TasksDbContext>();

        await db.Tasks.ExecuteDeleteAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Empty_database_returns_an_empty_page()
    {
        using var client = _fixture.Factory.CreateClient();

        var page = await GetPageAsync(client, 20);

        Assert.Empty(page.Items);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Default_page_size_is_twenty()
    {
        await SeedAsync(21);

        using var client = _fixture.Factory.CreateClient();

        var page = await client.GetFromJsonAsync<TaskPageResponse>(
            "/api/tasks/");

        Assert.NotNull(page);
        Assert.Equal(20, page.Items.Count);
        Assert.NotNull(page.NextCursor);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(100)]
    public async Task Following_cursors_returns_all_tasks_in_order(
        int pageSize)
    {
        var tasks = await SeedAsync(7);

        // Seed timestamps increase with the ID, with ties in groups
        // of three. Descending order therefore reverses the seed order.
        var expectedIds = tasks
            .Reverse()
            .Select(x => x.Id)
            .ToArray();

        using var client = _fixture.Factory.CreateClient();

        var actualIds = new List<Guid>();
        string? cursor = null;

        for (var offset = 0;
             offset < expectedIds.Length;
             offset += pageSize)
        {
            var page = await GetPageAsync(
                client,
                pageSize,
                cursor);

            var expectedPage = expectedIds
                .Skip(offset)
                .Take(pageSize)
                .ToArray();

            Assert.Equal(
                expectedPage,
                page.Items.Select(x => x.Id).ToArray());

            actualIds.AddRange(page.Items.Select(x => x.Id));

            var hasMore =
                offset + page.Items.Count < expectedIds.Length;

            if (hasMore)
                Assert.NotNull(page.NextCursor);
            else
                Assert.Null(page.NextCursor);

            cursor = page.NextCursor;
        }

        Assert.Equal(expectedIds, actualIds.ToArray());
        Assert.Equal(
            actualIds.Count,
            actualIds.Distinct().Count());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("abc")]
    public async Task Invalid_page_size_returns_bad_request(
        string pageSize)
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/tasks/?pageSize={Uri.EscapeDataString(pageSize)}");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("%%%")]
    [InlineData("not-a-cursor")]
    [InlineData("MQ")] // Valid base64url, but not a valid payload.
    public async Task Invalid_cursor_returns_bad_request(
        string cursor)
    {
        using var client = _fixture.Factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/tasks/?pageSize=2&cursor=" +
            Uri.EscapeDataString(cursor));

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task Newer_insert_does_not_shift_the_next_page()
    {
        var tasks = await SeedAsync(4);

        using var client = _fixture.Factory.CreateClient();

        var first = await GetPageAsync(client, 2);

        Assert.Equal(
            new[] { tasks[3].Id, tasks[2].Id },
            first.Items.Select(x => x.Id).ToArray());

        Assert.NotNull(first.NextCursor);

        await using (var scope =
            _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<TasksDbContext>();

            db.Tasks.Add(new TaskItem
            {
                Id = Guid.CreateVersion7(),
                Title = "Inserted after the first page",
                IsCompleted = false,
                CreatedAtUtc = Epoch.AddHours(1),
                Version = 1
            });

            await db.SaveChangesAsync();
        }

        var second = await GetPageAsync(
            client,
            2,
            first.NextCursor);

        Assert.Equal(
            new[] { tasks[1].Id, tasks[0].Id },
            second.Items.Select(x => x.Id).ToArray());

        Assert.Null(second.NextCursor);
    }

    private async Task<TaskItem[]> SeedAsync(int count)
    {
        var tasks = Enumerable.Range(0, count)
            .Select(i => new TaskItem
            {
                Id = Guid.Parse(
                    $"00000000-0000-0000-0000-{i + 1:D12}"),
                Title = $"Task {i + 1}",
                IsCompleted = false,

                // Equal timestamps deliberately exercise the ID
                // tie-breaker, including across page boundaries.
                CreatedAtUtc = Epoch.AddSeconds(i / 3),
                Version = 1
            })
            .ToArray();

        await using var scope =
            _fixture.Factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<TasksDbContext>();

        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync();

        return tasks;
    }

    private static async Task<TaskPageResponse> GetPageAsync(
        HttpClient client,
        int pageSize,
        string? cursor = null)
    {
        var uri = $"/api/tasks/?pageSize={pageSize}";

        if (cursor is not null)
        {
            uri += "&cursor=" +
                Uri.EscapeDataString(cursor);
        }

        using var response = await client.GetAsync(uri);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content
            .ReadFromJsonAsync<TaskPageResponse>();

        Assert.NotNull(page);

        return page;
    }
}
