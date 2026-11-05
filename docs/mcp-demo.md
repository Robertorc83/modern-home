# MCP demo: booking a showing from Claude Code

A real run against the Docker stack (`docker compose up`), recorded on 2026-11-05. The MCP server needs no
Anthropic key: Claude Code brings the model.

## 1. Create a token and register the server

In the web app, signed in as **Buyer · Acme Realty** → *AI assistant* → **Create token**. The page shows the
exact command (the token below is already revoked):

```bash
claude mcp add --transport http showings http://localhost:5080/mcp \
  --header "Authorization: Bearer hss_pat_C0fO…"
```

```
$ claude mcp list
showings: http://localhost:5080/mcp (HTTP) - ✔ Connected
```

## 2. The prompt

```
$ claude
> Find me a Saturday morning showing in Denver under 600k and book it.
```

## 3. Transcript

**Claude:** I'll load the showings tools, search Denver listings under $600k, find Saturday morning slots, and
book one.

**Tool call** `search_listings` `{"city": "Denver", "maxPrice": 600000}`

```
2 listing(s):
- 250 Columbine St #310, Denver (Cherry Creek) · $499,000 · 1 bd / 1 ba Condo · listing id 6aec297120515b22d2ac343c
- 1420 Larimer St #5B, Denver (LoDo) · $585,000 · 2 bd / 2 ba Condo · listing id 6aec297120515b22d2ac3436
```

**Tool call** `find_open_slots` `{"availabilityText": "Saturday morning", "city": "Denver", "maxPrice": 600000}`

```
Read "Saturday morning" as: Sat 8:00 AM–12:00 PM (America/Denver, next 14 days, parsed by rule-based).
2 listing(s) with open slots (times in America/Denver):
- 250 Columbine St #310, Denver (Cherry Creek) · $499,000 · 1 bd / 1 ba Condo · listing id 6aec297120515b22d2ac343c · 12 matching slot(s)
    Sat, Nov 7: 8:00 AM [slotStart=2026-11-07T15:00:00Z], 8:30 AM [slotStart=2026-11-07T15:30:00Z], 9:00 AM [slotStart=2026-11-07T16:00:00Z], 9:30 AM [slotStart=2026-11-07T16:30:00Z], 10:00 AM [slotStart=2026-11-07T17:00:00Z], 10:30 AM [slotStart=2026-11-07T17:30:00Z]
    Sat, Nov 14: 8:00 AM [slotStart=2026-11-14T15:00:00Z], 8:30 AM [slotStart=2026-11-14T15:30:00Z], 9:00 AM [slotStart=2026-11-14T16:00:00Z], ...
- 1420 Larimer St #5B, Denver (LoDo) · $585,000 · 2 bd / 2 ba Condo · listing id 6aec297120515b22d2ac3436 · 7 matching slot(s)
    Sat, Nov 7: 9:00 AM [slotStart=2026-11-07T16:00:00Z], 9:45 AM [slotStart=2026-11-07T16:45:00Z], 10:30 AM [slotStart=2026-11-07T17:30:00Z]
    Sat, Nov 14: 9:00 AM [slotStart=2026-11-14T16:00:00Z], 9:45 AM [slotStart=2026-11-14T16:45:00Z], 10:30 AM [slotStart=2026-11-14T17:30:00Z], 11:15 AM [slotStart=2026-11-14T18:15:00Z]
```

**Claude:** Both listings have slots this Saturday. I'll book the 2-bed LoDo condo at 9:45 AM on Nov 7.

**Tool call** `book_showing` `{"listingId": "6aec297120515b22d2ac3436", "slotStart": "2026-11-07T16:45:00Z"}`
*(not marked read only, so an interactive Claude Code session asks for approval here)*

```
Requested: 1420 Larimer St #5B, Denver on Sat, Nov 7, 9:45 AM MST. Agent: Alex Rivera. Status: Requested. Showing id: 6aec2b6d915d57d7be571dcd.
```

**Claude:** Booked. The showing is requested for Saturday, Nov 7 at 9:45 AM Mountain time.

> **What I found** in Denver under $600k with Saturday morning availability:
> - **1420 Larimer St #5B, LoDo** at $585,000. 2 bed / 2 bath condo. Open Sat Nov 7 at 9:00, 9:45, and 10:30 AM, plus four slots on Nov 14.
> - **250 Columbine St #310, Cherry Creek** at $499,000. 1 bed / 1 bath condo. Open Sat Nov 7 and Nov 14 every half hour from 8:00 to 10:30 AM.
>
> **What I booked:** the Larimer St condo, since it is the only two-bedroom and a mid-morning slot was open this Saturday.
>
> | Field | Value |
> |---|---|
> | Showing id | 6aec2b6d915d57d7be571dcd |
> | Time | Sat, Nov 7, 2026 at 9:45 AM MST |
> | Listing agent | Alex Rivera |
> | Status | Requested, awaiting agent confirmation |

## 4. What happened on the other side

The agent's inbox (signed in as **Agent · Acme Realty**) showed the request the moment the tool returned, with no
refresh, through the change stream → SSE feed:

```
GET /showings/inbox?status=Requested   (as agent@acme.test)
6aec2b6d915d57d7be571dcd  Bea Martin  1420 Larimer St #5B  Sat, Nov 7, 9:45 AM MST  source=mcp
```

The outbox row was written in the booking transaction, relayed to SQS by the Worker and delivered through SES
(LocalStack), visible at `http://localhost:4566/_aws/ses`:

```
To: agent@acme.test   Subject: New showing request: 1420 Larimer St #5B
```

## 5. What a buyer token cannot do

```
$ curl -s -X POST http://localhost:5080/mcp -H "Authorization: Bearer hss_pat_…" \
    -H 'content-type: application/json' -H 'accept: application/json, text/event-stream' \
    -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_showing_requests","arguments":{}}}'
{"error":{"code":-32602,"message":"Unknown tool: 'list_showing_requests'"},"id":1,"jsonrpc":"2.0"}
```

Agent tools are not listed to buyers and are rejected if called. A token from Northstar Homes cannot see Acme's
listings at all (`McpTests.Brokerage_b_token_cannot_see_brokerage_a_listings`). Booking an already taken slot
returns a tool error with the three nearest open slots, not an exception.

## Claude Desktop

```json
{
  "mcpServers": {
    "showings": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://localhost:5080/mcp", "--header", "Authorization: Bearer <token>"]
    }
  }
}
```
