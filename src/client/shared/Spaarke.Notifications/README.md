# @spaarke/notifications

Host-agnostic client library for the Spaarke notification spine (spec `spaarke-notification-spine-r1`,
FR-05). Negotiates + connects to the Layer-C SignalR delivery service (task 020), routes incoming
events by `kind` to per-kind handlers, and automatically falls back to the poll endpoint (task 022,
FR-06) when the live connection is unavailable — the client half of the ADR-032 degrade path (NFR-04).

**Consumed by**: the SpaarkeAi workspace, record-form PCFs, and standalone code pages (spec §5A.7 #2,
"R3's three hosts"). This is the ONE client for the spine — do not hand-roll a second connection/poll
implementation for a new host; consume this package instead (CLAUDE.md §6/§11 forked-consumer rule).

## Install

Add as a `file:` dependency (standard workspace mechanism — see any consumer's `package.json`, e.g.
`src/solutions/SpaarkeAi/package.json`):

```json
{
  "dependencies": {
    "@microsoft/signalr": "^8.0.7",
    "@spaarke/auth": "file:../../client/shared/Spaarke.Auth",
    "@spaarke/notifications": "file:../../client/shared/Spaarke.Notifications"
  }
}
```

`@spaarke/auth` and `@microsoft/signalr` are **peerDependencies** of this package (not bundled) —
the consuming host must install its own copy. This is intentional: `@spaarke/auth` MUST remain a
process-wide singleton (ADR-028 INV-7 — one `PublicClientApplication` instance shared via
`getAuthProvider()`); a second copy pulled in transitively would create an isolated MSAL cache and
break silent SSO.

**Precondition**: the host must have already called `@spaarke/auth`'s `initAuth(...)` before using
this library — negotiate/poll calls resolve the BFF base URL from the shared auth provider's config
(same mechanism `authenticatedFetch` uses everywhere else). This library does not take a `bffBaseUrl`
parameter; it is host-agnostic precisely because it defers to the host's already-initialized auth
config.

## Quick start

```typescript
import { NotificationsClient } from '@spaarke/notifications';

const client = new NotificationsClient();

// Register per-kind handlers BEFORE calling start() so no early signal is missed.
const unregister = client.registerHandler('communication-arrived', (event) => {
  // event: { outboxRowId, kind, envelope?, source: 'live' | 'poll' }
  console.log('New communication:', event.outboxRowId, event.source);
});

try {
  await client.start();
} catch (err) {
  // Negotiate/connect failed (auth failure, SignalR disabled, network error).
  // Poll-fallback has ALREADY started in the background (NFR-04) — signals are
  // not being dropped. Render a degraded-connection indicator if desired.
  console.warn('Live connection unavailable, falling back to poll:', err);
}

// Later, on unmount:
unregister();
await client.stop();
```

## Public API

### `NotificationsClient`

The primary entrypoint. Composes negotiate/connect, kind-routing, and poll-fallback.

| Member | Signature | Notes |
|---|---|---|
| `constructor` | `(options?: NotificationsClientOptions)` | `pollIntervalMs`, `pollMaxBackoffMs`, `onConnectionStateChange` |
| `registerHandler` | `(kind: NotificationKind, callback: NotificationHandler) => () => void` | Returns an unregister function |
| `start` | `() => Promise<void>` | Negotiates + connects. **Rejects** on failure (typed error) — see below |
| `stop` | `() => Promise<void>` | Stops polling, closes the live connection, clears all handlers |
| `connectionState` | getter → `NotificationsConnectionState` | `'idle' \| 'connecting' \| 'connected' \| 'reconnecting' \| 'polling' \| 'stopped'` |

**`start()` failure behavior** (acceptance criterion): on negotiate/connect failure — including an
unauthenticated or expired-token caller — `start()` (1) starts poll-fallback immediately so no signal
is silently dropped, then (2) re-throws the original error as a rejected promise so the host can
render a degraded state rather than being misled into thinking a live channel exists. Errors you may
see: `AuthError` / `ApiError` (from `@spaarke/auth`), or `SignalRUnavailableError` (this package —
thrown when the server reports SignalR is disabled, HTTP 503).

**Reconnect/degrade behavior**: once connected, a `HubConnection` disconnect first attempts automatic
reconnect (`withAutomaticReconnect`); while reconnecting, `connectionState` is `'reconnecting'` and
poll-fallback runs in parallel so signals aren't missed mid-reconnect. If automatic reconnect is
exhausted, `connectionState` becomes `'polling'` and stays there until `start()` is called again.

### `KindRouter`

Lower-level building block — `NotificationsClient` uses one internally, but it's exported for callers
who want to compose their own negotiate/poll wiring.

