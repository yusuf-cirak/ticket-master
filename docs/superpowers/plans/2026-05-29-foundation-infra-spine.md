# Foundation & Infra Spine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the modular-monolith skeleton plus the async-messaging spine, proving an `outbox` row flows through Debezium to Kafka and back into an idempotent consumer — end to end, in an automated test.

**Architecture:** A single ASP.NET Core host (`TicketMaster.Api`) composes modules. Shared primitives live in three `BuildingBlocks` projects (Domain, Application, Infrastructure). Cross-module talk is via MediatR 12.5.0 (in-process sync request/response) and a transactional Outbox table read by Debezium (Kafka Connect) and published to Kafka (async events). A throwaway `Platform` module exercises the spine. Dependency rules between projects are enforced by NetArchTest in CI.

**Tech Stack:** .NET 10 (LTS), C# 14, **MediatR 12.5.0** (in-process messaging, last OSS release), **MassTransit 8.5.9 + MassTransit.Kafka 8.5.9** (Kafka consumption via the Kafka rider; last open-source v8 — v9 is commercial), EF Core 10 + Npgsql, **YC.Monad** (Result/Option/Error) + YC.Monad.EntityFrameworkCore, PostgreSQL (latest), Kafka latest (KRaft) + Kafka Connect with Debezium latest (Outbox Event Router SMT), Redis latest, Elasticsearch latest, StackExchange.Redis, Serilog, xUnit + Testcontainers + FluentAssertions + NetArchTest.Rules. All infra container images use the `:latest` tag per project directive.

---

## File Structure

```text
global.json                                  # pin .NET 10 SDK
Directory.Build.props                         # shared: nullable, LangVersion, TreatWarningsAsErrors
docker-compose.yml                            # postgres, kafka, kafka-connect(debezium), redis, elasticsearch
deploy/debezium/outbox-connector.json         # Debezium connector config (Outbox Event Router)

src/
  BuildingBlocks/
    TicketMaster.BuildingBlocks.Domain/
      Entity.cs                                # Entity<TId> base
      AggregateRoot.cs                         # AggregateRoot<TId> + domain-event buffer
      IDomainEvent.cs
      ValueObject.cs
      StronglyTypedId.cs                       # GuidId base record
      # Result/Option/Error come from the YC.Monad NuGet package (not hand-rolled)
    TicketMaster.BuildingBlocks.Application/
      DependencyInjection.cs                   # AddApplicationMessaging(handlerAssemblies) -> MediatR
      # IRequest/IRequestHandler/IPipelineBehavior come from the MediatR 12.5.0 package
    TicketMaster.BuildingBlocks.Infrastructure/
      Outbox/OutboxMessage.cs                  # outbox row entity
      Outbox/IOutboxWriter.cs
      Outbox/OutboxWriter.cs                   # serialize domain/integration event -> outbox row
      Inbox/InboxMessage.cs                    # consumer dedup row
      Inbox/IInboxStore.cs
      Inbox/InboxStore.cs
      Messaging/IntegrationEvent.cs            # base record for cross-module events
      # Kafka consumption uses MassTransit's Kafka rider (IConsumer<T>), configured in the
      # API host; no custom BackgroundService. Idempotency via IInboxStore inside consumers.

  Modules/
    Platform/
      TicketMaster.Modules.Platform.Contracts/
        PingIntegrationEvent.cs                # integration event published via outbox
      TicketMaster.Modules.Platform/
        PlatformModule.cs                      # DI + endpoint registration
        PlatformDbContext.cs                   # owns `platform` schema + outbox table
        PingEndpoint.cs                        # POST /platform/ping -> writes outbox row in a tx
        PingConsumer.cs                        # consumes PingIntegrationEvent from Kafka

  Bootstrap/
    TicketMaster.Api/
      Program.cs                               # Serilog, module wiring, health endpoint
      appsettings.json
      appsettings.Development.json

tests/
  TicketMaster.ArchitectureTests/
    DependencyRuleTests.cs                     # NetArchTest dependency guards
  TicketMaster.IntegrationTests/
    SpineEndToEndTests.cs                      # outbox -> Debezium -> Kafka -> consumer
    Fixtures/PlatformStackFixture.cs           # Testcontainers: postgres + kafka + connect

TicketMaster.slnx                              # already exists (empty)
```

**Responsibilities:**
- `BuildingBlocks.Domain` — pure domain primitives, zero infra dependencies.
- `BuildingBlocks.Application` — messaging abstractions + in-process dispatcher.
- `BuildingBlocks.Infrastructure` — EF/Kafka/Outbox/Inbox plumbing shared by every module.
- `Modules.Platform` — disposable proof-of-spine module; deleted (or kept as smoke test) once real modules land.
- `TicketMaster.Api` — composition root.

---

## Task 0: Repository-level config

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`

- [ ] **Step 1: Pin the SDK**

`global.json`:
```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

- [ ] **Step 2: Shared build props**

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Verify SDK resolves**

Run: `dotnet --version`
Expected: `10.0.1xx` (matches global.json; no SDK-not-found error).

- [ ] **Step 4: Commit**

```bash
git add global.json Directory.Build.props
git commit -m "build: pin .NET 10 SDK and shared build props"
```

---

## Task 1: BuildingBlocks.Domain primitives

**Files:**
- Create: `src/BuildingBlocks/TicketMaster.BuildingBlocks.Domain/TicketMaster.BuildingBlocks.Domain.csproj`
- Create: `src/BuildingBlocks/TicketMaster.BuildingBlocks.Domain/IDomainEvent.cs`
- Create: `.../StronglyTypedId.cs`
- Create: `.../Entity.cs`
- Create: `.../AggregateRoot.cs`
- Create: `.../ValueObject.cs`
- Reference: `YC.Monad` NuGet package (provides `Result`, `Result<T>`, `Option<T>`, `Error`)
- Test: `tests/TicketMaster.UnitTests/BuildingBlocks/AggregateRootTests.cs`

- [ ] **Step 1: Create the project and add to solution**

Run:
```bash
dotnet new classlib -n TicketMaster.BuildingBlocks.Domain -o src/BuildingBlocks/TicketMaster.BuildingBlocks.Domain
rm src/BuildingBlocks/TicketMaster.BuildingBlocks.Domain/Class1.cs
dotnet sln TicketMaster.slnx add src/BuildingBlocks/TicketMaster.BuildingBlocks.Domain
dotnet add src/BuildingBlocks/TicketMaster.BuildingBlocks.Domain package YC.Monad
```
Expected: `Project ... added to the solution.` and the `YC.Monad` package restored.

> `Result`, `Result<T>`, `Option<T>`, and `Error` come from `YC.Monad` — do NOT hand-roll them.
> Key API: `Result.Success()`, `Result<T>.Success(value)`, `Error.Create("CODE", "message")`
> (optional HTTP status third arg), `ErrorCache.NotFound/BadRequest/Unauthorized/Forbidden`,
> and railway methods `Match / Map / Bind / Tap / TapError / Ensure` (+ async variants),
> `ResultRailway.Try(...)`. `YC.Monad` is a pure functional library (no EF/Kafka), so it does
> not violate the Domain "no infrastructure" architecture rule.

