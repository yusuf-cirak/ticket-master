# TicketMaster — Domain & Architecture Design

**Date:** 2026-05-29
**Status:** Approved (brainstorming) — pending implementation plan
**Architectural style:** Modular Monolith, DDD, future microservice-ready

---

## 1. Overview

A TicketMaster / Biletix–style platform: browse and search events, and purchase
tickets under high traffic. Built as a **modular monolith** — one deployable, split
into domain modules that communicate only through contracts.

**Goals:** low-latency search · stable under high traffic · no double-booking ·
loose inter-module coupling · future microservice extraction · high observability ·
dynamic cache management.

**Key decisions made during design:**

| Topic | Decision |
|---|---|
| Domain scope | Full domain map — deepen existing 4 modules + add 7 new contexts |
| Seating model | **Reserved seating only** (SeatMap → Section → Row → Seat) |
| Pricing | **Fixed price** per seat/section (no tiers, no dynamic pricing) |
| Booking/Inventory/Ordering split | **Approach A** — thin Booking, Inventory owns seat truth, Ordering = saga |
| Waiting Room | **Dynamic gate**, off by default; activates per-event past a concurrency threshold |
| Active-reservation slot | Counted from **seat-hold** moment |
| Integration events | **Kafka from day one** — transactional Outbox → **Debezium** (CDC) → Kafka |
| Observability | Serilog → console (JSON) + **log sidecar** batch-persisting every 5s; OTel deferred |

---

## 2. Bounded Contexts

11 contexts total.

| Context | Type | Responsibility |
|---|---|---|
| **Ticket Inventory** | **Core** | Seat truth, status lifecycle, concurrency, double-booking prevention |
| **Booking** | **Core** | Short-lived seat-hold reservation session |
| Event | Supporting | Event / Venue / Performer catalog; seat-map *template* |
| Ordering | Supporting | Checkout saga orchestration |
| Payment | Supporting | Provider abstraction (Stripe/PayPal), retries |
| Refund & Cancellation | Supporting | Reverse-flow saga (refund + release + cancel) |
| Ticket Delivery | Supporting | QR/e-ticket generation + gate validation |
| Waiting Room | Supporting | Conditional admission control (semaphore + overflow queue) |
| Search | Generic | Read-optimized projection (Elasticsearch) |
| Identity & Access | Generic | Customer registration, auth, roles |
| Notification | Generic | Email / SMS / push (event subscriber) |

### 2.1 Context Map

```text
Identity ──(published CustomerId)──► everyone                 [upstream]

Waiting Room ──(admission token gate)──► Booking              [conditional upstream gate]

Event ──(Customer/Supplier)──► Ticket Inventory               [seat map needs event]
Event ──(Kafka projection)──► Search                          [Search conformist]

Booking ──(hold seats, sync mediator)──► Ticket Inventory     [partnership]
Ordering ──(saga: orchestrates)──► Booking, Inventory, Payment [Ordering = customer of each]
Payment ──(ACL)──► Stripe                            [anti-corruption layer]
Refund ──(reverse saga)──► Payment, Inventory, Ordering

Notification ◄──(subscribes Kafka events)── ALL               [open-host / published events]
Ticket Delivery ◄──(OrderPaid)── Ordering                     [downstream]
Search ◄──(SeatSold / EventPublished)── Inventory, Event       [downstream projection]
```

### 2.2 Integration Mechanisms

- **Sync request/response** — needs an immediate answer (e.g. `EventExistsQuery`):
  in-process **MediatR 12.5.0** (last open-source release) — `IRequest`/`IRequestHandler`,
  pipeline behaviors for validation/logging/tx. Stays in-process even after a microservice
  split would swap it for an RPC call.
- **Async integration events** — fire-and-forget pub/sub (`SeatSold`, `OrderPaid`…):
  transactional **Outbox** → **Debezium** (CDC reads outbox tables) → **Kafka** → subscribers.
- **Search** — Kafka-consumer projection into Elasticsearch (eventual consistency).
- **Anti-corruption** — Payment wraps Stripe/PayPal; Search wraps Elasticsearch.

### 2.3 Shared Kernel & Published Language

- **BuildingBlocks** (shared kernel): `AggregateRoot`, `Entity`, `ValueObject`,
  `IDomainEvent`, `StronglyTypedId`. Functional result/error handling uses the
  **YC.Monad** package (`Result`, `Result<T>`, `Option<T>`, `Error`) — not hand-rolled;
  `YC.Monad.EntityFrameworkCore` is available for EF-layer result integration.
- **Contracts** (published language, one project per module): cross-module
  `IRequest` definitions + integration-event DTOs. Pure records, no logic.
- **Rule:** no module references another module's internals — only its `*.Contracts`.

---

## 3. Tactical DDD

