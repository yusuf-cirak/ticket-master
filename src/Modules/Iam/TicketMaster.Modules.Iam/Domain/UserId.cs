using Vogen;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>Strongly-typed user id (Vogen-generated equality/validation/EF+JSON converters). Seeded time-ordered.</summary>
[ValueObject<Guid>]
public readonly partial struct UserId
{
    private static Validation Validate(Guid input) =>
        input != Guid.Empty ? Validation.Ok : Validation.Invalid("User id cannot be empty.");

    public static UserId New() => From(Guid.CreateVersion7());
}