- [ ] **Step 2: Write the domain primitives**

`IDomainEvent.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Domain;

public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredOn { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
```

`StronglyTypedId.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Domain;

public abstract record StronglyTypedId(Guid Value)
{
    public override string ToString() => Value.ToString();
}
```

`Entity.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Domain;

public abstract class Entity<TId> where TId : notnull
{
    public TId Id { get; protected set; } = default!;

    protected Entity(TId id) => Id = id;
    protected Entity() { }

    public override bool Equals(object? obj) =>
        obj is Entity<TId> other && GetType() == other.GetType() && Id.Equals(other.Id);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
```

`AggregateRoot.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Domain;

public abstract class AggregateRoot<TId> : Entity<TId> where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected AggregateRoot(TId id) : base(id) { }
    protected AggregateRoot() { }

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

`ValueObject.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Domain;

public abstract class ValueObject
{
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public override bool Equals(object? obj) =>
        obj is ValueObject other && GetType() == other.GetType()
        && GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());

    public override int GetHashCode() =>
        GetEqualityComponents().Aggregate(0, (h, c) => HashCode.Combine(h, c));
}
```

> **No `Result.cs`** — `Result`, `Result<T>`, `Option<T>`, and `Error` are provided by the
> `YC.Monad` package added in Step 1. Reference `using YC.Monad;` where needed.

- [ ] **Step 3: Create the unit test project (first use) and write the failing test**

Run:
```bash
dotnet new xunit -n TicketMaster.UnitTests -o tests/TicketMaster.UnitTests
dotnet sln TicketMaster.slnx add tests/TicketMaster.UnitTests
dotnet add tests/TicketMaster.UnitTests reference src/BuildingBlocks/TicketMaster.BuildingBlocks.Domain
dotnet add tests/TicketMaster.UnitTests package FluentAssertions
rm tests/TicketMaster.UnitTests/UnitTest1.cs
```

`tests/TicketMaster.UnitTests/BuildingBlocks/AggregateRootTests.cs`:
```csharp
using FluentAssertions;
using TicketMaster.BuildingBlocks.Domain;
using Xunit;

namespace TicketMaster.UnitTests.BuildingBlocks;

public sealed record SampleId(Guid Value) : StronglyTypedId(Value);

public sealed record SampleRaised(string What) : DomainEvent;

public sealed class Sample : AggregateRoot<SampleId>
{
    public Sample(SampleId id) : base(id) { }
    public void Do() => Raise(new SampleRaised("done"));
}

public class AggregateRootTests
{
    [Fact]
    public void Raise_buffers_domain_event()
    {
        var sample = new Sample(new SampleId(Guid.NewGuid()));
        sample.Do();
        sample.DomainEvents.Should().ContainSingle(e => e is SampleRaised);
    }

    [Fact]
    public void ClearDomainEvents_empties_buffer()
    {
        var sample = new Sample(new SampleId(Guid.NewGuid()));
        sample.Do();
        sample.ClearDomainEvents();
        sample.DomainEvents.Should().BeEmpty();
    }
}
```

> No `Result` test here — `Result`/`Error` are the `YC.Monad` package's responsibility, not ours.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/TicketMaster.UnitTests -v minimal`
Expected: PASS (2 passed).

- [ ] **Step 5: Commit**

```bash
git add src/BuildingBlocks/TicketMaster.BuildingBlocks.Domain tests/TicketMaster.UnitTests global.json
git commit -m "feat(domain): add BuildingBlocks.Domain primitives"
```

---

## Task 2: BuildingBlocks.Application — in-process messaging (MediatR)

> **SUPERSEDED — implemented with MediatR 12.5.0, not a custom mediator.** Per project
> directive, in-process messaging uses **MediatR 12.5.0** (last open-source / Apache-2.0
> release). The custom `IRequest`/`ISender`/`IPipelineBehavior`/`Sender` code blocks below
> are obsolete; the committed implementation is the source of truth. What was actually built:
> - `dotnet add ...Application package MediatR --version 12.5.0`
> - **Deleted** all custom messaging files; `IRequest<TResponse>`, `IRequestHandler<,>`,
>   `IPipelineBehavior<,>`, `ISender`/`IMediator`, `RequestHandlerDelegate<T>` now come from MediatR.
> - `DependencyInjection.AddApplicationMessaging(this IServiceCollection, params Assembly[] handlerAssemblies)`
>   → `services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(handlerAssemblies))`.
> - `SenderTests` rewritten against MediatR `ISender` (Echo handler + UpperBehavior pipeline). 4 unit tests pass.
> Downstream tasks: register handlers by passing each module's Application assembly to
> `AddApplicationMessaging(...)`; register pipeline behaviors explicitly via
> `services.AddTransient<IPipelineBehavior<,>, ...>` or MediatR `AddOpenBehavior`.

**Files (original custom-mediator design — obsolete, retained for history):**
- Create: `src/BuildingBlocks/TicketMaster.BuildingBlocks.Application/TicketMaster.BuildingBlocks.Application.csproj`
- Create: `.../Messaging/IRequest.cs`
- Create: `.../Messaging/ISender.cs`
- Create: `.../Messaging/IPipelineBehavior.cs`
- Create: `.../Messaging/Sender.cs`
- Create: `.../DependencyInjection.cs`
- Test: `tests/TicketMaster.UnitTests/BuildingBlocks/SenderTests.cs`

- [ ] **Step 1: Create project + references**

Run:
```bash
dotnet new classlib -n TicketMaster.BuildingBlocks.Application -o src/BuildingBlocks/TicketMaster.BuildingBlocks.Application
rm src/BuildingBlocks/TicketMaster.BuildingBlocks.Application/Class1.cs
dotnet sln TicketMaster.slnx add src/BuildingBlocks/TicketMaster.BuildingBlocks.Application
dotnet add src/BuildingBlocks/TicketMaster.BuildingBlocks.Application reference src/BuildingBlocks/TicketMaster.BuildingBlocks.Domain
dotnet add src/BuildingBlocks/TicketMaster.BuildingBlocks.Application package Microsoft.Extensions.DependencyInjection.Abstractions
```

- [ ] **Step 2: Write the messaging abstractions**

`Messaging/IRequest.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Application.Messaging;

public interface IRequest<TResponse>;

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
```

`Messaging/ISender.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Application.Messaging;

public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
}
```

`Messaging/IPipelineBehavior.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Application.Messaging;

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}
```

`Messaging/Sender.cs`:
```csharp
using Microsoft.Extensions.DependencyInjection;

namespace TicketMaster.BuildingBlocks.Application.Messaging;

internal sealed class Sender(IServiceProvider provider) : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handler = provider.GetService(handlerType)
            ?? throw new InvalidOperationException($"No handler registered for {requestType.Name}.");

        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var behaviors = ((IEnumerable<object>)provider.GetServices(behaviorType)).Reverse().ToArray();

        RequestHandlerDelegate<TResponse> next = () => (Task<TResponse>)handlerType
            .GetMethod("Handle")!
            .Invoke(handler, [request, cancellationToken])!;

        foreach (var behavior in behaviors)
        {
            var captured = next;
            var method = behaviorType.GetMethod("Handle")!;
            next = () => (Task<TResponse>)method.Invoke(behavior, [request, captured, cancellationToken])!;
        }

        return next();
    }
}
```

