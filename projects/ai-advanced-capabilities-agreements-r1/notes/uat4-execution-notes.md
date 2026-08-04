# UAT Round 4 — execution notes

Owner feedback table: see "UAT Round 4" in `notes/uat-round1-2026-08-03.md`.

---

## agent-C — items #10a + #10b (2026-08-04)

Scope: (10a) background-run UX — move a dismissed review's liveness to the Compose tab header;
(10b) root-cause the "analysis lost on leaving SpaarkeAi" defect and fix/escalate. Opus, FULL rigor
(server lifetime change + BFF §10). HARD BOUNDARIES honored: no edits to `ConversationPane.tsx`,
`SuggestionCard`/`useSuggestionCards`, other conversation card/chip files, or `ComposeFormatToolbar`;
no `.claude/**` / `current-task.md` / `TASK-INDEX.md` writes; no git commit/push.

---

## 10b (investigated FIRST) — ROOT CAUSE: the run is CANCELLED on client disconnect → verdict (iii)

### (i) Does the server-side run survive the client disconnect? **NO — it is cancelled and nothing is ledgered.**

The owner's repro door is the "Review an NDA" chip (click-path). That review dispatches via
`chips.dispatchBinding` → `dispatchConsumer` → **`POST /api/ai/chat/sessions/{id}/dispatch`**
(`ConversationPane.tsx` chip path; client helper documented at `composeApplyLeg.ts:6`). The server
surface is `DispatchSessionEndpoint.DispatchAsync`.

Evidence (all file:line in `src/server/api/Sprk.Bff.Api/`):

1. **The whole review runs inside the request lifetime, bound to `RequestAborted`.**
   `Api/Ai/DispatchSessionEndpoint.cs:128` (pre-fix) — `var cancellationToken = httpContext.RequestAborted;`
   That token was passed straight into the orchestrator:
   `DispatchSessionEndpoint.cs:243-244` — `orchestrator.DispatchAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken)`.

2. **`DispatchAsync` is a single blocking pipeline**: the FIRST `MoveNextAsync()` runs the executor
   (the long gpt-5 Reasoning review), then the ADR-040 ledger write, and only THEN yields the terminal
   chunk. So the entire review + store happen inside that first `MoveNextAsync`
   (`DispatchSessionEndpoint.cs:245`).
   - Executor: `Services/Ai/Chat/SessionDispatchOrchestrator.cs:628-630` —
     `_actionRunner.RunAsync(effectiveAction, boundInputs, runContext, cancellationToken)` — the request
     token flows to the OpenAI HTTP call; `:632-635` re-throws `OperationCanceledException`.
   - Ledger write (ADR-040 store-before-render): `SessionDispatchOrchestrator.cs:678-685` —
     `_outputRouter.RouteAsync(session, binding, output, sourceRefs, cancellationToken: cancellationToken)`
     — the STORE itself is gated on the SAME request-tied token.
   - Terminal chunk is yielded AFTER the store: `SessionDispatchOrchestrator.cs:698, 709`
     (`ProgressiveRenderGuard.EnsureStored(routed.Entry)` → `yield return DeserializeResultChunk(...)`).

3. **On disconnect, the OCE is swallowed and the handler returns — nothing ledgered.**
   `DispatchSessionEndpoint.cs:251-258` — the pre-stream probe's `catch (OperationCanceledException) { ...; return; }`.
   Navigating away from the SpaarkeAi code page tears the SPA down → the SSE `fetch`/EventSource for
   `/dispatch` aborts → `RequestAborted` fires → the in-flight `MoveNextAsync` (running gpt-5 OR the
   store) throws OCE → the handler returns with **no `RouteAsync` completion**. The analysis is
   genuinely LOST server-side, not merely un-rendered.

