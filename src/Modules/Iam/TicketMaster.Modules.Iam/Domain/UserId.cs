using TicketMaster.BuildingBlocks.Domain.Identifiers;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>Strongly-typed identifier for the User aggregate (users/claims/logins/tokens).</summary>
public sealed record UserId(Guid Value) : GuidId(Value), IParsable<UserId>
{
    public static UserId New() => new(Guid.CreateVersion7());

    /// <summary>Validated factory for inbound values: rejects the empty GUID.</summary>
    public static UserId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("UserId cannot be empty.", nameof(value))
        : new(value);

    public static UserId Parse(string s, IFormatProvider provider) => new(Guid.Parse(s));

    public static bool TryParse(string s, IFormatProvider provider, out UserId result)
    {
        if (Guid.TryParse(s, out var value))
        {
            result = new UserId(value);
            return true;
        }

        result = null;
        return false;
    }
}
