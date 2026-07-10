using System.Linq.Expressions;

namespace TicketMaster.BuildingBlocks.Domain.Identifiers;

/// <summary>
/// Rebuilds a strongly-typed id from its underlying value via the id's <c>(TValue)</c> constructor — compiled once
/// per closed type and cached. Used by the EF value converter and the JSON converter so no per-id factory method or
/// marker interface is needed; a plain <c>record UserId(Guid Value) : GuidId(Value)</c> is enough.
/// </summary>
public static class StronglyTypedIdFactory<TId, TValue>
{
    public static readonly Func<TValue, TId> Create = Build();

    private static Func<TValue, TId> Build()
    {
        var constructor = typeof(TId).GetConstructor([typeof(TValue)])
            ?? throw new InvalidOperationException(
                $"Strongly-typed id '{typeof(TId)}' must have a public constructor taking a single '{typeof(TValue)}'.");

        var value = Expression.Parameter(typeof(TValue), "value");
        return Expression.Lambda<Func<TValue, TId>>(Expression.New(constructor, value), value).Compile();
    }
}
