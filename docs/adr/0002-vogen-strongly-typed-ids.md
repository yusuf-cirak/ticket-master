# ADR-0002: Strongly-typed ids with Vogen

**Status:** Accepted

## Context
Primitive `Guid`/`int` ids are interchangeable at compile time — passing a `UserId` where an `EventId` is expected
compiles fine and fails at runtime. We want type-safe ids without reflection cost or hand-rolled boilerplate.

## Decision
Ids and small value objects are **Vogen** source-generated value objects:
`[ValueObject<Guid>(conversions: Conversions.EfCoreValueConverter | Conversions.SystemTextJson)]`. Vogen generates the
EF Core converter and System.Text.Json converter at compile time (reflection-free). Each module's DbContext registers
its converters with `configurationBuilder.RegisterVogenEfCoreConverters(assembly)`.

A hand-rolled `StronglyTypedId`/`ValueObject` base was tried and reverted — Vogen is less code and faster.

## Consequences
- Mixing id types is a compile error.
- `EfCoreValueConverter` generation requires an EF Core reference, so **Domain** value objects (no EF dependency by
  design) use `SystemTextJson` only; ids that need EF conversion live in the module that owns the DbContext.
- In EF projections, select the converted property directly (`e.Id`), not `e.Id.Value` — the latter doesn't translate.
