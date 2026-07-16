using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BuildingBlocks.Domain.Auditing;
using BuildingBlocks.Domain.Security;

namespace BuildingBlocks.Security;

/// <summary>
/// Default <see cref="IAuditLogger"/> that writes each audit event as a dedicated structured log record (its own
/// <c>ILogger</c> category "Audit"), so the append-only trail rides the existing OTel/OTLP pipeline — actor,
/// correlation_id and trace context are already attached by the logging processors. A module that needs a queryable
/// or signed audit store can replace this registration with a persistent implementation.
/// </summary>
internal sealed partial class LoggingAuditLogger(ILogger<LoggingAuditLogger> logger, ICurrentUser currentUser) : IAuditLogger
{
    public Task LogAsync(
        string action,
        string entityType = null,
        string entityId = null,
        IReadOnlyDictionary<string, object> data = null,
        CancellationToken cancellationToken = default)
    {
        using var scope = data is { Count: > 0 } ? logger.BeginScope(data) : null;
        Emit(logger, action, currentUser.UserId.Match(() => "anonymous", id => id), entityType, entityId);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 9001, Level = LogLevel.Information,
        Message = "audit {auditAction} by {actor} on {entityType}/{entityId}")]
    private static partial void Emit(
        ILogger logger, string auditAction, string actor, string entityType, string entityId);
}

public static class AuditLoggerExtensions
{
    /// <summary>Registers the log-backed <see cref="IAuditLogger"/> (scoped, reads the ambient actor).</summary>
    public static WebApplicationBuilder AddAuditLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IAuditLogger, LoggingAuditLogger>();
        return builder;
    }
}
