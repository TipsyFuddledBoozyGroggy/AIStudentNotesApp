using System.Data.Common;

namespace Tether.SharedKernel.Persistence;

/// <summary>
/// PostgreSQL error classification shared by the retry loops (lesson L-LOCKORDER, docs/data/lock-order-audit.md F-8). Reads the provider-neutral
/// <see cref="DbException.SqlState"/>, which Npgsql's <c>PostgresException</c> overrides, so the kernel needs no Npgsql reference.
/// </summary>
public static class PgErrors
{
    public const string DeadlockDetected = "40P01";
    public const string SerializationFailure = "40001";
    public const string ForeignKeyViolation = "23503";

    /// <summary>True when any exception in the chain is deadlock_detected (40P01) or serialization_failure (40001): the transaction rolled back, so a retry is safe.</summary>
    public static bool IsDeadlockOrSerialization(Exception ex) => HasState(ex, DeadlockDetected) || HasState(ex, SerializationFailure);

    /// <summary>True when any exception in the chain is foreign_key_violation (23503), for example a parent deleted between a read and an insert.</summary>
    public static bool IsForeignKeyViolation(Exception ex) => HasState(ex, ForeignKeyViolation);

    private static bool HasState(Exception ex, string state)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is DbException { SqlState: { } s } && s == state) return true;
        return false;
    }
}
