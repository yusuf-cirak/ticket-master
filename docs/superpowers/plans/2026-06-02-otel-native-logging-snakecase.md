# OTel-Native Logging + snake_case Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Serilog + fluent-bit + CorrelationId-package logging stack with an OpenTelemetry-native logs pipeline (ILogger → OTLP → SigNoz) where correlation is the auto-generated `trace_id` and every log attribute key is normalized to `snake_case` by an in-process `BaseProcessor<LogRecord>`.

**Architecture:** Logs join traces+metrics on the same OTel SDK + shared Resource, exported over OTLP. The SDK stamps `trace_id`/`span_id` on every LogRecord automatically (no enricher, no CorrelationId package, no baggage bridge). A `SnakeCaseLogRecordProcessor` rewrites attribute keys (`OrderId` → `order_id`) on the way out, with cached conversions. C# code stays PascalCase. The outbox→Debezium→Kafka→consumer correlation chain keeps working because `OutboxWriter` now captures `Activity.Current.TraceId` (instead of trace baggage), and the existing `MessageContextFilter` re-parents the consumer span from the `traceparent` header.

**Tech Stack:** .NET 10, OpenTelemetry .NET (Logs SDK + OTLP exporter), SigNoz (OTLP collector), xUnit + FluentAssertions, `OpenTelemetry.Exporter.InMemory` (tests).

**NOTE for implementer:** Before writing/modifying any C# file, invoke the `yc-dotnet:index` skill (project standard, per CLAUDE.md).

**Two empirical checks (verify in-IDE during the relevant task, fall back as noted):**
1. `LogRecord.Attributes` has a public setter in the installed OpenTelemetry version (≥1.5 it does). Task 2 depends on it.
2. `builder.Logging.AddOpenTelemetry(Action<OpenTelemetryLoggerOptions>)` + `options.AddOtlpExporter()` resolve (core `OpenTelemetry` + OTLP packages). Task 3 depends on it.

---

## File Structure

**ServiceDefaults — new:**
- `Logging/SnakeCase.cs` — pure string converter, cached. One responsibility: PascalCase/acronym → snake_case.
- `Logging/SnakeCaseLogRecordProcessor.cs` — `BaseProcessor<LogRecord>` that renames attribute keys via `SnakeCase`.
- `Correlation/TraceResponseMiddleware.cs` — adds `X-Trace-Id` response header from the current Activity.

**ServiceDefaults — modified:**
- `Observability/ObservabilityExtensions.cs` — add the logs pipeline (shared Resource, snake processor, OTLP) alongside traces+metrics; extract a shared Resource helper.
- `ServiceDefaultsExtensions.cs` — drop Serilog + Correlation calls; `UseServiceDefaults` uses the trace-response middleware.
- `TicketMaster.ServiceDefaults.csproj` — remove all `Serilog.*` + `CorrelationId` packages.

**ServiceDefaults — deleted:**
- `Logging/SerilogExtensions.cs`, `Logging/TraceContextEnricher.cs`, `Correlation/CorrelationExtensions.cs`.

**Infrastructure — modified:**
- `Outbox/OutboxWriter.cs` — capture `Activity.Current.TraceId` instead of trace baggage.

**Host / tests / infra — modified:**
- `src/Host/TicketMaster.AppHost/appsettings.json` + `appsettings.Development.json` — remove `Serilog` config blocks.
- `tests/TicketMaster.IntegrationTests/SpineFixture.cs` — drop the dead `Serilog:LogFile` setting (keep `OTEL_SDK_DISABLED`).
- `tests/TicketMaster.UnitTests/TicketMaster.UnitTests.csproj` — add ServiceDefaults project ref + OTel test packages.
- `infra/docker-compose.yml` — remove the `fluent-bit` service, its volumes, and `Serilog__LogFile` env on `app-host`; keep `OTEL_EXPORTER_OTLP_ENDPOINT`.
- `infra/log-sidecar/` — delete the folder (`fluent-bit.conf`, `parsers.conf`).

---

## Task 1: snake_case converter

**Files:**
- Create: `src/BuildingBlocks/TicketMaster.ServiceDefaults/Logging/SnakeCase.cs`
- Modify: `tests/TicketMaster.UnitTests/TicketMaster.UnitTests.csproj` (add ServiceDefaults project reference)
- Test: `tests/TicketMaster.UnitTests/ServiceDefaults/SnakeCaseTests.cs`

