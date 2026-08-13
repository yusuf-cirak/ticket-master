namespace BuildingBlocks.Application.Telemetry;

/// <summary>
/// Cross-cutting telemetry/correlation names shared across the host defaults (the HTTP boundary) and the
/// infrastructure (outbox writer + consume filter). Kept in the shared kernel so the producer, the logs, and
/// the consumer all agree on one source of truth — no duplicated magic strings.
/// </summary>
public static class TelemetryConstants
{
    /// <summary>Activity-baggage and log-attribute key for the durable correlation id.</summary>
    public const string CorrelationIdKey = "correlation_id";

    /// <summary>Activity-baggage and log-attribute key for the causation id.</summary>
    public const string CausationIdKey = "causation_id";

    /// <summary>Inbound/outbound HTTP header carrying the correlation id.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary>Outbound HTTP header echoing the trace id for direct trace lookup.</summary>
    public const string TraceIdHeader = "X-Trace-Id";

    public const string TraceIdKey = "trace_id";

    /// <summary>Activity-baggage and log-attribute key for the authenticated user id (the JWT <c>sub</c>).</summary>
    public const string UserIdKey = "user_id";

    /// <summary>Activity-baggage and log-attribute key for the client IP (post forwarded-headers resolution).</summary>
    public const string ClientIpKey = "client_ip";

    /// <summary>Activity-baggage and log-attribute key for the resolved tenant id.</summary>
    public const string TenantIdKey = "tenant_id";

    /// <summary>
    /// OpenTelemetry instrument (Meter / ActivitySource) names for the building blocks. Per OTel convention an
    /// instrument name identifies the <b>library</b> that emits it, not the host app — so these stay product-neutral
    /// (the app's identity is the Resource <c>service.name</c>). Defined once here so the emitter and the OTel
    /// pipeline registration agree without duplicated literals across projects that don't reference each other.
    /// </summary>
    public const string RateLimitingMeterName = "BuildingBlocks.RateLimiting";

    /// <summary>ActivitySource name for messaging spans re-parented across the outbox boundary.</summary>
    public const string MessagingActivitySourceName = "BuildingBlocks.Messaging";
}
