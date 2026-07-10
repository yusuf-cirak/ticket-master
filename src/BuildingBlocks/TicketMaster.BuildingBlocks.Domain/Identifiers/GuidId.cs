namespace TicketMaster.BuildingBlocks.Domain.Identifiers;

/// <summary>
/// Base for a strongly-typed GUID id. Derive a sealed record — value equality, EF conversion, and JSON
/// (de)serialization come from the base machinery, which rebuilds the concrete type from its <c>(Guid)</c>
/// constructor. Implement <see cref="IParsable{TSelf}"/> on the concrete id if you want route/query binding.
/// <code>public sealed record UserId(Guid Value) : GuidId(Value), IParsable&lt;UserId&gt; { ... }</code>
/// </summary>
public abstract record GuidId(Guid Value)
{
    public sealed override string ToString() => Value.ToString();
}