`DependencyInjection.cs`:
```csharp
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.BuildingBlocks.Application.Messaging;

namespace TicketMaster.BuildingBlocks.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplicationMessaging(this IServiceCollection services)
    {
        services.AddScoped<ISender, Sender>();
        return services;
    }

    public static IServiceCollection AddHandlersFrom(this IServiceCollection services, Assembly assembly)
    {
        var handlerInterface = typeof(IRequestHandler<,>);
        foreach (var type in assembly.GetTypes().Where(t => t is { IsAbstract: false, IsInterface: false }))
        {
            foreach (var i in type.GetInterfaces()
                         .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterface))
            {
                services.AddScoped(i, type);
            }
        }
        return services;
    }
}
```

- [ ] **Step 3: Write the failing test**

`tests/TicketMaster.UnitTests/BuildingBlocks/SenderTests.cs`:
```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.BuildingBlocks.Application;
using TicketMaster.BuildingBlocks.Application.Messaging;
using Xunit;

namespace TicketMaster.UnitTests.BuildingBlocks;

public sealed record Echo(string Text) : IRequest<string>;

public sealed class EchoHandler : IRequestHandler<Echo, string>
{
    public Task<string> Handle(Echo request, CancellationToken ct) => Task.FromResult(request.Text);
}

public sealed class UpperBehavior : IPipelineBehavior<Echo, string>
{
    public async Task<string> Handle(Echo request, RequestHandlerDelegate<string> next, CancellationToken ct)
        => (await next()).ToUpperInvariant();
}

public class SenderTests
{
    private static ISender Build(bool withBehavior)
    {
        var services = new ServiceCollection();
        services.AddApplicationMessaging();
        services.AddScoped<IRequestHandler<Echo, string>, EchoHandler>();
        if (withBehavior)
            services.AddScoped<IPipelineBehavior<Echo, string>, UpperBehavior>();
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task Send_routes_to_handler()
    {
        var result = await Build(withBehavior: false).Send(new Echo("hi"));
        result.Should().Be("hi");
    }

    [Fact]
    public async Task Send_runs_pipeline_behavior()
    {
        var result = await Build(withBehavior: true).Send(new Echo("hi"));
        result.Should().Be("HI");
    }
}
```

- [ ] **Step 4: Add reference + run**

Run:
```bash
dotnet add tests/TicketMaster.UnitTests reference src/BuildingBlocks/TicketMaster.BuildingBlocks.Application
dotnet test tests/TicketMaster.UnitTests -v minimal
```
Expected: PASS (all tests, including 2 new).

- [ ] **Step 5: Commit**

```bash
git add src/BuildingBlocks/TicketMaster.BuildingBlocks.Application tests/TicketMaster.UnitTests
git commit -m "feat(application): in-process mediator with pipeline behaviors"
```

---

## Task 3: BuildingBlocks.Infrastructure — Outbox + Inbox + integration event base

**Files:**
- Create: `src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure/TicketMaster.BuildingBlocks.Infrastructure.csproj`
- Create: `.../Messaging/IntegrationEvent.cs`
- Create: `.../Outbox/OutboxMessage.cs`
- Create: `.../Outbox/IOutboxWriter.cs`
- Create: `.../Outbox/OutboxWriter.cs`
- Create: `.../Inbox/InboxMessage.cs`
- Create: `.../Inbox/IInboxStore.cs`
- Create: `.../Inbox/InboxStore.cs`
- Test: `tests/TicketMaster.UnitTests/BuildingBlocks/OutboxWriterTests.cs`

- [ ] **Step 1: Create project + references**

Run:
```bash
dotnet new classlib -n TicketMaster.BuildingBlocks.Infrastructure -o src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure
rm src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure/Class1.cs
dotnet sln TicketMaster.slnx add src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure
dotnet add src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure reference src/BuildingBlocks/TicketMaster.BuildingBlocks.Domain
dotnet add src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure package Microsoft.EntityFrameworkCore.Relational
dotnet add src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure package Confluent.Kafka
dotnet add src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure package Microsoft.Extensions.Hosting.Abstractions
```

- [ ] **Step 2: Integration event base + outbox row**

`Messaging/IntegrationEvent.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Infrastructure.Messaging;

public abstract record IntegrationEvent
{
    public Guid MessageId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOn { get; init; } = DateTimeOffset.UtcNow;
}
```

`Outbox/OutboxMessage.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Infrastructure.Outbox;

// One row per integration event. Column names match the Debezium Outbox Event Router config.
public sealed class OutboxMessage
{
    public Guid Id { get; init; }                 // = MessageId; router 'id' field
    public string AggregateType { get; init; } = default!; // -> topic suffix (e.g. "platform")
    public string AggregateId { get; init; } = default!;   // -> Kafka partition key
    public string EventType { get; init; } = default!;     // CLR type name, header
    public int Version { get; init; } = 1;
    public DateTimeOffset OccurredOn { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string Payload { get; init; } = default!;       // jsonb
}
```

`Outbox/IOutboxWriter.cs`:
```csharp
using TicketMaster.BuildingBlocks.Infrastructure.Messaging;

namespace TicketMaster.BuildingBlocks.Infrastructure.Outbox;

public interface IOutboxWriter
{
    // Appends an outbox row to the current DbContext change-tracker (NOT saved here —
    // the caller's SaveChanges commits event + state in one transaction).
    OutboxMessage Enqueue(IntegrationEvent @event, string aggregateType, string aggregateId,
        string? correlationId = null, string? causationId = null);
}
```

`Outbox/OutboxWriter.cs`:
```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TicketMaster.BuildingBlocks.Infrastructure.Messaging;

namespace TicketMaster.BuildingBlocks.Infrastructure.Outbox;

public sealed class OutboxWriter(DbContext dbContext) : IOutboxWriter
{
    public OutboxMessage Enqueue(IntegrationEvent @event, string aggregateType, string aggregateId,
        string? correlationId = null, string? causationId = null)
    {
        var row = new OutboxMessage
        {
            Id = @event.MessageId,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            EventType = @event.GetType().Name,
            Version = 1,
            OccurredOn = @event.OccurredOn,
            CorrelationId = correlationId,
            CausationId = causationId,
            Payload = JsonSerializer.Serialize(@event, @event.GetType())
        };
        dbContext.Set<OutboxMessage>().Add(row);
        return row;
    }
}
```

- [ ] **Step 3: Inbox dedup store**

`Inbox/InboxMessage.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Infrastructure.Inbox;

public sealed class InboxMessage
{
    public Guid MessageId { get; init; }     // dedup key (= producer MessageId)
    public string Consumer { get; init; } = default!;
    public DateTimeOffset ProcessedOn { get; init; }
}
```

`Inbox/IInboxStore.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Infrastructure.Inbox;

public interface IInboxStore
{
    // Returns true if this (messageId, consumer) pair is new and was recorded; false if already seen.
    Task<bool> TryMarkProcessed(Guid messageId, string consumer, CancellationToken cancellationToken);
}
```

