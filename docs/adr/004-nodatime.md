# ADR 004: NodaTime, and storing UTC plus the IANA zone

**Status:** accepted · **Date:** 2026-11-05

## Context

A seller says "Saturdays 9 to 12". A buyer in Toronto is free "Saturday noon to 3". The listing is in Denver.
Next March the clocks move, on different dates in different countries, and one Sunday in November has two 1:30 a.m.
`DateTime` with its `Kind` flag makes these bugs easy to write and hard to see.

## Decision

* **NodaTime everywhere in the domain.** `Instant` for points in time, `LocalDateTime` and `LocalTime` for what
  people type, `DateTimeZone` from the TZDB provider for conversions. The compiler stops you from adding an
  `Instant` to a `LocalTime`.
* **Store UTC plus the IANA zone.** Showings store `slotStartUtc` / `slotEndUtc` as BSON dates (an `Instant`
  serializer) and the listing's `timeZone` (for example `America/Denver`). Listings store their rules as wall
  clock times plus the zone. Users carry a display zone.
* **Expand rules in local time, convert each slot on its own.** `SlotGenerator` walks each day in the listing's
  zone and maps every local start with `zone.MapLocal`:
  * spring forward: a wall time inside the gap does not exist, so the slot is skipped (never shifted onto an
    hour the seller did not offer);
  * fall back: an ambiguous wall time resolves to the earlier offset, so the repeated hour is offered once;
  * slot length is elapsed time, and the slot must still end inside the window after conversion.
* **Buyer ranges are read leniently** (`InZoneLeniently`): a range starting at 2:30 a.m. on the spring forward
  day starts at 3:30, which is what the person meant.
* **Display converts at the edge.** Every slot view carries the buyer's local label and the listing's local label
  with its abbreviation ("11:00 AM" / "9:00 AM MST"), so a Toronto buyer booking a Denver house sees both.

Tests pin all of this (`SlotGeneratorTests`, `MatcherTests`): the 2027-03-14 transition in Denver, the
2026-11-01 fall back, Toronto buyer against a Denver listing, and day grouping where a 10:30 p.m. Denver slot
lands on the next day in Toronto.

## Consequences

* The web app receives ISO instants and local strings and formats them with `Intl` in an explicit zone; it never
  guesses from the browser.
* The TZDB ships with NodaTime and updates with the package, independent of the host OS.
* `DateTime` appears only at serialization and SDK boundaries.
