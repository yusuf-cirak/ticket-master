namespace TicketMaster.Modules.Iam.Infrastructure;

/// <summary>Token lifetimes for the IAM issuer, bound from configuration section <c>Iam:Tokens</c>.</summary>
public sealed class TokenIssuanceOptions
{
    public const string SectionName = "Iam:Tokens";

    /// <summary>Access-token lifetime in minutes. Short by design — revocation is coarse, so keep the window small.</summary>
    public int AccessTokenMinutes { get; init; } = 15;

    /// <summary>Refresh-token lifetime in days.</summary>
    public int RefreshTokenDays { get; init; } = 14;
}
