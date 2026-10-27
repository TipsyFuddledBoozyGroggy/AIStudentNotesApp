namespace Tether.Courses.Contracts;

/// <summary>
/// Public port of the Courses module. Other modules (Lectures, later) use this instead of touching course tables.
/// </summary>
public interface ICourseOwnership
{
    /// <summary>True only if the course exists, is not purged, and belongs to <paramref name="userId"/>
    /// (which must also be the current user).</summary>
    Task<bool> IsOwnedByAsync(Guid userId, Guid courseId, CancellationToken ct = default);
}
