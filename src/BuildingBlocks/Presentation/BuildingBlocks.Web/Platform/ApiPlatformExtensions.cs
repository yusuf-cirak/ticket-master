using Asp.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;

namespace BuildingBlocks.Web;

/// <summary>
/// API platform defaults: versioning and feature flags.
/// <list type="bullet">
/// <item><b>Versioning</b> — default v1.0, assumed when unspecified (so existing routes keep working), versions
/// reported back in the <c>api-supported-versions</c> header, read from either the URL segment (<c>/v1/…</c>) or
/// the <c>X-Api-Version</c> header.</item>
/// <item><b>Feature flags</b> — <c>Microsoft.FeatureManagement</c> bound to the <c>FeatureManagement</c> config
/// section, so behavior can be toggled per environment / progressively rolled out without a redeploy.</item>
/// </list>
/// </summary>
public static class ApiPlatformExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApiVersioningDefaults()
        {
            services
                .AddApiVersioning(o =>
                {
                    o.DefaultApiVersion = new ApiVersion(1, 0);
                    o.AssumeDefaultVersionWhenUnspecified = true;
                    o.ReportApiVersions = true;
                    o.ApiVersionReader = ApiVersionReader.Combine(
                        new UrlSegmentApiVersionReader(),
                        new HeaderApiVersionReader("X-Api-Version"));
                });

            return services;
        }

        public IServiceCollection AddFeatureFlags()
        {
            services.AddFeatureManagement(); // reads the "FeatureManagement" configuration section
            return services;
        }
    }
}