using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TaskBoard.Api.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace TaskBoard.Api.IntegrationTests;

public sealed class TaskBoardFactory(string connectionString)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<NpgsqlDataSource>();

            services.AddSingleton<NpgsqlDataSource>(_ =>
                NpgsqlDataSource.Create(connectionString));
        });
    }
}

public sealed class TaskBoardFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:18")
            .Build();

    public TaskBoardFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Factory = new TaskBoardFactory(_postgres.GetConnectionString());

        await using var scope = Factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<TasksDbContext>();

        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (Factory is not null)
                await Factory.DisposeAsync();
        }
        finally
        {
            await _postgres.DisposeAsync();
        }
    }
}
