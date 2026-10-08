using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using TaskBoard.Api.Configuration;
using TaskBoard.Api.Data;
using TaskBoard.Api.Features.Tasks;
using TaskBoard.Api.Health;

var builder = WebApplication.CreateBuilder(args);

// Fail fast on a missing or unusable connection string. This checks only
// that the configuration text is present and well-formed; it never opens a
// connection, so an unreachable database still reaches the readiness
// check instead of crashing the process at boot.
if (!TasksConnectionString.TryResolve(
        builder.Configuration,
        out var tasksConnectionString,
        out var configurationError))
{
    throw new InvalidOperationException(configurationError);
}

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<TasksDatabaseHealthCheck>(
        "tasks-database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

builder.Services.AddSingleton<NpgsqlDataSource>(
    _ => NpgsqlDataSource.Create(tasksConnectionString));

builder.Services.AddDbContext<TasksDbContext>((services, options) =>
{
    options.UseNpgsql(
        services.GetRequiredService<NpgsqlDataSource>());
});

var app = builder.Build();

app.UseExceptionHandler();

app.MapTaskEndpoints();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    // Liveness must stay independent of PostgreSQL, so no check
    // is selected here.
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

public partial class Program { }
