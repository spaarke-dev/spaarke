# Task 051 — Suggestion renderer branch in the Assistant (FR-16): Implementation Notes

> **Status**: ✅ Completed 2026-07-22. Phase 5 Wave 16 — the proactive-suggestion consumer surface. FULL rigor (sonnet/high, executed on opus session ≥ tier). All conversation-pane suites green; both Step 9.5 gates CLEAN; escalation trigger did NOT fire (routes through the existing dispatch). Frontend-only (SpaarkeAi + jest config); no BFF, no new package.

## What shipped

| Artifact | Change |
|---|---|
| `src/solutions/SpaarkeAi/src/components/conversation/SuggestionCard.tsx` (NEW) | Presentational single card, a SIBLING of `ConsumerChips.tsx`. Mirrors the `chip` style-class token choices verbatim in spirit (`borderRadiusMedium`, `colorNeutralBackground1`, 1px `colorBrandStroke2` border, `shadow2`, hover `colorBrandBackground2Hover`/`Pressed`, disabled `colorNeutralBackgroundDisabled`) — Fluent v9 tokens ONLY (ADR-021 dark-mode-correct). Renders title (+ optional snippet), arrow-after icon, `data-testid=suggestion-card-{suggestionId}`. Stateless — click reports via `onAction`. |
| `src/solutions/SpaarkeAi/src/components/conversation/useSuggestionCards.tsx` (NEW) | The hook analog of `useConsumerChips.tsx` for the Layer-C SPINE path. Subscribes to `kind=suggestion` via the injected `subscribe` (the ONE host-wide `@spaarke/notifications` client). On each signal → **re-ground** from `GET /api/notifications/pending` (task 022, oid-scoped + read-time expiry-filtered server-side). **Pre-mount expiry filter** (`expiresAt <= now` excluded from the rendered set entirely — not rendered-then-disabled). Click → **re-fetch/re-ground BEFORE acting** (confirm the outbox row is STILL pending) → hand the fresh envelope to the host's `onSuggestionAction` (task-052 dispatch plug-point); stale/revoked or any failure → stable local line (ADR-019) + NO dispatch. Structural wire types declared inline (the package `dist` type decls lag source — same reason `notificationsBootstrap.ts` uses `NotificationEventLite`). |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/SuggestionCard.test.tsx` (NEW, 7 tests) | Component: renders-from-model + click; ADR-021 dark-mode (`webLightTheme`/`webDarkTheme`, no inline color literal). Hook lifecycle: signal→renders valid row; expired→absent (pre-mount filter); click re-fetches BEFORE dispatch (jest `invocationCallOrder`); stale re-fetch → no dispatch + stable line. |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (MODIFIED) | Imports `useSuggestionCards` + `getNotificationsClient`. Wires the hook: `subscribe` = `getNotificationsClient().registerHandler`; `fetchPending` = `authenticatedFetch(\`${bffBaseUrl}/api/notifications/pending\`)` unwrapping `{ items }`; `onSuggestionAction` routes through `chips.dispatchBinding` (held in a ref so the subscription never re-arms on dispatch-state churn); `inject` = `injection.inject`. Renders `suggestions.suggestionSlot` at the TOP of the content region (a proactive suggestion arrives independent of a dispatch turn → NOT the transcript-footer chip slot). |
| `src/solutions/SpaarkeAi/src/__mocks__/notifications.ts` (NEW) + `jest.config.ts` (MODIFIED) | `@spaarke/notifications` ships ESM `dist` Jest can't transform + drives a live SignalR connection. ConversationPane now pulls it into every ConversationPane test's module graph. Mapped `^@spaarke/notifications$` to a no-op `NotificationsClient` stub (mirrors the existing `@spaarke/auth` / `@spaarke/sdap-client` jest-mock pattern) — the unit tests inject their own subscriber doubles and never exercise the real client. |

## Design decisions (documented)

### The 052 coupling — `onSuggestionAction` is the dispatch plug-point (escalation-respecting)
The `SuggestionEnvelope` carries `actionHint` (e.g. `"review"`) + `regardingRecordId` — NOT a `bindingId`. The `actionHint`→Binding resolution is **task 052's re-entry contract** (not yet built). 051 therefore owns: visual + pre-mount expiry filter + re-fetch-before-action + graceful-fail + dark-mode; 052 owns the `actionHint`→Binding dispatch. The hook routes the re-validated action through a host-provided `onSuggestionAction(envelope)` callback. **ConversationPane's interim wiring routes it through the EXISTING `chips.dispatchBinding`** (→ `runBindingDispatch` → `dispatchConsumer`) — the SAME path the Click-path chips use. This is why the task's escalation trigger ("a second dispatch pipeline is a hard MUST NOT") **did not fire**. Until 052 formalizes the mapping, an unresolved binding surfaces the stable ADR-019 line via `runBindingDispatch`'s own `.catch()` — a safe interim.

