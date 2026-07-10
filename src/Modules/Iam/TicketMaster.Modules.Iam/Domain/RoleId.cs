using TicketMaster.BuildingBlocks.Domain.Identifiers;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>Strongly-typed role id. Seeded time-ordered.</summary>
public sealed record RoleId(Guid Value) : GuidId(Value), IParsable<RoleId>
{
    public static RoleId New() => new(Guid.CreateVersion7());

    /// <summary>Validated factory for inbound values: rejects the empty GUID.</summary>
    public static RoleId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("Role id cannot be empty.", nameof(value))
        : new(value);

    public static RoleId Parse(string s, IFormatProvider provider) => new(Guid.Parse(s));

    public static bool TryParse(string s, IFormatProvider provider, out RoleId result)
    {
        if (Guid.TryParse(s, out var value))
        {
            result = new RoleId(value);
            return true;
        }

        result = null;
        return false;
    }
}
