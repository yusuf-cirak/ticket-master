using TicketMaster.BuildingBlocks.Domain.Identifiers;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>Strongly-typed identifier for the User aggregate (users/claims/logins/tokens).</summary>
public sealed record UserId(Guid Value) : GuidId(Value), IGuidId<UserId>
{
    public static UserId New() => new(Guid.CreateVersion7());

    /// <summary>Trusted rehydration (EF / JSON) — no validation.</summary>
    public static UserId FromGuid(Guid value) => new(value);

    /// <summary>Validated factory for inbound values: rejects the empty GUID.</summary>
    public static UserId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("UserId cannot be empty.", nameof(value))
        : new(value);
}