`Inbox/InboxStore.cs`:
```csharp
using Microsoft.EntityFrameworkCore;

namespace TicketMaster.BuildingBlocks.Infrastructure.Inbox;

public sealed class InboxStore(DbContext dbContext) : IInboxStore
{
    public async Task<bool> TryMarkProcessed(Guid messageId, string consumer, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Set<InboxMessage>()
            .AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, cancellationToken);
        if (exists) return false;

        dbContext.Set<InboxMessage>().Add(new InboxMessage
        {
            MessageId = messageId,
            Consumer = consumer,
            ProcessedOn = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
```

- [ ] **Step 4: Write the failing test (outbox serialization, in-memory EF)**

Run:
```bash
dotnet add tests/TicketMaster.UnitTests reference src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure
dotnet add tests/TicketMaster.UnitTests package Microsoft.EntityFrameworkCore.InMemory
```

`tests/TicketMaster.UnitTests/BuildingBlocks/OutboxWriterTests.cs`:
```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TicketMaster.BuildingBlocks.Infrastructure.Messaging;
using TicketMaster.BuildingBlocks.Infrastructure.Outbox;
using Xunit;

namespace TicketMaster.UnitTests.BuildingBlocks;

public sealed record ThingHappened(string Name) : IntegrationEvent;

public sealed class OutboxTestContext(DbContextOptions<OutboxTestContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder b) =>
        b.Entity<OutboxMessage>().HasKey(x => x.Id);
}

public class OutboxWriterTests
{
    [Fact]
    public async Task Enqueue_writes_serialized_row_on_save()
    {
        var options = new DbContextOptionsBuilder<OutboxTestContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var ctx = new OutboxTestContext(options);

        var writer = new OutboxWriter(ctx);
        var evt = new ThingHappened("abc");
        writer.Enqueue(evt, aggregateType: "platform", aggregateId: "agg-1", correlationId: "corr-1");
        await ctx.SaveChangesAsync();

        var row = await ctx.Set<OutboxMessage>().SingleAsync();
        row.Id.Should().Be(evt.MessageId);
        row.AggregateType.Should().Be("platform");
        row.AggregateId.Should().Be("agg-1");
        row.EventType.Should().Be(nameof(ThingHappened));
        row.CorrelationId.Should().Be("corr-1");
        row.Payload.Should().Contain("abc");
    }
}
```

- [ ] **Step 5: Run**

Run: `dotnet test tests/TicketMaster.UnitTests -v minimal`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure tests/TicketMaster.UnitTests
git commit -m "feat(infrastructure): outbox writer, inbox store, integration event base"
```

---

## Task 4: Kafka consumption via MassTransit (Kafka rider)

> **REVISED — use MassTransit, not a custom Confluent.Kafka BackgroundService.** Per project
> directive, Kafka consumption uses the **MassTransit Kafka rider** (`MassTransit` 8.5.9 +
> `MassTransit.Kafka` 8.5.9 — last open-source v8; v9 is commercial). The
> `KafkaConsumerOptions` / `IIntegrationEventHandler` / `KafkaBackgroundConsumer` design in
> the obsolete code blocks below is dropped. New approach:
> - Task 4 = add `MassTransit` + `MassTransit.Kafka` (both 8.5.9) to BuildingBlocks.Infrastructure; build clean; commit. No custom consumer types.
> - Module integration events are MassTransit message contracts (the `IntegrationEvent` records). Consumers implement `MassTransit.IConsumer<TEvent>`.
> - The bus (in-memory) + Kafka rider are configured once in the **API host** (Task 7): `AddMassTransit(x => { x.UsingInMemory(...); x.AddRider(r => { r.AddConsumer<PingConsumer>(); r.UsingKafka((ctx,k) => { k.Host(bootstrap); k.TopicEndpoint<PingIntegrationEvent>("tm.platform.events", "platform", e => e.ConfigureConsumer<PingConsumer>(ctx)); }); }); });`
> - **Debezium interop:** Debezium publishes plain JSON (no MassTransit envelope), so the Kafka rider must use a **raw JSON deserializer** (`k.UseRawJsonSerializer()` / topic-endpoint raw-json config — verify exact API against MassTransit 8.5 docs at implementation time) so the topic value binds to the message type.
> - Idempotency: consumers call `IInboxStore.TryMarkProcessed(context.Message.MessageId, "<consumer>", ct)` (Task 3) before acting. The Task 10 integration test is the behavioural gate.

**Files (original custom-consumer design — obsolete, retained for history):**
- Create: `src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure/Kafka/KafkaConsumerOptions.cs`
- Create: `.../Kafka/IIntegrationEventHandler.cs`
- Create: `.../Kafka/KafkaBackgroundConsumer.cs`

> Behavioural verification of this consumer is in the Task 10 integration test (real Kafka). This task delivers compiling, reviewed code; no unit test (a BackgroundService against a broker is an integration concern).

- [ ] **Step 1: Options + handler abstraction**

`Kafka/KafkaConsumerOptions.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Infrastructure.Kafka;

public sealed class KafkaConsumerOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string GroupId { get; set; } = default!;
    public IReadOnlyList<string> Topics { get; set; } = [];
}
```

`Kafka/IIntegrationEventHandler.cs`:
```csharp
namespace TicketMaster.BuildingBlocks.Infrastructure.Kafka;

