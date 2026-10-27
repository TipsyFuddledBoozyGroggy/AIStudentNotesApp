using Tether.SharedKernel;

namespace Tether.Courses.Domain;

internal sealed class Course : IUserOwned
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public string? Term { get; set; }
    public string? Instructor { get; set; }
    public string? Color { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    /// <summary>PostgreSQL xmin; optimistic concurrency token and ETag source.</summary>
    public uint Version { get; set; }
}
