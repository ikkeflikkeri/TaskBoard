using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TaskBoard.Api.Data;
using TaskBoard.Api.Features.Tasks;
using Xunit;

namespace TaskBoard.Api.IntegrationTests;

public sealed class TaskDatabaseConstraintTests(
    TaskBoardFixture fixture)
    : IClassFixture<TaskBoardFixture>
{
    // The database constraint uses btrim("Title"), which trims
    // ordinary spaces only. Tabs and newlines are intentionally
    // outside this test's scope; this does not assert parity with
    // HTTP whitespace validation.
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("     ")]
    public async Task Database_rejects_empty_or_space_only_titles(
        string title)
    {
        var task = NewTask();
        task.Title = title;

        await AssertCheckViolationAsync(
            task,
            "ck_tasks_title_not_blank");
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public async Task Database_rejects_non_positive_versions(
        long version)
    {
        var task = NewTask();
        task.Version = version;

        await AssertCheckViolationAsync(
            task,
            "ck_tasks_version_positive");
    }

    [Fact]
    public async Task Database_accepts_a_valid_task()
    {
        var task = NewTask();

        await using (var scope =
            fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider
                .GetRequiredService<TasksDbContext>();

            db.Tasks.Add(task);

            await db.SaveChangesAsync();
        }

        // Read through a fresh context to verify persistence,
        // rather than inspecting the tracked entity.
        await using var readScope =
            fixture.Factory.Services.CreateAsyncScope();

        var readDb = readScope.ServiceProvider
            .GetRequiredService<TasksDbContext>();

        var persisted = await readDb.Tasks
            .AsNoTracking()
            .SingleAsync(x => x.Id == task.Id);

        Assert.Equal(task.Title, persisted.Title);
        Assert.Equal(task.Version, persisted.Version);
        Assert.Equal(task.CreatedAtUtc, persisted.CreatedAtUtc);
        Assert.False(persisted.IsCompleted);
    }

    private async Task AssertCheckViolationAsync(
        TaskItem task,
        string expectedConstraint)
    {
        await using var scope =
            fixture.Factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<TasksDbContext>();

        db.Tasks.Add(task);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            async () =>
            {
                await db.SaveChangesAsync();
            });

        var postgres = Assert.IsType<PostgresException>(
            exception.InnerException);

        Assert.Equal(
            PostgresErrorCodes.CheckViolation,
            postgres.SqlState);

        Assert.Equal(
            expectedConstraint,
            postgres.ConstraintName);
    }

    private static TaskItem NewTask() =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Title = "Database constraint test",
            IsCompleted = false,

            // Exact whole-second timestamp avoids precision differences.
            CreatedAtUtc = new DateTime(
                2026, 1, 1, 12, 0, 0,
                DateTimeKind.Utc),

            Version = 1
        };
}