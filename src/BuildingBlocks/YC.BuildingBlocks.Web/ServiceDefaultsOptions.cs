using YC.BuildingBlocks.HealthChecks;
using YC.BuildingBlocks.Observability;
using YC.BuildingBlocks.Security;

namespace YC.BuildingBlocks.Web;

/// <summary>
/// Code-based configuration for <see cref="ServiceDefaultsExtensions.AddServiceDefaults(Microsoft.AspNetCore.Builder.WebApplicationBuilder, System.Action{ServiceDefaultsOptions})"/>.
/// Each member layers over the corresponding appsettings section, so a host can configure the cross-cutting stack in
/// code without an appsettings dependency. All members are optional — unset ones keep the config-bound defaults.
/// </summary>
public sealed class ServiceDefaultsOptions
{
    /// <summary>Overrides the telemetry/cache service name (otherwise the <c>ServiceName</c> config key, then the app name).</summary>
    public string ServiceName { get; set; }

    /// <summary>Code-based override for JWT bearer options (layered over the <c>Authentication</c> section).</summary>
    public Action<JwtOptions> Jwt { get; set; }

    /// <summary>Code-based override for tail log-sampling options (layered over the <c>Logging:Sampling</c> section).</summary>
    public Action<LogSamplingOptions> LogSampling { get; set; }

    /// <summary>Configuration-key names the default readiness checks probe.</summary>
    public HealthCheckSettings HealthChecks { get; set; } = new();
}
