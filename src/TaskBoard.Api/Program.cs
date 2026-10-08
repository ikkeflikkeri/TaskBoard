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
        // errors map, while handler validation produces one with errors.
        // Normalizing here keeps one envelope that a client can parse without
        // special-casing which layer rejected the request. MVC's ModelState is
        // never populated for minimal-API binding failures, so the offending
        // value is identified by comparing the query string against the
        // endpoint's declared parameters — see BindingFailureErrors.
        if (problem.Status is StatusCodes.Status400BadRequest
            && problem is not HttpValidationProblemDetails)
        {
            context.ProblemDetails = new HttpValidationProblemDetails(
                BindingFailureErrors.From(context.HttpContext))
            {
                Status = problem.Status,

                // The same title the framework gives handler-produced
                // validation problems.
                Title = ValidationProblemTitle,
                Type = problem.Type,
                Detail = problem.Detail,
                Instance = problem.Instance,

                // The default writer has already added traceId to the original
                // before calling this. Replacing the instance wholesale would
                // silently drop it, leaving a 400 that cannot be correlated
                // with the log — which is the documented way to diagnose one.
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

// AddProblemDetails registers the Problem Details writer but not the
// status-code pages that actually invoke it. Without this, any status code
// the handlers did not produce themselves — a binding failure, or the bare
// Results.NotFound() returned for a missing task — leaves the response with
// an empty body. Placing it here rather than at each call site keeps one
// shape for every error, including 404s from routing.
//
// The handler is explicit because the parameterless overload is content
// negotiation: its writer declines when Accept excludes problem+json and
// the middleware then falls back to plain text, so a browser asking for HTML
// would receive a body outside the documented contract. Writing through
// IProblemDetailsService unconditionally keeps the media type a fixed part of
// the contract rather than something the client can negotiate away. An error
// body is more useful than an error a client cannot parse, so this ignores
// Accept for failures only; successful responses still negotiate normally.
// AddProblemDetails registers the Problem Details writer but not the
// status-code pages that actually invoke it. Without this, any status code
// the handlers did not produce themselves — a binding failure, or the bare
// Results.NotFound() returned for a missing task — leaves the response with
// an empty body. Placing it here rather than at each call site keeps one
// shape for every error, including 404s from routing.
//
// The handler exists only to name the media type. The parameterless overload
// negotiates: its writer declines when Accept excludes problem+json and the
// middleware falls back to plain text, which would hand a client a body
// outside the documented contract. UnconditionalProblemDetailsWriter removes
// that negotiation from the error path, so an error body is always written in
// the one format the README promises — a body the client can parse beats one
// it would rather have negotiated. Successful responses still negotiate
// normally.
app.UseStatusCodePages(async (StatusCodeContext pageContext) =>
{
    var httpContext = pageContext.HttpContext;

    // The default writer declines when Accept excludes problem+json, and the
    // status-code pages then fall back to plain text. That would make the
    // media type of an error body negotiable, which the documented contract
    // does not allow: a client is told to read one media type for every
    // error, and one that cannot parse the body it was handed cannot act on
    // the failure. A body the client can parse beats one it would rather
    // have negotiated.
    //
    // The header is restored below, so nothing downstream — including
    // logging — sees it altered.
    var accept = httpContext.Request.Headers.Accept;

    try
    {
        httpContext.Request.Headers.Accept =
            "application/problem+json";

        var problemService = httpContext.RequestServices
            .GetRequiredService<IProblemDetailsService>();

        // WriteAsync rather than TryWriteAsync: with the header above, the
        // writer always accepts, and this keeps CustomizeProblemDetails in
        // play so a status-code-page body is the same shape a handler's.
        await problemService.WriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = httpContext.Response.StatusCode
                }
            });
    }
    finally
    {
        httpContext.Request.Headers.Accept = accept;
    }
});

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
