using Microsoft.Extensions.Configuration;
using TaskBoard.Api.Configuration;

namespace TaskBoard.Api.IntegrationTests;

/// <summary>
/// Startup configuration validation, exercised directly against an
/// in-memory configuration root.
/// </summary>
/// <remarks>
/// These cases construct configuration rather than breaking a container or
/// a network. A missing key is a property of the configuration, so testing
/// it through an infrastructure failure would test something else and make
/// the suite flaky. <see cref="TaskStartupValidationTests"/> covers the same
/// rules through a real host build.
/// </remarks>
public sealed class TaskConfigurationTests
{
    private const string Valid =
        "Host=localhost;Port=5432;Database=taskboard;Username=taskboard";

    [Fact]
    public void Usable_connection_string_is_accepted()
    {
        var ok = TryResolve(Valid, out var resolved, out var error);

        Assert.True(ok, error);
        Assert.Equal(Valid, resolved);
    }

    [Fact]
    public void Unix_socket_connection_string_is_accepted()
    {
        const string socket = "Host=/var/run/postgresql;Database=taskboard";

        var ok = TryResolve(socket, out var resolved, out var error);

        Assert.True(ok, error);
        Assert.Equal(socket, resolved);
    }

    [Fact]
    public void Missing_connection_string_is_rejected()
    {
        Assert.False(TryResolve(null, out _, out var error));
        AssertStartupGuidance(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_connection_string_is_rejected(string value)
    {
        Assert.False(TryResolve(value, out _, out var error));
        AssertStartupGuidance(error);
    }

    [Theory]
    // No host and no socket: Npgsql would fall back to the OS user and
    // the local socket, connecting somewhere unintended rather than failing.
    [InlineData("Database=taskboard")]
    [InlineData("Username=taskboard;Password=p;Port=5432")]
    // No database: PostgreSQL would default to the connecting user's name.
    [InlineData("Host=localhost;Port=5432")]
    [InlineData("Host=localhost;Database=")]
    // Syntactically malformed input that Npgsql cannot parse at all.
    [InlineData("this is not a connection string")]
    public void Unusable_connection_string_is_rejected(string value)
    {
        Assert.False(TryResolve(value, out _, out var error));
        AssertStartupGuidance(error);
    }

    [Theory]
    [InlineData("Database=caller-supplied-secret")]
    [InlineData("caller supplied nonsense Password=caller-supplied-secret")]
    public void Rejection_message_never_echoes_the_value(string value)
    {
        Assert.False(TryResolve(value, out _, out var error));

        Assert.DoesNotContain(
            "caller-supplied-secret",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Rejection_message_does_not_vary_with_the_value()
    {
        // Two unrelated inputs rejected for different reasons — one
        // parses but lacks a host, the other does not parse at all — must
        // produce byte-identical messages. If either leaked its input, the
        // messages would differ.
        Assert.False(TryResolve(
            "Database=taskboard",
            out _,
            out var missingHost));

        Assert.False(TryResolve(
            "totally nonsense",
            out _,
            out var unparseable));

        Assert.Equal(missingHost, unparseable);
    }

    [Fact]
    public void Rejection_message_never_echoes_the_password()
    {
        // Well-formed but missing a host, so it fails validation while
        // carrying a secret worth protecting.
        const string value =
            "Database=taskboard;Password=super-secret-value";

        Assert.False(TryResolve(value, out _, out var error));

        Assert.DoesNotContain(
            "super-secret-value",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Configuration_root_reads_the_connection_string_key()
    {
        // The guidance message names the environment-variable form; this
        // pins that the form actually feeds the key the validator reads.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TasksConnectionString.ConfigKey] = Valid
            })
            .Build();

        Assert.True(
            TasksConnectionString.TryResolve(
                configuration,
                out var resolved,
                out var error),
            error);

        Assert.Equal(Valid, resolved);
    }

    private static bool TryResolve(
        string? value,
        out string? connectionString,
        out string? error)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TasksConnectionString.ConfigKey] = value
            })
            .Build();

        return TasksConnectionString.TryResolve(
            configuration,
            out connectionString,
            out error);
    }

    private static void AssertStartupGuidance(string? error)
    {
        Assert.NotNull(error);
        Assert.Contains(
            TasksConnectionString.ConfigKey,
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            TasksConnectionString.EnvironmentVariableName,
            error,
            StringComparison.Ordinal);
    }
}
