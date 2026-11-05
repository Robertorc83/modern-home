# API contract (v1)

The ASP.NET Core API listens on `http://localhost:5080` (in Docker: `http://api:8080`).
Every error is RFC 7807 ProblemDetails: `{ type, title, status, detail, errors? }`.
All instants are ISO 8601 UTC strings (`2026-11-07T16:00:00Z`): `slotStart`, `slotStartUtc`, `startUtc`, `expiresAt`.
Local times are ISO local strings without offset (`2026-11-07T09:00`) and always travel with an IANA zone: the
buyer's `ranges` on `/match` and the `localStart`/`localEnd` fields on slots. `slotStart` on holds and bookings is
the `startUtc` of a slot returned by `/match` or `/listings/{id}/slots`; the optional `timeZone` only chooses the
zone used for the labels in a 409's `alternatives`.

Auth: `Authorization: Bearer <jwt>` (web, via the Next BFF) or `Bearer hss_pat_...` (MCP).
JWT claims: `sub` (user id), `tenantId`, `role` (`BrokerageAdmin` | `Agent` | `Buyer`), `name`, `email`.

## Identity

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `/auth/login` | `{ email, password, tenantSlug? }` | `{ token, user }`. The same email may exist in several brokerages: when the password matches more than one and no `tenantSlug` is given, 409 ProblemDetails `"That email exists in more than one brokerage; pass tenantSlug."` with `tenants: [{ slug, name }]`; repeat with `tenantSlug`. Rate limited per client IP (60/min) and per email (10/min) → 429 |
| POST | `/auth/register-buyer` | `{ tenantSlug, email, password, name }` | `{ token, user }` (409 if email exists) |
| GET | `/auth/me` | | `user` |
| GET | `/tenants` | anonymous | `[{ slug, name, defaultTimeZone }]` |
| GET | `/auth/tokens` | | `[{ id, name, prefix, createdAt, lastUsedAt, revokedAt }]` |
| POST | `/auth/tokens` | `{ name }` | `{ id, name, token }` (the raw token is shown once) |
| DELETE | `/auth/tokens/{id}` | | 204 (revoke) |
| GET | `/admin/agents` | BrokerageAdmin (JWT only; personal access tokens get 403) | `[{ id, name, email, timeZone, listingCount }]` |
| POST | `/admin/agents` | BrokerageAdmin `{ name, email, password }` | `user` |

`user` = `{ id, email, name, role, tenantId, tenantSlug, tenantName, timeZone }`

## Listings

`listing` =
```json
{
  "id": "...", "ownerUserId": "...", "ownerName": "Alex Agent", "status": "Active",
  "address": "1420 Larimer St #5B", "city": "Denver", "neighborhood": "LoDo",
  "price": 585000, "beds": 2, "baths": 2, "propertyType": "Condo",
  "attributes": { "hoaMonthly": 450, "floor": 5 },
  "description": "...", "photos": ["https://..."],
  "timeZone": "America/Denver",
  "location": { "lat": 39.7508, "lng": -104.9997 },
  "showingRules": {
    "slotMinutes": 30, "bufferMinutes": 15, "leadTimeHours": 12, "horizonDays": 14,
    "autoConfirm": false,
    "weekly": [{ "dayOfWeek": "Saturday", "start": "09:00", "end": "12:00" }],
    "blackoutDates": ["2026-11-26"]
  }
}
```
`propertyType`: `House` | `Condo` | `Townhouse`. `status`: `Active` | `Draft` | `Off`.
`dayOfWeek`: `Monday`..`Sunday`.

| Method | Path | Notes |
|---|---|---|
| GET | `/listings?mine=true&city=&maxPrice=&minBeds=&propertyType=&near=&nearLat=&nearLng=&radiusKm=` | Agent with `mine=true` gets own (otherwise Active or own); Buyer sees Active only; Admin sees all. `radiusKm` 0 < r ≤ 500, lat/lng in range, else 400. Listings carry a `version` (optimistic concurrency: send it back on PUT; a stale version → 409) |
| GET | `/listings/{id}` | 404 when not found or another tenant |
| POST | `/listings` | Agent. Body = listing without id/owner. Returns listing |
| PUT | `/listings/{id}` | Agent owner (or Admin). Body = listing fields |
| PUT | `/listings/{id}/showing-rules` | Agent owner. Body = showingRules |
| GET | `/listings/{id}/slots?from=&to=&timeZone=` | open slots: `{ listingId, timeZone, viewerTimeZone, days: [ dayGroup ] }` |
| GET | `/listings/{id}/showings` | Agent owner / Admin: showings on the listing |
| POST | `/listings/{id}/photos/upload-url` | Agent owner `{ contentType }` → `{ uploadUrl, key, displayUrl, expiresAt }` (presigned S3 PUT; 422 when AWS is off) |

