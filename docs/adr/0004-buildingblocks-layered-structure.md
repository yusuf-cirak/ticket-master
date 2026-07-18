# ADR-0004: Layered building blocks with enforced dependency direction

**Status:** Accepted

## Context
The shared infrastructure had grown to 13 flat projects with a `YC.` prefix — hard to navigate and with no guardrail
against a lower layer accidentally depending on an outer one.

## Decision
Building blocks are named `BuildingBlocks.*` (no vendor prefix) and physically grouped by clean-architecture layer:
`Domain/`, `Application/`, `Infrastructure/`, `Presentation/`. Dependencies only point inward. This is enforced by
`NetArchTest` rules in `DependencyRuleTests`:

- Domain depends on nothing (no EF/Kafka, no Application/Infrastructure).
- Application depends on Domain only (no Infrastructure/Presentation).
- Infrastructure must not depend on Presentation. (Presentation → Infrastructure *is* allowed, e.g. RateLimiting → Caching.)

## Consequences
- A misplaced reference fails CI, not code review.
- Namespaces are folder-independent, so moving a project between layer folders doesn't churn `using`s.
- New blocks slot into a layer and inherit the rule automatically.
