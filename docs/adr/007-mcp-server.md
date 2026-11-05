# ADR 007: MCP as a second interface over the same application services

**Status:** accepted · **Date:** 2026-11-05

## Context

Buyers increasingly ask an assistant to "find me a Saturday showing under 600k and book it". An assistant needs
a tool interface, not a web page. The scheduling rules, tenant isolation and double booking guarantee must apply
exactly as they do on the web.

## Decision

An `Mcp` module inside the monolith, using the official C# SDK (`ModelContextProtocol.AspNetCore`) over
**Streamable HTTP at `/mcp`**, stateless. No separate server.

* **Same services, not HTTP.** Tools call `AvailabilityService`, `BookingService`, `ListingService` and the
  availability parser directly. The tenant context is populated from the token in a request filter, exactly as
  the REST middleware does, so a tool cannot act outside the caller's tenant or role.
* **Personal access tokens.** The web app's "Connect an AI assistant" page creates a token (`hss_pat_...`). Only its
  SHA-256 hash is stored, with the owning user; it inherits that user's tenant and role and can be revoked.
  A policy scheme routes `Bearer hss_pat_*` to the PAT handler and everything else to JWT validation. Missing or
  revoked tokens get 401 at the endpoint.
* **Tools filtered by role**, twice: `tools/list` returns only the caller's tools, and `tools/call` rejects anything
  else as an unknown tool. Buyers: `search_listings`, `find_open_slots`, `book_showing`, `list_my_showings`,
  `cancel_showing`. Agents: `list_showing_requests`, `respond_to_request`, `get_listing_schedule`.
* **Annotations tell the client what is safe.** Read tools carry `readOnlyHint`; `book_showing`,
  `cancel_showing` and `respond_to_request` do not, so clients such as Claude Code ask the user before calling
  them. `cancel_showing` is marked destructive.
* **Results are for people and models.** Addresses, prices, local times in the buyer's zone, and the exact
  `slotStart` to pass back. A taken slot is a tool error with the three nearest open slots, not an exception.
* **No Anthropic key on the server.** The MCP client brings the model.

`McpTests` drives the server with the SDK client: a buyer token lists only buyer tools, books, gets a clean error
on the second attempt; a brokerage B token cannot see brokerage A's listings; an agent token confirms a request;
missing and revoked tokens get 401. `docs/mcp-demo.md` is a transcript from a real `claude mcp add` session.

## Consequences

* One place to change a business rule; both interfaces pick it up.
* The booking made from Claude Code appears in the agent's inbox on screen immediately, through the same change
  stream as a web booking.
* A token is as powerful as its user. Revocation is immediate (the handler checks the stored record on each call).
