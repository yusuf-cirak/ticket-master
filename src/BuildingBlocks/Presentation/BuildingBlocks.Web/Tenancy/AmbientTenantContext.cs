using BuildingBlocks.Application.Abstractions.Tenancy;
using YC.Monad;

namespace BuildingBlocks.Web;

/// <summary>
/// Scoped <see cref="ITenantContext"/>: holds the tenant resolved for this request/job, with an <see cref="AsyncLocal{T}"/>
/// override that <see cref="BeginScope"/> uses so a nested block (background work, admin task) can act as another tenant
/// and restore on dispose. Read live, so a query filter that captures this context always sees the current value.
/// </summary>
internal sealed class AmbientTenantContext : ITenantContext
{
    private static readonly AsyncLocal<Guid?> Override = new();
    private Guid? _resolved;

    public Option<Guid> TenantId => (Override.Value ?? _resolved) is { } id ? Option<Guid>.Some(id) : Option<Guid>.None();

    public bool HasTenant => TenantId.HasValue;

    /// <summary>Set once by the resolution middleware from the request; not part of the public surface.</summary>
    internal void SetResolved(Guid? tenantId) => _resolved = tenantId;

    public IDisposable BeginScope(Guid tenantId)
    {
        var previous = Override.Value;
        Override.Value = tenantId;
        return new Restore(() => Override.Value = previous);
    }

    private sealed class Restore(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
