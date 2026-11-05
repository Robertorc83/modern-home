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
| `PUBLIC_MCP_URL` | server, runtime | `http://localhost:5080/mcp` | MCP URL shown on the "Connect an AI assistant" page (read per request, so one image serves any deployment) |
| `COOKIE_SECURE` | server, runtime | unset | `true` forces the `Secure` cookie flag. Otherwise it is set automatically in production when the request arrived over HTTPS (`x-forwarded-proto: https`), and left off over plain HTTP (the Docker demo) |

Docker: `docker build -t hss-web . && docker run -p 3000:3000 -e API_URL=http://host.docker.internal:5080 hss-web`.

## How it talks to the API (BFF)

The browser never sees the JWT.

- `POST /api/auth/login` and `/api/auth/register` call the API, then store the token in an
  `httpOnly`, `SameSite=Lax` cookie (`hss_session`). `POST /api/auth/logout` clears it. They require
  a JSON body and reject cross-site requests (`Sec-Fetch-Site: cross-site` or a foreign `Origin`);
  non-GET proxy requests get the same check.
- `/api/proxy/[...path]` forwards every other call to the API with `Authorization: Bearer <cookie>`,
  passing status and ProblemDetails bodies through. Both it and the auth routes send
  `X-Forwarded-For` (the client IP from an upstream proxy's `x-forwarded-for`/`x-real-ip`, omitted
  when unknown) and `X-Forwarded-Proto`, so the API can rate-limit per client. It streams `text/event-stream` unbuffered
  (and forwards `Last-Event-ID`), which powers the live inbox from the API's MongoDB change stream.
  While the API is unreachable it answers stream requests with an empty SSE body carrying a
  `retry:` hint, and the client hook reconnects with backoff and refetches once it is back.
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
