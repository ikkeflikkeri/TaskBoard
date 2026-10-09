using Microsoft.AspNetCore.Diagnostics;
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

if (!TasksConnectionString.TryResolve(
        builder.Configuration,
        out var tasksConnectionString,
        out var configurationError))
{
    throw new InvalidOperationException(configurationError);
}

const string ValidationProblemTitle =
    "One or more validation errors occurred.";

builder.Services.Configure<RouteHandlerOptions>(options =>
    options.ThrowOnBadRequest = false);
builder.Services.AddSingleton<IProblemDetailsWriter, ApiProblemDetailsWriter>();
builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
    {
        var problem = context.ProblemDetails;

        if (problem.Status is StatusCodes.Status400BadRequest
            && problem is not HttpValidationProblemDetails)
        {
            context.ProblemDetails = new HttpValidationProblemDetails(
                BindingFailureErrors.From(context.HttpContext))
            {
                Status = problem.Status,

                Title = ValidationProblemTitle,
                Type = problem.Type,
                Detail = problem.Detail,
                Instance = problem.Instance,

                Extensions = new Dictionary<string, object?>(
                    problem.Extensions,
                    StringComparer.Ordinal)
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

app.UseStatusCodePages();

app.MapTaskEndpoints();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

public partial class Program { }
