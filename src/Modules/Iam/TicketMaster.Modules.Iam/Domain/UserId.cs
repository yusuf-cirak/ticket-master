using Vogen;

namespace TicketMaster.Modules.Iam.Domain;

/// <summary>Strongly-typed identifier for the User aggregate. Vogen generates value equality, the EF value converter,
/// the System.Text.Json converter, and IParsable (route/query binding).</summary>
[ValueObject<Guid>(conversions: Conversions.EfCoreValueConverter | Conversions.SystemTextJson)]
public readonly partial struct UserId
{
    public static UserId New() => From(Guid.CreateVersion7());

    private static Validation Validate(Guid value) =>
        value == Guid.Empty ? Validation.Invalid("UserId cannot be empty.") : Validation.Ok;
}