- [ ] **Step 1: Add the ServiceDefaults project reference to the test project**

In `tests/TicketMaster.UnitTests/TicketMaster.UnitTests.csproj`, add to the existing `ProjectReference` ItemGroup:

```xml
    <ProjectReference Include="..\..\src\BuildingBlocks\TicketMaster.ServiceDefaults\TicketMaster.ServiceDefaults.csproj" />
```

- [ ] **Step 2: Write the failing test**

Create `tests/TicketMaster.UnitTests/ServiceDefaults/SnakeCaseTests.cs`:

```csharp
using FluentAssertions;
using TicketMaster.ServiceDefaults;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class SnakeCaseTests
{
    [Theory]
    [InlineData("OrderId", "order_id")]
    [InlineData("userId", "user_id")]
    [InlineData("HTTPStatusCode", "http_status_code")]
    [InlineData("IOError", "io_error")]
    [InlineData("Name", "name")]
    [InlineData("trace_id", "trace_id")]        // already snake → unchanged
    [InlineData("service.name", "service.name")] // dotted semconv → unchanged
    [InlineData("", "")]
    public void Convert_normalizes_to_snake_case(string input, string expected) =>
        SnakeCase.Convert(input).Should().Be(expected);

    [Fact]
    public void Convert_is_idempotent()
    {
        var once = SnakeCase.Convert("OrderId");
        SnakeCase.Convert(once).Should().Be("order_id");
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/TicketMaster.UnitTests -c Debug --filter "FullyQualifiedName~SnakeCaseTests"`
Expected: FAIL — `SnakeCase` does not exist (compile error).

- [ ] **Step 4: Write the implementation**

Create `src/BuildingBlocks/TicketMaster.ServiceDefaults/Logging/SnakeCase.cs`:

```csharp
using System.Collections.Concurrent;
using System.Text;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Converts log attribute key names (PascalCase / camelCase, acronym-aware) to snake_case, the
/// OpenTelemetry semantic-convention casing. Conversions are cached because the same template
/// property names recur on every log call (hot path).
/// </summary>
internal static class SnakeCase
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Convert(string name) =>
        string.IsNullOrEmpty(name) ? name : Cache.GetOrAdd(name, Transform);

    private static string Transform(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var prev = i > 0 ? name[i - 1] : '\0';
                var next = i + 1 < name.Length ? name[i + 1] : '\0';
                // Boundary before an uppercase letter when the previous char is lower/digit
                // (camel boundary) or it ends an acronym that flows into a new word (HTTPStatus).
                var boundary = i > 0 &&
                    (char.IsLower(prev) || char.IsDigit(prev) ||
                     (char.IsUpper(prev) && char.IsLower(next)));
                if (boundary && sb.Length > 0 && sb[^1] != '_') sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/TicketMaster.UnitTests -c Debug --filter "FullyQualifiedName~SnakeCaseTests"`
Expected: PASS (9 cases).

- [ ] **Step 6: Commit**

```bash
git add src/BuildingBlocks/TicketMaster.ServiceDefaults/Logging/SnakeCase.cs tests/TicketMaster.UnitTests/ServiceDefaults/SnakeCaseTests.cs tests/TicketMaster.UnitTests/TicketMaster.UnitTests.csproj
git commit -m "feat(observability): snake_case log attribute key converter"
```

---

## Task 2: snake_case LogRecord processor

**Files:**
- Create: `src/BuildingBlocks/TicketMaster.ServiceDefaults/Logging/SnakeCaseLogRecordProcessor.cs`
- Modify: `tests/TicketMaster.UnitTests/TicketMaster.UnitTests.csproj` (add OTel test packages)
- Test: `tests/TicketMaster.UnitTests/ServiceDefaults/SnakeCaseLogRecordProcessorTests.cs`

- [ ] **Step 1: Add OpenTelemetry test packages**

In `tests/TicketMaster.UnitTests/TicketMaster.UnitTests.csproj`, add to the package `ItemGroup`:

```xml
    <PackageReference Include="OpenTelemetry" Version="1.15.3" />
    <PackageReference Include="OpenTelemetry.Exporter.InMemory" Version="1.15.3" />
```

- [ ] **Step 2: Write the failing test**

