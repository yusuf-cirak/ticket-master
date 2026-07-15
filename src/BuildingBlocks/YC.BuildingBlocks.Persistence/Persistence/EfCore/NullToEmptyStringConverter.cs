using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace YC.BuildingBlocks.Persistence.EfCore;

/// <summary>
/// Normalizes string columns: on write a null (or whitespace-only) value is stored as a trimmed, non-null string;
/// on read a null from the database surfaces as the empty string. Apply via <c>ConfigureConventions</c>:
/// <c>configurationBuilder.Properties&lt;string&gt;().HaveConversion&lt;NullToEmptyStringConverter&gt;()</c>. Combined with
/// non-null domain string properties (initialized to <see cref="string.Empty"/>), the database never stores or
/// returns null for these columns, and stored values carry no surrounding whitespace.
/// </summary>
public sealed class NullToEmptyStringConverter : ValueConverter<string, string>
{
    public NullToEmptyStringConverter()
        : base(model => (model ?? string.Empty).Trim(), provider => provider ?? string.Empty)
    {
    }
}
