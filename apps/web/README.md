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
| `PUBLIC_ORIGIN` | server, runtime | unset | Origin(s) the app is published under, comma-separated (`https://showings.example.com`). Accepted as same-origin by the CSRF check in addition to the origin computed from the request |
| `TRUSTED_PROXY_HOPS` | server, runtime | `0` | How many reverse proxies append to `X-Forwarded-For` before the request reaches the app; picks which entry is reported to the API as the client IP (see below) |

Docker: `docker build -t hss-web . && docker run -p 3000:3000 -e API_URL=http://host.docker.internal:5080 hss-web`.

### Behind a reverse proxy

The BFF decides two things from the incoming request: the origin the browser is on (for the CSRF
check on `POST`/`PUT`/`DELETE`) and the client IP it reports to the API (for rate limiting).

- **Forwarded headers.** Have the proxy send `X-Forwarded-Proto` and `X-Forwarded-Host` (the
  first value of each is used when a chain is present). Without them Next fills them in from the
  socket, so the app believes it is `http://<upstream host>` and rejects every browser request as
  cross-site. If the proxy cannot be configured, set `PUBLIC_ORIGIN` to the public origin(s) instead.
- **`COOKIE_SECURE=true`** when TLS terminates at the proxy and `X-Forwarded-Proto` is not passed;
  otherwise the `Secure` flag follows the forwarded protocol in production.
- **Client IP.** The proxy should append the socket address to `X-Forwarded-For`. The app
  forwards the rightmost entry (the one the nearest proxy saw), never the leftmost, which any
  caller can write. With more than one appending proxy, set `TRUSTED_PROXY_HOPS` to the number of
  extra hops so the pick moves that many entries to the left. With no proxy at all the header is
  the socket address Next supplied, i.e. the caller.

## How it talks to the API (BFF)

The browser never sees the JWT.

- `POST /api/auth/login` and `/api/auth/register` call the API, then store the token in an
  `httpOnly`, `SameSite=Lax` cookie (`hss_session`). `POST /api/auth/logout` clears it. They require
  a JSON body and reject cross-site requests (`Sec-Fetch-Site: cross-site` or a foreign `Origin`);
  non-GET proxy requests get the same check.
- `/api/proxy/[...path]` forwards every other call to the API with `Authorization: Bearer <cookie>`,
  passing status and ProblemDetails bodies through. Both it and the auth routes send
  `X-Forwarded-For` (the client IP, see "Behind a reverse proxy") and `X-Forwarded-Proto`, so the
  API can rate-limit per client. It streams `text/event-stream` unbuffered (and forwards
  `Last-Event-ID`), which powers the live inbox from the API's MongoDB change stream; a
  `connected` event is prepended so the client knows it reached the real stream. While the API is
  unreachable it answers stream requests with a silent SSE body (a `retry:` hint, held open for
  30 s) so the browser keeps retrying; the indicator stays on "Connecting…" until a `connected` or
  `showing` event arrives, and the page refetches once per recovered connection.
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
