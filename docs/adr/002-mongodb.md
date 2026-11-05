# ADR 002: MongoDB, and why it earned its place

**Status:** accepted · **Date:** 2026-11-05

## Context

The listing is a naturally document shaped aggregate: core fields plus per property type attributes (a condo has
an HOA fee and a floor, a house has a lot size) plus the showing rules, which are always read together with the
listing. Scheduling needs geospatial search, live updates and short lived holds. The brief's stack names MongoDB.
Rather than use it as a generic store, the design leans on features relational databases do not have, and is
honest about the one place a relational database would be better.

## Decision

Use MongoDB 8 as a single node replica set (transactions and change streams require a replica set), through the
official driver, behind tenant scoped repositories.

### What MongoDB gives this domain

| Need | MongoDB feature | Where |
|---|---|---|
| Listings with per type attributes and embedded rules | Documents; `$jsonSchema` validator keeps core fields typed while `attributes` stays open | `ListingSchema` |
| "Homes near LoDo, sorted by distance" | `2dsphere` index + `$geoNear` (with the tenant filter inside `query`) | `ListingRepository.SearchAsync` |
| Live agent inbox and buyer "My showings" | Change stream on `showings`, filtered server side by tenant and user, resume token as the SSE event id | `ShowingChangeFeed` |
| Slot held while a buyer confirms, released automatically | `slotHolds` with a TTL index on `expiresAt` and a unique index on `(tenantId, activeSlotKey)` | `SchedulingSchema` |
| Email queued if and only if the booking committed | Multi document transaction: showing + outbox row + audit entry | `BookingService`, `MongoUnitOfWork` |
| **No double booking** | Slot discretization + unique partial index (below) | `SchedulingSchema`, `BookingService` |

### No double booking, guaranteed by the database

Slots snap to a fixed grid derived from the listing's `slotMinutes` and `bufferMinutes`, so two buyers asking for
"the same slot" produce the same key. A showing that holds its slot (Requested or Confirmed) carries
`activeSlotKey = "{listingId}:{slotStartUtc}"`. The field is unset on Declined or Cancelled.

```
db.showings.createIndex({ tenantId: 1, activeSlotKey: 1 },
  { unique: true, partialFilterExpression: { activeSlotKey: { $exists: true } } })
```

Booking is an insert; the index decides who wins. A duplicate key error maps to HTTP 409 (with the three nearest
open slots in the ProblemDetails). No read then write check, so 20 concurrent requests for one slot produce
exactly one success (`BookingTests.Twenty_parallel_bookings...`). The `$jsonSchema` validator additionally rejects
an active showing without a key, or an inactive one with a key.

The key alone only stops two bookings with the *same start*. If an agent changes the slot length from 30 to 45
minutes, the new grid contains a start that straddles an existing 30 minute booking under a different key.
So the booking transaction also inserts one **claim per 5 minute bucket** the showing covers into `slotClaims`,
unique on `(tenantId, listingId, bucketStartUtc)`. Overlap is then impossible under any grid, still decided by
the database, and still one insert path (`OverlapTests.Changing_the_slot_length_cannot_create_an_overlapping_booking`).
Decline and cancel delete the claims with the key.

## Trade off

Discrete slots make the uniqueness constraint possible. If the product ever needed free form durations (a buyer
asks for 9:10 to 9:50), overlapping ranges cannot be expressed as a unique index. The right tool for that is
PostgreSQL with a range type and an exclusion constraint (`EXCLUDE USING gist (listing_id WITH =, slot WITH &&)`).
Persistence is behind repositories and the booking rule lives in one method, so that change stays contained to
the Scheduling module's infrastructure.

Document validation is `strict`, which means seed and migration code must keep up with the schema. That is the
point: the schema is flexible on purpose, not loose.
