using Vogen;

namespace TicketMaster.Modules.Events.Domain;

/// <summary>Strongly-typed event id (Version-7 GUID). Vogen generates the reflection-free EF + JSON converters.</summary>
[ValueObject<Guid>(conversions: Conversions.EfCoreValueConverter | Conversions.SystemTextJson)]
public readonly partial struct EventId
{
    public static EventId New() => From(Guid.CreateVersion7());
}
