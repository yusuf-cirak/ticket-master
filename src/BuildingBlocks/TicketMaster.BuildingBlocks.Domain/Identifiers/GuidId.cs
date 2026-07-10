namespace TicketMaster.BuildingBlocks.Domain.Identifiers;

/// <summary>
/// Base for a strongly-typed GUID id. Derive a sealed record and implement <see cref="IGuidId{TSelf}"/> so one
/// line of code gives you value equality, EF conversion, JSON (de)serialization, and route/query binding:
/// <code>public sealed record UserId(Guid Value) : GuidId(Value), IGuidId&lt;UserId&gt; { public static UserId FromGuid(Guid v) => new(v); }</code>
/// </summary>
public abstract record GuidId(Guid Value)
{
    public sealed override string ToString() => Value.ToString();
}

/// <summary>
/// Ties a concrete <see cref="GuidId"/> to its own type via <see cref="FromGuid"/> (used by the EF converter and JSON
/// factory to rebuild the strong type from the raw <see cref="Guid"/>) and hands you <see cref="IParsable{TSelf}"/>
/// for free so the id binds directly from route/query strings.
/// </summary>
public interface IGuidId<TSelf> : IParsable<TSelf>
    where TSelf : GuidId, IGuidId<TSelf>
{
    static abstract TSelf FromGuid(Guid value);

    static TSelf IParsable<TSelf>.Parse(string s, IFormatProvider provider)
        => TSelf.FromGuid(Guid.Parse(s));

    static bool IParsable<TSelf>.TryParse(string s, IFormatProvider provider, out TSelf result)
    {
        if (Guid.TryParse(s, out var value))
        {
            result = TSelf.FromGuid(value);
            return true;
        }

        result = null;
        return false;
    }
}
