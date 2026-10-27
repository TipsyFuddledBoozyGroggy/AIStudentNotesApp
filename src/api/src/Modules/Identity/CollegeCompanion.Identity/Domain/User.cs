namespace CollegeCompanion.Identity.Domain;

internal sealed class User
{
    public Guid Id { get; set; }
    /// <summary>Stable id from the identity provider (Google "sub"); "dev:{email}" for dev-login users.</summary>
    public string ExternalSubject { get; set; } = "";
    /// <summary>Stored lower-case.</summary>
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
