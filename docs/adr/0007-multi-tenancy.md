# ADR-0007: Row-based multi-tenancy with an automatic query filter

**Status:** Accepted (Phase 1 — infrastructure + Events; Iam users to follow)

## Context
The template needs tenant isolation. A prior project scoped tenants with a **manual** `.ApplyTenantFilter()` a developer
had to remember on every read — one forgotten call leaks another tenant's data (a critical security bug; their own code
even had an `[Obsolete]` note admitting it).

## Decision
**Shared database + a `tenant_id` discriminator column + an EF Core global query filter**, so scoping is automatic:

- **Read**: `ApplyTenantFilters(this)` adds `HasQueryFilter(e => e.TenantId == CurrentTenantId)` to every `ITenantOwned`
  entity. A read can't forget to scope; a deliberate cross-tenant/admin read opts out per query with `IgnoreQueryFilters()`.
- **Write**: `TenantSaveChangesInterceptor` stamps the ambient tenant on insert and **rejects** any add/modify whose
  tenant differs from the current one (no cross-tenant writes) and any tenant-owned write with no ambient tenant (fail closed).
- **Ambient tenant**: `ITenantContext` (scoped), resolved at the edge by a chain — JWT `tenant_id` claim → `X-Tenant-Id`
  header → (extensible, e.g. subdomain) — and pushed into Activity baggage as `tenant_id` (rides logs/traces like
  correlation_id). `BeginScope(tenantId)` overrides it for background jobs.
- No RLS (app-level filter only, per the isolation vs. ops trade-off chosen for this template).

`TenantId` is a plain `Guid` (a Vogen id would need an EfCoreValueConverter, which needs an EF reference Domain must not
have — enforced by ADR-0004's arch test).

## Consequences
- Cross-tenant leakage is impossible by default; proven by an integration test (tenant A cannot list or fetch tenant B's
  events, and a tenant-less write is refused).
- **Trade-off**: with app-level filtering only, code that calls `IgnoreQueryFilters()` bypasses isolation — reserve it
  for audited admin/system paths. RLS could be added later as defense-in-depth.
- Opting an entity in is one line (`: ITenantOwned`) + the context applying the filter + registering the interceptor.
- Phase 2: `User : ITenantOwned`, registration assigns a tenant, login resolves it into the JWT `tenant_id` claim.
