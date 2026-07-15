using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YC.BuildingBlocks.Observability;

/// <summary>
/// Wires tail log sampling (<see cref="LogSamplingOptions"/>): buffer Information-and-below per request, then
/// flush-or-drop at request end via <see cref="LogSamplingMiddleware"/>. Disabled in Development so local logs
/// stay complete. Errors (Warning+) always stream; an errored request always flushes its full buffer.
/// </summary>
public static class LogSamplingExtensions
{
    public static WebApplicationBuilder AddLogSampling(this WebApplicationBuilder builder, Action<LogSamplingOptions> configure = null)
    {
        var optionsBuilder = builder.Services.AddOptions<LogSamplingOptions>()
            .Bind(builder.Configuration.GetSection(LogSamplingOptions.SectionName));
        if (configure is not null)
            optionsBuilder.Configure(configure); // code-based config wins over the appsettings section

        // Effective options for the registration-time decisions below (IOptions isn't built yet); UseLogSampling
        // later reads the resolved IOptions so it honors the same code config.
        var effective = builder.Configuration.GetSection(LogSamplingOptions.SectionName).Get<LogSamplingOptions>() ?? new LogSamplingOptions();
        configure?.Invoke(effective);

        if (builder.Environment.IsDevelopment() || !effective.Enabled)
            return builder;

        builder.Logging.AddPerIncomingRequestBuffer(effective.BufferBelowLevel);
        return builder;
    }

    public static WebApplication UseLogSampling(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<LogSamplingOptions>>().Value;
        if (!app.Environment.IsDevelopment() && options.Enabled)
            app.UseMiddleware<LogSamplingMiddleware>();

        return app;
    }
}
