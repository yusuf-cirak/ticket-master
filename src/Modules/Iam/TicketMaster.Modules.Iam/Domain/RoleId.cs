using Vogen;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>Strongly-typed role id (Vogen-generated). Seeded time-ordered.</summary>
[ValueObject<Guid>(conversions: Conversions.Default | Conversions.EfCoreValueConverter)]
public readonly partial struct RoleId
{
    private static Validation Validate(Guid input) =>
        input != Guid.Empty ? Validation.Ok : Validation.Invalid("Role id cannot be empty.");

    public static RoleId New() => From(Guid.CreateVersion7());
}
