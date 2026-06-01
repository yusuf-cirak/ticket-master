using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// One global JSON contract for every minimal-API endpoint (<see cref="ConfigureHttpJsonOptions"/>), so request
/// parsing and response shaping are identical service-wide instead of per-endpoint defaults:
/// <list type="bullet">
/// <item><b>MaxDepth = 32</b> — rejects deeply nested payloads, the cheap stack-exhaustion DoS vector.</item>
/// <item><b>enums as strings</b> — stable, readable wire values that survive enum reordering (never raw ints).</item>
/// <item><b>omit nulls on write</b> — smaller, cleaner responses; absence means "not set", not <c>null</c>.</item>
/// <item><b>reject unmapped members</b> — an unknown request property is a client contract error (400), not
/// silently dropped, so typos and version drift surface immediately.</item>
/// </list>
/// Casing stays System.Text.Json's default camelCase. Numbers stay strict (no quoted-number coercion).
/// </summary>
public static class JsonExtensions
{
    private const int MaxJsonDepth = 32;

    public static IServiceCollection AddJsonDefaults(this IServiceCollection services)
    {
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.MaxDepth = MaxJsonDepth;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
        });

        return services;
    }
}
