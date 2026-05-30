# Outbox → CDC → Kafka (Debezium)

How integration events leave the modular monolith. There is **no manual `CREATE PUBLICATION`** — Debezium provisions everything on first connect.

## Flow

```
module writes a row to <schema>.outbox  (same tx as its state change)
        │  Postgres WAL (wal_level=logical)
        ▼
Debezium PostgresConnector (pgoutput)  ── reads via replication slot tm_outbox_slot
        │                                  filtered by publication tm_outbox_pub
        ▼
Outbox EventRouter SMT
   • routes by column aggregate_type  → topic  tm.<aggregate_type>.events
   • event key  = aggregate_id        (partition key, per-aggregate ordering)
   • event id   = id  (header: id)    (== app-assigned UUIDv7 MessageId)
   • expands the jsonb payload string → real JSON object
        ▼
Kafka topic tm.<ctx>.events  → MassTransit Kafka rider → consumer → <schema>.inbox (dedup)
```

## Why it works without `CREATE PUBLICATION`

- Postgres runs with `wal_level=logical` (compose `command: postgres -c wal_level=logical`; test fixture sets it too).
- `plugin.name=pgoutput` (built into Postgres — no plugin install).
- On first connect Debezium **auto-creates** the replication slot (`tm_outbox_slot`) and the publication (`tm_outbox_pub`). This needs the `tm` user to have **REPLICATION** (the docker postgres superuser default does).

> **Production:** managed Postgres (RDS / Cloud SQL) provisions least-privilege roles — grant `REPLICATION` to the connector user explicitly, or pre-create `tm_outbox_pub`/`tm_outbox_slot` and set `publication.autocreate.mode=disabled`.

## Why the config is explicit (not defaults)

Debezium's default `publication.autocreate.mode` is **`all_tables`**, which publishes *every* table to the slot — every domain table's WAL would stream through Debezium for no reason. Instead:

| Key | Value | Reason |
|-----|-------|--------|
| `publication.autocreate.mode` | `filtered` | Publication covers **only** `table.include.list`, nothing else. |
| `publication.name` | `tm_outbox_pub` | Stable, owned name (not the default `dbz_publication`). |
| `slot.name` | `tm_outbox_slot` | Stable, owned name (not the default `debezium`). |
| `snapshot.mode` | `never` | Outbox rows are transient; stream only new changes, don't replay history. (`no_data` is equivalent on Debezium ≥2.6.) |
| `table.include.list` | `platform.outbox` | The single source of truth for which outboxes are captured. |

## Adding a module

Each module owns its schema and an `outbox` table (snake_case, via `EFCore.NamingConventions`). To capture a new module, **append its outbox table** to `table.include.list`, e.g.:

```
"table.include.list": "platform.outbox,event.outbox,inventory.outbox"
```

One connector, one slot, one publication captures all module outboxes; the EventRouter routes each to `tm.<aggregate_type>.events` by the `aggregate_type` column.

## Files

- `outbox-connector.json` — the connector config (register via `curl -X POST localhost:8083/connectors -H 'Content-Type: application/json' -d @outbox-connector.json`).
- The integration test mirrors this config in `tests/TicketMaster.IntegrationTests/SpineFixture.cs` (`RegisterOutboxConnectorAsync`) — **keep both in sync**.
