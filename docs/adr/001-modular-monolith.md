# ADR 001: Modular monolith over microservices

**Status:** accepted · **Date:** 2026-11-05

## Context

The product has five clear bounded contexts (identity, tenancy, listings, scheduling, notifications) plus two
delivery interfaces (REST for the web app, MCP for AI assistants). A real estate brokerage SaaS at this stage has
one team and one deployment target. The hard parts are domain correctness (time zones, double booking, tenant
isolation), not independent scaling of services.

## Decision

One ASP.NET Core host, with each module as its own class library:

```
src/Api                 host: auth wiring, ProblemDetails, OpenAPI, composition root
src/Modules/Identity    users, JWT, personal access tokens, admin agent management
src/Modules/Tenancy     tenants (brokerages)
src/Modules/Listings    listings, showing rules, geospatial search, photo storage
src/Modules/Scheduling  slot generation, matching, holds, booking lifecycle, change feed
src/Modules/Notifications  domain event handlers that write the outbox; sent email log
src/Modules/Ai          availability parsing (Claude or rule based)
src/Modules/Mcp         MCP server over the Scheduling/Listings application services
src/BuildingBlocks      tenant context, Result type, domain event dispatcher, outbox, audit, Mongo base repository
src/Worker              outbox relay and delivery (separate process)
```

Rules that keep it modular rather than a big ball of mud:

* A module exposes an `Application` service and `Contracts` (DTOs, events). Other modules reference those, never
  `Domain` or `Infrastructure` types. Repositories are `internal` or only consumed inside their module.
* Cross module reactions go through in process **domain events** dispatched by a 30 line dispatcher in
  BuildingBlocks (no MediatR, whose licence changed). Handlers run in the caller's scope and transaction, which is
  what makes the outbox write atomic with the booking.
* The MCP module calls `BookingService` and `AvailabilityService` directly, not HTTP. One domain layer, two
  interfaces, the same tenant and role checks.
* Each module owns its collections, indexes and `$jsonSchema` validators (`IMongoSchemaSetup`), applied at startup.

## Consequences

* One process to run, test and deploy; integration tests boot the whole thing with `WebApplicationFactory` and a
  Testcontainers MongoDB in a couple of seconds.
* Refactoring across modules is a compile error away, not a contract negotiation.
* If Scheduling ever needs to scale separately, its boundary is already a project reference and a handful of
  events; extracting it means replacing the in process dispatcher with a broker for those events.
* The trade off: all modules share one database and one deployment cadence. For this product that is a feature.
