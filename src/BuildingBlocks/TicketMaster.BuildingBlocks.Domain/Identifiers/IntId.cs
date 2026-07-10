using System.Globalization;

namespace TicketMaster.BuildingBlocks.Domain.Identifiers;

/// <summary>Base for a strongly-typed int id (e.g. a database identity key). See <see cref="GuidId"/> for the pattern.</summary>
public abstract record IntId(int Value)
{
    public sealed override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
