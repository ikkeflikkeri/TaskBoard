using Microsoft.EntityFrameworkCore;
using TaskBoard.Api.Data;

namespace TaskBoard.Api.Features.Tasks;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/tasks");

        group.MapPost("/", Create);
        group.MapGet("/{id:guid}", GetById);
        group.MapGet("/", List);
        group.MapPut("/{id:guid}", Update);
    }

    private static async Task<IResult> Create(
        CreateTaskRequest request,
        TasksDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var title = request.Title?.Trim();

        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            return InvalidTitle();

        var task = new TaskItem
        {
            Id = Guid.CreateVersion7(),
            Title = title,
            IsCompleted = false,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            Version = 1
        };

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/tasks/{task.Id}",
            ToResponse(task));
    }

    private static async Task<IResult> GetById(
        Guid id,
        TasksDbContext db,
        CancellationToken ct)
    {
        var result = await db.Tasks
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new TaskResponse(
                x.Id,
                x.Title,
                x.IsCompleted,
                x.CreatedAtUtc,
                x.Version))
            .SingleOrDefaultAsync(ct);

        return result is null
            ? Results.NotFound()
            : Results.Ok(result);
    }

    private static async Task<IResult> List(
        int? limit,
        TasksDbContext db,
        CancellationToken ct)
    {
        var pageSize = limit ?? 50;

        if (pageSize is < 1 or > 100)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["limit"] = ["Must be between 1 and 100."]
                });
        }

        var results = await db.Tasks
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Take(pageSize)
            .Select(x => new TaskResponse(
                x.Id,
                x.Title,
                x.IsCompleted,
                x.CreatedAtUtc,
                x.Version))
            .ToListAsync(ct);

        return Results.Ok(results);
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateTaskRequest request,
        TasksDbContext db,
        CancellationToken ct)
    {
        var title = request.Title?.Trim();

        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            return InvalidTitle();

        if (request.Version < 1)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["version"] = ["Must be a positive version."]
                });
        }

        var task = await db.Tasks.SingleOrDefaultAsync(
            x => x.Id == id, ct);

        if (task is null)
            return Results.NotFound();

        if (task.Version != request.Version)
            return VersionConflict();

        task.Title = title;
        task.IsCompleted = request.IsCompleted;
        task.Version++;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return VersionConflict();
        }

        return Results.Ok(ToResponse(task));
    }

    private static IResult InvalidTitle() =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["title"] = ["Must contain 1–200 characters after trimming."]
            });

    private static IResult VersionConflict() =>
        Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Task version conflict",
            detail: "Reload the task before submitting another update.");

    private static TaskResponse ToResponse(TaskItem task) =>
        new(
            task.Id,
            task.Title,
            task.IsCompleted,
            task.CreatedAtUtc,
            task.Version);
}

public sealed record CreateTaskRequest(string? Title);

public sealed record UpdateTaskRequest(
    string? Title,
    bool IsCompleted,
    long Version);

public sealed record TaskResponse(
    Guid Id,
    string Title,
    bool IsCompleted,
    DateTime CreatedAtUtc,
    long Version);
