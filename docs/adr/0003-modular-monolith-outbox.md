# ADR-0003: Modular monolith — one DbContext + one outbox per module

**Status:** Accepted

## Context
Modules must stay independently evolvable and must not share tables, yet integration events have to publish reliably
(no dual-write between the database and Kafka).

## Decision
Each module owns a DbContext in its own Postgres schema and its own **outbox** table. A command appends an
`IntegrationEvent` to the outbox in the *same* `SaveChanges` transaction as the state change; **Debezium** tails the
outbox via logical replication and routes rows to Kafka; consumers apply them idempotently through an **inbox** with a
unique constraint. Modules communicate only through their `*.Contracts` assemblies (integration events), never by
referencing each other's internals — enforced by `DependencyRuleTests`.

`IOutboxWriter` is registered **keyed by its DbContext** and injected with `[FromKeyedServices(nameof(XDbContext))]`.

## Consequences
- Atomic state-change + event; at-least-once delivery with exactly-once *effect* via the inbox.
- **An unkeyed `IOutboxWriter` is a trap**: with two producing modules the last registration wins and silently hijacks
  every injection, so one module writes to another's outbox and Debezium never sees it. Always register keyed.
- Debezium's `table.include.list` is per-module (`{schema}.outbox`); each producing module adds its own connector entry.