### 3.1 Ticket Inventory (Core)

Transactional unit = **per-seat-per-event** (avoids locking a whole event).

- **SeatInventory** *(aggregate root)*
  `Id, EventId, SeatId, Status{Available|Held|Sold}, Hold?, Price(Money), Version`
  - VO **Hold** `{ CustomerId, ExpiresAt }`
  - VO **SeatLocation** `{ Section, Row, Number }`
  - VO **Money** (fixed price)
  - **Invariants:**
    - `Available → Held` sets hold + TTL
    - `Held → Sold` only by hold owner, before expiry
    - `Held → Available` on release or expiry
    - `Sold` is terminal until a refund releases it
  - **Concurrency:** Redis distributed lock (fast gate) + DB row lock + optimistic
    `Version` on the conditional sell update.
  - **Events:** `SeatHeld, SeatHoldExpired, SeatHoldReleased, SeatSold, SeatReleased`
- Availability counts are a read projection (feed Search + cache).

### 3.2 Booking (Core) — thin session

- **ReservationSession** *(aggregate root)*
  `Id, CustomerId, EventId, RequestedSeatIds[], Status{Active|SeatsHeld|Confirmed|Expired|Cancelled}, ExpiresAt, AdmissionToken?`
  - **Invariants:** confirm only if all requested seats are held in Inventory;
    expiry releases all holds; (optional) one active session per customer per event.
  - **Events:** `ReservationStarted, SeatsHeld, ReservationConfirmed, ReservationExpired, ReservationCancelled`
  - Delegates actual seat holds to Inventory; owns only session lifecycle + TTL.

### 3.3 Ordering — saga owner

- **Order** *(aggregate root)*
  `Id, CustomerId, EventId, Lines[], TotalAmount, Status{Pending|AwaitingPayment|Paid|Failed|Cancelled}, PaymentId?, ReservationSessionId`
  - Entity **OrderLine** `{ SeatId, SeatLocation, Price }`
  - **Invariant:** `TotalAmount = Σ Lines.Price`; cannot pay an empty order;
    linear status transitions.
  - **Events:** `OrderPlaced, OrderAwaitingPayment, OrderPaid, OrderFailed, OrderCancelled`

### 3.4 Payment

- **Payment** *(aggregate root)*
  `Id, OrderId, Amount(Money), Provider{Stripe|PayPal}, Status{Initiated|Authorized|Captured|Failed|Refunded}, ProviderRef, Attempts`
  - Port `IPaymentProvider` + adapters = ACL; retry policy on transient failures.
  - **Events:** `PaymentInitiated, PaymentSucceeded, PaymentFailed, PaymentRefunded`

### 3.5 Refund & Cancellation — reverse saga

- **RefundRequest** *(aggregate root)*
  `Id, OrderId, CustomerId, SeatIds[], Reason, Amount, Status{Requested|Approved|Processing|Completed|Rejected}`
  - VO **RefundPolicy** `{ CutoffBeforeEvent, RefundPercentage }`
  - **Saga:** `RefundRequested → policy check → Payment.Refund → Inventory.Release → Order.Cancel → RefundCompleted`
  - **Events:** `RefundRequested, RefundApproved, RefundCompleted, RefundRejected`

### 3.6 Event — catalog

- **Event** *(aggregate root)*
  `Id, Name, Category, StartDate, EndDate, VenueId, PerformerIds[], Status{Draft|Published|Cancelled|Completed}, SalesWindow`
- **Venue** *(aggregate root)*
  `Id, Name, Address, SeatMap(Sections → Rows → Seats layout)` — seat-map **template**
  lives here; Inventory references it by id.
- **Performer** *(aggregate root)* `Id, Name, Genre`
- **Events:** `EventPublished, EventCancelled, EventRescheduled, SalesOpened`

### 3.7 Ticket Delivery

- **Ticket** *(aggregate root)*
  `Id, OrderId, SeatId, EventId, CustomerId, QrToken(secure), Status{Issued|Delivered|Validated|Void}, Channel`
  - Generated on `OrderPaid`.
  - **Invariant:** validates **exactly once** at the gate.
  - **Events:** `TicketGenerated, TicketDelivered, TicketValidated, TicketVoided`

### 3.8 Waiting Room — dynamic admission control

**Off by default.** Activates per-event only when in-flight reservations cross a
threshold. Below threshold, reservation starts immediately with no queue.

- **AdmissionControl** (per EventId)
  `Threshold (default ~100, configurable per event), ActiveReservationCount, Queue(Redis sorted set, score = enqueue ts)`
  - Op `TryEnter(customerId, eventId)`:
    - `ActiveCount < Threshold` → **admit immediately**, `INCR`, issue AdmissionToken
    - `ActiveCount ≥ Threshold` → **enqueue** → return `Queued(position, etaEstimate)`
  - Op `Release(customerId, eventId)` → `DECR` → if queue non-empty, admit next (issue token)
  - VO **AdmissionToken** `{ TokenId, CustomerId, EventId, ExpiresAt }`
