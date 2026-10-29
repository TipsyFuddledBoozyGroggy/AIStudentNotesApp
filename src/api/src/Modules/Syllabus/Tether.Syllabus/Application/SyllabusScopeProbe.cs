using Tether.SharedKernel;
using Tether.SharedKernel.Ai;
using Tether.Syllabus.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tether.Syllabus.Application;

/// <summary>
/// Answers the AI gateway's "is this document job still wanted" question (kind syllabus, scope id = syllabus id). User-scoped read, true only
/// while the syllabus exists for that user and its extraction job is running. The mock extractor makes no AI call today; the seam is ready.
/// </summary>
internal sealed class SyllabusScopeProbe(IServiceScopeFactory scopes) : IAiDocumentScopeProbe
{
    public string Kind => AiScopeKinds.Syllabus;

    public async Task<bool> IsLiveAsync(Guid userId, Guid scopeId, CancellationToken ct)
    {
        await using var scope = scopes.CreateUserScope(userId);
        var db = scope.ServiceProvider.GetRequiredService<SyllabusDbContext>();
        return await db.Jobs.AsNoTracking().AnyAsync(j => j.SyllabusId == scopeId && j.State == "running", ct);
    }
}
