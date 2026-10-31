using Tether.Courses.Contracts;
using Tether.SharedKernel;

namespace Tether.Lectures.Application;

/// <summary>Lectures side of <see cref="ICourseLifecycleHandler"/>: ends recording before a course is archived or purged.</summary>
/// <remarks>Scoped: one instance per request, so the session ids announced before the purge commit are the ones re-announced after it.</remarks>
internal sealed class CourseLifecycleHandler(SessionService sessions, ICurrentUser currentUser) : ICourseLifecycleHandler
{
    private IReadOnlyList<Guid> _purged = [];

    public Task OnArchivingAsync(Guid userId, Guid courseId, CancellationToken ct = default) =>
        currentUser.UserId == userId ? sessions.EndSessionsForCourseAsync(courseId, purge: false, ct) : Task.CompletedTask;

    public async Task OnPurgingAsync(Guid userId, Guid courseId, CancellationToken ct = default)
    {
        if (currentUser.UserId == userId) _purged = await sessions.EndSessionsForCourseAsync(courseId, purge: true, ct);
    }

    /// <summary>NAS-01: after the commit, every purged session is announced deleted again (observers cancel, tombstone and close hub connections).</summary>
    public Task OnPurgedAsync(Guid userId, Guid courseId, CancellationToken ct = default) =>
        currentUser.UserId == userId ? sessions.NotifyPurgedAsync(userId, _purged, ct) : Task.CompletedTask;
}
