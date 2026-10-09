using TaskBoard.Api.Configuration;

namespace TaskBoard.Api.IntegrationTests;

/// <summary>
/// Startup validation, exercised through a real host build.
/// </summary>
/// <remarks>
/// The success case points at a syntactically valid connection string
/// naming nothing that listens, which is the case a deployment can actually
/// get wrong: correct configuration, database not up. Building the host
/// proves validation is a startup concern rather than something that waits
/// for the first request, and that a valid-but-unreachable database still
/// starts the process. Readiness reporting for that state is covered by
/// <see cref="TaskHealthOutageTests"/>, which observes the response
/// rather than inferring it from a host that merely started.
/// </remarks>
public sealed class TaskStartupValidationTests
{
    private const string ConfiguredButUnreachable =
        UnreachableDatabase.ConnectionString;

    [Fact]
    public void Valid_configuration_builds_the_host()
    {
        using var factory =
            TaskBoardFactory.Start(ConfiguredButUnreachable);

        Assert.NotNull(factory.Services);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("this is not a connection string")]
    public void Invalid_configuration_fails_at_startup(string? value)
    {
        var exception =
            TaskBoardFactory.StartExpectingFailure(value);

        Assert.Contains(
            TasksConnectionString.ConfigKey,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_failure_message_does_not_echo_the_value()
    {
        const string value = "Database=taskboard;Password=super-secret";

        var exception = TaskBoardFactory.StartExpectingFailure(value);

        Assert.DoesNotContain(
            "super-secret",
            exception.Message,
            StringComparison.Ordinal);
    }
}
