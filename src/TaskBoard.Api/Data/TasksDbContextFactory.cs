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
///
/// Commands that only compare the model against the migration snapshot never
/// open a connection, so an unconfigured environment still yields a usable
/// DbContext. Commands that write to a database still require the real
/// connection string.
/// </remarks>
public sealed class TasksDbContextFactory
    : IDesignTimeDbContextFactory<TasksDbContext>
{
    /// <summary>
    /// Placeholder for commands that never connect, such as
    /// `migrations has-pending-model-changes`. Using a localhost port that
    /// nothing listens on makes an accidental connection attempt fail loudly
    /// instead of touching a real database.
    /// </summary>
    private const string UnconfiguredConnectionString =
        "Host=localhost;Port=1;Database=taskboard;Username=unconfigured";

    public TasksDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TasksDbContext>()
            .UseNpgsql(ResolveConnectionString())
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
            ?? UnconfiguredConnectionString;
    }
}