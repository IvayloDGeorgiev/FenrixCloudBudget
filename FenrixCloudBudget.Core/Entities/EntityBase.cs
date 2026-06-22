namespace FenrixCloudBudget.Core.Entities;

/// <summary>Common audit fields shared by all persisted entities.</summary>
public abstract class EntityBase
{
    public int Id { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedUtc { get; set; }
}
