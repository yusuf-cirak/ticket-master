using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Honors <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> from a trusted edge so <c>RemoteIpAddress</c> and the
/// request scheme reflect the real client behind the load balancer — which both <c>client_ip</c> logging and the
/// rate-limiter's IP partitioning depend on. Anti-spoofing: outside Development the forwarded headers are trusted
/// only from explicitly configured proxy IPs (<c>ForwardedHeaders:KnownProxies</c>); if none are configured the
/// headers are ignored rather than blindly trusted. <c>UseForwardedHeaders()</c> must run first in the pipeline.
/// </summary>
public static class ForwardedHeadersExtensions
{
    public static WebApplicationBuilder AddForwardedHeaders(this WebApplicationBuilder builder)
    {
        var knownProxies = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        var isDevelopment = builder.Environment.IsDevelopment();

        builder.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1; // a single trusted hop (the edge LB / ingress)

            // Dev: keep the framework default (loopback trusted) so local X-Forwarded-* is honored for testing.
            // Other environments: trust ONLY the configured proxy IPs — never an unverified forwarded header.
            if (isDevelopment)
                return;

            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var proxy in knownProxies)
                if (IPAddress.TryParse(proxy, out var ip))
                    o.KnownProxies.Add(ip);
        });

        return builder;
    }
}
