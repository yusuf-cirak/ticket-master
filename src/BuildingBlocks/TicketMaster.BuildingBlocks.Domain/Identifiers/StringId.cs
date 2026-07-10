namespace TicketMaster.BuildingBlocks.Domain.Identifiers;

/// <summary>
/// Base for a strongly-typed string id (slug, external key, …). The value is normalized once at construction —
/// null becomes empty and surrounding whitespace is trimmed — so equality and persistence are consistent.
/// See <see cref="GuidId"/> for the pattern.
/// </summary>
public abstract record StringId(string Value)
{
    public string Value { get; } = (Value ?? string.Empty).Trim();

    public sealed override string ToString() => Value;
}
