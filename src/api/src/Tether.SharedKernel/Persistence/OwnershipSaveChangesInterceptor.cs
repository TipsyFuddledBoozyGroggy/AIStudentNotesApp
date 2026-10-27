using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tether.SharedKernel.Persistence;

/// <summary>
/// Defense in depth behind the global query filters: stamps UserId on insert and refuses to
/// persist any added/modified/deleted user-owned entity that does not belong to the current user.
/// </summary>
public sealed class OwnershipSaveChangesInterceptor(ICurrentUser currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enforce(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Enforce(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Enforce(DbContext? context)
    {
        if (context is null) return;
        foreach (var entry in context.ChangeTracker.Entries<IUserOwned>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            var me = currentUser.UserId
                ?? throw new OwnershipViolationException("Writing user-owned data requires an authenticated user.");
            var entity = entry.Entity;
            if (entry.State == EntityState.Added)
            {
                if (entity.UserId == Guid.Empty) entity.UserId = me;
                else if (entity.UserId != me) throw new OwnershipViolationException("Cannot create data for another user.");
            }
            else
            {
                var original = entry.Property(nameof(IUserOwned.UserId)).OriginalValue as Guid?;
                if (entity.UserId != me || original != me)
                    throw new OwnershipViolationException("Cannot modify data owned by another user.");
            }
        }
    }
}

public sealed class OwnershipViolationException(string message) : InvalidOperationException(message);
