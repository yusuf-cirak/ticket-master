using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpLogging;

namespace YC.BuildingBlocks.Web;

/// <summary>
/// Per-endpoint logging policy, declared at the route: sensitivity is a property of the endpoint, set once
/// where it is mapped, rather than guessed from the path at log time.
/// <list type="bullet">
/// <item><see cref="LogWithoutBody{TBuilder}"/> — keep request/response metadata + headers but never the body
/// (login, registration, payment, anything carrying credentials or PII).</item>
/// <item><see cref="SuppressLogging{TBuilder}"/> — no request/response log entry at all (e.g. token refresh,
/// internal callbacks, health probes).</item>
/// </list>
/// Both ride on the framework's own <c>WithHttpLogging</c> endpoint metadata, which the HTTP logging middleware
/// honors natively (it must run after routing — see the pipeline in ServiceDefaults).
/// </summary>
public static class LoggingPolicyExtensions
{
    // Metadata, headers and timing — everything except the bodies.
    private const HttpLoggingFields MetadataOnly =
        HttpLoggingFields.RequestPropertiesAndHeaders
        | HttpLoggingFields.ResponsePropertiesAndHeaders
        | HttpLoggingFields.Duration;

    public static TBuilder LogWithoutBody<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithHttpLogging(MetadataOnly);

    public static TBuilder SuppressLogging<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithHttpLogging(HttpLoggingFields.None);
}
