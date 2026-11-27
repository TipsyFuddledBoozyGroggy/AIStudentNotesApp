namespace Tether.Identity.Domain;

internal sealed class User
{
    public const string GooglePrefix = "google:";
    public const string DevPrefix = "dev:";

    public Guid Id { get; set; }
    /// <summary>"google:{sub}" for Google sign-in (auth.v1 section 6, unique); "dev:{email}" for dev-login users (Development only).</summary>
    public string ExternalSubject { get; set; } = "";
    /// <summary>Stored lower-case. Display only: never a key, not unique (the same address under two Google subjects is two users).</summary>
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Whether Google's <c>hd</c> claim was present at the last sign-in (null for dev users). The domain itself is never stored.</summary>
    public bool? HostedDomainPresent { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
