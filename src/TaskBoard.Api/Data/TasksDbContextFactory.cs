using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TaskBoard.Api.Data;

/// <summary>
/// Supplies a DbContext to the EF Core tools (migrations, drift checks) without
/// booting the API, so those commands do not need a live connection string.
/// </summary>
/// <remarks>
/// The connection string is read from the same configuration sources the API
/// uses. It is not hardcoded: a factory that pointed at a fixed database would
/// silently apply migrations to the wrong instance whenever the developer's
/// configured database differed.
/// </remarks>
public sealed class TasksDbContextFactory
    : IDesignTimeDbContextFactory<TasksDbContext>
{
    public TasksDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();

        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new TasksDbContext(options);
    }

    private static string ResolveConnectionString()
    {
        // EF CLI commands read Configuration, which maps the
        // ConnectionStrings__Tasks environment variable to the
        // ConnectionStrings:Tasks key.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(
                "appsettings.Development.json",
                optional: true)
            .AddEnvironmentVariables()
            .Build();

        return configuration.GetConnectionString("Tasks")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Tasks is required for EF Core " +
                "commands. Set the ConnectionStrings__Tasks " +
                "environment variable. See README.md.");
    }
}