Create `tests/TicketMaster.UnitTests/ServiceDefaults/SnakeCaseLogRecordProcessorTests.cs`. This boots a real OTel logging pipeline with an in-memory exporter (the FAANG-grade way to assert exported telemetry shape), so the test proves end-to-end that exported attribute keys are snake_case:

```csharp
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using TicketMaster.ServiceDefaults;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class SnakeCaseLogRecordProcessorTests
{
    [Fact]
    public void Exported_attribute_keys_are_snake_case()
    {
        var exported = new List<LogRecord>();

        using var factory = LoggerFactory.Create(b => b.AddOpenTelemetry(o =>
        {
            o.ParseStateValues = true;
            o.AddProcessor(new SnakeCaseLogRecordProcessor());
            o.AddInMemoryExporter(exported);
        }));

        factory.CreateLogger("test").LogInformation("Order {OrderId} for {UserId}", 7, 42);

        var record = exported.Should().ContainSingle().Subject;
        var keys = new List<string>();
        record.Attributes.Should().NotBeNull();
        foreach (var kv in record.Attributes!) keys.Add(kv.Key);

        keys.Should().Contain("order_id");
        keys.Should().Contain("user_id");
        keys.Should().NotContain("OrderId");
        keys.Should().NotContain("UserId");
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/TicketMaster.UnitTests -c Debug --filter "FullyQualifiedName~SnakeCaseLogRecordProcessorTests"`
Expected: FAIL — `SnakeCaseLogRecordProcessor` does not exist (compile error).

- [ ] **Step 4: Write the implementation**

Create `src/BuildingBlocks/TicketMaster.ServiceDefaults/Logging/SnakeCaseLogRecordProcessor.cs`:

```csharp
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Rewrites every log attribute key to snake_case (OpenTelemetry semantic-convention casing) so the
/// app keeps PascalCase message-template properties (<c>{OrderId}</c>) while SigNoz/Grafana receive
/// <c>order_id</c>. Runs in-process on the logging pipeline — the idiomatic OTel extension point —
/// with cached key conversions so the per-record cost is a handful of dictionary lookups.
/// </summary>
internal sealed class SnakeCaseLogRecordProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord record)
    {
        if (record.Attributes is not { Count: > 0 } attributes)
            return;

        var changed = false;
        var rewritten = new List<KeyValuePair<string, object?>>(attributes.Count);
        foreach (var attribute in attributes)
        {
            var key = SnakeCase.Convert(attribute.Key);
            if (key != attribute.Key) changed = true;
            rewritten.Add(new KeyValuePair<string, object?>(key, attribute.Value));
        }

        // Only reassign when something actually changed — semconv-only records (all keys already
        // snake/dotted) skip the write entirely.
        if (changed)
            record.Attributes = rewritten;
    }
}
```

