using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TicketMaster.ServiceDefaults;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class JsonDefaultsTests
{
    private enum Color { Red, Green }

    private sealed record Sample(Color Shade, string Note);

    private static JsonSerializerOptions ResolveOptions()
    {
        var options = new ServiceCollection()
            .AddJsonDefaults()
            .BuildServiceProvider()
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>();
        return options.Value.SerializerOptions;
    }

    [Fact]
    public void Caps_depth_and_rejects_unmapped_members()
    {
        var o = ResolveOptions();

        o.MaxDepth.Should().Be(32);
        o.UnmappedMemberHandling.Should().Be(JsonUnmappedMemberHandling.Disallow);
        o.DefaultIgnoreCondition.Should().Be(JsonIgnoreCondition.WhenWritingNull);
        o.Converters.OfType<JsonStringEnumConverter>().Should().ContainSingle();
    }

    [Fact]
    public void Serializes_enums_as_upper_snake_strings_and_omits_nulls()
    {
        var json = JsonSerializer.Serialize(new Sample(Color.Green, null), ResolveOptions());

        json.Should().Contain("\"GREEN\"").And.NotContain("note");
    }

    [Fact]
    public void Unknown_property_in_payload_is_rejected()
    {
        var act = () => JsonSerializer.Deserialize<Sample>("""{"shade":"RED","unknown":1}""", ResolveOptions());

        act.Should().Throw<JsonException>();
    }
}
