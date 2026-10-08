using Microsoft.EntityFrameworkCore;
using TaskBoard.Api.Features.Tasks;

namespace TaskBoard.Api.Data;

public sealed class TasksDbContext(
    DbContextOptions<TasksDbContext> options) : DbContext(options)
{
    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var task = modelBuilder.Entity<TaskItem>();

        task.ToTable("tasks", table =>
        {
            table.HasCheckConstraint(
                "ck_tasks_title_not_blank",
                "length(btrim(\"Title\")) > 0");

            table.HasCheckConstraint(
                "ck_tasks_version_positive",
                "\"Version\" > 0");
        });

        task.HasKey(x => x.Id);

        task.Property(x => x.Title)
            .HasMaxLength(200)
            .IsRequired();

        task.Property(x => x.CreatedAtUtc)
            .HasColumnType("timestamp with time zone");

        task.Property(x => x.Version)
            .IsConcurrencyToken();

        task.HasIndex(x => new { x.CreatedAtUtc, x.Id });
    }
}
