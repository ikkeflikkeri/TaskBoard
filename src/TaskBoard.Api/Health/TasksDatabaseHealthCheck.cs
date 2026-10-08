using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TaskBoard.Api.Data;

namespace TaskBoard.Api.Health;

public sealed class TasksDatabaseHealthCheck(
    IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<TasksDbContext>();

        // Connectivity only. This does not assert that the schema
        // is current, so it must not be read as a migration check.
        var canConnect = await db.Database.CanConnectAsync(
            cancellationToken);

        return canConnect
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy(
                "Cannot connect to the tasks database.");
    }
}