# UAT Round 3 — items #7 and #8 — execution notes (2026-08-04)

> Owner-directed remediation (screenshot: Assistant chip "Review an NDA" click-path,
> AppligentNDA_Signed.docx). Both items are client-side only (`src/server/**` read-only per HARD
> BOUNDARY — verified: no server file touched). No POML (UAT-round item, per instruction). Rigor:
> FULL (code changes across `src/solutions/SpaarkeAi` + `src/client/shared/Spaarke.AI.Widgets`).

## Where the two seams actually live

- **Chip-path dispatch seam (item #7)**: `ConversationPane.handleReviewNda`
  (`src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`) — the "Review an
  NDA" chip's click handler. Pre-fix: called `mountFileInCompose(...)` then immediately
  `chips.dispatchBinding(ndaReviewBindingId, { slots: { fileIds: [fileId] }, sessionIdOverride })`
  — no depth ask, defaulting the review to whatever the server's own catalog default resolves to.
  `ndaReviewBindingId` (resolved via capability discovery on `consumerType` — defaults `"nda-review"`,
  task 064 generalizes it per classified docType) is the EXACT SAME bindingId
  `useAgreementReviewGate`'s `deps.reviewBindingId` already holds (`ConversationPane.tsx:828`:
  `reviewBindingId: ndaReviewBindingId`) — confirming the gate controller can serve this chip-click
  path directly, with zero new bindingId plumbing.

- **070's depth-turn machinery (reused, not rebuilt)**: `useAgreementReviewGate.ts` —
  `pendingDepthRef` (the single-pending-decision ref), `buildAgreementReviewDepthChoiceChips()` /
  `buildAgreementReviewDepthChoiceMessage()` (`agreementReviewRouting.ts`), the
  `LOCAL_CHIP.agreementReviewDepthQuick`/`agreementReviewDepthThorough` chip ids, and
  `handleGateChipAction`'s depth-branch dispatch routing (checked FIRST, before the type-decision
  `pendingRef`). Task 070 built this exactly for the gate's auto-proceed / explicit-door /
  composite-post-pick branches; item #7 is a FOURTH branch on the SAME machinery.