**Why ADR-040 "store-before-render" does NOT save it:** store-before-render only guarantees ordering
*within a completed request*. Here the store write is itself gated on `RequestAborted`, so a disconnect
during the 120–140s review (the common case — the review takes minutes; see UAT #1) aborts the store
before it runs. The contrast is instructive: the SSE `/messages` path's ToolChain ledger write
DELIBERATELY uses `CancellationToken.None` — `Api/Ai/ChatEndpoints.cs:758`, comment *"ledger write
completes even if the client disconnects mid-render."* The `/dispatch` output write did **not** get that
treatment. (The sibling gate-resolve path `ChatEndpoints.cs:2294` and `/summarize` share the same
`RequestAborted` coupling.)

### (ii) Client-side loss + return path

Even with (iii) fixed, on cold reload nothing *points the user back*: the durable-recall path (FR-16)
re-materializes findings only when the user reopens that document's Compose session
(`WorkspacePane.tsx` analysis-entry effect → `GET /ai/chat/sessions/by-analysis/{id}` +
`workspace.widget_load{compose}` → `ComposeWorkspace.materializeComposeDraftFromLedger`;
server read `ChatEndpoints.ProjectComposeOutputs:1312`). Tab persistence (task 065) carries `widgetData`
round-trip, so a restored Compose tab re-reads its ledger — meaning **once (iii) is fixed, the findings
are RECOVERABLE on reopen, not lost.**

**Durable "Your agreement review finished — Open" notification — investigated, NOT additive-small for
this repro; ESCALATED (see below).** The notification spine (ADR-047) is fully shipped and the Assistant
pane already renders "You have N new notifications"
(`useSuggestionCards.tsx:335` ← `GET /api/notifications/pending`
← `OutboxService.GetPendingAsync` ← `sprk_notificationoutbox`). Producing one is a single
`OutboxService.WriteAsync` call. BUT three blockers make it NOT contained for the owner's actual door:
  - **Unbound session.** The owner's door is DIRECT-Compose (chip), which mints an UNBOUND session
    (no `sprk_analysis` `HostContext` — same fact as round-1 item #2). The `suggestion` envelope REQUIRES
    `regardingRecordId` + `regardingRecordType` (`useSuggestionCards.tsx:224-228`); an unbound review has
    no durable Analysis record to point "Open" at.
  - **Producer is a server change on a hot shared path.** Completion happens inside
    `SessionDispatchOrchestrator.DispatchAsync` (shared by every dispatch). A producer there needs
    review-binding discrimination + ADR-047 grounding/fan-out gating + idempotency — not an additive-small
    edit.
  - **Wrong "Open" door.** The shipped suggestion click handler opens the regarding record in a MODAL
    (`ConversationPane.tsx` `previewNavigationService.openRecordModal`), not the Compose reopen door.
    Rerouting it to `conversation.session_switch` + `workspace.widget_load{compose}` is a
    **`ConversationPane.tsx` edit — forbidden this wave** (agent-B owns conversation/**).

Per the task's fallback clause ("else document precisely what's needed and implement the History-based
restore guidance"), I implemented the (iii) root-cause fix (which converts "lost" → "recoverable on
reopen") and documented the exact notification-producer delta below. The **History overlay already lists
the session** (`HistoryOverlay.tsx:366` `GET /api/ai/chat/sessions?limit=50`) as the manual restore path
today.

### (iii) SERVER FIX (contained, §10-justified) — decouple execution + ledger from the response lifetime

**File:** `src/server/api/Sprk.Bff.Api/Api/Ai/DispatchSessionEndpoint.cs` (the chip-path handler — the
owner's exact repro door; one endpoint file). Change:
- Introduced `var executionToken = CancellationToken.None;` alongside the existing
  `cancellationToken = httpContext.RequestAborted`.
- The orchestrator now runs under `executionToken` (`DispatchAsync(request, executionToken)
  .GetAsyncEnumerator(executionToken)`), so the review + ADR-040 ledger write **run to completion even if
  the client disconnects**. The first `MoveNextAsync` yields the terminal chunk only AFTER
  `OutputRouter.RouteAsync` stored the `SessionOutput`, so the store can no longer be aborted mid-flight.
- The SSE **writes** still use `cancellationToken` (`RequestAborted`), so streaming stays cancellable — we
  stop writing to a dead socket. Added an outer `catch (OperationCanceledException)` around the streaming
  loop so a cancelled write to the disconnected client is swallowed cleanly (the durable work already
  completed) instead of surfacing a 5xx.

This mirrors the exact precedent at `ChatEndpoints.cs:758` (`CancellationToken.None` — "ledger write
completes even if the client disconnects"). The request scope (DI / OBO) stays alive for the run, so the
executor's dependencies are not torn down early. **Not applied** to the sibling `/summarize` +
gate-resolve paths (out of scope for this repro; `/summarize` is a fast gpt-4o-mini call rarely
interrupted) — noted for symmetry if the owner wants it.

**§10 BFF checklist:** logic-only change in ONE existing endpoint method — no new package / endpoint / DI
registration / type. `dotnet build src/server/api/Sprk.Bff.Api` → **0 errors**. No publish-size delta
(no new reference); baseline ~49.63 MB incl. PDBs unchanged. No new HIGH CVE (no dependency change).
Hot-path: BFF=Y (declared in `design.md`).

**Escalation (10b marked PARTIAL):**

🔔 **Human Input Required — durable review-complete notification producer**

- **Situation:** the (iii) fix makes the analysis durable + recoverable on reopen. A *proactive*
  "Your agreement review finished — Open" affordance that survives full navigate-away/return requires the
  notification-spine producer, which is NOT additive-small for the owner's UNBOUND direct-Compose door.
- **Exact delta needed (for a future BFF-owning task, cross-boundary with conversation/**):**
  1. **Bind first, or choose a regarding.** Either require the review door to bind an `sprk_analysis`
     (round-1 item #2's "Promote to Analysis" path — then `regardingRecordType='sprk_analysis'`), or use
     the `sprk_document` as the regarding (weaker — "Open" lands on the document record, not the review).
  2. **Producer (Layer D, server).** After the ADR-040 ledger write in the review completion path, call
     `OutboxService.WriteAsync(userId, NotificationKind.Suggestion, envelope, regardingRecordId,
     regardingRecordType, expiresAt)` where `envelope.title="Your agreement review finished"`,
     `actionHint="Open"`. Ground + gate per ADR-047 (record-security fan-out; idempotent on
     `(owner, kind, regardingRecordId)`); MUST write BEFORE the best-effort SignalR ping;
     add a `tests/integration/seam/**` slice test (ADR-047 §52). `userId`/`actingUserEmail` are already
     available at `DispatchSessionEndpoint.cs:185, 208`.
  3. **"Open" routing (conversation/**, forbidden this wave).** Reroute the suggestion click for this
     `source` from `previewNavigationService.openRecordModal(...)` to the Compose reopen door
     (`conversation.session_switch{sessionId}` + `workspace.widget_load{widgetType:'compose', seed}`),
     so "Open" lands in the review surface, not an OOB record form.
- **Recommendation:** land (iii) now (done); take the notification producer as a scoped follow-on that
  also fixes the unbound-session binding (dovetails with round-1 item #2's promote path). This is the SAME
  follow-on already filed in `notes/uat-round1-2026-08-03.md` (task 071 "durable notification layer NOT
  built here") — this note supplies the precise delta.

---

## 10a — BACKGROUND-RUN UX: move a dismissed review's liveness to the Compose tab header

**Design.** On "Continue working in background", the progress modal already unmounts fully
(`useNdaReviewRunProgress` sets `visible=false` → `NdaReviewProgressModal` `if (!visible) return null`,
`NdaReviewProgressModal.tsx:209`), so the dismissed card leaves NO mounted/visible remnant. The run's
liveness now moves to the WORKSPACE tab strip: a tiny Fluent `Spinner size="extra-tiny"` on the running
Compose tab header, visible until the run completes; completion continues to flow through the existing
`ReviewCompleteToast` rules (unchanged).

Wired entirely via an ADDITIVE PaneEventBus discriminant (mirrors the uat3
`nda_review_progress_visibility` precedent), because the progress state lives in the Assistant pane
(`useNdaReviewRunProgress`) and the tab strip lives in the Workspace pane. Emitting from the hook (not
`ConversationPane.tsx`) keeps the change inside my owned files.

**Signal:** `workspace.nda_review_background_run { backgroundRunActive: boolean }` where
`backgroundRunActive = (status === 'running' && dismissed)` — true exactly while a dismissed run is still
executing; false on mount, while the modal is visible, and on terminal (complete/fail). ADR-030 additive
(unknown types ignored by existing subscribers); ADR-015 Tier-1 boolean.

**Files changed (all mine per the file-scoped boundary):**
- `src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` — new `nda_review_background_run`
  discriminant + `backgroundRunActive?: boolean` field (additive).
- `src/client/shared/Spaarke.AI.Widgets/src/index.ts` — barrel now re-exports
  `useOptionalDispatchPaneEvent` (was only exported from the events sub-barrel).
- `src/solutions/SpaarkeAi/src/components/conversation/useNdaReviewRunProgress.ts` — emits the signal via
  `useOptionalDispatchPaneEvent` (no-ops without a provider, so the hook stays bus-optional for its unit
  test).
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` — subscribes to
  `nda_review_background_run`, tracks `composeReviewRunningInBackground`, passes it down.
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManagerComponent.tsx` — new
  `composeReviewRunning?: boolean` prop; renders the tiny spinner on `compose` tab headers when true
  (`data-testid="workspace-tab-review-spinner-{id}"`), scoped to compose tabs (a review only runs on a
  document open in a Compose tab).

Did NOT touch `ConversationPane.tsx` (the existing `nda_review_progress_visibility` emit at :706 is
unchanged; my new emit lives in the hook).

---

## Tests

**Client (SpaarkeAi jest — `@spaarke/ai-widgets` resolves to source, no dist rebuild needed):**
- NEW `useNdaReviewRunProgress.backgroundRun.test.tsx` (5 tests): emits false on mount/idle; stays false
  while the modal is visible; **true on dismiss** (liveness → tab); **false again on complete** (spinner
  clears); false on fail.
- NEW `WorkspaceTabManagerComponent.reviewSpinner.test.tsx` (4 tests): spinner shows on the compose tab
  when `composeReviewRunning`; hidden when false (clear-on-completion); hidden by default; **never on a
  non-compose tab**.
- Card-unmounted-after-dismiss is covered by the existing `NdaReviewProgressModal.test.tsx`
  ("`visible=false` renders nothing even while status is running/complete").
- Regression: `useNdaReviewRunProgress NdaReviewProgressModal WorkspaceTabManagerComponent` →
  **40 passed / 6 suites**. `WorkspacePane ReviewCompleteToast` → **46 passed / 15 suites**.
- `Spaarke.AI.Widgets` `npm run build` (tsc) → **0 errors**. SpaarkeAi `npm run typecheck`
  (tsc-surface-gate) → **Surface-owned: 0** (73 pre-existing shared-lib errors, same Phase-B baseline as
  prior rounds).

**Server (BFF):**
- NEW in `tests/integration/contract/Api/Ai/DispatchSessionEndpointContractTests.cs`:
  - `Post_LedgerWrite_RunsUnderDisconnectDecoupledToken_NotRequestAborted` — deterministic seam guarantee:
    the `RouteAsync` ledger write receives a token with `CanBeCanceled == false` (proves the store is
    decoupled from `RequestAborted`).
  - `Post_ClientDisconnectsMidReview_LedgerWriteStillCompletes` — behavioral: holds the "review"
    mid-flight (a `TaskCompletionSource` gate — no `Task.Delay`), cancels the HTTP request (navigate-away),
    then asserts the ADR-040 ledger write STILL completed (`LastRouted != null`). Fails on the old
    `RequestAborted`-coupled code.
  - Additive fixture support (default no-op, existing tests unaffected): `StubOpenAiClient.GateBeforeReturn`
    + `RecordingOutputRouter.RouteCalledTask` / `LastRouteCancellationToken`.
- `dotnet build src/server/api/Sprk.Bff.Api` → 0 errors. `dotnet test tests/unit/Sprk.Bff.Api.Tests`
  (filter `DispatchSessionEndpointContractTests`, where the contract glob compiles them) →
  **17 passed / 0 failed** (15 pre-existing + my 2 new disconnect-durability tests).

---

## Status flips (see `notes/uat-round1-2026-08-03.md` UAT Round 4 table)
- **#10a → done** (tab spinner + card fully unmounted; tests green).
- **#10b → PARTIAL** — (iii) root-cause SERVER fix landed (analysis now survives disconnect + is
  recoverable on reopen); durable proactive "review finished — Open" notification ESCALATED with the exact
  producer delta (blocked on unbound-session binding + a forbidden conversation/** click-routing edit).

---

## agent-B #9 (2026-08-04)

Scope: after a QUICK-depth agreement review completes, offer a "Rerun a full analysis (~2–3 min)"
CARD; clicking it re-dispatches the SAME document's review at THOROUGH depth. Sonnet, FULL rigor
(new client abstraction + a `conversation/**` hot-file touch). HARD BOUNDARIES honored: surface =
`src/solutions/SpaarkeAi/src/components/conversation/**` ONLY — did NOT touch
`NdaReviewProgressModal`/`useNdaReviewRunProgress`/`ReviewCompleteToast`/`WorkspacePane`/shell
(agent-C's this wave, confirmed via `git status` as concurrently modified, uncommitted) or
`Spaarke.Compose.Components` (agent-A's toolbar); no `.claude/**` / `current-task.md` /
`TASK-INDEX.md` writes; no git commit/push.

### Context loaded first (per the brief)

- `notes/070-execution-notes.md` — the `reviewDepth` slot, `useAgreementReviewGate.ts`'s
  branch shapes (`dispatchReview`, `dispatchDirectReview`, `runExplicit`'s two-mode contract),
  and the exact seam task 070 built for the quick-scan caveat (`useConsumerChips.tsx`'s
  `isNdaReview` branch reads `opts.slots.reviewDepth` from closure scope).
- `notes/uat3-execution-notes.md` — `runDirectReview`'s design (the direct chip/card door's
  bare wire shape, no `subDomain`) and the non-blocking progress-modal notification matrix
  (informs why I did NOT add a new PaneEventBus signal — see below).
- `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md` — confirmed CARD (persistent act-on item),
  not chip (turn-grounded next-step); the shipped card seam is `SuggestionCard.tsx` +
  `useSuggestionCards.tsx` (spaarke-notification-spine-r1 task 051).
- `docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md` + `notes/033-execution-notes.md`
  (session-coincidence design) — informed the compose-tab-targeting investigation below.

### Trigger seam (found, confirmed exact)

`useConsumerChips.tsx`'s `runBindingDispatch`, `isNdaReview` branch — the SAME closure that
already reads `opts?.slots?.reviewDepth === "quick"` for the 070 quick-scan caveat. Added ONE
more read there: when `reviewDepth==="quick"`, extract `fileId` (`opts.slots.fileIds[0]`) and
`subDomain` (`opts.slots.subDomain`, may be `undefined` for the direct-chip door) and call a new
optional dep, `onQuickReviewComplete?.({fileId, subDomain})`. Fires ONCE per completed quick
dispatch (never for thorough/absent depth, never for a non-NDA-review result) — verified by 6
new tests in `useConsumerChips.surface-launch.test.tsx`.

### Card (the established proactive-card visual, reused directly — NOT `useSuggestionCards`)

New `useRerunFullAnalysisCard.tsx`: a small, session-turn-scoped controller (`pending: {fileId,
subDomain?} | null`, plain `React.useState`) that renders `SuggestionCard` (imported directly —
title="Rerun a full analysis", snippet="(~2–3 min)") inside a single-row bordered panel. Single-
slot by construction — "render once per quick run, no stacking" falls out of the shape (a second
`showFor` call REPLACES, never adds, a card). NOT `useSuggestionCards`: there is no server outbox
row, no expiry, no BFF re-ground, no dismiss endpoint — this card's data source is a client-
observed dispatch completion, not the Layer-C notification spine. Rendered in ConversationPane's
JSX alongside `{suggestions.suggestionSlot}` (the established top-of-pane card region), reset on
`handleSessionCreated` (mirrors every other per-session controller in this file).

### Action — `AgreementReviewGateController.rerunThorough(fileId, subDomainKey?)`

New method on `useAgreementReviewGate.ts` (reached from the card via a ref-forwarding pattern —
`rerunThoroughRef`, mirroring the file's own EXISTING `ndaReviewEmitRef`/`ndaRunRef` precedent,
since `agreementReviewGate` is constructed AFTER `chips` but the card is armed FROM `chips`).
Dispatches THOROUGH immediately: no classify, no ask, no gate-state mutation (`resolvedRef`/
`pendingDepthRef`/`lastResolvedSubDomainKeyRef` are untouched — a one-off re-fire, not a gate
resolution). Mirrors `dispatchReview`'s wire shape when `subDomainKey` is known (resolves
`resultLabel` via the SAME `loadRegistry`+`displayNameFor` read `runExplicit` already uses), or
`dispatchDirectReview`'s bare shape when it isn't (the direct-chip door never carried one).

### The compose-tab-targeting investigation (the load-bearing finding this task surfaced)

The design brief asked me to "study how a second Compose tab can be opened for the same doc" and
explicitly pre-authorized a STOP-and-report + rerun-in-place fallback if the plumbing didn't
support a true new tab. It doesn't, and here is the exact evidence (read-only investigation of
`WorkspacePane.tsx`, which is agent-C's this wave — I did not edit it):

- `WorkspacePane.deriveComposeInstanceKey(widgetData)` derives a compose tab's document identity
  key. For the `compose.upload` seed shape (`{sessionId, sessionFileId, fileName}` — what
  `mountFileInCompose` sends, and what EVERY review dispatch's mount uses), the key is
  `upload:<sessionFileId>` — **keyed on `sessionFileId` alone**, nothing else.
- `WorkspacePane`'s widget_load handler (`~line 1444-1476`) looks up an EXISTING compose tab
  whose `composeTabInstanceKey` matches this derived key and, if found, **REUSES it**
  (`manager.updateTab` + `manager.setActiveTab`) rather than minting a new one.
- `WorkspacePane.compose-multi-tab.test.tsx` (pre-existing, spaarkeai-compose-r2) pins this
  EXACT contract as a regression: *"re-seeding the SAME document (same binding, later turn)
  REUSES its tab — no duplicate."*
- The task's own constraint is "reuse the known fileId — no re-upload." Reusing the SAME
  `sessionFileId` for the thorough rerun's mount, through the ONLY mount mechanism reachable
  from `conversation/**` (`mountFileInCompose`), therefore **always** resolves to the SAME
  `upload:<sessionFileId>` key as the quick run's own tab — REUSE, not a new tab, by
  construction. There is no flag/parameter on the seed that requests "mint a new instance
  anyway" — that would require a WorkspacePane-side key-derivation change (e.g. accepting a
  caller-supplied instance-key suffix), which is (a) outside `conversation/**`'s file boundary,
  and (b) WorkspacePane is explicitly agent-C's file this wave.
- Considered and rejected: fabricating a `compose.speDriveItemId`/`compose.draft.ledgerRef` seed
  instead (both derive a DIFFERENT key, `stored:<id>` / `draft:<id>`) to force a new tab.
  Rejected because (a) `conversation/**` does not track the reviewed file's SPE identity
  (`speDriveItemId`/`sprkDocumentId`) anywhere — only `sessionFileId`/`fileName` — so there is
  nothing genuine to put there; and (b) the `draft` shape is semantically for AI-authored
  content (inline HTML), not "reopen this already-uploaded document" — misusing it would open a
  tab that doesn't actually reload the reviewed document.

**Conclusion, per the task's own pre-approved fallback:** this ships **rerun-in-place** — the
SAME tab is reused (and brought to the front via the SAME `mountFileInCompose` idempotent-reuse
path every other re-mount already relies on), and the thorough findings supersede the quick
scan's visually in that one tab/session (comment threads are additive at the editor level per
`ComposeWorkspace.placeAdvisoryComments`; the Review Summary panel's `reviewSummaryFindings` is
wholesale-replaced on each LIVE `onAdvisoryComments` event, so the panel shows the THOROUGH
results after the rerun). **Follow-on, not built:** a WorkspacePane-side "force a distinct
instance key" seam (e.g. an additive `compose.upload.instanceSuffix` field folded into
`deriveComposeInstanceKey`) would let a rerun open genuinely side-by-side — flagged for
whichever task next owns `WorkspacePane.tsx`.

### Files

- **New:** `src/solutions/SpaarkeAi/src/components/conversation/useRerunFullAnalysisCard.tsx`.
- **Modified:** `useConsumerChips.tsx` (+`extractFirstFileIdSlot`/`extractStringSlot` helpers,
  +`onQuickReviewComplete` dep + call site + dep-array entry), `useAgreementReviewGate.ts`
  (+`rerunThorough` on the interface + implementation + returned object), `ConversationPane.tsx`
  (import, `rerunThoroughRef` + `rerunFullAnalysisCard` controller declared before `chips`,
  `onQuickReviewComplete` wired into `useConsumerChips({...})`, ref assignment after
  `agreementReviewGate` is constructed, session-reset call, JSX render of `cardSlot`).
- **New tests:** `useRerunFullAnalysisCard.test.tsx` (8). **Extended:**
  `useConsumerChips.surface-launch.test.tsx` (+6, new describe block), `useAgreementReviewGate.
  test.ts` (+7, new describe block).

### Tests (exact)

```
useRerunFullAnalysisCard.test.tsx (new)                          8/8   PASS
useConsumerChips.surface-launch.test.tsx                        18/18  PASS (12 pre-existing + 6 new)
useAgreementReviewGate.test.ts                                  46/46  PASS (39 pre-existing + 7 new)

SpaarkeAi src/components/conversation (60 suites)               607/607 PASS
SpaarkeAi full package (npx jest)                                916/916 PASS (96/96 suites)
SpaarkeAi typecheck (tsc-surface-gate)                           0 surface-owned errors (73
                                                                  pre-existing shared-lib errors,
                                                                  unchanged baseline)
SpaarkeAi build (vite)                                           clean, 4025 modules, ribbon
                                                                  bundles rebuilt
```

One transient full-suite run (before this table) showed ~130 failures, ALL with the identical
signature `useOptionalDispatchPaneEvent is not a function` inside `useNdaReviewRunProgress.ts` —
traced to a concurrent, in-progress, uncommitted edit by agent-C (their own #10a note above
confirms: *"`Spaarke.AI.Widgets/src/index.ts` — barrel now re-exports `useOptionalDispatchPaneEvent`
(was only exported from the events sub-barrel)"*) that landed on disk between my two runs. Not
caused by my changes — zero files I touched are in that failure's call stack — and the very next
run (after their concurrent edit completed) was 100% green, confirmed stable across two
consecutive full-suite runs. Also ran `npm run build` in `Spaarke.AI.Widgets` once (a pure `tsc`
recompile of already-committed+uncommitted source, zero tracked-file changes — confirmed via
`git status --short` before/after) while diagnosing this, per the established 060/uat3 precedent
for stale-`dist/` gaps.

### Quality gates (self-run, FULL rigor per CLAUDE.md §8)

**code-review (self):** no `any` introduced; no try/catch-log-rethrow; every guard
(`extractFirstFileIdSlot`'s malformed-slot tolerance, `rerunThorough`'s `!fileId ||
!reviewBindingId` early-return) names a concrete failure mode mirroring an existing sibling
guard; every widened interface (`ConsumerChipsDeps.onQuickReviewComplete`,
`AgreementReviewGateController.rerunThorough`) is additive, verified by the full 916/916-test
package run showing zero new failures; comments explain WHY throughout (esp. the compose-tab
finding above).

**adr-check (self):** ADR-039 (grounded execution) — `rerunThorough` reuses
`dispatchReviewBinding` verbatim, no new dispatch mechanism, `reviewDepth` stays the closed
two-value client intent. PASS. ADR-041 (no double-ask) — the card itself IS the one-time
affirmative ask; once clicked or dismissed it never re-offers for that run. PASS. ADR-021
(Fluent v9 tokens) — `useRerunFullAnalysisCard.tsx`'s one new style block uses `tokens.*`
exclusively. PASS. ADR-030 (PaneEventBus) — N/A, deliberately NOT touched: the trigger and the
card both live inside `ConversationPane`'s own component tree (no cross-pane signal needed),
so a plain host-owned callback is the smaller, correct mechanism — no new bus discriminant.
§10 BFF Hygiene — N/A, zero server-side changes. §11 Component Justification — stated inline
above (existing/extension/cost-of-doing-nothing for the new hook, the new gate method, and the
new dep). CLAUDE.md §6.5 — no ADR conflict surfaced; the compose-tab constraint is a
file-boundary/wave-scoping fact, not an ADR MUST/MUST NOT tension, so §6.5's protocol does not
apply — it is reported per the task's own pre-authorized STOP-and-report clause instead.

No Critical or Warning findings.