> **Empirical check (this task):** if `record.Attributes = rewritten;` fails to compile (no setter in the installed OTel version), stop and report — the fallback is to upgrade the OTel package to a version exposing the setter (≥1.5) rather than work around it. Do not silently drop the normalization.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/TicketMaster.UnitTests -c Debug --filter "FullyQualifiedName~SnakeCaseLogRecordProcessorTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/BuildingBlocks/TicketMaster.ServiceDefaults/Logging/SnakeCaseLogRecordProcessor.cs tests/TicketMaster.UnitTests/ServiceDefaults/SnakeCaseLogRecordProcessorTests.cs tests/TicketMaster.UnitTests/TicketMaster.UnitTests.csproj
git commit -m "feat(observability): snake_case LogRecord processor with in-memory exporter test"
```

---

## Task 3: wire OTel logs into the observability pipeline

**Files:**
- Modify: `src/BuildingBlocks/TicketMaster.ServiceDefaults/Observability/ObservabilityExtensions.cs`

- [ ] **Step 1: Replace the file contents**

Add the logs pipeline (shared Resource via a helper, snake processor, OTLP exporter) alongside the existing traces+metrics. Full new file:

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace TicketMaster.ServiceDefaults;

internal static class ObservabilityExtensions
{
    /// <summary>
    /// OpenTelemetry traces + metrics + logs over OTLP (default http://localhost:4317, overridable via
    /// OTEL_EXPORTER_OTLP_ENDPOINT), all sharing one Resource. The SDK stamps trace_id/span_id on every
    /// LogRecord automatically, so logs correlate with traces with no enricher; attribute keys are
    /// normalized to snake_case on export.
    /// </summary>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, string serviceName)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
        var environment = builder.Environment.EnvironmentName;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddTicketMasterResource(serviceName, version, environment))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSource("Npgsql")                  // EF Core / Npgsql database spans
                .AddSource("MassTransit")             // Kafka rider produce/consume spans
                .AddSource("TicketMaster.Messaging")  // consume spans re-parented across the outbox boundary
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()       // HTTP server: rate, errors, duration (RED)
                .AddHttpClientInstrumentation()       // outbound HTTP
                .AddRuntimeInstrumentation()          // GC, heap, thread pool, exceptions
                .AddProcessInstrumentation()          // process CPU + memory (USE)
                .AddMeter("MassTransit")
                .AddMeter("Npgsql")
                .AddOtlpExporter());

        // Logs: ILogger → OTLP, same Resource, trace_id/span_id auto-attached, keys snake_cased.
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeScopes = true;            // carry BeginScope state (e.g. causationId from the consumer)
            o.IncludeFormattedMessage = true;  // render the message body, not just the template
            o.ParseStateValues = true;         // turn structured state into attributes the processor can rename
            o.SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddTicketMasterResource(serviceName, version, environment));
            o.AddProcessor(new SnakeCaseLogRecordProcessor());
            o.AddOtlpExporter();
        });

        return builder;
    }

    private static ResourceBuilder AddTicketMasterResource(
        this ResourceBuilder resource, string serviceName, string version, string environment) =>
        resource
            .AddService(serviceName, serviceVersion: version)
            .AddAttributes(
            [
                new("deployment.environment", environment),
                new("host.name", Environment.MachineName),
                new("process.pid", Environment.ProcessId)
            ]);
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/BuildingBlocks/TicketMaster.ServiceDefaults -c Debug`
Expected: succeeds, 0 warnings.

> **Empirical check (this task):** if `builder.Logging.AddOpenTelemetry(...)` or `o.AddOtlpExporter()` don't resolve, add `using OpenTelemetry.Logs;` (already present) and confirm the `OpenTelemetry.Exporter.OpenTelemetryProtocol` package is referenced (it is — see csproj). The logs OTLP extension ships in the same package as the traces/metrics one.

- [ ] **Step 3: Commit**

```bash
git add src/BuildingBlocks/TicketMaster.ServiceDefaults/Observability/ObservabilityExtensions.cs
git commit -m "feat(observability): OTLP logs pipeline sharing the traces/metrics Resource"
```

---

## Task 4: trace-response middleware (X-Trace-Id)

**Files:**
- Create: `src/BuildingBlocks/TicketMaster.ServiceDefaults/Correlation/TraceResponseMiddleware.cs`
- Test: `tests/TicketMaster.UnitTests/ServiceDefaults/TraceResponseMiddlewareTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/TicketMaster.UnitTests/ServiceDefaults/TraceResponseMiddlewareTests.cs`:

```csharp
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using TicketMaster.ServiceDefaults;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class TraceResponseMiddlewareTests
{
    [Fact]
    public async Task Adds_trace_id_response_header_from_current_activity()
    {
        using var source = new ActivitySource("test-source");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("req")!;
        var context = new DefaultHttpContext();
        var middleware = new TraceResponseMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);
        // OnStarting callbacks fire as the response begins; trigger them explicitly.
        await context.Response.StartAsync();

        context.Response.Headers["X-Trace-Id"].ToString().Should().Be(activity.TraceId.ToString());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TicketMaster.UnitTests -c Debug --filter "FullyQualifiedName~TraceResponseMiddlewareTests"`
Expected: FAIL — `TraceResponseMiddleware` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `src/BuildingBlocks/TicketMaster.ServiceDefaults/Correlation/TraceResponseMiddleware.cs`:

```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Echoes the current trace id (which is the correlation id in this system) back to the caller as the
/// <c>X-Trace-Id</c> response header, so clients — including the mobile app — can quote it when
/// reporting issues and operators can jump straight to the trace in SigNoz.
/// </summary>
internal sealed class TraceResponseMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString();
        if (!string.IsNullOrEmpty(traceId))
        {
            context.Response.OnStarting(static state =>
            {
                var (response, id) = ((HttpResponse, string))state;
                response.Headers["X-Trace-Id"] = id;
                return Task.CompletedTask;
            }, (context.Response, traceId));
        }

        return next(context);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/TicketMaster.UnitTests -c Debug --filter "FullyQualifiedName~TraceResponseMiddlewareTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BuildingBlocks/TicketMaster.ServiceDefaults/Correlation/TraceResponseMiddleware.cs tests/TicketMaster.UnitTests/ServiceDefaults/TraceResponseMiddlewareTests.cs
git commit -m "feat(observability): X-Trace-Id response header middleware"
```

