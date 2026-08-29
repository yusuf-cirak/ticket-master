using Vogen;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>Strongly-typed role id, backed by a database-generated int identity (roles are reference data, so no
/// client-side factory beyond <c>From</c> for reconstruction; no validation since the value is 0 before insert).</summary>
[ValueObject<int>(conversions: Conversions.EfCoreValueConverter | Conversions.SystemTextJson)]
public readonly partial struct RoleId
{
    public static bool operator <(RoleId left, RoleId right) => left.CompareTo(right) < 0;
    public static bool operator <=(RoleId left, RoleId right) => left.CompareTo(right) <= 0;
    public static bool operator >(RoleId left, RoleId right) => left.CompareTo(right) > 0;
    public static bool operator >=(RoleId left, RoleId right) => left.CompareTo(right) >= 0;
}
