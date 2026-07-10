using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore;

/// <summary>
/// Keeps string columns null-free: a null model value is written as the empty string, and a null read from the
/// database surfaces as the empty string. Apply via <c>ConfigureConventions</c>:
/// <c>configurationBuilder.Properties&lt;string&gt;().HaveConversion&lt;NullToEmptyStringConverter&gt;()</c>. Combined with
/// non-null domain string properties (initialized to <see cref="string.Empty"/>), the database never stores or
/// returns null for these columns.
/// </summary>
public sealed class NullToEmptyStringConverter : ValueConverter<string, string>
{
    public NullToEmptyStringConverter()
        : base(model => model ?? string.Empty, provider => provider ?? string.Empty)
    {
    }
}
