using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace YC.BuildingBlocks.Web;

/// <summary>
/// Default CORS policy bound from <c>Cors:AllowedOrigins</c>. With no configured origins the policy allows
/// nothing, so cross-origin access is denied by default and only the explicitly listed origins are permitted.
/// </summary>
public static class CorsExtensions
{
    public static WebApplicationBuilder AddCorsPolicy(this WebApplicationBuilder builder)
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        builder.Services.AddCors(o => o.AddDefaultPolicy(policy =>
        {
            if (origins.Length > 0)
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }));

        return builder;
    }
}
