# Task 021 — Shared Client Subscriber Library: Deviations & Notes

> Task: `021-shared-client-subscriber-lib.poml` · Rigor: FULL · Model: sonnet @ high

## Summary

Built `@spaarke/notifications` at `src/client/shared/Spaarke.Notifications/` — the ONE host-agnostic
client for the notification spine. Builds standalone with zero TypeScript errors, 20/20 jest tests
pass, zero host-project imports (grep-verified), and the SpaarkeAi workspace consumes it as a thin
first-integration wired into `main.tsx`'s bootstrap sequence.

## Deviations from the POML (all directional-mode adaptations, none escalation-worthy)

1. **`@spaarke/auth` and `@microsoft/signalr` are `peerDependencies`, not regular `dependencies`.**
   The POML's pattern reference cites Spaarke.Auth's package.json conventions, which use
   `peerDependencies` for *external SDKs* (`@azure/msal-browser`). I extended that same reasoning to
   `@spaarke/auth` itself (an *internal* shared package): if `@spaarke/notifications` depended on it as
   a regular `file:` dependency, npm could install a second nested copy, producing a second
   `PublicClientApplication`/MSAL cache instance — a direct violation of ADR-028 INV-7 ("All consumers
   share ONE `PublicClientApplication` instance"). The acceptance criterion's own wording
   ("no other `src/client/shared/**` ... as a build-time dependency **beyond declared
   peerDependencies**") supports this reading. `@microsoft/signalr` is peer-dependency'd for
   consistency with the same external-SDK pattern, even though it has no singleton-instance risk.

2. **No `bffBaseUrl` parameter anywhere in the public API.** `authenticatedFetch` already resolves
   relative URLs against the host's already-initialized `@spaarke/auth` config (see
   `resolveUrl`/`buildBffApiUrl` in `authenticatedFetch.ts`). Requiring the host to pass the same URL
   again to this library would be a second, potentially-divergent source of truth. Documented as a
   design decision in the README ("Design notes").

3. **`NotificationEvent.envelope` is optional and a `NotificationsClient` orchestrator class was
   added** (not explicitly named in the POML's `<outputs>`, which lists `negotiate.ts`, `kindRouter.ts`,
   `pollFallback.ts`, `package.json`, `test/kindRouter.test.ts`, `README.md`). The POML's `<goal>`
   describes three capabilities (negotiate/connect, kind-routing, poll-fallback) that need to compose
   into a single client a host actually calls; `NotificationsClient` is that composition point and is
   the class documented as the primary entrypoint in the README's Quick Start. `KindRouter`,
   `negotiate`/`connectSignalR`, and `startPollFallback` remain independently exported for advanced
   composition, satisfying the POML's individual-file outputs.

4. **Test directory is `tests/` (plural), not `test/` (singular) as literally listed in
   `<outputs>`.** Followed the established repo convention (Spaarke.Auth's `tests/` + its
   `jest.config.js` `testMatch: ['**/tests/**/*.test.ts']`) instead of the POML's literal singular
   spelling, since the POML explicitly directs "following Spaarke.Auth's package.json conventions"
   and step mode is `directional`.

5. **Two extra test files beyond the required `kindRouter.test.ts`**: `negotiate.test.ts` (negotiate
   contract, 503→`SignalRUnavailableError` mapping, auth-error passthrough) and `pollFallback.test.ts`
   (interval scheduling, exponential backoff/cap, `{ items }` response parsing). 20 tests total, all
   passing. Beyond the POML's explicit ask ("add a jest suite covering kind-routing dispatch +
   unknown-kind skip") but low-cost and directly exercises the acceptance criteria for negotiate
   failure typing (#7) and poll-fallback bounded interval (#4).

## Contract verification (escalation trigger did NOT fire)

- **Task 020's negotiate contract**: `POST /api/notifications/negotiate` → `{ url, accessToken }`
  (camelCase — ASP.NET Core Minimal API default `JsonNamingPolicy.CamelCase`, no override found in
  `Program.cs`). Matches what was built. SignalR-disabled path returns 503 via
  `FeatureDisabledException` → mapped client-side to `SignalRUnavailableError`.
- **Task 013's envelope/kind taxonomy**: `NotificationKind` kebab-case wire values
  (`suggestion`, `communication-assessed`, `communication-arrived`, `job-complete`, `share`,
  `system-alert`) and `CommunicationEnvelope`/`SuggestionEnvelope` field lists mirrored verbatim in
  `src/types.ts`. No divergence from spec §5A.3/§5B.4.
- **Task 022's pending endpoint** (not yet merged when this task started — parallel wave 5): built
  `pollFallback.ts` against the documented shape (spec FR-06 + task-012 `OutboxNotification`
  projection), defensively accepting either a bare array or `{ items: [...] }`. **Task 022 landed
  concurrently during this task's execution** — post-hoc verification against the shipped
  `NotificationsEndpoints.cs` (`GetPendingAsync` / `NotificationsPendingResponse` /
  `PendingNotificationItem`) confirms the response IS `{ "items": [{ "outboxRowId", "kind", "envelope" }] }`,
  exactly matching the defensive parsing already written. Updated code comments + README from
  "provisional" to "confirmed"; no functional change was needed.

No material contract mismatch was found anywhere — the escalation trigger in the POML did not fire.

## Verification performed

- `npm run build` in `Spaarke.Notifications`: **zero TypeScript errors** (after also building
  `Spaarke.Auth` first — its `dist/` was not yet built in this worktree; a one-time prerequisite for
  any consumer, not specific to this task).
- `npm test`: **20/20 passing** across `kindRouter.test.ts`, `negotiate.test.ts`, `pollFallback.test.ts`.
- Grep of `src/**/*.ts` for imports touching `src/solutions/SpaarkeAi/**`, any PCF project, or any
  code-page project: **zero matches**.
- Grep for `D-AUTH-7` and raw `fetch(`: the comment appears exactly once, at the
  `HubConnectionBuilder`/`.start()` call site in `negotiate.ts`; no other raw-`fetch(` call exists
  anywhere in the package.
- SpaarkeAi workspace: `npm install` (added `@spaarke/notifications` + `@microsoft/signalr` as
  `file:`/regular dependencies), confirmed `node_modules/@spaarke/notifications` resolves via the
  standard npm `file:` link mechanism (not a relative path reach-around), then
  `npm run typecheck` (the project's `tsc-surface-gate.mjs`, scoped to `src/**`): **0 surface-owned
  errors** (232 pre-existing shared-lib errors deferred to that project's own Phase B, unrelated to
  this change — confirmed via grep that none reference "notification").

## First-consumer wiring (POML step 7)

Added `src/solutions/SpaarkeAi/src/services/notificationsBootstrap.ts` — a thin module exposing
`getNotificationsClient()` (singleton) and `initNotificationsClient()` (registers log-only handlers
for the three ACTIVE kinds, then calls `client.start()`, non-fatal on failure). Wired into
`main.tsx`'s `bootstrap()` immediately after auth init (`void initNotificationsClient();`), mirroring
the existing non-fatal-optional-init pattern already used for AppInsights in the same function.
Deliberately does NOT render any suggestion/communication UI — that is task 051's job. Future
consumers (task 051) should call `getNotificationsClient().registerHandler(...)` rather than
constructing a second client instance.

## Quality gates (Step 9.5)

Self-reviewed (`code-review` skill workflow applied directly — no separate reviewer available in this
single-agent execution). Found and fixed two real correctness bugs before completion, plus one
justification note:

1. **Warning → fixed: `NotificationsClient.stop()` resource leak.** Calling
   `HubConnection.stop()` triggers the SDK's own `onclose` callback internally. The registered
   `onclose` handler called `startPolling()` unconditionally, so an intentional `stop()` would leave
   a poll-interval timer running forever in the background. Fixed with a `stopping` guard flag set at
   the start of `stop()` and checked inside `onclose`; `start()` resets the flag so a client can be
   restarted after a `stop()`. Regression test added:
   `tests/NotificationsClient.test.ts` — "stop() does not leave a poll loop running".
2. **Warning → fixed: `pollFallback.ts` conflated a handler exception with a fetch failure.** If a
   caller's `onEvent` handler threw (e.g. a bug in a downstream consumer, or — before this fix — a
   theoretical `KindRouter.dispatch` edge case), the whole tick's `try` block would catch it and
   treat the poll as FAILED: trigger `onError`, double the backoff interval. The fetch itself had
   actually succeeded. Fixed by wrapping each `onEvent` call in its own try/catch and moving the
   backoff-reset (`currentDelayMs = baseIntervalMs`) to immediately after the fetch/parse succeeds,
   before the per-item dispatch loop. Regression test added: "isolates a throwing onEvent handler...".
3. **Suggestion (documented, not code-changed): the `accessToken: string` field on
   `NegotiateResponse`.** This pattern-matches ADR-028's `MUST NOT add accessToken: string ... props
   anywhere in client code` rule on its face. Judged NOT a violation — it is the SignalR-service-scoped
   token required verbatim by `@microsoft/signalr`'s `accessTokenFactory` contract, the same class of
   documented exception as MSAL.js result objects / Power BI's `IReportEmbedConfiguration` already
   enumerated in the ADR. Added an explicit clarifying comment on the field in `negotiate.ts` so a
   future `adr-check` pass (or reviewer) has the reasoning inline rather than needing to re-derive it.

Post-fix: rebuilt (zero TS errors), full suite re-run (26/26 passing across 4 suites, up from 20/20
across 3 — added `NotificationsClient.test.ts` + one `pollFallback.test.ts` case), and re-ran
SpaarkeAi's scoped typecheck (still 0 surface-owned errors).

## HUMAN-ATTENTION

None. No ADR conflict encountered; no scope expansion beyond the task boundary. The two correctness
bugs above were caught and fixed within this task's own quality-gate pass — flagging here only for
visibility, not because further human action is needed.
