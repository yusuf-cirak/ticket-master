using System.Globalization;
using TicketMaster.BuildingBlocks.Domain.Identifiers;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>Strongly-typed role id. Backed by a database-generated int identity (roles are reference data).</summary>
public sealed record RoleId(int Value) : IntId(Value), IParsable<RoleId>
{
    /// <summary>For reconstructing a known id (tests, references). New roles get their id from the database on insert.</summary>
    public static RoleId From(int value) => value <= 0
        ? throw new ArgumentException("Role id must be positive.", nameof(value))
        : new(value);

    public static RoleId Parse(string s, IFormatProvider provider)
        => new(int.Parse(s, provider ?? CultureInfo.InvariantCulture));

    public static bool TryParse(string s, IFormatProvider provider, out RoleId result)
    {
        if (int.TryParse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            result = new RoleId(value);
            return true;
        }

        result = null;
        return false;
    }
}
