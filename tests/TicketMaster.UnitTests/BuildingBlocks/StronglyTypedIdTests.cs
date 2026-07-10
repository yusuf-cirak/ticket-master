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
    private sealed record TestId(Guid Value) : GuidId(Value), IGuidId<TestId>
    {
        public static TestId FromGuid(Guid value) => new(value);
    }

    // Route/query binding reaches TryParse through the IParsable<T> constraint (as minimal APIs do), not directly —
    // the interface members are explicit implementations.
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
    public void Ef_converter_maps_to_and_from_the_underlying_guid()
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

    private sealed record AltId(Guid Value) : GuidId(Value), IGuidId<AltId>
    {
        public static AltId FromGuid(Guid value) => new(value);
    }
}
