using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using TaskBoard.Api.Data;
using TaskBoard.Api.Features.Tasks;
using TaskBoard.Api.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<TasksDatabaseHealthCheck>(
        "tasks-database",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

builder.Services.AddSingleton<NpgsqlDataSource>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();

    var connectionString = configuration.GetConnectionString("Tasks")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Tasks is required.");

    return NpgsqlDataSource.Create(connectionString);
});

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