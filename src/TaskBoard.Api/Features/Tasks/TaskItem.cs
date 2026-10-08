namespace TaskBoard.Api.Features.Tasks;

public sealed class TaskItem
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public bool IsCompleted { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public long Version { get; set; }
}