- **Progress dialog (item #8)**: `NdaReviewProgressModal.tsx`
  (`src/solutions/SpaarkeAi/src/components/conversation/`) — the exact component named in the
  bug report (step labels "Analyzing clauses" / "Writing advisory notes", tagline "Measuring
  twice, flagging once…" among the `NDA_REVIEW_WORKING_PHRASES` rotation). Driven by
  `useNdaReviewRunProgress.ts`'s tiny state machine (`status: idle|running|complete|error`),
  mounted unconditionally near the top of `ConversationPane.tsx`'s JSX (`<NdaReviewProgressModal
  .../>`, portals to `document.body` regardless of tree position) and opened via
  `useConsumerChips`'s `onChipDispatched` callback (`ndaRun.begin()` when the dispatched binding ===
  `ndaReviewBindingId`) and closed/completed via `onDispatchResult`
  (`isNdaReviewResult(result)` → `ndaRun.complete()`).

## Item #7 — design + implementation

**Design decision**: add `runDirectReview(fileId, fileName)` to the
`AgreementReviewGateController` interface — a FOURTH entry point alongside `runGate` /
`runExplicit` / `handleGateChipAction`, purpose-built for the direct chip/card click. It:

1. No-ops if `reviewBindingId` is unavailable (mirrors `handleReviewNda`'s existing negative-path
   structure — the caller already checks this and shows its own "capability unavailable" message
   using `cardLabel`, which the gate doesn't have access to).
2. Sets `pendingDepthRef.current = { fileId, fileName, target: { kind: "direct" } }` — a NEW
   variant on the existing `PendingDepthChoiceTarget` discriminated union (alongside `"single"` and
   `"both"`), carrying NEITHER a `subDomainKey` NOR classifier-resolution tracking, because there
   was never a classification on this path.
3. Enqueues a NEW pure message `buildAgreementReviewDirectDepthChoiceMessage()` ("How deep should
   this review go?") — deliberately NOT the existing `buildAgreementReviewDepthChoiceMessage(displayName)`
   ("Ready to review as **{displayName}**…"), because the direct path has no type display name to
   embed (embedding the chip's `cardLabel`, e.g. "Review an NDA", would read as "review as Review an
   NDA" — grammatically wrong). The CHIPS are the exact same
   `buildAgreementReviewDepthChoiceChips()` reused verbatim — only the message copy differs, which
   is a legitimate content difference, not a second mechanism.
4. `handleGateChipAction`'s existing depth-branch gained a THIRD arm: `target.kind === "direct"` →
   calls a new `dispatchDirectReview(fileId, reviewDepth)` instead of `dispatchReview(...)` (which
   always sets a `subDomain` slot) or `dispatchBothSequentially(...)`.
5. `dispatchDirectReview` mirrors `handleReviewNda`'s PRE-existing dispatch body EXACTLY —
   `slots: { fileIds: [fileId], reviewDepth }`, no `resultLabel`, no `subDomain` — only adding
   `reviewDepth`. It does NOT call `mountFileInCompose` (unlike `dispatchReview`, which mounts with
   `activeWorkType: 'agreement-analysis'`) — mounting stays in `ConversationPane.handleReviewNda`,
   fired immediately at click time (unchanged), so the document opens in Compose regardless of how
   long the user takes to answer the depth question.

**`ConversationPane.handleReviewNda`** now calls `agreementReviewGate.runDirectReview(fileId,
fileName)` instead of `chips.dispatchBinding(...)` directly; the negative branch (bindingId
unavailable → `cardLabel` message) is unchanged.

### Why NOT reuse `dispatchReview`/the "single" target kind

`dispatchReview` unconditionally sets `subDomain: subDomainKey` and `resultLabel: displayName` in
the wire body, and calls `mountFileInCompose(fileId, fileName, AGREEMENT_ANALYSIS_WORK_TYPE)` (a
DIFFERENT `activeWorkType` than `handleReviewNda`'s pre-existing bare `mountFileInCompose(fileId,
fileName)` call). Routing the direct-chip path through `dispatchReview` would have silently added a
`subDomain` slot AND changed the mount's `activeWorkType` for every "Review an NDA" chip click — a
behavior change well beyond "add a depth ask," and explicitly out of scope (the task brief: "Keep
every other consumer chip's behavior unchanged... No double-ask: after the depth pick, dispatch
immediately (type is already known — the chip IS the type commitment)"). The new `"direct"` target
kind + `dispatchDirectReview` keep the wire shape byte-identical to pre-070 except for the ONE
additive `reviewDepth` field — the smallest correct fix.

### §11 Component justification

1. **Existing** — `pendingDepthRef`, `buildAgreementReviewDepthChoiceChips`, the two depth
   `LOCAL_CHIP` ids, and `handleGateChipAction`'s depth-branch routing already exist (task 070) and
   do 90% of the work.
2. **Extension** — added ONE new `PendingDepthChoiceTarget` variant (`"direct"`) and ONE new
   dispatch helper (`dispatchDirectReview`), both following the EXACT shape of the existing
   `"both"` variant + `dispatchBothSequentially` pairing — not a novel pattern, not a second
   mechanism.
3. **Cost-of-doing-nothing** — without this, the fastest, most-used entry point into an Agreement
   Review (the classified-upload chip — the owner's own UAT round-3 repro path) has NO way to pick
   Quick over Thorough, forcing every chip-driven review to pay the full ~135s Reasoning-tier
   latency with no escape hatch (the exact UAT round-1 item #1 complaint, unresolved for this one
   entry point until now).

## Item #8 — design + implementation

**Fix (as prescribed by the task brief, per MODAL-DECISION-CRITERIA.md's Family-2 "custom UX
surface" allowance + ASSISTANT-UI-ELEMENT-CRITERIA.md)**:

1. `NdaReviewProgressModal.tsx`: `<Dialog open modalType="alert">` → `<Dialog open modalType="non-modal"
   onOpenChange={(_, data) => { if (!data.open) onDismiss(); }}>`. Fluent v9's `"non-modal"` renders
   NO backdrop scrim and NO focus trap — the rest of the app (workspace tabs, the composer, etc.)
   stays fully interactive while the review runs. This is an established codebase pattern, not a
   new one (`CommunicationActions/CommunicationActionsApp.tsx:737-738` already uses
   `modalType="non-modal"` for the exact same "don't block interaction with the rest of the page"
   reason).
2. New prop `visible: boolean` (from `useNdaReviewRunProgress`'s new `visible` field) replaces the
   old `status === 'idle'` render gate — `visible = status !== 'idle' && !dismissed`.
3. New prop `onDismiss: () => void` — wired to a new "Continue working in background" `<Button
   appearance="subtle">` (visible only while `status === 'running'` — once the run reaches a
   terminal state the existing linger timer auto-dismisses within ~1s/~3.2s anyway, so a manual
   dismiss button there is low-value). ALSO wired to `onOpenChange`'s Escape/light-dismiss path —
   Escape and the button do the SAME thing (hide, keep running), never `onClose` (the terminal
   reset-to-idle) — dismissing mid-run must never look like the review was cancelled.
4. `useNdaReviewRunProgress.ts`: new `dismissed` state + `dismiss()` action + derived `visible`.
   `begin()` clears `dismissed` — a FRESH run always starts visible, even if the PRIOR run was
   dismissed. Dismissing never touches `status` — `complete()`/`fail()` still fire normally on a
   dismissed run (so `ReviewCompleteToast` still gets its completion signal), they just don't
   re-show the modal.

### Notification matrix (decided + tested)

The bug report's "cannot switch tabs... defeats the 071 toast entirely" pointed at a race the
BLOCKING modal made structurally impossible to reach: since the modal (pre-fix) held the whole app
hostage, a review could never be "still running AND the user on a different workspace tab" at the
same time — item #8 makes that combination reachable for the first time. This creates a genuine
double-notification risk (the modal shows "Review complete" AND the toast pops up) that needed a
decision:

| Dialog state | Active tab | Toast fires? | Why |
|---|---|---|---|
| open + visible (not dismissed) | on Compose tab | No | Pre-existing suppression (`activeWidgetTypeRef.current === "compose"`) — user is looking at Compose, sees the result render directly. |
| open + visible (not dismissed) | **off-tab (NEW reachable case)** | **No** | The modal ITSELF is still showing the outcome on screen (floats over every tab, non-modal) — a toast at the same instant would be a double notification. |
| dismissed | on Compose tab | No | Pre-existing suppression — unchanged; the user will see the result render into the Compose editor directly regardless of dismissal. |
| dismissed | off-tab | **Yes** | The 071 "notify me" case this component exists for — now ACTUALLY reachable (pre-fix, dismissing wasn't possible at all, so "dismissed" never happened; and even if it somehow had, off-tab was unreachable while the modal blocked switching). |
| never opened this session (`progressVisible` never dispatched) | off-tab | Yes | Defensive default — an unset/unknown signal never suppresses (matches the pre-#8 behavior byte-for-byte when the modal machinery was never exercised). |

**Mechanism**: a new PaneEventBus discriminant, `nda_review_progress_visibility` (additive,
ADR-030 — same "signal infrastructure" shape as the existing `active_widget_changed`), carrying
`progressVisible: boolean`. `ConversationPane.tsx` broadcasts it via a `React.useEffect` watching
`ndaRun.visible` (fires on EVERY visibility transition: `begin()` → true, `dismiss()` → false,
terminal auto-close → false). `ReviewCompleteToast.tsx` tracks the latest value in a new
`progressModalVisibleRef` (mirroring the EXISTING `activeWidgetTypeRef` pattern exactly) and adds
ONE new suppression check (`if (progressModalVisibleRef.current) return;`) alongside the existing
Compose-tab check — either condition suppresses.

**Ordering note (why there is no race)**: `useConsumerChips`'s `onDispatchResult` callback calls
`ndaReviewEmitRef.current(result)` (which dispatches `compose_advisory_comments` — the event the
toast listens for) BEFORE `ndaRunRef.current.complete()` (the state-machine transition that
eventually flips `visible` false once its linger timer elapses). So at the EXACT moment the toast's
completion signal fires, `progressModalVisibleRef.current` still reflects whatever the LAST
`begin()`/`dismiss()` call set it to — never a stale-by-one-render value racing the completion
itself. The two visibility-changing actions that matter (`begin()`→true, `dismiss()`→false) are both
discrete, prior, user/lifecycle-driven events, not something that can race the completion tick.

### §11 Component justification (the new PaneEventBus discriminant)

1. **Existing** — `active_widget_changed` is the closest existing analog (a "signal
   infrastructure" broadcast for downstream panes to react to), but carries tab-identity
   semantics, not modal-visibility semantics — not reusable as-is.
2. **Extension** — added ONE additive discriminant (`nda_review_progress_visibility`) + ONE field
   (`progressVisible`) to the EXISTING `workspace` channel (no 5th channel; ADR-030's own
   additive-types rule), consumed via the SAME `usePaneEvent`/`useDispatchPaneEvent` hooks every
   other discriminant uses — zero new plumbing.
3. **Cost-of-doing-nothing** — without a cross-component visibility signal, "dialog open+visible
   while off-tab" (the newly-reachable case) would double-notify the user (modal + toast
   simultaneously) — a concrete, testable UX defect (see the `ReviewCompleteToast` matrix tests
   below, which fail without this signal).

## Files modified

**Client (`src/solutions/SpaarkeAi/src/components/conversation/`)**:
- `agreementReviewRouting.ts` — new `buildAgreementReviewDirectDepthChoiceMessage()`.
- `useAgreementReviewGate.ts` — `PendingDepthChoiceTarget`'s new `"direct"` variant;
  `dispatchDirectReview`; `runDirectReview`; `handleGateChipAction`'s new `"direct"` arm;
  `AgreementReviewGateController.runDirectReview` on the interface + returned object.
- `ConversationPane.tsx` — `handleReviewNda` now calls `agreementReviewGate.runDirectReview`
  instead of dispatching directly; new `React.useEffect` broadcasting
  `nda_review_progress_visibility`; `<NdaReviewProgressModal>` mount site passes `visible`/`onDismiss`.
- `NdaReviewProgressModal.tsx` — `modalType="non-modal"`; new `visible`/`onDismiss` props; new
  "Continue working in background" dismiss button + `dismissRow` style; `Button` import added.
- `useNdaReviewRunProgress.ts` — new `dismissed` state, `visible` derived field, `dismiss()` action;
  `begin()` clears `dismissed`.

**Client (`src/solutions/SpaarkeAi/src/components/shell/`)**:
- `ReviewCompleteToast.tsx` — new `progressModalVisibleRef`; subscribes to
  `nda_review_progress_visibility`; new suppression check alongside the existing Compose-tab check.

**Client (`src/client/shared/Spaarke.AI.Widgets/src/events/`)**:
- `PaneEventTypes.ts` — new `nda_review_progress_visibility` discriminant on the `workspace`
  channel's `type` union; new `progressVisible?: boolean` field.

**Tests (new/modified)**:
- `useAgreementReviewGate.test.ts` — new describe block "UAT round-3 item #7: runDirectReview"
  (7 tests): depth-turn insertion, Quick/Thorough dispatch with NO `subDomain`, sessionIdOverride
  threading, no-`getLastResolvedSubDomainKey`-tracking, no-op-when-bindingId-unavailable, and an
  explicit regression test proving the confirm-gate branch is unaffected (still carries `subDomain`
  + `resultLabel` exactly as task 070 established).
- `ConversationPane.review-chip-depth-ask.e2e.test.tsx` — **NEW** file (3 tests): drives the REAL
  `ConversationPane` over a real `PaneEventBus`, simulating the CLS-CHAT docType classifier's
  `event_classification` SSE event to render the "Review an NDA" chip, clicking it, asserting the
  depth-choice turn renders (no immediate dispatch), then answering it and asserting the dispatch's
  wire body carries `reviewDepth` with NO `subDomain` slot. Also proves the file still opens in
  Compose immediately at click time (before the depth question is answered).
- `useNdaReviewRunProgress.test.ts` — new describe block "UAT round-3 item #8 (non-blocking
  dismiss)" (5 tests): `dismiss()` hides without touching `status`; a dismissed run stays hidden
  through `complete()`/`fail()`; a fresh `begin()` always starts visible even after a prior
  dismissal; `dismiss()` while idle is a harmless no-op.
- `NdaReviewProgressModal.test.tsx` — `renderModal` helper extended with `visible`/`onDismiss`
  params; new describe block "UAT round-3 item #8" (7 tests): `visible=false` renders nothing
  regardless of `status`; the dismiss button renders while running and calls `onDismiss` (not
  `onClose`); the dismiss button is absent on terminal states; Escape routes to `onDismiss`; the
  dialog's ARIA role is `dialog` (non-modal/modal), never `alertdialog` (the OLD blocking variant).
- `ReviewCompleteToast.test.tsx` — new describe block "UAT round-3 item #8 (progress-modal
  notification matrix)" (4 tests): the exact 3-row matrix above (open+visible off-tab suppressed;
  dismissed+on-tab suppressed; dismissed+off-tab fires) plus the unset-signal defensive-default
  case.

## Test results (exact)

```
useAgreementReviewGate.test.ts                                    39/39 PASS
NdaReviewProgressModal.test.tsx + useNdaReviewRunProgress.test.ts  24/24 PASS
ReviewCompleteToast.test.tsx                                        8/8 PASS
ConversationPane.review-chip-depth-ask.e2e.test.tsx (NEW)            3/3 PASS

SpaarkeAi src/components/conversation (58 suites)              580/581 PASS
  1 pre-existing failure: HardSlashExecutor.test.ts "/save-to-matter POSTs ..." — a `< 100ms`
  elapsed-timing assertion; PASSES in isolation (43/43, re-confirmed this task) — the SAME
  documented pre-existing flake task 070's own execution notes named ("flaky under the full-suite's
  machine load, unrelated to this task — no file this task touched is in that suite's import
  graph"). Confirmed unrelated: this task never touches HardSlashExecutor.ts or its imports.

SpaarkeAi full package (npx jest)                               885/886 PASS (92/93 suites)
  Same single pre-existing flake as above — zero NEW failures.

SpaarkeAi typecheck (tsc-surface-gate)                            0 surface-owned errors (73
  pre-existing shared-lib errors, unrelated/deferred — unchanged baseline, matches task 070's own
  report exactly)
```

BFF: not run — zero server files touched this task (`src/server/**` read-only per HARD BOUNDARY,
verified via `git status`/diff review — every changed file lives under
`src/solutions/SpaarkeAi/src/components/` or `src/client/shared/Spaarke.AI.Widgets/src/events/`).
No publish-size measurement needed (§10 BFF Hygiene N/A — no BFF touch).

## Quality gates (self-run, FULL rigor per CLAUDE.md §8)

**code-review (self)**:
- No `any` introduced. New types are fully typed discriminated unions
  (`PendingDepthChoiceTarget`'s `"direct"` variant) or plain booleans (`visible`, `progressVisible`,
  `dismissed`).
- No try/catch-log-rethrow patterns added.
- No defensive code without a concrete failure mode: `runDirectReview`'s `!reviewBindingId`
  early-return and `dispatchDirectReview`'s matching guard both cover the same
  "capability-discovery hasn't resolved / catalog unreachable" case every sibling method
  (`dispatchReview`, `dispatchBothSequentially`) already guards against identically.
- New abstractions (`"direct"` target kind, `dispatchDirectReview`, `runDirectReview`,
  `nda_review_progress_visibility`) are each justified inline (§11, above) and mirror an EXISTING
  shape in the same file/module — none are novel patterns.
- Every widened interface (`AgreementReviewGateController.runDirectReview`,
  `NdaReviewProgressModalProps.visible`/`onDismiss`, `NdaReviewRunProgress.visible`/`dismiss`,
  `WorkspacePaneEvent.progressVisible`) is purely additive — verified by the full 885/886-test
  package run showing zero new failures.
- Comments explain WHY throughout (the "why not reuse dispatchReview" rationale, the ordering-note
  for why there is no race on the notification signal, the dismiss-vs-close distinction).

**adr-check (self)**:
- **ADR-039** (grounded execution, ONE dispatch protocol, closed catalogs): `runDirectReview`/
  `dispatchDirectReview` reuse `dispatchReviewBinding` (`chips.dispatchBinding`) verbatim — no new
  dispatch mechanism. `reviewDepth` is unchanged from task 070 (closed two-value client intent,
  server owns the tier mapping). PASS.
- **ADR-041** (confirmation/completion policy, no double-ask): the direct chip path gets EXACTLY
  ONE new question (the depth ask) — the chip click itself is the type commitment, never re-asked.
  PASS.
- **ADR-021** (Fluent v9, dark mode, tokens only): the new dismiss button + `dismissRow` style use
  `tokens.spacingVerticalS` — no hardcoded colors/hex. PASS.
- **ADR-030** (typed PaneEventBus channels; additive-types rule; prefer subscribing over new
  emits): the new `nda_review_progress_visibility` discriminant is ADDITIVE on the existing
  `workspace` channel (no 5th channel) — the "prefer subscribing" guidance does not forbid a new
  emit when a genuinely new signal is needed (the EXACT precedent is `active_widget_changed`,
  added for the same "signal infrastructure" reason). Documented with a concrete
  cost-of-doing-nothing (§11 above) and a passing regression-proof test suite. PASS.
- **CLAUDE.md §11** (component justification): stated above for both the new gate method and the
  new PaneEventBus signal. PASS.
- **CLAUDE.md §6.5** (ADR conflict resolution protocol): no conflict surfaced. `modalType="non-modal"`
  for a Fluent v9 custom progress surface is already an established codebase pattern
  (`CommunicationActionsApp.tsx`) and fits MODAL-DECISION-CRITERIA.md's Family 2 ("a custom UX
  surface... that doesn't map to a single Dataverse form") without tension.
- **§10 BFF Hygiene**: N/A — zero server-side changes this task.

No Critical or Warning findings.

## Deviations / escalations

**No `<escalation>` trigger fired.** Both fixes were fully navigable as additive, minimal-delta
extensions of already-shipped mechanisms (070's depth-turn machinery for #7; the existing
`useNdaReviewRunProgress` state machine + the existing `active_widget_changed`-style "signal
infrastructure" PaneEventBus pattern for #8).

**Judgment calls made (documented, not silent)**:
1. **New message copy for the direct path, not a parameterized reuse of the existing depth-choice
   message.** `buildAgreementReviewDepthChoiceMessage(displayName)` bakes in "review as
   **{displayName}**" phrasing that assumes a classified type; the direct chip path has none. Wrote
   a small NEW pure function (`buildAgreementReviewDirectDepthChoiceMessage()`) rather than force a
   nonsensical `displayName` through the existing one. The CHIPS (the actual reused "machinery" per
   the task brief) are unchanged.
2. **New PaneEventBus discriminant for the notification matrix**, rather than a simpler
   same-component fix. Considered and rejected: since `NdaReviewProgressModal`'s state lives inside
   `ConversationPane` (conversation/**) and `ReviewCompleteToast` is a SEPARATE component mounted by
   `ThreePaneShell` (shell/**), there is no existing cross-component channel other than the
   PaneEventBus for one to observe the other's visibility. Extending the bus additively (matching
   the EXISTING `active_widget_changed` precedent exactly) was the smallest correct fix; a bespoke
   ad-hoc pub/sub would have violated §11 reuse-first.
3. **Dismiss button shown only while `status === 'running'`**, not also on `complete`/`error`. The
   terminal-state linger timer (900ms complete / 3200ms error) already auto-dismisses almost
   immediately — a manual dismiss affordance there would be visible for well under a second, adding
   visual noise for near-zero value. Documented, not an oversight.
4. **Escape routes to `dismiss()`, never `onClose()`.** A progress indicator that Escape-closes to a
   TERMINAL reset (as if the review had finished/errored) would misrepresent the actual state (the
   review is still running). Dismiss (hide, keep running) is the only correct semantics for a
   light-dismiss gesture on a non-modal progress surface.

No HARD BOUNDARY violations: `.claude/**`, `current-task.md`, `TASK-INDEX.md` — not touched;
`src/server/**` — not touched (read-only, verified); no git commit/push performed.
