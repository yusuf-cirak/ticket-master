# ADR-0008: Selectable outbox relay — Debezium or in-app polling

**Status:** Accepted

## Context
The outbox reaches Kafka via **Debezium** (logical replication → Outbox Event Router). That's the best default, but some
deployments can't run Debezium/Kafka-Connect. They still need reliable delivery without a dual-write.

## Decision
`Outbox:Relay` = `Debezium` (default) | `Polling`. In **Polling** mode a `BackgroundService` per module DbContext
relays the outbox itself, and the claim is deliberately split from the publish so a broker round-trip never holds a DB
connection (which would starve the pool):

1. **Claim** — one `UPDATE … WHERE id IN (SELECT … FOR UPDATE SKIP LOCKED LIMIT n) RETURNING …`: leases a batch and
   returns it in a single auto-committed statement, releasing the row locks + the connection immediately. `SKIP LOCKED`
   lets multiple relay instances run without contending.
2. **Publish** — each row is produced to Kafka with **no DB connection held**.
3. **Complete** — a short second statement stamps `processed_on`. A failed publish keeps its lease and is retried once
   the lease expires (a crashed relay self-heals the same way).

The Kafka producer emits the **exact wire format Debezium's Outbox Event Router would** — topic
`tm.{aggregate_type}.events`, key = aggregate id, value = raw JSON payload, same identity/trace headers — so the
raw-JSON MassTransit consumers can't tell the two relays apart. Adds `processed_on`/`locked_until`/`attempts` columns
(unused by the Debezium path).

## Consequences
- One config value switches relay strategy; consumers and producers are unaffected.
- Run exactly one relay: Debezium **or** polling, never both (they'd double-publish). The columns coexist harmlessly.
- At-least-once delivery with an idempotent producer + the inbox's dedup → exactly-once effect, same as the CDC path.
- Proven by tests: claim/lease/SKIP-LOCKED against real Postgres, and the produced message matches the Debezium format.