### No mount-time fetch — re-ground on signal only (test-interference + redundancy fix)
An initial version fetched `/pending` on mount to surface already-pending suggestions. That added an uncontrolled async fetch to EVERY `ConversationPane` mount, which broke the fake-timers `ConversationPane.event-path.test.tsx` suite (17 tests timed out — floating async against the shared `authenticatedFetch` mock). It was also redundant: the task-021 client's poll-fallback fires an immediate first tick on `start()` (task 022 FR-06), delivering any already-pending suggestion as an event through the same handler. Resolution: **re-ground only on an actual `suggestion` signal** (live push = signal-only → we fetch the envelope; poll tick also routes through the handler). Documented limitation: a suggestion created while the user was away, on a FRESH live SignalR connection with no replay, surfaces on the next live ping or poll tick rather than instantly at mount — acceptable for the proactive surface.

### Live pushes are envelope-less → the card MUST re-fetch (NFR-02/03)
The SignalR signal carries `{ outboxRowId, kind }` only (task 020) — no envelope on the wire. So the renderer cannot render from the push; it re-grounds from the BFF. This is the same invariant the POML mandates for the click-time re-check (`re-fetch/re-ground via BFF at action time`), so the read path (`/api/notifications/pending`) serves both the render-source and the freshness/access re-check.

### Placement — top-of-content slot, not the transcript footer
`ConsumerChips` render inline in the transcript footer (`transcriptFooterSlot`) because they follow a dispatch turn. A proactive suggestion arrives independent of any turn, so `suggestions.suggestionSlot` renders at the top of the content region (near the profile nudge), visible regardless of transcript state. Renders `null` when there are no live/non-expired suggestions.

## Acceptance — all 7 criteria met
1. ✅ Valid non-expired envelope → compact card with the ConsumerChips bordered-card treatment, Fluent v9 tokens only (component test + dark-mode test).
2. ✅ Expired `expiresAt` → card does NOT render (pre-mount filter, asserted by absence).
3. ✅ Click → BFF re-fetch/re-ground executes and completes BEFORE any dispatch (jest `invocationCallOrder` assertion).
4. ✅ Re-fetch shows stale/revoked → no dispatch + a stable non-raw failure line (ADR-019).
5. ✅ Dispatch routes through the exact `dispatchConsumer` mechanism (`chips.dispatchBinding`) — no new endpoint/pipeline (escalation trigger cleared).
6. ✅ ADR-021 dark-mode test passes under both `webLightTheme` and `webDarkTheme` (no hardcoded color).
7. ✅ Existing `ConsumerChips` / `useConsumerChips` / `ConversationPane` tests pass unmodified — the 21 conversation suites (119 tests) all green; the two touched sibling files' behavior is unchanged (only additive imports + a new sibling slot).

## Verification
- **New suite** `SuggestionCard.test.tsx`: 7/7 pass.
- **Sibling regression guard**: `ConsumerChips.test.tsx` + all `ConversationPane*` suites + `SuggestionCard.test.tsx` = **21 suites / 119 tests, all pass** (criterion 7).
- **TS surface gate** (`npm run typecheck`, the build's tsc gate): **Surface-owned: 0** (243 pre-existing shared-lib errors deferred to Phase B — not mine).
- **Lint**: no findings on the four new/changed files.
- **Full jest run** (`npx jest`, 73 suites): the only real failure is `three-pane-compose-coordination.e2e.test.tsx` (9 tests) — **PRE-EXISTING**: it fails identically on the baseline (my ConversationPane + jest.config edits stashed), throwing inside `useComposeWorkspaceReceivers` (a task-104 compose hook), wholly unrelated to notifications. `CreateOnSaveAssociation` + `MyAssistantDialog` failed only under the heavy parallel full-run and **pass 25/25 in isolation** with my changes (parallel-load flakiness, not a regression).

## Step 9.5 gates — both CLEAN
- **code-review**: 0 Critical / 0 Warning / 2 informational — (i) `onSuggestionAction` passes `actionHint` as the binding id (interim until task 052 formalizes the mapping; intentional + documented in-code); (ii) `nowMs` recomputed per render (cheap read; the expiry filter must evaluate at render time). ADR-019 stable-line failure path confirmed; no secrets; no raw error surfaced.
- **adr-check**: 0 violations. ADR-021 (tokens-only, dark-mode test), ADR-039 (actionHint presentation-only; dispatch via existing path, zero client intent detection), ADR-019 (stable local line), ADR-010 (no runtime interface seam — TS shapes only). §10 BFF Hygiene N/A (frontend-only, no BFF touch, no new package → no publish-size/CVE impact). Escalation trigger did NOT fire.

## For downstream
- **Task 052 (suggestion dispatch parity)**: implement the `actionHint`→Binding re-entry. The seam is `onSuggestionAction(envelope: SuggestionEnvelopeLite)` in `ConversationPane.tsx` (currently `chips.dispatchBinding(envelope.actionHint, { slots: { regardingRecordId } })`). Replace the interim `actionHint`-as-bindingId with the real resolution; the card's re-fetch-before-dispatch + graceful-fail + expiry-filter are already in place. Add the dispatch-parity seam test there.
- **To see suggestions in an environment**: `Notifications:Suggestions:Enabled=true` (task 050 deny-by-default) AND a live Azure SignalR resource OR the poll fallback active; the card re-grounds via `GET /api/notifications/pending`.
