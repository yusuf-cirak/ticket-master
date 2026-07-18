# ADR-0005: Pin the last open-source releases of MediatR and MassTransit

**Status:** Accepted

## Context
MediatR and MassTransit moved to commercial licensing in later versions. This project depends on both for in-process
messaging (MediatR pipeline) and Kafka consumption (MassTransit rider), and we want to stay on OSS terms.

## Decision
Pin to the last open-source releases: **MediatR 12.5.0** and **MassTransit 8.5.x** (v8 rider, the last OSS v8). Kafka
runs through the MassTransit rider rather than raw `Confluent.Kafka`, keeping consumer identity/headers native.
Vulnerability advisories are addressed by overriding transitive versions (see `Directory.Packages.props`), and CI fails
on any High/Critical `dotnet list package --vulnerable` result.

## Consequences
- No forced upgrade onto a commercial license; version bumps are deliberate.
- If a security advisory ever lands on a pinned version with no OSS fix, the choice is revisited here.
