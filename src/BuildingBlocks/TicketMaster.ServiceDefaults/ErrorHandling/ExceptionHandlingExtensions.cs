using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.BuildingBlocks.Domain.Observability;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// RFC 9457 problem responses for the whole app: a single <see cref="GlobalExceptionHandler"/> for unhandled
/// exceptions, plus a ProblemDetails customizer that stamps <c>traceId</c> + <c>correlation_id</c> + the request
/// path on <b>every</b> problem response — handler-produced and framework-produced (404, 400 model binding, 415)
/// alike — so all error payloads look identical and are traceable.
/// </summary>
public static class ExceptionHandlingExtensions
{
    public static WebApplicationBuilder AddGlobalExceptionHandling(this WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                var problem = context.ProblemDetails;
                problem.Instance ??= context.HttpContext.Request.Path;

                var activity = Activity.Current;
                problem.Extensions[TelemetryConstants.TraceIdKey] = activity?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;

                if (activity?.GetBaggageItem(TelemetryConstants.CorrelationIdKey) is { } correlationId)
                    problem.Extensions[TelemetryConstants.CorrelationIdKey] = correlationId;
            });

        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        
        return builder;
    }
}
