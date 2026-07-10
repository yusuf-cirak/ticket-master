using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TicketMaster.BuildingBlocks.Domain.Identifiers;

namespace TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore.Identifiers;

// The read lambda invokes the cached factory delegate (a static field) — legal in an expression tree, and it rebuilds
// the id from its (TValue) constructor with no marker interface required.

/// <summary>Stores a <see cref="GuidId"/> as its underlying <see cref="Guid"/>; rebuilds the strong type on read.</summary>
public sealed class GuidIdConverter<TId> : ValueConverter<TId, Guid>
    where TId : GuidId
{
    public GuidIdConverter() : base(id => id.Value, value => StronglyTypedIdFactory<TId, Guid>.Create(value)) { }
}

/// <summary>Stores a <see cref="StringId"/> as its underlying <see cref="string"/>.</summary>
public sealed class StringIdConverter<TId> : ValueConverter<TId, string>
    where TId : StringId
{
    public StringIdConverter() : base(id => id.Value, value => StronglyTypedIdFactory<TId, string>.Create(value)) { }
}

/// <summary>Stores an <see cref="IntId"/> as its underlying <see cref="int"/>.</summary>
public sealed class IntIdConverter<TId> : ValueConverter<TId, int>
    where TId : IntId
{
    public IntIdConverter() : base(id => id.Value, value => StronglyTypedIdFactory<TId, int>.Create(value)) { }
}