// Raw envelope handed to a per-message handler. Payload is the Debezium-routed JSON value;
// EventType + MessageId come from Kafka headers set by the Outbox Event Router.
public interface IIntegrationEventHandler
{
    Task Handle(string eventType, Guid messageId, string payloadJson, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: The background consumer**

`Kafka/KafkaBackgroundConsumer.cs`:
```csharp
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TicketMaster.BuildingBlocks.Infrastructure.Kafka;

public sealed class KafkaBackgroundConsumer(
    KafkaConsumerOptions options,
    IServiceScopeFactory scopeFactory,
    ILogger<KafkaBackgroundConsumer> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

    private async Task ConsumeLoop(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(options.Topics);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(stoppingToken);
            }
            catch (OperationCanceledException) { break; }

            if (result?.Message is null) continue;

            try
            {
                var eventType = Header(result.Message, "eventType") ?? "unknown";
                var messageId = Guid.TryParse(Header(result.Message, "id"), out var id) ? id : Guid.NewGuid();

                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler>();
                await handler.Handle(eventType, messageId, result.Message.Value, stoppingToken);

                consumer.Commit(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process message on {Topic}; will retry (no commit).", result.Topic);
                // No commit => at-least-once redelivery. Inbox dedup makes reprocessing safe.
            }
        }

        consumer.Close();
    }

    private static string? Header(Message<string, string> message, string key)
    {
        if (message.Headers is null) return null;
        return message.Headers.TryGetLastBytes(key, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure -v minimal`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure
git commit -m "feat(infrastructure): generic at-least-once Kafka background consumer"
```

---

## Task 5: Platform module — Contracts + DbContext

**Files:**
- Create: `src/Modules/Platform/TicketMaster.Modules.Platform.Contracts/TicketMaster.Modules.Platform.Contracts.csproj`
- Create: `.../Platform.Contracts/PingIntegrationEvent.cs`
- Create: `src/Modules/Platform/TicketMaster.Modules.Platform/TicketMaster.Modules.Platform.csproj`
- Create: `.../Platform/PlatformDbContext.cs`

- [ ] **Step 1: Contracts project**

Run:
```bash
dotnet new classlib -n TicketMaster.Modules.Platform.Contracts -o src/Modules/Platform/TicketMaster.Modules.Platform.Contracts
rm src/Modules/Platform/TicketMaster.Modules.Platform.Contracts/Class1.cs
dotnet sln TicketMaster.slnx add src/Modules/Platform/TicketMaster.Modules.Platform.Contracts
dotnet add src/Modules/Platform/TicketMaster.Modules.Platform.Contracts reference src/BuildingBlocks/TicketMaster.BuildingBlocks.Infrastructure
```

`PingIntegrationEvent.cs`:
```csharp
using TicketMaster.BuildingBlocks.Infrastructure.Messaging;

namespace TicketMaster.Modules.Platform.Contracts;

public sealed record PingIntegrationEvent(string Note) : IntegrationEvent;
```

- [ ] **Step 2: Module project + EF/Npgsql packages**

Run:
```bash
dotnet new classlib -n TicketMaster.Modules.Platform -o src/Modules/Platform/TicketMaster.Modules.Platform
rm src/Modules/Platform/TicketMaster.Modules.Platform/Class1.cs
dotnet sln TicketMaster.slnx add src/Modules/Platform/TicketMaster.Modules.Platform
dotnet add src/Modules/Platform/TicketMaster.Modules.Platform reference src/Modules/Platform/TicketMaster.Modules.Platform.Contracts
dotnet add src/Modules/Platform/TicketMaster.Modules.Platform reference src/BuildingBlocks/TicketMaster.BuildingBlocks.Application
dotnet add src/Modules/Platform/TicketMaster.Modules.Platform package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Modules/Platform/TicketMaster.Modules.Platform package Microsoft.AspNetCore.Http.Abstractions
dotnet add src/Modules/Platform/TicketMaster.Modules.Platform package Microsoft.Extensions.Hosting.Abstractions
```

- [ ] **Step 3: DbContext owning the `platform` schema (outbox + inbox tables)**

`PlatformDbContext.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using TicketMaster.BuildingBlocks.Infrastructure.Inbox;
using TicketMaster.BuildingBlocks.Infrastructure.Outbox;

namespace TicketMaster.Modules.Platform;

public sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<InboxMessage> Inbox => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.AggregateType).HasColumnName("aggregate_type").IsRequired();
            e.Property(x => x.AggregateId).HasColumnName("aggregate_id").IsRequired();
            e.Property(x => x.EventType).HasColumnName("event_type").IsRequired();
            e.Property(x => x.Version).HasColumnName("version");
            e.Property(x => x.OccurredOn).HasColumnName("occurred_on");
            e.Property(x => x.CorrelationId).HasColumnName("correlation_id");
            e.Property(x => x.CausationId).HasColumnName("causation_id");
            e.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        });

        b.Entity<InboxMessage>(e =>
        {
            e.ToTable("inbox");
            e.HasKey(x => new { x.MessageId, x.Consumer });
            e.Property(x => x.Consumer).HasColumnName("consumer");
            e.Property(x => x.ProcessedOn).HasColumnName("processed_on");
        });
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Modules/Platform/TicketMaster.Modules.Platform -v minimal`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Platform
git commit -m "feat(platform): contracts + DbContext with outbox/inbox tables"
```

---

## Task 6: Platform module — endpoint, consumer, registration

**Files:**
- Create: `src/Modules/Platform/TicketMaster.Modules.Platform/PingEndpoint.cs`
- Create: `.../Platform/PingConsumer.cs`
- Create: `.../Platform/PlatformModule.cs`

- [ ] **Step 1: Ping endpoint — writes an outbox row inside a transaction**

`PingEndpoint.cs`:
```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TicketMaster.BuildingBlocks.Infrastructure.Outbox;
using TicketMaster.Modules.Platform.Contracts;

namespace TicketMaster.Modules.Platform;

public static class PingEndpoint
{
    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/platform/ping", async (PingRequest body, PlatformDbContext db, IOutboxWriter outbox, CancellationToken ct) =>
        {
            var aggregateId = Guid.NewGuid().ToString();
            outbox.Enqueue(new PingIntegrationEvent(body.Note), aggregateType: "platform", aggregateId: aggregateId);
            await db.SaveChangesAsync(ct); // single tx: (here only the outbox row; real modules also persist state)
            return Results.Accepted($"/platform/ping/{aggregateId}", new { aggregateId });
        });

        return app;
    }
}

public sealed record PingRequest(string Note);
```

- [ ] **Step 2: Ping consumer — idempotent via inbox**

`PingConsumer.cs`:
```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TicketMaster.BuildingBlocks.Infrastructure.Inbox;
using TicketMaster.BuildingBlocks.Infrastructure.Kafka;
using TicketMaster.Modules.Platform.Contracts;

namespace TicketMaster.Modules.Platform;

public sealed class PingConsumer(
    PlatformDbContext db,
    ILogger<PingConsumer> logger) : IIntegrationEventHandler
{
    public const string ConsumerName = "platform.ping";

    public async Task Handle(string eventType, Guid messageId, string payloadJson, CancellationToken ct)
    {
        if (eventType != nameof(PingIntegrationEvent)) return;

        var inbox = new InboxStore(db);
        if (!await inbox.TryMarkProcessed(messageId, ConsumerName, ct))
        {
            logger.LogInformation("Duplicate {MessageId} ignored.", messageId);
            return;
        }

        var evt = JsonSerializer.Deserialize<PingIntegrationEvent>(payloadJson);
        logger.LogInformation("PingIntegrationEvent consumed: {Note} ({MessageId})", evt?.Note, messageId);
    }
}
```

- [ ] **Step 3: Module registration (DI + DbContext + consumer wiring)**

