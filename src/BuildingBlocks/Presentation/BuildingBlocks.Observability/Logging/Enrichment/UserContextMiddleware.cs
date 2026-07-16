using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using BuildingBlocks.Domain.Observability;

namespace BuildingBlocks.Observability;

/// <summary>
/// Enriches the request's telemetry with <c>user_id</c> (the authenticated <c>sub</c>) and <c>client_ip</c> by
/// writing them to the current Activity's baggage — the same channel as <c>correlation_id</c>, so they surface on
/// every downstream log line (via <see cref="BaggageLogRecordProcessor"/>) and on the request span. Must run after
/// authentication (User populated) and after forwarded headers (RemoteIpAddress = real client). Neither key is in
/// the sensitive-mask set, so both are emitted verbatim.
/// </summary>
public sealed class UserContextMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        if (Activity.Current is not { } activity) return next(context);
        
        
        if (UserId(context.User) is { Length: > 0 } userId)
            Apply(activity, TelemetryConstants.UserIdKey, userId);

        if (context.Connection.RemoteIpAddress?.ToString() is { Length: > 0 } clientIp)
            Apply(activity, TelemetryConstants.ClientIpKey, clientIp);

        return next(context);
    }

    private static string UserId(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
            ? user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            : null;

    private static void Apply(Activity activity, string key, string value)
    {
        activity.SetBaggage(key, value);
        activity.SetTag(key, value);
    }
}
