using Tether.Courses.Contracts;
using Tether.SharedKernel;

namespace Tether.Lectures.Application;

/// <summary>Lectures side of <see cref="ICourseLifecycleHandler"/>: ends recording before a course is archived or purged.</summary>
internal sealed class CourseLifecycleHandler(SessionService sessions, ICurrentUser currentUser) : ICourseLifecycleHandler
{
    public Task OnArchivingAsync(Guid userId, Guid courseId, CancellationToken ct = default) =>
        currentUser.UserId == userId ? sessions.EndSessionsForCourseAsync(courseId, purge: false, ct) : Task.CompletedTask;

    public Task OnPurgingAsync(Guid userId, Guid courseId, CancellationToken ct = default) =>
        currentUser.UserId == userId ? sessions.EndSessionsForCourseAsync(courseId, purge: true, ct) : Task.CompletedTask;
}
