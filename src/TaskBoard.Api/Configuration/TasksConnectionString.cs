using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace TaskBoard.Api.Configuration;

/// <summary>
/// Resolves and validates the PostgreSQL connection string the API needs.
/// </summary>
/// <remarks>
/// Validation is deliberately offline. It inspects only the text of the
/// connection string and never opens a socket, so a deployment that is
/// configured incorrectly fails at startup while a database that is merely
/// unreachable is still reported through readiness. Keeping those two
/// failures apart is the reason this exists rather than a connectivity
/// probe at boot.
/// </remarks>
public static class TasksConnectionString
{
    public const string Name = "Tasks";
    public const string ConfigKey = $"ConnectionStrings:{Name}";
    public const string EnvironmentVariableName = "ConnectionStrings__Tasks";

    // Both messages name the key and the environment-variable form, and
    // neither embeds the value. A connection string is a credential: it
    // carries a password, and a startup failure is exactly the kind of
    // event that ends up in logs and crash reports.
    private const string MissingMessage =
        $"Required configuration '{ConfigKey}' was not set. Set it in " +
        "configuration under \"ConnectionStrings\": { \"Tasks\": \"...\" }, " +
        $"or set the {EnvironmentVariableName} environment variable. " +
        "The connection string value is never included in this message.";

    private const string InvalidMessage =
        $"Configuration '{ConfigKey}' is not a usable PostgreSQL " +
        "connection string. Provide a host and a database name, for example " +
        "\"Host=localhost;Port=5432;Database=taskboard;Username=taskboard\". " +
        $"Set it in configuration or via the {EnvironmentVariableName} " +
        "environment variable. The connection string value is never " +
        "included in this message.";

    /// <summary>
    /// Attempts to read the connection string from configuration.
    /// </summary>
    /// <param name="connectionString">
    /// The value to hand to Npgsql, or <c>null</c> when it is unusable.
    /// </param>
    /// <param name="error">
    /// A credential-safe description of what is wrong, or <c>null</c> when
    /// the value is usable.
    /// </param>
    public static bool TryResolve(
        IConfiguration configuration,
        [NotNullWhen(true)] out string? connectionString,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var value = configuration.GetConnectionString(Name);

        if (string.IsNullOrWhiteSpace(value))
        {
            connectionString = null;
            error = MissingMessage;
            return false;
        }

        return TryValidate(value, out connectionString, out error);
    }

    private static bool TryValidate(
        string value,
        [NotNullWhen(true)] out string? connectionString,
        [NotNullWhen(false)] out string? error)
    {
        connectionString = null;
        error = null;

        NpgsqlConnectionStringBuilder builder;

        try
        {
            builder = new NpgsqlConnectionStringBuilder(value);
        }
        catch (ArgumentException)
        {
            // The parse error from Npgsql quotes the offending keyword,
            // which is still caller-supplied text, so it is not surfaced.
            error = InvalidMessage;
            return false;
        }

        // Host and database are the two pieces that cannot be inferred
        // from the rest of the string. Without them Npgsql would silently
        // fall back to the OS user and the local socket, which connects
        // to the wrong database rather than failing. A Unix socket is
        // carried by the Host keyword as a path, so it is covered here.
        if (string.IsNullOrWhiteSpace(builder.Host)
            || string.IsNullOrWhiteSpace(builder.Database))
        {
            error = InvalidMessage;
            return false;
        }

        connectionString = value;
        return true;
    }
}
