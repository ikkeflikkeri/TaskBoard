using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskBoard.Api.Data;
using TaskBoard.Api.Features.Tasks;
using Xunit;

namespace TaskBoard.Api.IntegrationTests;

public sealed class TaskConcurrencyTests
    : IClassFixture<TaskBoardFixture>
{
    private readonly TaskBoardFixture _fixture;

    public TaskConcurrencyTests(TaskBoardFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Second_writer_cannot_overwrite_first_writer()
    {
        var id = Guid.CreateVersion7();

        await using (var seedScope =
            _fixture.Factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider
                .GetRequiredService<TasksDbContext>();

            db.Tasks.Add(new TaskItem
            {
                Id = id,
                Title = "Original",
                IsCompleted = false,
                CreatedAtUtc = DateTime.UtcNow,
                Version = 1
            });

            await db.SaveChangesAsync();
        }

        await using var firstScope =
            _fixture.Factory.Services.CreateAsyncScope();

        await using var secondScope =
            _fixture.Factory.Services.CreateAsyncScope();

        var firstDb = firstScope.ServiceProvider
            .GetRequiredService<TasksDbContext>();

        var secondDb = secondScope.ServiceProvider
            .GetRequiredService<TasksDbContext>();

        // Both contexts observe version 1 before either writes.
        var first = await firstDb.Tasks.SingleAsync(x => x.Id == id);
        var second = await secondDb.Tasks.SingleAsync(x => x.Id == id);

        first.Title = "First writer";
        first.Version++;

        second.Title = "Second writer";
        second.Version++;

        await firstDb.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => secondDb.SaveChangesAsync());

        await using var verifyScope =
            _fixture.Factory.Services.CreateAsyncScope();

        var verifyDb = verifyScope.ServiceProvider
            .GetRequiredService<TasksDbContext>();

        var persisted = await verifyDb.Tasks
            .AsNoTracking()
            .SingleAsync(x => x.Id == id);

        Assert.Equal("First writer", persisted.Title);
        Assert.Equal(2L, persisted.Version);
    }
}
