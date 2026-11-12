# ADR-0005: Stay on OSS messaging libraries — Mediator (source-generated) and pinned MassTransit 8

**Status:** Accepted (amended: MediatR replaced by Mediator)

## Context
MediatR and MassTransit moved to commercial licensing in later versions. This project depends on in-process messaging
(request/response + pipeline behaviors) and Kafka consumption (MassTransit rider), and we want to stay on OSS terms.

## Decision
- In-process messaging: **`Mediator` 3.0.2** (martinothamar, Apache-2.0; `Mediator.Abstractions` +
  `Mediator.SourceGenerator`). It replaced the previously pinned **MediatR 12.5.0** (last OSS release) in
  `4ba157e refactor(messaging): migrate MediatR 12.4.1 -> Mediator (source-generated) 3.0.2`. Source-generated dispatch,
  no runtime reflection; `AddMediator()` is generated only in the outermost project (`TicketMaster.AppHost`), where the
  pipeline behaviors are listed in declared order.
- Kafka: pin **MassTransit 8.5.x** (v8 rider, the last OSS v8). Kafka runs through the MassTransit rider rather than raw
  `Confluent.Kafka`, keeping consumer identity/headers native.

Vulnerability advisories are addressed by overriding transitive versions (see `Directory.Packages.props`), and CI fails
on any High/Critical `dotnet list package --vulnerable` result.

## Consequences
- No forced upgrade onto a commercial license; version bumps are deliberate.
- If a security advisory ever lands on a pinned version with no OSS fix, the choice is revisited here.
