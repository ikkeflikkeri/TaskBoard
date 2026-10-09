using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace TaskBoard.Api.IntegrationTests;

/// <summary>
/// Hosts the real application with one extra route that throws, so the
/// unhandled-exception path can be observed rather than assumed.
/// </summary>
/// <remarks>
/// The route is registered by an <see cref="IStartupFilter"/> that appends it
/// to the pipeline *after* Program.cs has finished building its own
/// middleware. That ordering matters: the failing endpoint must sit behind
/// the <c>UseExceptionHandler</c> Program.cs installs, or the test would
/// exercise a pipeline no deployment actually runs.
///
/// No service is replaced. Swapping the Problem Details writer or the
/// exception handler out would test the substitute rather than the wiring
/// this API ships, and would let the test pass while the shipped
/// configuration leaks.
///
/// The connection string reaches the host the same way
/// <see cref="TaskBoardFactory"/> delivers it, because Program.cs validates
/// it before any factory hook could supply it.
/// </remarks>
public sealed class ThrowingEndpointFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// A credential-shaped string standing in for whatever an exception
    /// message might leak — a connection string, a token, a file path.
    /// </summary>
    public const string Secret =
        "Host=db.internal;Password=hunter2-do-not-leak";

    public static ThrowingEndpointFactory Start() =>
        ConnectionStringEnvironmentScope
            .StartHost<ThrowingEndpointFactory>(
                UnreachableDatabase.ConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development, not Testing: this is the environment the README tells
        // developers to run in, and the one where a framework change is most
        // likely to start including exception detail. Under Testing the
        // response would be clean whatever the API's configuration says.
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
            services.AddSingleton<IStartupFilter, ThrowingRouteFilter>());
    }

    /// <summary>
    /// Appends a failing endpoint after the application's own pipeline, so
    /// the exception travels back through the real exception handler.
    /// </summary>
    private sealed class ThrowingRouteFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(
            Action<IApplicationBuilder> next)
        {
            return app =>
            {
                next(app);

                app.UseRouting();

                app.UseEndpoints(endpoints =>
                    endpoints.MapGet(
                        "/throw-sensitive-detail",
                        ThrowSensitiveDetail));
            };
        }
    }

    // A handler that always fails, so the exception-handling pipeline is
    // exercised for real rather than simulated.
    private static string ThrowSensitiveDetail() =>
        throw new InvalidOperationException(
            $"Unhandled failure carrying {Secret}.");
}