namespace Tether.SharedKernel;

/// <summary>The authenticated caller. Always derived from the server-side session, never from client input.</summary>
public interface ICurrentUser
{
    /// <summary>Null when the request is anonymous.</summary>
    Guid? UserId { get; }

    /// <summary>
    /// Id of the server-side login session (identity.sessions) behind this request's cookie, or null. Lets features such as
    /// audio tickets bind to one login so that logout invalidates them. Default null keeps test fakes source compatible.
    /// </summary>
    Guid? AuthSessionId => null;
}

/// <summary>Marker for entities owned by exactly one user. UserId is stamped and verified by the ownership interceptor.</summary>
public interface IUserOwned
{
    Guid UserId { get; set; }
}

public static class Ids
{
    /// <summary>Time-ordered, non-guessable id (UUIDv7).</summary>
    public static Guid New() => Guid.CreateVersion7();
}
