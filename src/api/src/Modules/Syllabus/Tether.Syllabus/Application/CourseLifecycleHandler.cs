using Tether.Courses.Contracts;
using Tether.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Tether.Syllabus.Application;

/// <summary>
/// SB-05: Syllabus side of <see cref="ICourseLifecycleHandler"/>. A course purge removes the course's syllabi by FK cascade
/// (S002 fk_syllabi_course_user); this handler does what a cascade cannot: it cuts off running extraction jobs and their in-flight AI calls
/// (<see cref="JobCancellation"/>, which also calls <c>IAiScopeCutoff</c>), writes the content-free <c>syllabus_deleted</c> audit row for every
/// syllabus the cascade removed, and removes the course's mock calendar events (the hook a real calendar.v1 provider needs).
/// </summary>
/// <remarks>
/// Scoped: one instance per request, so the syllabus ids seen before the commit are the ones re-announced after it (same pattern as the Lectures
/// handler, NAS-01). BEFORE the commit (<see cref="OnPurgingAsync"/>) it only cancels: nothing is written, so a purge that rolls back (a
/// concurrent course edit is a 409) leaves every syllabus intact; a cancelled job resumes when its lease expires. AFTER the commit
/// (<see cref="OnPurgedAsync"/>) is the authority: it cancels again (a job claimed in the gap), and audits each syllabus that is now gone,
/// including one created in the gap between the two notices (found by its surviving <c>syllabus_created</c> audit row).
/// No create-key tombstone is written: a live create key cascades with the course (fk_idempotency_keys_course_user), the course id is gone, so a
/// late retry of that create is <c>404</c> at the ownership step, and a tombstone written before the commit would wrongly answer <c>404</c> for a
/// syllabus that survives a rolled-back purge (docs/data/schema-syllabus-p2.md section 6, X-02).
/// </remarks>
internal sealed class SyllabusCourseLifecycleHandler(SyllabusCore core, JobCancellation jobs, MockCalendar calendar, ICurrentUser currentUser,
    ILogger<SyllabusCourseLifecycleHandler> log) : ICourseLifecycleHandler
{
    /// <summary>How far before the pre-commit notice a create may have started and still have committed in the gap (its audit time is its transaction start).</summary>
    internal static readonly TimeSpan GapMargin = TimeSpan.FromMinutes(10);

    private IReadOnlyList<Guid> _announced = [];
    private DateTimeOffset _noticeAt;

    /// <summary>Archive keeps the course's syllabi (they stay readable and deletable); nothing to stop.</summary>
    public Task OnArchivingAsync(Guid userId, Guid courseId, CancellationToken ct = default) => Task.CompletedTask;

    public async Task OnPurgingAsync(Guid userId, Guid courseId, CancellationToken ct = default)
    {
        if (currentUser.UserId != userId) return;
        _noticeAt = core.Now();
        _announced = await core.Db.Syllabi.AsNoTracking().Where(s => s.CourseId == courseId).Select(s => s.Id).ToListAsync(ct);
        foreach (var id in _announced) jobs.Cancel(id);   // running extraction and any in-flight AI call for the scope
    }

    /// <summary>Must not throw (the delete has committed): failures are logged by exception type only.</summary>
    public async Task OnPurgedAsync(Guid userId, Guid courseId, CancellationToken ct = default)
    {
        if (currentUser.UserId != userId) return;
        try
        {
            core.Db.ChangeTracker.Clear();
            var since = (_noticeAt == default ? core.Now() : _noticeAt) - GapMargin;
            var createdRecently = await core.Db.Audit.AsNoTracking()
                .Where(a => a.Action == "syllabus_created" && a.OccurredAt >= since && !_announced.Contains(a.SyllabusId))
                .Select(a => a.SyllabusId).Distinct().ToListAsync(ct);
            var candidates = _announced.Concat(createdRecently).Distinct().ToList();
            if (candidates.Count > 0)
            {
                var alive = await core.Db.Syllabi.AsNoTracking().Where(s => candidates.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct);
                var gone = candidates.Except(alive).ToList();
                foreach (var id in gone) jobs.Cancel(id);
                var audited = await core.Db.Audit.AsNoTracking().Where(a => a.Action == "syllabus_deleted" && gone.Contains(a.SyllabusId))
                    .Select(a => a.SyllabusId).Distinct().ToListAsync(ct);
                var now = core.Now();
                var toAudit = gone.Except(audited).ToList();
                foreach (var id in toAudit) core.Audit(id, "syllabus_deleted", now);
                if (toAudit.Count > 0) await core.Db.SaveChangesAsync(ct);
                var late = toAudit.Count(id => !_announced.Contains(id));
                if (late > 0) log.LogWarning("Course purge removed {Count} syllabus(es) created after the pre-commit notice; audited now", late);
            }
            await calendar.RemoveCourseEventsAsync(courseId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("Syllabus course purge follow-up failed with {ExceptionType}", ex.GetType().Name);   // type only, never content
        }
    }
}
