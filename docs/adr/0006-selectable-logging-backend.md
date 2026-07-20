# ADR-0006: Selectable logging backend — OpenTelemetry or Serilog

**Status:** Accepted

## Context
Logs ship to SigNoz over OTLP with a specific shape: a service resource, `correlation_id`/`causation_id`/`user_id`/
`client_ip` from Activity baggage, automatic `trace_id`/`span_id`, snake_case attribute keys, and masked sensitive
values. The pipeline is OpenTelemetry-native. Some teams standardize on Serilog and want it as the logging backend
without losing any of that shape or changing the destination.

## Decision
`Observability:LoggingBackend` (enum `OpenTelemetry` | `Serilog`, default `OpenTelemetry`) selects the log pipeline in
`AddObservability`. **Traces and metrics stay OpenTelemetry in both modes** — only logs switch.

- **OpenTelemetry**: `ILogger` → OTel LoggerProvider → processors (Baggage → SnakeCase → Mask) → OTLP.
- **Serilog**: `ILogger` → Serilog → enrichers (`BaggageEnricher`, `SnakeCaseMaskingEnricher`) → `Serilog.Sinks.OpenTelemetry`.

Parity is kept by sharing the primitives: both backends call the same `SnakeCase.Convert` and the same
`SensitiveDataLogRecordProcessor.IsSensitive` set, and both read the same baggage keys (`TelemetryConstants`). The OTLP
sink attaches `trace_id`/`span_id` from `Activity.Current`, matching the OTel-native LogRecord fields.

## Consequences
- One config value flips the backend; nothing else in the app changes.
- New fields to enrich or mask are added once (shared helpers) and both backends inherit them.
- Serilog mode adds `Serilog.AspNetCore` + `Serilog.Sinks.OpenTelemetry`; enrichers are unit-tested and a boot test
  covers both backends wiring cleanly.
