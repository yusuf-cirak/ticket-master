using Microsoft.AspNetCore.Http;

namespace TicketMaster.ServiceDefaults;

public static class HttpContextExtensions
{
    /// <summary>
    /// Resolves the caller's IP: the first hop of <c>X-Forwarded-For</c>, else <c>X-Real-IP</c>, else the socket's
    /// remote address (empty if none). NOTE: the forwarded headers are only trustworthy behind a proxy you control —
    /// configure ForwardedHeaders with KnownProxies/KnownNetworks so a client can't spoof them.
    /// </summary>
    public static string GetClientIpAddress(this HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
            return forwardedFor.Split(',')[0].Trim();

        var realIp = context.Request.Headers["X-Real-IP"].ToString();
        if (!string.IsNullOrWhiteSpace(realIp))
            return realIp.Trim();

        return context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }
}
