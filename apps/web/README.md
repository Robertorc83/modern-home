# Web app (Next.js)

The browser UI for the Home Showing Scheduler. Next.js (App Router) + Tailwind + shadcn/ui.

## Run

```bash
npm install
API_URL=http://localhost:5080 npm run dev   # http://localhost:3000
```

Sign in with a one-click demo account on `/login` (password `demo1234` for every seeded user).

| Variable | Where | Default | Purpose |
|---|---|---|---|
| `API_URL` | server, runtime | `http://localhost:5080` (`http://api:8080` in Docker) | ASP.NET Core API base URL |
| `NEXT_PUBLIC_MCP_URL` | browser, **build time** | `http://localhost:5080/mcp` | URL shown on the "Connect an AI assistant" page |
| `COOKIE_SECURE` | server, runtime | unset | Set to `true` behind HTTPS |

Docker: `docker build -t hss-web . && docker run -p 3000:3000 -e API_URL=http://host.docker.internal:5080 hss-web`.

## How it talks to the API (BFF)

The browser never sees the JWT.

- `POST /api/auth/login` and `/api/auth/register` call the API, then store the token in an
  `httpOnly`, `SameSite=Lax` cookie (`hss_session`). `POST /api/auth/logout` clears it.
- `/api/proxy/[...path]` forwards every other call to the API with `Authorization: Bearer <cookie>`,
  passing status and ProblemDetails bodies through. It streams `text/event-stream` unbuffered
  (and forwards `Last-Event-ID`), which powers the live inbox from the API's MongoDB change stream.
- `src/proxy.ts` (Next 16's middleware) redirects to `/login` when there is no cookie. Server
  layouts resolve the user via `/auth/me` and enforce the role per section; a 401 clears the cookie.

## Layout

```
src/app/(app)/agent      inbox (live), listings, listing editor (weekly window grid + slot preview)
src/app/(app)/buyer      find showings (manual ranges or AI text), booking with slot holds, my showings (live)
src/app/(app)/admin      agents + invite, all listings, all showings, outbox
src/app/(app)/settings   connect an AI assistant (personal access tokens, MCP commands)
src/app/api              BFF route handlers
src/lib                  typed API client, contract types, time-zone helpers
```

Times are always rendered with an explicit IANA zone (`Intl.DateTimeFormat`), preferring the
labels the API already computed.
