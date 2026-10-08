using Microsoft.EntityFrameworkCore;
using Npgsql;
using TaskBoard.Api.Data;
using TaskBoard.Api.Features.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
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
app.MapHealthChecks("/health/live");

app.Run();

public partial class Program { }