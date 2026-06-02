# SigNoz (observability backend)

SigNoz is the telemetry backend for the platform: **traces + metrics + logs**, all exported by the
AppHost directly over OTLP. There is no log file or sidecar — logs are OpenTelemetry-native (ILogger →
OTLP), so they share the same Resource and pipeline as traces and metrics.

The SigNoz deploy is large and self-contained (ClickHouse, OTel collector, query service, UI), so it
is **not vendored** — `infra/docker-compose.yml` pulls it in via Docker Compose `include` from the
upstream clone. One `up` brings up the platform infra **and** SigNoz together.

## Setup

```bash
# 1. Clone SigNoz next to this repo (default include path: ../../signoz/deploy/docker/docker-compose.yaml).
git clone -b main https://github.com/SigNoz/signoz.git   # run from the parent of this repo
#    (or set SIGNOZ_DOCKER_COMPOSE to the docker-compose.yaml path if cloned elsewhere)

# 2. Bring up everything (infra + SigNoz) and prune stale containers:
docker compose -f infra/docker-compose.yml up -d --remove-orphans

docker ps          # verify the signoz / clickhouse / otel-collector containers are healthy
```

- **UI:** http://localhost:8080
- **OTLP gRPC:** localhost:4317  ← AppHost traces/metrics/logs exporter (default `OTEL_EXPORTER_OTLP_ENDPOINT`)

## How the platform talks to it

| Signal | Source | Path |
|--------|--------|------|
| Traces | AppHost (`ServiceDefaults` → `AddObservability`) | OTLP/gRPC → `localhost:4317` |
| Metrics | AppHost (`ServiceDefaults` → `AddObservability`) | OTLP/gRPC → `localhost:4317` |
| Logs | AppHost `ILogger` → OpenTelemetry logs | OTLP/gRPC → `localhost:4317` |

The AppHost runs on the host, so it reaches SigNoz at `localhost:4317`. The OTel SDK stamps every log
record with the active `trace_id`/`span_id` automatically, so logs correlate with traces in the UI with
no enricher. Attribute keys are normalized to `snake_case` (OTel semantic-convention casing) by an
in-process `SnakeCaseLogRecordProcessor`, while the C# code keeps PascalCase message templates.

When the AppHost runs **inside** `infra/docker-compose.yml` (profile `app`), it joins `signoz-net` and
exports to `http://signoz-otel-collector:4317` instead — see the `app-host` service env. Override the
endpoint for any other environment with the standard `OTEL_EXPORTER_OTLP_ENDPOINT` variable.

## Notes / limitations

- **Correlation id is the trace id.** It is auto-generated per request (W3C trace context), echoed to
  callers as the `X-Trace-Id` response header, and needs no separate id, package, or middleware.
- **Trace continuity across the outbox** is preserved (the raw-JSON Debezium path carries no MassTransit
  envelope). `OutboxWriter` stores the W3C `traceparent` in `outbox.trace_parent` (and the same trace id
  in `outbox.correlation_id`); the EventRouter copies it to a Kafka `traceparent` header; the global
  `MessageContextFilter` re-parents the consume span to it. So the HTTP request and the Kafka consume
  appear in a single trace in SigNoz, and consumer logs carry the same `trace_id`.
