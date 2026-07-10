namespace TicketMaster.BuildingBlocks.Domain.Identifiers;

/// <summary>Base for a strongly-typed string id (e.g. a slug or external key). See <see cref="GuidId"/> for the pattern.</summary>
public abstract record StringId(string Value)
{
    public sealed override string ToString() => Value;
}

public interface IStringId<TSelf> : IParsable<TSelf>
    where TSelf : StringId, IStringId<TSelf>
{
    static abstract TSelf FromString(string value);

    static TSelf IParsable<TSelf>.Parse(string s, IFormatProvider provider)
        => TSelf.FromString(s);

    static bool IParsable<TSelf>.TryParse(string s, IFormatProvider provider, out TSelf result)
    {
        if (string.IsNullOrEmpty(s))
        {
            result = null;
            return false;
        }

        result = TSelf.FromString(s);
        return true;
    }
}
