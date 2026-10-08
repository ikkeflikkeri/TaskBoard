using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskBoard.Api.Data;

/// <summary>
/// Supplies a DbContext to the EF Core tools (migrations, drift checks) without
/// booting the API, so those commands do not need a live connection string.
/// </summary>
public sealed class TasksDbContextFactory
    : IDesignTimeDbContextFactory<TasksDbContext>
{
    public TasksDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=taskboard;Username=postgres")
            .Options;

        return new TasksDbContext(options);
    }
}