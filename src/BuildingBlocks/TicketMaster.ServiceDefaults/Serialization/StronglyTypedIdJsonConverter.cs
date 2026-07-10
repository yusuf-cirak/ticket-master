using System.Text.Json;
using System.Text.Json.Serialization;
using TicketMaster.BuildingBlocks.Domain.Identifiers;

namespace TicketMaster.ServiceDefaults.Serialization;

/// <summary>
/// Serializes any strongly-typed id as its bare underlying value (a UserId is just its GUID on the wire, not an
/// object) and reads it back into the strong type. Register once in the JSON options; it covers every current and
/// future GuidId/StringId/IntId.
/// </summary>
public sealed class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeof(GuidId).IsAssignableFrom(typeToConvert)
        || typeof(StringId).IsAssignableFrom(typeToConvert)
        || typeof(IntId).IsAssignableFrom(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var openConverter =
            typeof(GuidId).IsAssignableFrom(typeToConvert) ? typeof(GuidIdJsonConverter<>) :
            typeof(StringId).IsAssignableFrom(typeToConvert) ? typeof(StringIdJsonConverter<>) :
            typeof(IntIdJsonConverter<>);

        return (JsonConverter)Activator.CreateInstance(openConverter.MakeGenericType(typeToConvert));
    }
}

public sealed class GuidIdJsonConverter<TId> : JsonConverter<TId> where TId : GuidId, IGuidId<TId>
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : TId.FromGuid(reader.GetGuid());

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value);
    }
}

public sealed class StringIdJsonConverter<TId> : JsonConverter<TId> where TId : StringId, IStringId<TId>
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : TId.FromString(reader.GetString());

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value);
    }
}

public sealed class IntIdJsonConverter<TId> : JsonConverter<TId> where TId : IntId, IIntId<TId>
{
    public override TId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? null : TId.FromInt(reader.GetInt32());

    public override void Write(Utf8JsonWriter writer, TId value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteNumberValue(value.Value);
    }
}
