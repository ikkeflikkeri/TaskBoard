using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TaskBoard.Api.Configuration;
using TaskBoard.Api.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace TaskBoard.Api.IntegrationTests;

/// <summary>
/// A syntactically valid connection string naming nothing that listens.
/// </summary>
/// <remarks>
/// Port 1 on the loopback interface. Nothing listens there, so an accidental
/// connection attempt fails instead of reaching a real database. Several
/// fixtures need a host that builds without a reachable database, so the
/// value lives beside <see cref="ConnectionStringEnvironmentScope"/> — the
/// thing that publishes it — rather than in whichever test needed it first.
/// </remarks>
internal static class UnreachableDatabase
{
    public const string ConnectionString =
        "Host=127.0.0.1;Port=1;Database=taskboard;Username=taskboard";
}

/// <summary>
/// Publishes a connection string to the process environment for as long as
/// it is held, restoring the previous value on dispose.
/// </summary>
/// <remarks>
/// Program.cs validates configuration while the host is being built, which
/// happens before any WebApplicationFactory hook could supply it. The
/// environment is the only configuration source already read at that point,
/// so tests use the same environment-variable form the README documents for
/// operators.
///
/// The variable is process-wide, so holding a scope takes a process-wide
/// lock. Tests that never open a scope are unaffected and still run in
/// parallel with everything else; only scopes exclude each other, which is
/// narrower than disabling parallelisation for the whole assembly.
/// </remarks>
public sealed class ConnectionStringEnvironmentScope : IDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly string? _previous;
    private bool _released;

    private ConnectionStringEnvironmentScope(string? connectionString)
    {
        _previous = Environment.GetEnvironmentVariable(
            TasksConnectionString.EnvironmentVariableName);

        Environment.SetEnvironmentVariable(
            TasksConnectionString.EnvironmentVariableName,
            connectionString);
    }

    public static ConnectionStringEnvironmentScope Apply(
        string? connectionString)
    {
        Gate.Wait();

        try
        {
            return new ConnectionStringEnvironmentScope(connectionString);
        }
        catch
        {
            Gate.Release();
            throw;
        }
    }

    /// <summary>
    /// Publishes <paramref name="connectionString"/>, builds
    /// <typeparamref name="TFactory"/>, and starts it before the scope is
    /// released.
    /// </summary>
    /// <remarks>
    /// Every entry point that needs a started host goes through here, so the
    /// connection string is guaranteed to be in place for exactly the window
    /// during which Program.cs reads it, and no factory can forget to.
    /// </remarks>
    public static TFactory StartHost<TFactory>(string connectionString)
        where TFactory : WebApplicationFactory<Program>, new()
    {
        using var scope = Apply(connectionString);

        var factory = new TFactory();

        // Forces the host to finish starting while the scope is held.
        _ = factory.Services;

        return factory;
    }

    public void Dispose()
    {
        if (_released)
            return;

        _released = true;

        Environment.SetEnvironmentVariable(
            TasksConnectionString.EnvironmentVariableName,
            _previous);

        Gate.Release();
    }
}

/// <summary>
/// Hosts the real application, including the real connection-string
/// validation and data source registration.
/// </summary>
/// <remarks>
/// The connection string reaches the host through
/// <see cref="ConnectionStringEnvironmentScope"/>, and nothing overrides
/// <c>NpgsqlDataSource</c> afterwards. That is deliberate: a test override
/// would replace the production wiring, and the validated connection string
/// would never reach the data source that uses it.
///
/// Because validation reads configuration while the host is being built,
/// the connection string must be published for exactly that window. Every
/// entry point therefore goes through the static helpers here rather than
/// calling <see cref="CreateHost"/> directly, so the scope cannot be
/// forgotten at a call site.
/// </remarks>
public sealed class TaskBoardFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Builds a started host with <paramref name="connectionString"/>
    /// published to configuration.
    /// </summary>
    public static TaskBoardFactory Start(string connectionString) =>
        ConnectionStringEnvironmentScope
            .StartHost<TaskBoardFactory>(connectionString);

    /// <summary>
    /// Builds a started host whose startup fails because of
    /// <paramref name="connectionString"/>.
    /// </summary>
    public static InvalidOperationException StartExpectingFailure(
        string? connectionString)
    {
        using var configuration =
            ConnectionStringEnvironmentScope.Apply(connectionString);

        using var factory = new TaskBoardFactory();

        return Assert.Throws<InvalidOperationException>(
            () => factory.Services);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}

public sealed class TaskBoardFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:18")
            .Build();

    public TaskBoardFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Factory = TaskBoardFactory.Start(_postgres.GetConnectionString());

        await using var scope = Factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<TasksDbContext>();

        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (Factory is not null)
                await Factory.DisposeAsync();
        }
        finally
        {
            await _postgres.DisposeAsync();
        }
    }
}