`PlatformModule.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TicketMaster.BuildingBlocks.Infrastructure.Kafka;
using TicketMaster.BuildingBlocks.Infrastructure.Outbox;

namespace TicketMaster.Modules.Platform;

public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<PlatformDbContext>(o =>
            o.UseNpgsql(config.GetConnectionString("Postgres"),
                npg => npg.MigrationsHistoryTable("__ef_migrations", PlatformDbContext.Schema)));

        services.AddScoped<IOutboxWriter>(sp => new OutboxWriter(sp.GetRequiredService<PlatformDbContext>()));
        services.AddScoped<IIntegrationEventHandler, PingConsumer>();

        services.AddSingleton(new KafkaConsumerOptions
        {
            BootstrapServers = config["Kafka:BootstrapServers"] ?? "localhost:9092",
            GroupId = "platform",
            Topics = [config["Kafka:PlatformTopic"] ?? "tm.platform.events"]
        });
        services.AddHostedService<KafkaBackgroundConsumer>();

        return services;
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Modules/Platform/TicketMaster.Modules.Platform -v minimal`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Platform/TicketMaster.Modules.Platform
git commit -m "feat(platform): ping endpoint, idempotent consumer, module registration"
```

---

## Task 7: API host (composition root)

**Files:**
- Create: `src/Bootstrap/TicketMaster.Api/TicketMaster.Api.csproj`
- Create: `.../TicketMaster.Api/Program.cs`
- Create: `.../TicketMaster.Api/appsettings.json`
- Create: `.../TicketMaster.Api/appsettings.Development.json`

- [ ] **Step 1: Create web project + references + packages**

Run:
```bash
dotnet new web -n TicketMaster.Api -o src/Bootstrap/TicketMaster.Api
dotnet sln TicketMaster.slnx add src/Bootstrap/TicketMaster.Api
dotnet add src/Bootstrap/TicketMaster.Api reference src/Modules/Platform/TicketMaster.Modules.Platform
dotnet add src/Bootstrap/TicketMaster.Api reference src/BuildingBlocks/TicketMaster.BuildingBlocks.Application
dotnet add src/Bootstrap/TicketMaster.Api package Serilog.AspNetCore
dotnet add src/Bootstrap/TicketMaster.Api package Serilog.Sinks.Console
```

- [ ] **Step 2: Program.cs**

`Program.cs`:
```csharp
using Serilog;
using TicketMaster.BuildingBlocks.Application;
using TicketMaster.Modules.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter()));

builder.Services.AddApplicationMessaging(typeof(TicketMaster.Modules.Platform.PlatformModule).Assembly);
builder.Services.AddPlatformModule(builder.Configuration);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPlatformEndpoints();

app.Run();

public partial class Program; // for WebApplicationFactory in integration tests
```

- [ ] **Step 3: appsettings**

`appsettings.json`:
```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=ticketmaster;Username=tm;Password=tm"
  },
  "Kafka": {
    "BootstrapServers": "localhost:9092",
    "PlatformTopic": "tm.platform.events"
  },
  "Serilog": { "MinimumLevel": "Information" }
}
```

`appsettings.Development.json`:
```json
{
  "Serilog": { "MinimumLevel": "Debug" }
}
```

- [ ] **Step 4: Build the whole solution**

Run: `dotnet build TicketMaster.slnx -v minimal`
Expected: Build succeeded, 0 errors across all projects.

- [ ] **Step 5: Commit**

```bash
git add src/Bootstrap/TicketMaster.Api
git commit -m "feat(api): host with Serilog console + platform module wiring"
```

---

## Task 8: Infra — docker-compose + Debezium connector config

**Files:**
- Create: `docker-compose.yml`
- Create: `deploy/debezium/outbox-connector.json`

- [ ] **Step 1: docker-compose for the full local stack**

`docker-compose.yml`:
```yaml
services:
  postgres:
    image: postgres:latest
    command: ["postgres", "-c", "wal_level=logical"]
    environment:
      POSTGRES_DB: ticketmaster
      POSTGRES_USER: tm
      POSTGRES_PASSWORD: tm
    ports: ["5432:5432"]

  kafka:
    image: confluentinc/cp-kafka:latest
    environment:
      KAFKA_NODE_ID: 1
      KAFKA_PROCESS_ROLES: "broker,controller"
      KAFKA_CONTROLLER_QUORUM_VOTERS: "1@kafka:9093"
      KAFKA_LISTENERS: "PLAINTEXT://kafka:29092,CONTROLLER://kafka:9093,EXTERNAL://0.0.0.0:9092"
      KAFKA_ADVERTISED_LISTENERS: "PLAINTEXT://kafka:29092,EXTERNAL://localhost:9092"
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: "PLAINTEXT:PLAINTEXT,CONTROLLER:PLAINTEXT,EXTERNAL:PLAINTEXT"
      KAFKA_CONTROLLER_LISTENER_NAMES: "CONTROLLER"
      KAFKA_INTER_BROKER_LISTENER_NAME: "PLAINTEXT"
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
      CLUSTER_ID: "tm-local-cluster-0001"
    ports: ["9092:9092"]

  connect:
    image: debezium/connect:latest
    depends_on: [kafka, postgres]
    environment:
      BOOTSTRAP_SERVERS: "kafka:29092"
      GROUP_ID: "tm-connect"
      CONFIG_STORAGE_TOPIC: "_connect_configs"
      OFFSET_STORAGE_TOPIC: "_connect_offsets"
      STATUS_STORAGE_TOPIC: "_connect_status"
    ports: ["8083:8083"]

  redis:
    image: redis:latest
    ports: ["6379:6379"]

  elasticsearch:
    image: docker.elastic.co/elasticsearch/elasticsearch:latest
    environment:
      discovery.type: single-node
      xpack.security.enabled: "false"
      ES_JAVA_OPTS: "-Xms512m -Xmx512m"
    ports: ["9200:9200"]
```

- [ ] **Step 2: Debezium connector (Outbox Event Router)**

`deploy/debezium/outbox-connector.json`:
```json
{
  "name": "tm-outbox-connector",
  "config": {
    "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
    "tasks.max": "1",
    "database.hostname": "postgres",
    "database.port": "5432",
    "database.user": "tm",
    "database.password": "tm",
    "database.dbname": "ticketmaster",
    "topic.prefix": "tm",
    "plugin.name": "pgoutput",
    "schema.include.list": "platform",
    "table.include.list": "platform.outbox",
    "transforms": "outbox",
    "transforms.outbox.type": "io.debezium.transforms.outbox.EventRouter",
    "transforms.outbox.table.field.event.id": "id",
    "transforms.outbox.table.field.event.key": "aggregate_id",
    "transforms.outbox.table.field.event.type": "event_type",
    "transforms.outbox.table.field.event.payload": "payload",
    "transforms.outbox.route.by.field": "aggregate_type",
    "transforms.outbox.route.topic.replacement": "tm.${routedByValue}.events",
    "transforms.outbox.table.fields.additional.placement": "event_type:header:eventType,id:header:id,correlation_id:header:correlationId",
    "key.converter": "org.apache.kafka.connect.storage.StringConverter",
    "value.converter": "org.apache.kafka.connect.json.JsonConverter",
    "value.converter.schemas.enable": "false"
  }
}
```

> `route.topic.replacement` maps `aggregate_type="platform"` → topic `tm.platform.events`,
> matching `Kafka:PlatformTopic` in appsettings. Real modules reuse this exact connector by
> adding their `<schema>.outbox` table to `table.include.list` and `schema.include.list`.

- [ ] **Step 3: Smoke-test the stack manually (documented, not automated)**

Run:
```bash
docker compose up -d
# wait for connect to be healthy, then register the connector:
curl -s -X POST http://localhost:8083/connectors -H "Content-Type: application/json" -d @deploy/debezium/outbox-connector.json
curl -s http://localhost:8083/connectors/tm-outbox-connector/status
```
Expected: connector status `RUNNING` (state of connector and task both `RUNNING`).
Then: `docker compose down`.

- [ ] **Step 4: Commit**

```bash
git add docker-compose.yml deploy/debezium/outbox-connector.json
git commit -m "infra: docker-compose stack + Debezium outbox connector config"
```

---

## Task 9: Architecture dependency-rule tests

**Files:**
- Create: `tests/TicketMaster.ArchitectureTests/TicketMaster.ArchitectureTests.csproj`
- Create: `tests/TicketMaster.ArchitectureTests/DependencyRuleTests.cs`

- [ ] **Step 1: Create project + references**

Run:
```bash
dotnet new xunit -n TicketMaster.ArchitectureTests -o tests/TicketMaster.ArchitectureTests
dotnet sln TicketMaster.slnx add tests/TicketMaster.ArchitectureTests
rm tests/TicketMaster.ArchitectureTests/UnitTest1.cs
dotnet add tests/TicketMaster.ArchitectureTests package NetArchTest.Rules
dotnet add tests/TicketMaster.ArchitectureTests package FluentAssertions
dotnet add tests/TicketMaster.ArchitectureTests reference src/BuildingBlocks/TicketMaster.BuildingBlocks.Domain
dotnet add tests/TicketMaster.ArchitectureTests reference src/Modules/Platform/TicketMaster.Modules.Platform
```

- [ ] **Step 2: Write the rule tests**

`DependencyRuleTests.cs`:
```csharp
using FluentAssertions;
using NetArchTest.Rules;
using TicketMaster.BuildingBlocks.Domain;
using Xunit;

