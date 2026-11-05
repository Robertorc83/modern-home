# ADR 003: Shared collections with a tenantId filter in the repository base

**Status:** accepted · **Date:** 2026-11-05

## Context

A tenant is a real estate brokerage. Brokerages are many and small; their data must never mix, but their schemas
and traffic patterns are identical. Options: a database per tenant, a collection per tenant, or shared collections
with a discriminator.

## Decision

Shared collections. Every document carries `tenantId`, and every read and write goes through
`TenantScopedRepository<T>`, which ANDs `tenantId` from the authenticated `ITenantContext` into the filter and
stamps it on inserts. The tenant id comes from the JWT or personal access token claims, populated by middleware,
never from the request body (a body `tenantId` is ignored; `TenantIsolationTests.Tenant_comes_from_the_token_not_the_body`).

Consequences of the filter being structural rather than per query:

* A handler cannot forget it. There is no "find by id" that skips the tenant.
* A document from another tenant does not match, so the caller sees **404, not 403**. A 403 would confirm the
  document exists. `TenantIsolationTests` covers reads, writes, slots, holds, booking, showing transitions,
  geo search and the admin list.
* The two cross tenant reads in the system happen before a tenant is known (sign in by email, token lookup by
  hash) and live in one class, `CredentialStore`, so they are easy to audit.
* `$geoNear` must be the first pipeline stage, so the tenant filter goes into its `query` option rather than a
  later `$match`; the repository renders its scoped filter for that purpose.
* Change streams are filtered server side on `fullDocument.tenantId` (and the agent or buyer id).

Compound indexes start with `tenantId`, so per tenant queries stay selective as the collection grows.

## Why not a database per tenant

Stronger physical isolation and simpler per tenant backup, at the cost of N connection pools, N sets of indexes
to migrate, N change streams for the live inbox, and an onboarding step that provisions infrastructure. For
brokerages that are small and numerous, shared collections with a structural filter give the same logical
guarantee with far less operational surface. If a large enterprise tenant needed physical isolation, the
repository base is the one place that would learn to pick a database by tenant.