- **Slot semantics:** slot is taken at the **seat-hold** moment (ReservationSession
  becomes Active/SeatsHeld) and released on `ReservationConfirmed | ReservationExpired
  | ReservationCancelled`. Counts the people actively holding/paying.
- It is a **distributed semaphore (limit N) with an overflow queue** — transparent
  under normal load, buffering only past the threshold.
- **Booking ↔ Waiting Room contract:** Booking calls `TryEnter` before creating a
  session, and must `Release` on every terminal session state. TTL auto-release is a
  safety net so a crashed client cannot leak a slot.

### 3.9 Generic contexts (thin)

- **Identity & Access** — `Customer { Id, Email, Profile, Status }` + auth/roles
  (ASP.NET Identity). Publishes `CustomerId`. Event: `CustomerRegistered`.
- **Search** — no aggregates; `EventSearchDocument` (denormalized event + venue +
  performer + city + date + availability) in Elasticsearch, fed by Kafka projection.
- **Notification** — downstream only; subscribes `OrderPaid` (receipt),
  `ReservationConfirmed`, `RefundCompleted`, `EventRescheduled` (reminder). Channels:
  email / SMS / push + delivery status.

---

## 4. Solution Structure

```text
src/
├── Bootstrap/
│   └── TicketMaster.Api                    # ASP.NET Core host = composition root, wires all modules
├── Gateway/
│   └── TicketMaster.Gateway                # Ocelot (separate deployable ingress)
│
├── BuildingBlocks/
│   ├── ...BuildingBlocks.Domain            # AggregateRoot, Entity, ValueObject, IDomainEvent, Result<T>, StronglyTypedId
│   ├── ...BuildingBlocks.Application       # mediator abstractions, pipeline behaviors (validation, logging, tx, idempotency)
│   └── ...BuildingBlocks.Infrastructure    # EF base, Outbox, Kafka, Redis, log/metrics plumbing
│
├── Contracts/                              # PUBLISHED LANGUAGE — one per module, pure records, no logic
│   ├── ...Modules.Event.Contracts
│   ├── ...Modules.Inventory.Contracts
│   └── ...(one per module)
│
└── Modules/
    ├── Event/          { .Domain  .Application  .Infrastructure  (.entry) }
    ├── Inventory/      { same 4 }
    ├── Booking/        { same 4 }
    ├── Ordering/       { same 4 }
    ├── Payment/        { same 4 }
    ├── Refund/         { same 4 }
    ├── Search/         { same 4 }
    ├── Identity/       { same 4 }
    ├── Notification/   { same 4 }
    ├── WaitingRoom/    { same 4 }
    └── TicketDelivery/ { same 4 }
```

### 4.1 Dependency Rules (enforced by NetArchTest in CI)

| Project | May reference | Must NOT reference |
|---|---|---|
| `Module.Domain` | BuildingBlocks.Domain | EF, Redis, Kafka, any other module, any Contracts |
| `Module.Application` | own Domain, BuildingBlocks.Application, **any `*.Contracts`** | another module's Domain/Application/Infra |
| `Module.Infrastructure` | own Application+Domain, BuildingBlocks.Infrastructure | another module internals |
| `Module` (entry) | own 3 layers | other module entries |
| `Module.Contracts` | BuildingBlocks shared primitives only | anything with logic |
| `Api` host | all module entries | — |

**Cross-module call path:** `Booking.Application` → `Event.Contracts.EventExistsQuery`
→ in-process mediator → `Event.Application` handler. No internal reference ever
crosses a module line — only Contracts.

### 4.2 Data Isolation

- Single PostgreSQL, **schema-per-module** (`event.*`, `booking.*`, `inventory.*`, …).
- One DbContext + migration set per module.
- **No cross-schema foreign keys** — references by Id only (keeps a future DB split clean).
- PostgreSQL = transactional source of truth; Elasticsearch = read projection only.

---

## 5. Cross-Cutting Concerns

### 5.1 Seat Lock / Double-Booking — defense in depth

1. **Redis distributed lock** `lock:event:{eventId}:seat:{seatId}`, TTL ~10s — fast
   gate against concurrent hold attempts.
2. **DB hold**: `SeatInventory.Status Available → Held` with optimistic `Version`.
3. **Conditional sell guard** at confirm (DB is the authority, not Redis):
   ```sql
   UPDATE inventory.seat_inventory
   SET status = 'Sold', version = version + 1
   WHERE id = @id AND status = 'Held' AND hold_customer_id = @cust AND version = @ver;
   ```
   0 rows affected → conflict → fail + compensate.
