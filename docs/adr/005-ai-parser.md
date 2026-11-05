# ADR 005: AI behind an interface, a deterministic fallback, and a human in the loop

**Status:** accepted · **Date:** 2026-11-05

## Context

Buyers describe availability in words ("weekday evenings after 6 except Thursday, Saturday morning"). A language
model is good at that; it is also non deterministic, costs money, needs a key, and will occasionally be wrong.
A reviewer must be able to run the app with no key, and a buyer must never book against a misread sentence.

## Decision

* `IAvailabilityParser` turns text into weekly windows (`{days, start, end}`). Two implementations:
  * `ClaudeAvailabilityParser`: `claude-haiku-5-5` with a strict JSON schema as the output format (fast and
    cheap, which is what parsing one sentence needs). The official Anthropic .NET SDK is used.
  * `RuleBasedAvailabilityParser`: regexes for days, parts of day, "after N", "before N", "between A and B",
    ranges and "except". Deterministic, tested, no network.
* **Registration is by configuration**: `ANTHROPIC_API_KEY` set → Claude, otherwise rule based. The UI shows which
  provider is active (`GET /ai/provider`). If Claude fails (timeout, bad output), the rule based parser answers
  and the response says so in `warnings`.
* **The output is validated server side** regardless of provider (`WindowRules.Validate`: real day names, `HH:mm`,
  end after start), expanded into concrete ranges for the next 14 days, and summarized in plain English.
* **The buyer reviews before searching.** The UI shows the windows, the summary and the ranges; the buyer can
  remove ranges or edit the text. Only then does `/match` run. The same parser backs the MCP `find_open_slots`
  tool, which echoes its reading ("Read 'Saturday morning' as: Sat 8:00 AM–12:00 PM") so the assistant and the
  user can catch a misread.

## Consequences

* Swapping models or providers is one class. Prompt and schema live next to the code that validates them.
* The app is fully functional offline. The demo phrases work on either provider.
* The MCP server itself needs no Anthropic key: the client (Claude Code, Claude Desktop) brings the model.
