namespace TicketMaster.Modules.Iam.Domain;

/// <summary>
/// A persisted refresh token, stored only as a SHA-256 hash (the raw value is returned to the caller once and never
/// kept). Rotation on use: refreshing revokes the presented token and records the hash of its replacement, giving a
/// tamper-evident chain and single-use semantics. Not soft-deletable/auditable — it is short-lived security state,
/// not a business aggregate.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken() { } // EF

    private RefreshToken(Guid id, UserId userId, string tokenHash, DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc, string createdByIp)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        CreatedByIp = createdByIp;
    }

    public Guid Id { get; private set; }
    public UserId UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public string CreatedByIp { get; private set; } = string.Empty;
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string ReplacedByTokenHash { get; private set; } = string.Empty;

    public static RefreshToken Issue(UserId userId, string tokenHash, DateTimeOffset now, TimeSpan lifetime, string ip)
        => new(Guid.CreateVersion7(), userId, tokenHash, now, now + lifetime, ip);

    public bool IsActive(DateTimeOffset now) => RevokedAtUtc is null && now < ExpiresAtUtc;

    public void Revoke(DateTimeOffset now, string replacedByHash = "")
    {
        RevokedAtUtc ??= now; // keep the original revocation instant if already revoked
        ReplacedByTokenHash = replacedByHash;
    }
}