namespace TicketMaster.ArchitectureTests;

public class DependencyRuleTests
{
    private static readonly System.Reflection.Assembly DomainAsm = typeof(AggregateRoot<>).Assembly;
    private static readonly System.Reflection.Assembly PlatformAsm = typeof(TicketMaster.Modules.Platform.PlatformDbContext).Assembly;

    [Fact]
    public void Domain_must_not_depend_on_ef_or_kafka()
    {
        var result = Types.InAssembly(DomainAsm)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Confluent.Kafka", "Npgsql")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Domain has forbidden infra deps: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Module_must_not_depend_on_another_modules_internals()
    {
        // Platform must not reference any *.Modules.*.* namespace except its own + *.Contracts.
        var result = Types.InAssembly(PlatformAsm)
            .That().ResideInNamespaceStartingWith("TicketMaster.Modules.Platform")
            .And().DoNotResideInNamespaceEndingWith(".Contracts")
            .ShouldNot()
            .HaveDependencyOnAny("TicketMaster.Modules.Event", "TicketMaster.Modules.Booking",
                "TicketMaster.Modules.Inventory", "TicketMaster.Modules.Ordering")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"Module crossed an internal boundary: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
```

- [ ] **Step 3: Run**

Run: `dotnet test tests/TicketMaster.ArchitectureTests -v minimal`
Expected: PASS (2 passed). (The second test references not-yet-existing modules only as string names — that is fine; NetArchTest matches by name.)

- [ ] **Step 4: Commit**

```bash
git add tests/TicketMaster.ArchitectureTests
git commit -m "test(arch): dependency-rule guards via NetArchTest"
```

---

## Task 10: End-to-end spine integration test (Testcontainers)

**Files:**
- Create: `tests/TicketMaster.IntegrationTests/TicketMaster.IntegrationTests.csproj`
- Create: `tests/TicketMaster.IntegrationTests/Fixtures/PlatformStackFixture.cs`
- Create: `tests/TicketMaster.IntegrationTests/SpineEndToEndTests.cs`

> Proves: `POST /platform/ping` → outbox row → Debezium → `tm.platform.events` → `PingConsumer` records an inbox row. This is the spine's acceptance test.

- [ ] **Step 1: Create project + packages**

Run:
```bash
dotnet new xunit -n TicketMaster.IntegrationTests -o tests/TicketMaster.IntegrationTests
dotnet sln TicketMaster.slnx add tests/TicketMaster.IntegrationTests
rm tests/TicketMaster.IntegrationTests/UnitTest1.cs
dotnet add tests/TicketMaster.IntegrationTests reference src/Bootstrap/TicketMaster.Api
dotnet add tests/TicketMaster.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/TicketMaster.IntegrationTests package Testcontainers.PostgreSql
dotnet add tests/TicketMaster.IntegrationTests package Testcontainers.Kafka
dotnet add tests/TicketMaster.IntegrationTests package FluentAssertions
dotnet add tests/TicketMaster.IntegrationTests package Npgsql
```

> Note: Debezium/Kafka-Connect has no first-class Testcontainers module. The fixture starts
> Postgres + Kafka via Testcontainers and runs **Kafka Connect** as a raw container
> (`debezium/connect:latest`) using `Testcontainers` generic `ContainerBuilder`, registering the
> connector over its REST API. Tables are created by applying the module's EF migrations at
> startup (Step 3 of Task 11 adds the migration; this test depends on Task 11 being done).

- [ ] **Step 2: Stack fixture**

`Fixtures/PlatformStackFixture.cs`:
```csharp
using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;

namespace TicketMaster.IntegrationTests.Fixtures;

public sealed class PlatformStackFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:latest")
        .WithCommand("-c", "wal_level=logical")
        .WithDatabase("ticketmaster").WithUsername("tm").WithPassword("tm")
        .Build();

    public KafkaContainer Kafka { get; } = new KafkaBuilder()
        .WithImage("confluentinc/cp-kafka:latest")
        .Build();

    private IContainer _connect = default!;

    public string PostgresConnectionString => Postgres.GetConnectionString();
    public string KafkaBootstrap => Kafka.GetBootstrapAddress().Replace("PLAINTEXT://", "");

    public async Task InitializeAsync()
    {
        await Postgres.StartAsync();
        await Kafka.StartAsync();

        _connect = new ContainerBuilder()
            .WithImage("debezium/connect:latest")
            .WithEnvironment("BOOTSTRAP_SERVERS", $"{Kafka.IpAddress}:9092")
            .WithEnvironment("GROUP_ID", "tm-connect-it")
            .WithEnvironment("CONFIG_STORAGE_TOPIC", "_cfg")
            .WithEnvironment("OFFSET_STORAGE_TOPIC", "_off")
            .WithEnvironment("STATUS_STORAGE_TOPIC", "_sts")
            .WithPortBinding(8083, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8083).ForPath("/")))
            .Build();
        await _connect.StartAsync();
    }

