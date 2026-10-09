using System.Diagnostics;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace TaskBoard.Api.Configuration;

public sealed class ApiProblemDetailsWriter(
    IOptions<ProblemDetailsOptions> problemOptions,
    IOptions<JsonOptions> jsonOptions) : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        context.ProblemDetails.Status ??= context.HttpContext.Response.StatusCode;
        _ = TypedResults.Problem(context.ProblemDetails);
        context.ProblemDetails.Extensions["traceId"] =
            Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        problemOptions.Value.CustomizeProblemDetails?.Invoke(context);

        return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
            context.ProblemDetails,
            jsonOptions.Value.SerializerOptions.GetTypeInfo(
                context.ProblemDetails.GetType()),
            contentType: "application/problem+json",
            cancellationToken: context.HttpContext.RequestAborted));
    }
}
