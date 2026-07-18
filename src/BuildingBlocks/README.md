# Building Blocks

Product-neutral, reusable infrastructure shared by every module. Assemblies are named `BuildingBlocks.*` and grouped
by clean-architecture layer — a project only ever references *inward* (Presentation → Application → Domain;
Infrastructure → Application → Domain). The direction is enforced by
[`DependencyRuleTests`](../../tests/TicketMaster.ArchitectureTests/DependencyRuleTests.cs), so a violation fails CI.

A module opts into a block by adding a `ProjectReference` — nothing is on by default. Options are configured with an
`Action<T>` overload at registration; no block hard-codes anything product-specific.

## Layers

```
Domain/          BuildingBlocks.Domain            (no dependencies — the shared kernel)
Application/     BuildingBlocks.Application        (depends on Domain only)
Infrastructure/  Persistence · Messaging.Kafka · Caching · Locking · Configuration.Secrets
Presentation/    Web · Security · Observability · HealthChecks · RateLimiting · Modularity
```

## Blocks

| Block | Layer | Purpose |
|-------|-------|---------|
| **Domain** | Domain | Shared kernel: `AggregateRoot`/`Entity`, domain events, `Error`/`ValidationError`, common value objects. No EF, no framework. |
| **Application** | Application | Cross-cutting app concerns: MediatR pipeline behaviors (validation, locking, sensitive-request), `Result`-based abstractions, `PageRequest`/`PagedResult` streaming pagination, `IntegrationEvent`, `ICurrentUser` abstraction. |
| **Persistence** | Infrastructure | EF Core plumbing: snake_case convention, auditing/soft-delete/domain-event interceptors, transactional **outbox** + **inbox** (idempotent consume), Vogen converter registration, `ToPagedResultAsync`, startup migration runner. |
| **Messaging.Kafka** | Infrastructure | MassTransit 8 Kafka rider: publish/consume integration events, message headers, OTel trace propagation + correlation/causation baggage. |
| **Caching** | Infrastructure | FusionCache (L1 in-memory + L2 Redis, MessagePack) on a sentinel-aware connection; health check. |
| **Locking** | Infrastructure | Distributed lock over Redis (`IDistributedLock`), consumed by the Application `LockBehavior`. |
| **Configuration.Secrets** | Infrastructure | AES-GCM-encrypted configuration values decrypted at load from a `CONFIG_SECRET_KEY`. |
| **Web** | Presentation | Minimal-API helpers: `Result` → RFC9457 `ToApiResult`/`ToCreatedResult`, **idempotency** endpoint filter (`RequireIdempotency`), request logging opt-ins. |
| **Security** | Presentation | JWT auth (hardened claim layout), `HttpContext`-backed `ICurrentUser`, scope/permission authorization, audit logging. |
| **Observability** | Presentation | OTel-native traces + metrics + logs over OTLP (no Serilog/fluent-bit), snake_case log records, PII/log sampling. |
| **HealthChecks** | Presentation | Liveness/readiness endpoints + standard checks. |
| **RateLimiting** | Presentation | Native `UseRateLimiter` + per-endpoint opt-ins (`RequireApiRateLimiting`, burst-ban, adaptive admission); Redis or local mode; tiered per-principal partitioning. |
| **Modularity** | Presentation | `IModule` contract + type-based registration (`AddModules(m => m.Add<XModule>())`) and endpoint mounting. |

## Conventions

- **Result over exceptions** — handlers return `Result<T>`/`Option<T>` ([YC.Monad](../../docs/adr/0001-result-over-exceptions.md)); the Web layer maps failures to RFC9457.
- **Strongly-typed ids** — Vogen value objects, reflection-free EF + JSON converters ([ADR-0002](../../docs/adr/0002-vogen-strongly-typed-ids.md)).
- **One DbContext + one outbox per module** — an integration event and the state change commit in a single transaction; the writer is resolved keyed by DbContext ([ADR-0003](../../docs/adr/0003-modular-monolith-outbox.md)).
- **snake_case everywhere** — derived from CLR names by convention; no hand-written table/column names.