---

## Task 5: rewire ServiceDefaults entrypoints

**Files:**
- Modify: `src/BuildingBlocks/TicketMaster.ServiceDefaults/ServiceDefaultsExtensions.cs`

- [ ] **Step 1: Replace the file contents**

Drop Serilog logging + the CorrelationId package wiring; observability now also covers logs, and the pipeline default is the trace-response middleware:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace TicketMaster.ServiceDefaults;

/// <summary>
/// Aspire-style service defaults: one call wires the cross-cutting host concerns (UTC, OpenTelemetry
/// traces/metrics/logs, Kubernetes health checks) so each host stays thin and consistent. Correlation
/// is the auto-generated trace_id — no separate id, package, or middleware to manage.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public static WebApplicationBuilder AddServiceDefaults(this WebApplicationBuilder builder)
    {
        // Run the process in UTC regardless of the host's timezone, before any date/time API is touched.
        Environment.SetEnvironmentVariable("TZ", "UTC");
        TimeZoneInfo.ClearCachedData();

        var serviceName = builder.Configuration["ServiceName"] ?? builder.Environment.ApplicationName;

        builder.AddObservability(serviceName); // traces + metrics + logs (OTLP), snake_case keys
        builder.AddDefaultHealthChecks();

        return builder;
    }

    extension(WebApplication app)
    {
        /// <summary>Pipeline defaults: echo the trace id to callers (X-Trace-Id). Call first.</summary>
        public WebApplication UseServiceDefaults()
        {
            app.UseMiddleware<TraceResponseMiddleware>();
            return app;
        }

        /// <summary>Maps the default health endpoints (/health/liveness, /health/readiness, /health).</summary>
        public WebApplication MapDefaultEndpoints()
        {
            app.MapDefaultHealthEndpoints();
            return app;
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/BuildingBlocks/TicketMaster.ServiceDefaults -c Debug`
Expected: FAIL — `SerilogExtensions`/`CorrelationExtensions` are still in the project but reference removed packages, OR unused. This is expected; Task 6 deletes them. (If it builds, even better.)

- [ ] **Step 3: Commit (defer if build red until Task 6)**

Proceed to Task 6 before committing; commit both together there.

---

## Task 6: delete the Serilog + CorrelationId files and packages

**Files:**
- Delete: `src/BuildingBlocks/TicketMaster.ServiceDefaults/Logging/SerilogExtensions.cs`
- Delete: `src/BuildingBlocks/TicketMaster.ServiceDefaults/Logging/TraceContextEnricher.cs`
- Delete: `src/BuildingBlocks/TicketMaster.ServiceDefaults/Correlation/CorrelationExtensions.cs`
- Modify: `src/BuildingBlocks/TicketMaster.ServiceDefaults/TicketMaster.ServiceDefaults.csproj`

- [ ] **Step 1: Delete the three files**

```bash
git rm src/BuildingBlocks/TicketMaster.ServiceDefaults/Logging/SerilogExtensions.cs \
       src/BuildingBlocks/TicketMaster.ServiceDefaults/Logging/TraceContextEnricher.cs \
       src/BuildingBlocks/TicketMaster.ServiceDefaults/Correlation/CorrelationExtensions.cs
```

- [ ] **Step 2: Remove the Serilog + CorrelationId package references**

In `TicketMaster.ServiceDefaults.csproj`, delete the `<!-- Logging -->` block (all 6 `Serilog.*` packages) and the `<!-- Correlation -->` block (`CorrelationId`). The csproj's package ItemGroup keeps only the Observability + Health blocks:

```xml
  <ItemGroup>
    <!-- Observability -->
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.15.3" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.15.3" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.15.2" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" Version="1.15.1" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Process" Version="1.15.1-beta.1" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.15.1" />
    <!-- Health -->
    <PackageReference Include="AspNetCore.HealthChecks.Kafka" Version="9.0.0" />
    <PackageReference Include="AspNetCore.HealthChecks.NpgSql" Version="9.0.0" />
    <PackageReference Include="AspNetCore.HealthChecks.Redis" Version="9.0.0" />
    <PackageReference Include="AspNetCore.HealthChecks.UI.Client" Version="9.0.0" />
    <PackageReference Include="AspNetCore.HealthChecks.Uris" Version="9.0.0" />
  </ItemGroup>
```

> The core `OpenTelemetry` package (providing `BaseProcessor<LogRecord>`, `builder.Logging.AddOpenTelemetry`, `OpenTelemetryLoggerOptions`) is pulled in transitively by `OpenTelemetry.Extensions.Hosting`. If the build reports it missing, add `<PackageReference Include="OpenTelemetry" Version="1.15.3" />` to the Observability block.

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build TicketMaster.slnx -c Debug`
Expected: succeeds, 0 warnings (TreatWarningsAsErrors). If `OpenTelemetry` core is missing, apply the note above and rebuild.

- [ ] **Step 4: Commit Tasks 5 + 6 together**

```bash
git add -A
git commit -m "refactor(observability): drop Serilog + CorrelationId; logs go OTel-native"
```

---

## Task 7: capture trace_id in the outbox (keep the correlation chain)

**Files:**
- Modify: `src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure/Outbox/OutboxWriter.cs:21-22`
- Test: `tests/TicketMaster.UnitTests/BuildingBlocks/OutboxWriterTests.cs` (add a case)

- [ ] **Step 1: Write the failing test**

Add this `[Fact]` to the existing `OutboxWriterTests` class in `tests/TicketMaster.UnitTests/BuildingBlocks/OutboxWriterTests.cs` (add `using System.Diagnostics;` at the top):

```csharp
    [Fact]
    public async Task Enqueue_falls_back_to_current_trace_id_when_no_correlation_id_given()
    {
        var options = new DbContextOptionsBuilder<OutboxTestContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var ctx = new OutboxTestContext(options);

        using var source = new System.Diagnostics.ActivitySource("outbox-test");
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _)
                => System.Diagnostics.ActivitySamplingResult.AllData
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        using var activity = source.StartActivity("op")!;

        var writer = new OutboxWriter(ctx);
        writer.Enqueue(new ThingHappened("abc"), aggregateType: "platform", aggregateId: "agg-1");
        await ctx.SaveChangesAsync();

        var row = await ctx.Set<OutboxMessage>().SingleAsync();
        row.CorrelationId.Should().Be(activity.TraceId.ToString());
        row.TraceParent.Should().Be(activity.Id);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/TicketMaster.UnitTests -c Debug --filter "FullyQualifiedName~Enqueue_falls_back_to_current_trace_id"`
Expected: FAIL — `CorrelationId` is null (current code reads trace baggage `"correlation_id"`, which is unset).

- [ ] **Step 3: Change the fallback source**

In `src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure/Outbox/OutboxWriter.cs`, replace line 21-22:

```csharp
            // Fall back to the ambient correlation id (set into trace baggage at the HTTP boundary).
            CorrelationId = correlationId ?? Activity.Current?.GetBaggageItem("correlation_id"),
```

with:

```csharp
            // Correlation id is the trace id: auto-generated per request, W3C-propagated, and already
            // carried across the outbox boundary by TraceParent below. No separate id to manage.
            CorrelationId = correlationId ?? Activity.Current?.TraceId.ToString(),
```

- [ ] **Step 4: Run the outbox tests to verify they pass**

Run: `dotnet test tests/TicketMaster.UnitTests -c Debug --filter "FullyQualifiedName~OutboxWriterTests"`
Expected: PASS (both the existing explicit-correlationId test and the new trace_id-fallback test).

- [ ] **Step 5: Commit**

```bash
git add src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure/Outbox/OutboxWriter.cs tests/TicketMaster.UnitTests/BuildingBlocks/OutboxWriterTests.cs
git commit -m "refactor(outbox): correlation id = current trace id, not trace baggage"
```

---

## Task 8: strip the fluent-bit / Serilog file plumbing

**Files:**
- Modify: `src/Host/TicketMaster.AppHost/appsettings.json` (remove `Serilog` block)
- Modify: `src/Host/TicketMaster.AppHost/appsettings.Development.json` (remove `Serilog` block)
- Modify: `tests/TicketMaster.IntegrationTests/SpineFixture.cs:119-120` (drop the dead `Serilog:LogFile` setting)
- Modify: `infra/docker-compose.yml` (remove `fluent-bit` service + `Serilog__LogFile` env + log volume)
- Delete: `infra/log-sidecar/fluent-bit.conf`, `infra/log-sidecar/parsers.conf`

- [ ] **Step 1: Remove the Serilog config from appsettings**

In `src/Host/TicketMaster.AppHost/appsettings.json`, delete the trailing `"Serilog": { ... }` entry (and the comma on the line before it). In `src/Host/TicketMaster.AppHost/appsettings.Development.json`, replace the whole file with an empty object:

```json
{
}
```

- [ ] **Step 2: Drop the dead Serilog setting in the integration fixture**

In `tests/TicketMaster.IntegrationTests/SpineFixture.cs`, delete these two lines (119-120):

```csharp
                // Keep the Serilog file sink inside the (gitignored) test output, not the repo's infra/logs.
                host.UseSetting("Serilog:LogFile", Path.Combine(AppContext.BaseDirectory, "logs", "apphost-.json"));
```

Leave the `OTEL_SDK_DISABLED` setting and its comment in place.

- [ ] **Step 3: Remove fluent-bit from docker-compose**

Open `infra/docker-compose.yml`. Delete the entire `fluent-bit:` service block. On the `app-host` service, delete the `Serilog__LogFile` environment entry and any shared logs volume/mount that only existed to hand files to fluent-bit. Keep `OTEL_EXPORTER_OTLP_ENDPOINT=http://signoz-otel-collector:4317` and the `signoz-net` network. If a top-level `volumes:` entry (e.g. `app-logs`) is now unreferenced, remove it too.

> Verify with: `git grep -n "fluent\|Serilog__LogFile\|log-sidecar" infra/docker-compose.yml` → expect no matches after the edit.

- [ ] **Step 4: Delete the log-sidecar folder**

```bash
git rm -r infra/log-sidecar
```

- [ ] **Step 5: Validate the compose file**

Run: `docker compose -f infra/docker-compose.yml config --quiet`
Expected: no output, exit 0 (compose file is still valid). If `docker` is unavailable in this environment, skip and note it.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore(infra): remove fluent-bit log sidecar; logs ship via OTLP direct"
```

---

## Task 9: full verification

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build TicketMaster.slnx -c Debug`
Expected: succeeds, 0 warnings.

- [ ] **Step 2: Run the unit + architecture tests**

Run: `dotnet test tests/TicketMaster.UnitTests tests/TicketMaster.ArchitectureTests -c Debug`
Expected: all green — includes the 3 new SnakeCase/processor/middleware tests + the new outbox fallback test.

- [ ] **Step 3: Run the integration tests (Docker required)**

Run: `dotnet test tests/TicketMaster.IntegrationTests -c Debug`
Expected: green — the e2e boots the AppHost (now OTel-native logging) with `OTEL_SDK_DISABLED=true`, proving startup is intact without Serilog/CorrelationId. If Docker is unavailable here, note it and have the user run it.

- [ ] **Step 4: Confirm no stale references remain**

Run: `git grep -n "Serilog\|CorrelationId\|fluent" src tests infra | grep -v "correlation_id\|CorrelationId =\|CorrelationId {" `
Expected: only the outbox **column** `CorrelationId` (property/migrations) remains — no Serilog, no fluent-bit, no CorrelationId package usage.

- [ ] **Step 5: Final review + finish the branch**

Invoke `superpowers:requesting-code-review` for the diff, then `superpowers:finishing-a-development-branch` to merge.

---

## Self-Review notes

- **Spec coverage:** snake_case (Tasks 1–3) ✓; auto correlation = trace_id (Tasks 3 logs auto-stamp + 7 outbox) ✓; OTel-native logging / drop Serilog+CorrelationId+fluent-bit (Tasks 3,5,6,8) ✓; X-Trace-Id continuity for mobile (Task 4) ✓; chain unbroken (Task 7 + untouched `MessageContextFilter`) ✓.
- **Type consistency:** `SnakeCase.Convert` (Task 1) used by `SnakeCaseLogRecordProcessor` (Task 2) and exercised in Task 3; `TraceResponseMiddleware` (Task 4) registered in Task 5; resource helper `AddTicketMasterResource` defined and used only within Task 3.
- **Out of scope (intentional):** renaming the `correlation_id`/`causation_id` outbox columns or simplifying `MessageContextFilter` — the chain works as-is; a separate cleanup if desired.
