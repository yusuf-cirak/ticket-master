using System.ComponentModel.DataAnnotations;

namespace BuildingBlocks.Security;

/// <summary>How the JWT signing key is established and validated.</summary>
public enum JwtMode
{
    /// <summary>HMAC shared secret (<see cref="JwtOptions.SigningKey"/>). Simple; intended for dev/test only.</summary>
    Symmetric,

    /// <summary>OIDC: keys fetched from the issuer's JWKS at <see cref="JwtOptions.Authority"/>. Production shape.</summary>
    Authority
}

/// <summary>
/// Bound from the <c>Authentication</c> configuration section and validated at startup (fail-fast). One option set
/// covers both signing strategies so the same code path serves dev (symmetric secret) and prod (OIDC authority).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Authentication";

    public JwtMode Mode { get; set; } = JwtMode.Symmetric;

    /// <summary>Expected token issuer (<c>iss</c>). Required.</summary>
    [Required]
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Expected audience (<c>aud</c>). Required.</summary>
    [Required]
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC secret for <see cref="JwtMode.Symmetric"/>. Dev/test only — never a production secret in config.</summary>
    public string SigningKey { get; set; }

    /// <summary>OIDC issuer base URL for <see cref="JwtMode.Authority"/> (JWKS auto-discovered).</summary>
    public string Authority { get; set; }

    /// <summary>Require HTTPS for metadata/JWKS retrieval. Keep <c>true</c> outside local development.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Allowed clock drift on lifetime validation. Default 30s (the framework default of 5min is too lax).</summary>
    [Range(0, 300)]
    public int ClockSkewSeconds { get; set; } = 30;
}