4. **Hold expiry sweeper**: background job releases expired holds → `SeatHoldExpired`
   → Waiting Room slot release.

> Redis is the fast path; the **DB conditional update is the source of truth** — Redis
> must never be the authority for a paid seat.

### 5.2 Ordering Saga (orchestration)

```text
OrderPlaced
  → Payment.Initiate ──fail/timeout──► compensate: Inventory.ReleaseHolds → OrderFailed
  → PaymentSucceeded
  → Inventory.ConfirmSale ──fail──► compensate: Payment.Refund → OrderFailed
  → OrderPaid ──(Kafka)──► Ticket Delivery + Notification + Search availability
```

Saga state persisted in Ordering. Every step idempotent; compensations explicit.

### 5.3 Kafka + Outbox + Debezium

- **Transactional Outbox** per module: the domain event and the aggregate state are
  written in the **same DB transaction** to an `outbox` table in the module's schema
  (`event.outbox`, `inventory.outbox`, …). No dual-write problem.
- **Debezium** runs as a Kafka Connect connector tailing the PostgreSQL WAL. It reads
  rows inserted into the outbox tables and publishes them to Kafka. The **Outbox Event
  Router** SMT maps each row to its topic + partition key from the row columns — the
  application never talks to Kafka directly.
- **Topic per publishing context:** `tm.event.events`, `tm.inventory.events`,
  `tm.ordering.events`, `tm.payment.events`, `tm.refund.events`, `tm.identity.events`
  (set via the router's `route.topic` from an `aggregate_type` / topic column).
- **Partition key = aggregate id** (`eventId` for inventory, `orderId` for ordering) →
  per-aggregate ordering guarantee.
- Delivery is **at-least-once** (Debezium + Kafka); consumers are **idempotent** via an
  **Inbox** table (dedup on `messageId`).
- **Outbox row / envelope columns:** `id (messageId), aggregate_type, aggregate_id,
  event_type, version, occurred_at, correlation_id, causation_id, payload (jsonb)`.

```text
Module → (same tx) → schema.outbox table → Debezium (WAL CDC + Outbox Router) → Kafka topic → consumers
```

### 5.4 Search Synchronization

Kafka-consumer projection: Search subscribes `tm.event.events` + `tm.inventory.events`
(the same Debezium-fed topics from §5.3) and projects into Elasticsearch. Search does
**not** run its own CDC — it consumes the domain events Debezium already published from
the outbox. Eventually consistent.

### 5.5 Cache Sidecar

- Gateway-level response cache + a **dynamic policy store** (`{ route, ttl }` in Redis,
  editable at runtime). Business modules are cache-unaware.
- Invalidation: the sidecar subscribes to Kafka events (`EventUpdated`, `SeatSold` →
  bust availability cache).

```text
Application → Cache Sidecar → Redis
```

### 5.6 Idempotency

- Inbox table per consumer (dedup `messageId`).
- Payment requests carry an idempotency key.
- Saga steps safe to replay.

### 5.7 Observability

- **Serilog** structured logging → **console sink** (JSON / stdout).
- **Log sidecar** collects the console stream → buffers in memory → **batch-persists
  every 5 seconds** to the log store. The app never blocks on log I/O.
- `correlationId` flows Gateway → modules → Kafka envelope → consumers, and is on
  every log entry for cross-batch trace stitching.
- Metrics emitted as structured log events through the same pipeline: booking success
  rate, lock contention, hold-expiry rate, payment failures, cache hit ratio,
  **waiting-room queue depth + admission rate**.
- **OpenTelemetry distributed tracing — deferred** (out of v1 scope).

```text
Module → Serilog → console(JSON) → Log Sidecar → buffer → flush every 5s → log store
```

---

## 6. Consistency Strategy

- **Search:** availability-first; accepts eventual consistency.
- **Booking / Inventory:** consistency-first; double-booking is unacceptable — enforced
  by the DB conditional sell guard (§5.1).

---

## 7. Scalability

- Application is **stateless** (session/lock state in Redis, truth in Postgres) →
  horizontal scaling, Kubernetes, easy failover.
- Read/write separation: Postgres write path, Elasticsearch read path.

---

## 8. Future Evolution

Already in place for a microservice split: separated Contracts, defined module
boundaries, Kafka async messaging, schema-per-module data isolation.

**Possible later additions:** CQRS read models, event sourcing for Inventory,
read replicas, sharding, multi-region, OpenTelemetry tracing, dynamic pricing
(would introduce a Pricing context), Promotion/Discount context, Analytics/Reporting.

---

## 9. Out of Scope (v1)

- Dynamic pricing, price tiers, promotions/discounts.
- General-admission (capacity-only) seating.
- Analytics & Reporting context.
- Reviews/ratings.
- OpenTelemetry distributed tracing.
