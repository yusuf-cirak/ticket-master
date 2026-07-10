using TicketMaster.BuildingBlocks.Domain.Identifiers;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>Strongly-typed role id. Seeded time-ordered.</summary>
public sealed record RoleId(Guid Value) : GuidId(Value), IGuidId<RoleId>
{
    public static RoleId New() => new(Guid.CreateVersion7());

    /// <summary>Trusted rehydration (EF / JSON) — no validation.</summary>
    public static RoleId FromGuid(Guid value) => new(value);

    /// <summary>Validated factory for inbound values: rejects the empty GUID.</summary>
    public static RoleId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Role id cannot be empty.", nameof(value))
        : new(value);
}
