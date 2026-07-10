using System.Globalization;

namespace TicketMaster.BuildingBlocks.Domain.Identifiers;

/// <summary>Base for a strongly-typed int id (e.g. a database identity key). See <see cref="GuidId"/> for the pattern.</summary>
public abstract record IntId(int Value)
{
    public sealed override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

public interface IIntId<TSelf> : IParsable<TSelf>
    where TSelf : IntId, IIntId<TSelf>
{
    static abstract TSelf FromInt(int value);

    static TSelf IParsable<TSelf>.Parse(string s, IFormatProvider provider)
        => TSelf.FromInt(int.Parse(s, provider ?? CultureInfo.InvariantCulture));

    static bool IParsable<TSelf>.TryParse(string s, IFormatProvider provider, out TSelf result)
    {
        if (int.TryParse(s, NumberStyles.Integer, provider ?? CultureInfo.InvariantCulture, out var value))
        {
            result = TSelf.FromInt(value);
            return true;
        }

        result = null;
        return false;
    }
}
