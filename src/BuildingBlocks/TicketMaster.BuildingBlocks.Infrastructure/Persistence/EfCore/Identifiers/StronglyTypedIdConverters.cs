using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TicketMaster.BuildingBlocks.Domain.Identifiers;

namespace TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore.Identifiers;

// The value-out lambda calls a concrete static method (not the static-abstract interface member directly), because an
// expression tree may not reference a static virtual/abstract interface member (CS8927).

/// <summary>Stores a <see cref="GuidId"/> as its underlying <see cref="Guid"/>; rebuilds the strong type on read.</summary>
public sealed class GuidIdConverter<TId> : ValueConverter<TId, Guid>
    where TId : GuidId, IGuidId<TId>
{
    public GuidIdConverter() : base(id => id.Value, value => Create(value)) { }
    private static TId Create(Guid value) => TId.FromGuid(value);
}

/// <summary>Stores a <see cref="StringId"/> as its underlying <see cref="string"/>.</summary>
public sealed class StringIdConverter<TId> : ValueConverter<TId, string>
    where TId : StringId, IStringId<TId>
{
    public StringIdConverter() : base(id => id.Value, value => Create(value)) { }
    private static TId Create(string value) => TId.FromString(value);
}

/// <summary>Stores an <see cref="IntId"/> as its underlying <see cref="int"/>.</summary>
public sealed class IntIdConverter<TId> : ValueConverter<TId, int>
    where TId : IntId, IIntId<TId>
{
    public IntIdConverter() : base(id => id.Value, value => Create(value)) { }
    private static TId Create(int value) => TId.FromInt(value);
}
