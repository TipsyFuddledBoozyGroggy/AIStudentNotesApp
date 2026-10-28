namespace Tether.Courses.Contracts;

/// <summary>
/// Called by Courses, in the caller's user scope, BEFORE it archives or permanently deletes a course. Lectures implements it so a
/// course change can never leave a recording running or leave consent evidence incomplete (B2-06, B2-07, ADR-020).
/// Implementations may throw: Courses then does not change the course.
/// </summary>
public interface ICourseLifecycleHandler
{
    /// <summary>The course is about to be archived. Live sessions of the course must be stopped.</summary>
    Task OnArchivingAsync(Guid userId, Guid courseId, CancellationToken ct = default);

    /// <summary>The course is about to be purged (its sessions go with it by FK cascade). Live sessions are stopped with a consent Stop event; deletion is audited.</summary>
    Task OnPurgingAsync(Guid userId, Guid courseId, CancellationToken ct = default);
}
