using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Wires tail log sampling (<see cref="LogSamplingOptions"/>): buffer Information-and-below per request, then
/// flush-or-drop at request end via <see cref="LogSamplingMiddleware"/>. Disabled in Development so local logs
/// stay complete. Errors (Warning+) always stream; an errored request always flushes its full buffer.
/// </summary>
public static class LogSamplingExtensions
{
    public static WebApplicationBuilder AddLogSampling(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<LogSamplingOptions>()
            .Bind(builder.Configuration.GetSection(LogSamplingOptions.SectionName));

        if (!SamplingActive(builder))
            return builder;

        var level = builder.Configuration.GetSection(LogSamplingOptions.SectionName).Get<LogSamplingOptions>()?.BufferBelowLevel
                    ?? LogLevel.Information;

        builder.Logging.AddPerIncomingRequestBuffer(level);
        return builder;
    }

    public static WebApplication UseLogSampling(this WebApplication app)
    {
        if (SamplingActive(app))
            app.UseMiddleware<LogSamplingMiddleware>();
        
        return app;
    }

    private static bool SamplingActive(IHostApplicationBuilder builder) =>
        !builder.Environment.IsDevelopment()
        && (builder.Configuration.GetSection(LogSamplingOptions.SectionName).Get<LogSamplingOptions>()?.Enabled ?? true);

    private static bool SamplingActive(WebApplication app) =>
        !app.Environment.IsDevelopment()
        && (app.Configuration.GetSection(LogSamplingOptions.SectionName).Get<LogSamplingOptions>()?.Enabled ?? true);
}
