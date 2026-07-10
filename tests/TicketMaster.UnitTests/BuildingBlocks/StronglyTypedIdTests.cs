using System;
using System.Text.Json;
using FluentAssertions;
using TicketMaster.BuildingBlocks.Domain.Identifiers;
using TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore.Identifiers;
using TicketMaster.ServiceDefaults.Serialization;
using Xunit;

namespace TicketMaster.UnitTests.BuildingBlocks;

public class StronglyTypedIdTests
{
    private sealed record TestId(Guid Value) : GuidId(Value), IParsable<TestId>
    {
        public static TestId Parse(string s, IFormatProvider provider) => new(Guid.Parse(s));

        public static bool TryParse(string s, IFormatProvider provider, out TestId result)
        {
            if (Guid.TryParse(s, out var value)) { result = new TestId(value); return true; }
            result = null;
            return false;
        }
    }

    private sealed record AltId(Guid Value) : GuidId(Value);

    private sealed record Slug(string Value) : StringId(Value);

    [Fact]
    public void StringId_trims_and_treats_null_as_empty()
    {
        new Slug("  abc  ").Value.Should().Be("abc");
        new Slug(null).Value.Should().Be(string.Empty);
        new Slug(" x ").Should().Be(new Slug("x")); // normalized → equal
    }

    [Fact]
    public void String_column_converter_trims_on_write()
    {
        // Trim is the converter's job; the null-free guarantee comes from non-null (empty-initialized) domain
        // properties, so EF never hands the converter a null to begin with.
        var converter = new TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore.NullToEmptyStringConverter();
        converter.ConvertToProvider("  padded  ").Should().Be("padded");
    }

    // Route/query binding reaches TryParse through the IParsable<T> constraint, exactly as minimal APIs do.
    private static bool TryParseId<T>(string text, out T result) where T : IParsable<T>
        => T.TryParse(text, null, out result);

    [Fact]
    public void IParsable_round_trips_and_rejects_garbage()
    {
        var guid = Guid.NewGuid();

        TryParseId<TestId>(guid.ToString(), out var parsed).Should().BeTrue();
        parsed.Value.Should().Be(guid);

        TryParseId<TestId>("not-a-guid", out _).Should().BeFalse();
    }

    [Fact]
    public void Json_serializes_as_the_bare_value()
    {
        var options = new JsonSerializerOptions { Converters = { new StronglyTypedIdJsonConverterFactory() } };
        var guid = Guid.NewGuid();

        var json = JsonSerializer.Serialize(new TestId(guid), options);
        json.Should().Be($"\"{guid}\"");

        JsonSerializer.Deserialize<TestId>(json, options).Should().Be(new TestId(guid));
    }

    [Fact]
    public void Ef_converter_maps_to_and_from_the_underlying_guid_via_constructor()
    {
        var converter = new GuidIdConverter<TestId>();
        var guid = Guid.NewGuid();

        converter.ConvertToProvider(new TestId(guid)).Should().Be(guid);
        converter.ConvertFromProvider(guid).Should().Be(new TestId(guid));
    }

    [Fact]
    public void Distinct_id_types_with_the_same_guid_are_not_equal()
    {
        var guid = Guid.NewGuid();
        // Records compare the EqualityContract too, so two id families never collide even with the same value.
        ((object)new TestId(guid)).Equals(new AltId(guid)).Should().BeFalse();
    }
}