## Matching

`POST /match`
```json
{
  "timeZone": "America/Toronto",
  "ranges": [{ "start": "2026-11-07T09:00", "end": "2026-11-07T12:00" }],
  "city": null, "maxPrice": 600000, "minBeds": null, "propertyType": null,
  "near": "Denver" ,   // or null; place name OR use nearLat/nearLng
  "nearLat": null, "nearLng": null, "radiusKm": 25
}
```
Response:
```json
{
  "timeZone": "America/Toronto",
  "results": [{
    "listing": { /* listing */ },
    "distanceKm": 1.2,              // null when no near given
    "matchCount": 5,
    "days": [ dayGroup ]
  }],
  "suggestions": [ { "listing": {...}, "slots": [ slot ] } ]   // only when results is empty: nearest open slots
}
```
`dayGroup` = `{ "date": "2026-11-07", "label": "Sat, Nov 7", "slots": [ slot ] }` (grouped in the viewer's zone)
`slot` = `{ "startUtc": "...Z", "endUtc": "...Z", "localStart": "2026-11-07T11:00", "localEnd": "...", "listingLocalStart": "2026-11-07T09:00", "label": "11:00 AM", "listingLabel": "9:00 AM MST" }`

## Scheduling

`showing` = `{ id, listingId, listingAddress, listingCity, listingTimeZone, buyerId, buyerName, agentId, agentName, slotStartUtc, slotEndUtc, status, createdAt, updatedAt, listingLocalLabel: "Sat, Nov 7, 9:00 AM MST" }`
`status`: `Requested` | `Confirmed` | `Declined` | `Cancelled`

| Method | Path | Notes |
|---|---|---|
| POST | `/showings/holds` | Buyer `{ listingId, slotStart, timeZone? }` → `{ id, listingId, slotStartUtc, expiresAt }` (5 minutes, TTL). One live hold per buyer: a new hold releases the buyer's previous one; holding the same slot again extends it. 409 (with `alternatives`) if held or booked by someone else; 422 if not an open slot |
| DELETE | `/showings/holds/{id}` | Buyer releases own hold. 204 |
| POST | `/showings` | Buyer `{ listingId, slotStart, timeZone? }` → `showing` (201). 409 ProblemDetails with `alternatives: [slot]` (root level, labels in `timeZone`) when taken or overlapping. 422 when not an open slot |
| POST | `/showings/{id}/confirm` | Agent owner |
| POST | `/showings/{id}/decline` | Agent owner |
| POST | `/showings/{id}/cancel` | Buyer owner (or agent) |
| GET | `/showings/mine` | Buyer |
| GET | `/showings/inbox?status=` | Agent: own listings. Admin: all in tenant |
| GET | `/showings/stream` | `text/event-stream`. Events: `event: showing` `data: { showingId, status, operation }`. Supports `Last-Event-ID` (resume token) |

## AI

`POST /ai/parse-availability` `{ text, timeZone, now? }` →
```json
{
  "provider": "rule-based" ,   // or "claude-haiku-5-5"
  "windows": [{ "days": ["Monday","Tuesday"], "start": "18:00", "end": "21:00" }],
  "ranges": [{ "start": "2026-11-09T18:00", "end": "2026-11-09T21:00" }],
  "summary": "Weekdays except Thursday 6:00 PM–9:00 PM; Saturday 8:00 AM–12:00 PM",
  "warnings": []
}
```
Rate limited per user: 20 parses a minute (shared with the MCP `find_open_slots` tool) → 429 ProblemDetails.

`GET /ai/provider` → `{ provider }`

## Admin / ops

`GET /admin/outbox` → `{ outbox: [...], emails: [...], transport: "ses" | "dev-log", sesInboxUrl }` (recent outbox rows and sent emails; `sesInboxUrl` is LocalStack's `/_aws/ses` when AWS is on). `GET /health` → `{ status: "ok", mongo: "ok" }`.