    public async Task RegisterOutboxConnector()
    {
        var port = _connect.GetMappedPublicPort(8083);
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        var config = new
        {
            name = "tm-outbox-connector",
            config = new Dictionary<string, string>
            {
                ["connector.class"] = "io.debezium.connector.postgresql.PostgresConnector",
                ["tasks.max"] = "1",
                ["database.hostname"] = Postgres.IpAddress,
                ["database.port"] = "5432",
                ["database.user"] = "tm",
                ["database.password"] = "tm",
                ["database.dbname"] = "ticketmaster",
                ["topic.prefix"] = "tm",
                ["plugin.name"] = "pgoutput",
                ["schema.include.list"] = "platform",
                ["table.include.list"] = "platform.outbox",
                ["transforms"] = "outbox",
                ["transforms.outbox.type"] = "io.debezium.transforms.outbox.EventRouter",
                ["transforms.outbox.route.by.field"] = "aggregate_type",
                ["transforms.outbox.route.topic.replacement"] = "tm.${routedByValue}.events",
                ["transforms.outbox.table.field.event.id"] = "id",
                ["transforms.outbox.table.field.event.key"] = "aggregate_id",
                ["transforms.outbox.table.field.event.type"] = "event_type",
                ["transforms.outbox.table.field.event.payload"] = "payload",
                ["transforms.outbox.table.fields.additional.placement"] = "event_type:header:eventType,id:header:id",
                ["key.converter"] = "org.apache.kafka.connect.storage.StringConverter",
                ["value.converter"] = "org.apache.kafka.connect.json.JsonConverter",
                ["value.converter.schemas.enable"] = "false"
            }
        };
        var resp = await http.PostAsJsonAsync("/connectors", config);
        resp.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        await _connect.DisposeAsync();
        await Kafka.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}
```

- [ ] **Step 3: The end-to-end test**

`SpineEndToEndTests.cs`:
```csharp
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TicketMaster.IntegrationTests.Fixtures;
using TicketMaster.Modules.Platform;
using Xunit;

namespace TicketMaster.IntegrationTests;

public class SpineEndToEndTests(PlatformStackFixture fixture) : IClassFixture<PlatformStackFixture>
{
    [Fact]
    public async Task Ping_flows_outbox_to_debezium_to_kafka_to_consumer()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Postgres", fixture.PostgresConnectionString);
            b.UseSetting("Kafka:BootstrapServers", fixture.KafkaBootstrap);
            b.UseSetting("Kafka:PlatformTopic", "tm.platform.events");
        });

        // Apply migrations so platform.outbox / platform.inbox exist.
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.MigrateAsync();
        }

        await fixture.RegisterOutboxConnector();

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/platform/ping", new { note = "spine-proof" });
        response.EnsureSuccessStatusCode();

        // The KafkaBackgroundConsumer (hosted) should consume + write an inbox row. Poll up to 60s.
        var consumed = await WaitForInboxRow(fixture.PostgresConnectionString, timeout: TimeSpan.FromSeconds(60));
        consumed.Should().BeTrue("the ping event must reach PingConsumer via Debezium+Kafka");
    }

    private static async Task<bool> WaitForInboxRow(string connString, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM platform.inbox WHERE consumer = 'platform.ping'", conn);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (count > 0) return true;
            await Task.Delay(2000);
        }
        return false;
    }
}
```

- [ ] **Step 4: Run (requires Docker running)**

Run: `dotnet test tests/TicketMaster.IntegrationTests -v minimal`
Expected: PASS (1 passed). First run pulls images — may take several minutes.

- [ ] **Step 5: Commit**

```bash
git add tests/TicketMaster.IntegrationTests
git commit -m "test(integration): outbox->Debezium->Kafka->consumer end-to-end spine"
```

---

## Task 11: Platform EF migration

**Files:**
- Create: `src/Modules/Platform/TicketMaster.Modules.Platform/Migrations/*` (generated)

> Done after Task 6 so the schema is stable. Task 10's test depends on this migration existing.

- [ ] **Step 1: Add EF tooling design package + create the migration**

Run:
```bash
dotnet add src/Modules/Platform/TicketMaster.Modules.Platform package Microsoft.EntityFrameworkCore.Design
dotnet tool install --global dotnet-ef --version 10.*
dotnet ef migrations add InitialPlatform \
  --project src/Modules/Platform/TicketMaster.Modules.Platform \
  --startup-project src/Bootstrap/TicketMaster.Api \
  --context PlatformDbContext
```
Expected: `Done.` and a `Migrations/` folder with `*_InitialPlatform.cs` creating `platform.outbox` + `platform.inbox`.

- [ ] **Step 2: Verify the migration includes both tables in the `platform` schema**

Run: `dotnet build src/Modules/Platform/TicketMaster.Modules.Platform -v minimal`
Expected: Build succeeded. Open the generated migration; confirm `migrationBuilder.CreateTable(name: "outbox", schema: "platform", ...)` and `"inbox"` are present.

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Platform/TicketMaster.Modules.Platform/Migrations
git commit -m "feat(platform): initial EF migration (outbox + inbox)"
```

---

## Task 12: CI workflow

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] **Step 1: Workflow that builds, runs unit + architecture tests (integration tests gated to a Docker runner)**

`.github/workflows/ci.yml`:
```yaml
name: ci
on:
  push: { branches: [main, master] }
  pull_request: {}

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet restore TicketMaster.slnx
      - run: dotnet build TicketMaster.slnx --no-restore -c Release
      - run: dotnet test tests/TicketMaster.UnitTests --no-build -c Release
      - run: dotnet test tests/TicketMaster.ArchitectureTests --no-build -c Release
      - run: dotnet test tests/TicketMaster.IntegrationTests --no-build -c Release
        # ubuntu-latest has Docker preinstalled; Testcontainers uses it.
```

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: build + unit/arch/integration test pipeline"
```

---

## Self-Review

**Spec coverage (Plan 0 scope only):**
- Solution structure + dependency rules (spec §4, §4.1) → Tasks 1–7, 9. ✓
- BuildingBlocks shared kernel (spec §2.3) → Tasks 1–4. ✓
- Sync mediator (spec §2.2) → Task 2. ✓
- Outbox → Debezium → Kafka (spec §5.3) → Tasks 3, 8, 10. ✓
- Inbox idempotency (spec §5.6) → Tasks 3, 6. ✓
- Schema-per-module + no cross-schema FK (spec §4.2) → Task 5 (`platform` schema), Task 11. ✓
- Serilog console (spec §5.7) → Task 7. (Log sidecar batch-persist deferred to Plan 8 — noted.)
- Topic-per-context naming `tm.<ctx>.events` (spec §5.3) → Task 8 connector + Task 6 options. ✓
- Per-aggregate partition key (spec §5.3) → Task 8 `event.key = aggregate_id`. ✓
- Module contexts (Event, Inventory, …) → **out of Plan 0 scope** by design; covered by Plans 1–8.

**Placeholder scan:** No TBD/TODO; every code step shows full code; every run step shows expected output. ✓

**Type consistency:** `OutboxMessage` columns (`Id, AggregateType, AggregateId, EventType, Version, OccurredOn, CorrelationId, CausationId, Payload`) are identical across Task 3 (entity), Task 5 (EF mapping), Task 8 + Task 10 (Debezium field config). `IOutboxWriter.Enqueue` signature matches its single caller in Task 6. `IIntegrationEventHandler.Handle(eventType, messageId, payloadJson, ct)` matches `PingConsumer` (Task 6) and the consumer invocation (Task 4). `KafkaConsumerOptions` properties match producer/registration (Tasks 4, 6). `PlatformDbContext.Schema = "platform"` matches the connector's `schema.include.list`. ✓

**Ordering note:** Task 10 (integration test) depends on Task 11 (migration). Execute Task 11 before running Task 10's Step 4 — both are listed; flagged here so a sequential executor reorders the final run if needed.
