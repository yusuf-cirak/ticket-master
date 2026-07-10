using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using TicketMaster.BuildingBlocks.Domain.Observability;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Establishes the request's <c>correlation_id</c> — a durable, business-level identifier distinct from the
/// (per-request, sampling-prone) trace id. It is taken from the inbound <c>X-Correlation-ID</c> header when a
/// caller supplies one (so a client log line, a saga, or a retry can be tied together across many traces),
/// otherwise from an upstream-propagated baggage value, otherwise generated. The id is stored in the current
/// Activity's baggage — the single source of truth that flows automatically over HttpClient, is stamped onto
/// logs (<see cref="BaggageLogRecordProcessor"/>) and the span, and is carried across the outbox boundary —
/// and echoed back to the caller as <c>X-Correlation-ID</c> (plus <c>X-Trace-Id</c> for direct trace lookup).
/// </summary>
internal sealed class CorrelationMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;

        var correlationId = NormalizeOrGenerate(
            context.Request.Headers[TelemetryConstants.CorrelationIdHeader].FirstOrDefault(),
            activity?.GetBaggageItem(TelemetryConstants.CorrelationIdKey)); // from upstream

        // Baggage is the single source of truth: it flows over HttpClient automatically and is read back by
        // the log processor, the span, and the outbox writer. SetTag also surfaces it on the request span.
        activity?.SetBaggage(TelemetryConstants.CorrelationIdKey, correlationId);
        activity?.SetTag(TelemetryConstants.CorrelationIdKey, correlationId);

        // Response hasn't started yet; set headers eagerly (buffered until flush). Echo correlation + trace.
        context.Response.Headers[TelemetryConstants.CorrelationIdHeader] = correlationId;
        if (activity is not null)
        {
            context.Response.Headers[TelemetryConstants.TraceIdHeader] = activity.TraceId.ToString();
        }

        return next(context);
    }

    /// <summary>
    /// Accepts an inbound/upstream id only when it parses as a GUID, returning it in canonical lowercase "D"
    /// form; anything else (non-GUID, empty, header-injection payloads with CR/LF) is rejected and a fresh
    /// time-ordered UUIDv7 is generated. This is also what keeps the value safe to echo into a response header:
    /// only a normalized GUID is ever reflected, never raw caller input.
    /// </summary>
    private static string NormalizeOrGenerate(string inbound, string upstream)
    {
        if (Guid.TryParse(inbound, out var fromHeader))
            return fromHeader.ToString();
        if (Guid.TryParse(upstream, out var fromBaggage))
            return fromBaggage.ToString();
        return Guid.CreateVersion7().ToString(); // time-ordered UUIDv7
    }
}