- `registerHandler(kind: NotificationKind, callback: NotificationHandler): () => void`
- `dispatch(event): void` — routes to registered handlers; **logs and skips (never throws)** a `kind`
  not in the locked taxonomy (`ALL_NOTIFICATION_KINDS`). A recognized-but-unregistered kind (e.g. a
  RESERVED kind with no host handler yet) is a silent no-op, not a log line — this is the forward-compat
  guarantee: a future kind going active server-side never breaks an older shipped host.
- `clear(): void`

### `negotiate()` / `connectSignalR()`

- `negotiate(): Promise<{ url: string; accessToken: string }>` — calls `POST /api/notifications/negotiate`
  via `@spaarke/auth`'s `authenticatedFetch`. Throws `SignalRUnavailableError` on HTTP 503 (SignalR
  disabled server-side); rethrows `AuthError`/`ApiError` for anything else.
- `connectSignalR(onSignal): Promise<HubConnection>` — negotiates, then opens the SignalR connection.
  **The SignalR transport connection itself is the ONE enumerated ADR-028 `D-AUTH-7` raw-fetch
  exception** — see the comment at the `HubConnectionBuilder` call site in `src/negotiate.ts`. Every
  other HTTP call this package makes (negotiate, poll) goes through `authenticatedFetch`.

### `startPollFallback(options)`

- `options.onEvent: (event: NotificationEvent) => void` — one call per pending row returned by
  `GET /api/notifications/pending` (task 022, FR-06).
- `options.intervalMs` (default 30s), `options.maxBackoffMs` (default 5 min) — exponential backoff on
  consecutive failures, reset to `intervalMs` on the next success.
- Returns `{ stop(): void; isRunning: boolean }`.
- **Response shape**: `{ "items": [ { "outboxRowId", "kind", "envelope" }, ... ] }` — confirmed against
  the shipped `Sprk.Bff.Api.Api.Notifications.NotificationsPendingResponse` (task 022, landed in the
  same parallel wave as this task). `extractItems()` in `pollFallback.ts` also accepts a bare JSON
  array defensively, but the wrapped `{ items }` shape is what the server actually returns.

### Types (`src/types.ts`)

Mirrors of the server-side wire contract — kept in sync by hand with task 013's envelope types and
task 020's `NotificationSignal`. If the server contract changes, update this file.

- `NotificationKind` — closed union: `'suggestion' | 'communication-assessed' | 'communication-arrived'`
  (ACTIVE) `| 'job-complete' | 'share' | 'system-alert'` (RESERVED — valid values, no envelope/consumer yet).
- `CommunicationEnvelope`, `SuggestionEnvelope` — field-for-field mirrors of the C# records.
- `NotificationSignal` — the signal-only live-push payload (`outboxRowId` + `kind`, no envelope).
- `PendingNotificationItem` — one row from the poll endpoint (PROVISIONAL — see above).
- `NotificationEvent` — the normalized shape delivered to registered handlers: `{ outboxRowId, kind,
  envelope?, source: 'live' | 'poll' }`. `envelope` is **absent** for live SignalR pushes (the spine is
  signal-only, NFR-02/03) and **present** for poll-delivered events (task 022 returns full envelope
  detail). Handlers must not assume `envelope` is populated.

## Design notes

- **Why no `bffBaseUrl` parameter?** `authenticatedFetch` already resolves relative URLs against the
  host's `@spaarke/auth` runtime config (see `resolveUrl` in `authenticatedFetch.ts`). Requiring hosts
  to pass the same URL again here would be a second source of truth for the same value.
- **Why is `envelope` optional on `NotificationEvent`?** The spine's live SignalR push is deliberately
  signal-only (NFR-02/03 — "the spine never carries ungrounded/ungated content ... IDs + minimal
  display metadata only" is actually stricter than that: the live wire payload carries no envelope at
  all, just `outboxRowId` + `kind`). The poll endpoint, by contrast, returns the full envelope. A
  handler that needs envelope detail on a live push should re-fetch via the same poll endpoint (or a
  future dedicated single-row fetch) using `outboxRowId` — this library does not do that automatically
  today.
- **No second connection target.** This package wraps exactly ONE negotiate endpoint
  (`POST /api/notifications/negotiate`) and ONE poll endpoint (`GET /api/notifications/pending`). Do
  not add a host-specific hub URL or a second poll target — extend this package instead (CLAUDE.md §11).

## Contract lock (for task 025 / R3)

This README is the authoritative public-API reference for task 025's contract-lock deliverable to
`messaging-communication-app-r3`. The stable exports are everything re-exported from `src/index.ts`;
treat `NotificationsClient`, `registerHandler`, `NotificationEvent`, and the `NotificationKind` union
as the consumer-facing surface. `KindRouter`, `negotiate`, `connectSignalR`, and `startPollFallback`
are exported for advanced composition but `NotificationsClient` is the recommended integration point.
