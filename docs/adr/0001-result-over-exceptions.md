# ADR-0001: Result/Option over exceptions for expected failures

**Status:** Accepted

## Context
Validation failures, not-found, and business-rule violations are *expected* outcomes, not exceptional ones. Modeling
them as thrown exceptions hides them from the type system, couples control flow to stack unwinding, and makes the HTTP
error mapping ad hoc.

## Decision
Application/domain code returns `Result<T>` and `Option<T>` from **YC.Monad** (owned by the team). A handler that can
fail returns `Result<T>`; an absent value is `Option<T>.None`, never a null or empty-string sentinel. `Error.Create(code,
message, status)` carries an HTTP status so the Web layer maps failures to RFC9457 problem responses via `ToApiResult()`.
Exceptions are reserved for genuinely unexpected faults (caught by the global handler).

## Consequences
- Failure paths are visible in signatures and must be handled — no silent swallow.
- One mapping point (`ResultEndpointExtensions`) produces consistent RFC9457 errors.
- `ICurrentUser` exposes `Option<string>` so "no authenticated user" is a first-class state.
