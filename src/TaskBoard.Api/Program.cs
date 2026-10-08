using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

// Matches the title the framework gives handler-produced validation
// problems, so the two are indistinguishable apart from the keys inside
// errors. There is no public constant for it.
const string ValidationProblemTitle =
    "One or more validation errors occurred.";

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        var problem = context.ProblemDetails;

        // A request the framework could not bind produces a bare 400 with no
        // errors map, while handler validation produces one with errors. The
        // offending field name is not recoverable on this path — MVC's
        // ModelState is never populated for minimal-API binding failures — so
        // the key is generic and the README says so. Normalizing here keeps
        // one envelope that a client can parse without special-casing which
        // layer rejected the request.
        if (problem.Status is StatusCodes.Status400BadRequest
            && problem is not HttpValidationProblemDetails)
        {
            context.ProblemDetails = new HttpValidationProblemDetails(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["request"] =
                    [
                        "The request body could not be read. Send a JSON " +
                        "object matching the documented schema."
                    ]
                })
            {
                Status = problem.Status,

                // The same title the framework gives handler-produced
                // validation problems.
                Title = ValidationProblemTitle,
                Type = problem.Type,
                Detail = problem.Detail,
                Instance = problem.Instance
            };
        }
    });
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

// AddProblemDetails registers the Problem Details writer but not the
// status-code pages that actually invoke it. Without this, any status code
// the handlers did not produce themselves — a binding failure, or the bare
// Results.NotFound() returned for a missing task — leaves the response with
// an empty body. Placing it here rather than at each call site keeps one
// shape for every error, including 404s from routing.
app.UseStatusCodePages();

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
