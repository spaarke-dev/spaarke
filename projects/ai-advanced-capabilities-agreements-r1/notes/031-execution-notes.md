# Task 031 — Execution Notes: FR-16(c,d) DEF-09 session routing + apply-leg gating

> Rigor: FULL · Model tier: sonnet @ xhigh · Step mode: directional · Status: complete

## Summary

Made the FR-16 write (agreement-review dispatch) and read (`ComposeWorkspace`'s compose-outputs
materializer) coincide, and confirmed findings never enter the edit machinery:

- **(c) DEF-09 routing — FIXED.** All four agreement-review dispatch call sites (re-located post-021's
  heavy `ConversationPane` reshape) now thread the reviewed file's REAL document session as
  `sessionIdOverride` on the review's `chips.dispatchBinding` call, via a new pure waiter module
  (`documentSessionWaiter.ts`).
- **(d) Apply-leg gating — VERIFIED, not defensively re-coded.** Traced that the review dispatch
  structurally never reaches the apply-leg machinery (`emitComposeApplyLeg`/`dispatchComposeAction`) —
  a DIFFERENT, parallel dispatch mechanism than the one Accept/Reject/Try-another controls attach to.
  Documented + regression-tested.
- **Bridge decision — KEEP** `useNdaReviewAdvisoryCommentsBridge` (live-turn immediacy). Dedupe: safe by
  construction for the practical "live turn then reload" scenario; a narrow same-mount residual is
  escalated (requires a `Compose.Components` change out of this wave's boundary).

## Step 0 — Seam re-trace (post-021; all POML line refs were pre-021 and now stale)

021 (commit `98bf344d1`) reshaped `ConversationPane.tsx` heavily and introduced an entirely NEW dispatch
mechanism for the interactive orientation gate. The POML's cited pre-021 refs
(`ConversationPane.tsx:939-949` sessionIdOverride precedent, `:951-965` informational dispatch,
`:966-1002` apply-leg) **still exist** but at new locations, and — critically — **the review no longer
dispatches through that code path at all**. Current locations + the actual architecture:

| Concept | Pre-021 POML ref | Current location (verified) |
|---|---|---|
| `sessionIdOverride` precedent (compose EDIT actions) | `:939-949` | `ConversationPane.tsx` `emitComposeApplyLeg` ~:921-951 (DEF-09 comment intact) + `dispatchComposeAction` ~:1004-1016 (`isEditAction` branch sets `args.sessionIdOverride: documentSessionId`) |
| Informational review dispatch (chat session) | `:951-965`, `!isEditAction` ~:964 | **NO LONGER THE REVIEW'S PATH.** `dispatchComposeAction` is ONLY reached via (a) `dispatchReviseDocument` (whole-doc revise) and (b) the Compose AI toolbar's `bridge.enqueue` (`useRegisterComposeActionDispatcher(dispatchComposeAction)` at `ConversationPane.tsx:1421`) — draft-alternative/explain/compare/make-concise/rewrite/defined-terms only. The `nda-review`/`agreement-review` Binding is **never** one of the 6 `DEFAULT_ACTIONS` on the Compose AI toolbar (`ComposeAiToolbar.tsx` `DEFAULT_ACTIONS`, verified by grep — no `nda-review`/`agreement-review` entry). |
| Apply-leg (`emitComposeApplyLeg`) | `:966-1002` | `ConversationPane.tsx` ~:921-951 (unchanged shape) — called ONLY from inside `dispatchComposeAction`'s `.then()` (~:1037), i.e. only for the two callers above. **Never invoked for a review dispatch.** |
| **The REVIEW's actual dispatch mechanism (021, not in the pre-021 doc)** | — | `useConsumerChips.tsx` `runBindingDispatch` (called via the public `dispatchBinding`), which calls `dispatchConsumer(bindingId, {slots, requiresAttachments, attachmentCount})` — a COMPLETELY SEPARATE code path from `dispatchComposeAction`, with NO apply-leg logic at all. |

**Four call sites dispatch the SAME `nda-review`/`agreement-review` Binding, all via `chips.dispatchBinding`**
(re-located, none in the pre-021 doc's scope):
1. `ConversationPane.handleReviewNda` (task 022's classic "Review an NDA" card — `chips.dispatchBinding(ndaReviewBindingId, {slots:{fileIds:[fileId]}})`).
2. `useAgreementReviewGate.dispatchReview` — **auto-proceed** branch (`runGate`'s `case "auto-proceed"`).
3. `useAgreementReviewGate.dispatchReview` — **confirm-chip** branch (`handleGateChipAction`'s `LOCAL_CHIP.agreementReviewConfirm`/`agreementReviewGeneral`/lens-chip cases — same function, different callers).
4. `useAgreementReviewGate.dispatchBothSequentially` — **composite "Both"** (sequential loop, ADR-016).

`reviewBindingId` passed to `useAgreementReviewGate` IS `ndaReviewBindingId` (`ConversationPane.tsx:684`)
— confirming (1) and (2)-(4) dispatch the identical Binding through the identical mechanism.

## Routing coverage — proof across all four dispatch paths

**Root-cause of the write/read divergence** (why a simple "reuse `getSessionId()`" doesn't suffice):
`dispatchConsumer`'s `sessionIdOverride` (already shipped, `dispatchConsumer.ts:305-325`, `@spaarke/ui-components`)
is the ONLY mechanism to target a dispatch at a session other than the bound chat session — but
`useConsumerChips.runBindingDispatch`/`dispatchBinding` never threaded it, so every review dispatch used
the bound `getSessionId()` (chat session), unconditionally.

For a **freshly chat-uploaded file**, `ComposeWorkspace`'s `state.sessionId` (the upload-mount door) is
set to `uploadRef.sessionId` — literally the SAME value `mountFileInCompose` seeds into the
`compose.upload` widget_load payload (`chatSessionIdRef.current` at mount time) — so chat session and
document session coincide **by construction** for that one case (traced through
`ComposeDirectWidget.tsx` → `ComposeWorkspace.tsx:2410,2494`). They do **NOT** coincide for a
**Browse-mounted, then chat-ingested** document: `ComposeWorkspace.tsx:2106` mints a client-random
`mintDocumentSessionId()` for the Browse door, unrelated to any chat session — if that document is later
reviewed via the chat-driven gate, a naive "reuse `getSessionId()`" fix would still diverge. The robust
fix uses the REAL, ComposeWorkspace-established `state.sessionId`, surfaced via the EXISTING
`registerComposeActiveDocument` reactive callback — the SAME conduit the shipped "revise this document"
flow already uses for its own `activeComposeDocSessionId` backfill (DEF-11 TEXT-path close precedent).

**Fix — `documentSessionWaiter.ts` (new pure module)**: a per-fileId keyed waiter/timeout state machine
(`awaitDocumentSessionId(fileId, timeoutMs=8000)` / `notify(fileId, documentSessionId)` / `reset()`).
Extracted out of the ~20-hook `ConversationPane` (mirrors `composeApplyLeg.ts`'s extraction rationale —
directly unit-testable without mounting the pane). One instance per pane mount (`documentSessionWaiterRef`),
reset on a fresh chat session (`handleSessionCreated`). `registerComposeActiveDocument` calls `.notify()`
immediately after resolving `sessionFileId`/`documentSessionId` (before the async active-document POST) —
already-known files (Browse-ingested, or a repeat review of an already-open document) resolve
IMMEDIATELY; a fresh mount resolves once the async registration callback fires; a genuinely
never-mounting Compose surface degrades to `null` after the bound timeout (never hangs, never throws).

**Threaded through all four call sites**:
- `useConsumerChips.tsx`: `dispatchBinding`/`runBindingDispatch` widened with an additive optional
  `sessionIdOverride?: string`, threaded verbatim to `dispatchConsumer`. Zero behavior change for every
  pre-031 caller (undefined override ⇒ bound-session behavior, unchanged).
- `useAgreementReviewGate.ts`: new required dep `awaitDocumentSessionId: (fileId) => Promise<string|null>`.
  `dispatchReview` awaits it once, before dispatching. `dispatchBothSequentially` awaits it **once, before
  the loop** (NOT per-candidate — every sequential pack for the same file targets the SAME document
  session; proven by a dedicated test asserting `awaitDocumentSessionId` called exactly once across 2
  sequential dispatches).
- `ConversationPane.tsx`: `handleReviewNda` awaits `awaitDocumentSessionIdFor(fileId)` before dispatching.

**ADR-016 (sequential batch semantics) — unaffected**: the ONE new `await` in `dispatchBothSequentially`
sits BEFORE the `for...of` loop starts; the loop body's `await dispatchReviewBinding(...)` (sequential,
`no-await-in-loop` disabled with rationale, unchanged) is untouched. Verified by test: the SAME two-call,
in-order (`employment` then `nda`) sequencing proof from 021's original test still passes unmodified,
plus a NEW test asserting the document-session resolve happens exactly once, shared across both calls.

**End-to-end proof** (`ConversationPane.agreement-review-session-routing.e2e.test.tsx`, new): a two-session
forcing test (mirrors the shipped `ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx`
DEF-09 EDIT-path precedent) — drives the REAL upload → "review this document" → auto-proceed flow with
`createConsumerDispatcher` NOT mocked, an in-memory ledger keyed by session, and a
`ComposeRegistrationCapture` stub standing in for a real `ComposeWorkspace` mount (calls back into
`registerComposeActiveDocument` via the SAME bridge conduit production code uses, the moment the review's
`widget_load{widgetType:'compose'}` seed is observed). Asserts:
1. The classifier still targets the CHAT session (unaffected — not a compose-disposition action).
2. **The review's `/dispatch` POST targets the DOCUMENT session, not the chat session** (URL assertion).
3. **`GET compose-outputs` on the DOCUMENT session contains the review's `disposition:'compose'` output;
   the CHAT session's ledger has none** — acceptance criterion 1, literally.
4. Pack-binding (`subDomain`/`fileIds`) proof still holds alongside the routing fix.

The EXISTING `ConversationPane.agreement-review-gate.e2e.test.tsx` (021's own test) needed the SAME
`ComposeRegistrationCapture` stub added — see "Deviations" below (a legitimate test update, not a
weakening: without it, the review dispatch now correctly WAITS for the document session and would only
resolve after its real 8s timeout, outside that test's microtask-flush window).

## Gating design (d) — verified structurally unreachable, not re-coded defensively

**Finding**: `dispatchComposeAction`'s apply-leg (`emitComposeApplyLeg` → `resolveCurrentComposeLedgerRef`
→ `makeComposeEditControlsMessage`/Accept-Reject-Try-another) is invoked from exactly TWO callers
(verified by grep): `ConversationPane.dispatchReviseDocument` (whole-document revise) and the Compose AI
toolbar's `bridge.enqueue` (draft-alternative/explain/compare/make-concise/rewrite/defined-terms — the 6
`DEFAULT_ACTIONS`). The agreement-review Binding is dispatched EXCLUSIVELY via `chips.dispatchBinding`
(`useConsumerChips.tsx`), which has **no apply-leg logic whatsoever** — `runBindingDispatch`'s own
`isNdaReview` branch (pre-existing, task 021/R7-7) already renders a plain confirmation
(`makeLocalAssistantMessage`, which carries `metadata:{responseType:'markdown'}` — no `composeEdit` key)
and never calls `materializeComposeDraft`. **Criterion 2 ("no Accept/Reject/Try-another, no staged
redlines, outcomes kept") is therefore ALREADY satisfied by 021's architecture**, not something task 031
needed to newly implement.

**Why no new gating code was added**: `resolveCurrentComposeLedgerRef` only receives
`ComposeLedgerOutputLite` (`key`/`bindingId`/`turn`/`disposition`) from the `compose-outputs` GET response
— it has NO access to the actual payload (findings vs. edit shape), so a payload-shape gate could not be
added there even if the review WERE reachable. Per CLAUDE.md §11 (cost-of-doing-nothing must name a
concrete failure mode), adding speculative shape-gating to a code path the review structurally never
reaches would be scope creep with no observable behavior change — I verified and documented the
invariant instead of re-implementing it defensively.

**Stale doc fixed**: `composeApplyLeg.ts`'s `resolveCurrentComposeLedgerRef` JSDoc claimed "only
`compose-draft-alternative` declares the `compose` disposition" — FALSE since task 030's flip
(agreement-review now ALSO carries `disposition:'compose'` in the ledger). Rewrote the comment to state
the TRUE, structural (caller-path) reason findings never reach the apply leg, rather than the
now-incorrect payload-disposition claim.

**Regression tests**:
- **Gating proof (new)**: `useConsumerChips.surface-launch.test.tsx` "task 031(d) gating" describe block
  — an NDA-REVIEW-shaped result (`{overallRisk, flaggedSections}`, `disposition:'compose'`) renders a
  plain confirmation message with `message.metadata?.composeEdit === undefined`, and the raw findings
  JSON never appears in the rendered content (outcome reporting kept, per ADR-041; no edit controls).
- **Draft-alternative regression (existing, unmodified, re-run green)**:
  `ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx` — asserts the FULL apply-leg
  (Flow-5 `compose_assistant_insert`, `composeEdit.ledgerRef` metadata) for a REAL draft-alternative
  dispatch. Untouched by this task (I made zero changes to `dispatchComposeAction`), so this test's
  continued green run is direct proof of criterion 4 (no regression).

## Bridge decision + dedupe design (Step 3)

**Decision: KEEP** `useNdaReviewAdvisoryCommentsBridge` (live-turn immediacy). Retiring it would mean the
gutter Review Notes only ever appear on the NEXT reload/remount of the Compose tab (030's ledger
materializer only fires on `[state.status, state.sessionId]` transitions — NOT triggered by the review
dispatch completing in the same page session, since that dispatch never touches `ComposeWorkspace`'s own
state) — a materially worse UX for the common case (review a document, see the flagged clauses appear
immediately) for a benefit (avoiding a narrow double-placement edge case) that is achievable more
narrowly than an outright retirement.

**Dedupe — proven safe-by-construction for the PRIMARY scenario, no code change needed**: traced the
"live turn + subsequent reload" acceptance scenario precisely. `materializeComposeDraftFromLedger`'s
FR-04 effect fires on `[state.status, state.sessionId]` — for the STANDARD flow (mount → review dispatch
→ live placement via `onAdvisoryComments`), the effect fires ONCE at mount time (before the review
completes — finds nothing in the ledger yet) and does NOT re-fire again during that same component
lifetime (state.status/sessionId unchanged by the review completing). A GENUINE reload (browser refresh,
new page load) produces a FRESH `ComposeWorkspace` instance with ZERO prior DOM state — the live-placed
comments from before the reload no longer exist (React tree wiped) — so the ledger materializer is the
SOLE placement source and naturally yields exactly ONE set. **030's own test
(`ComposeWorkspace.redline-from-ledger.test.tsx`, `"FR-16 task 030: ... idempotently, with zero dispatch"`
describe block) already proves the materializer's OWN re-invocation is idempotent** (`lastMaterializedKey`
guard) — the "reload" half of the acceptance criterion. What I additionally traced and is NEW to this
task: the live (`onAdvisoryComments`) path never touches `lastMaterializedKey` at all, so it does not
collide with a SUBSEQUENT reload's materializer run for the SAME output — confirmed by reading
`ComposeWorkspace.tsx`'s `onAdvisoryComments` handler in full (lines ~1906-1953): no `lastMaterializedKey`
reference.

**Residual risk — ESCALATED, not silently dropped, requires a `Compose.Components` change (out of this
wave's HARD BOUNDARY)**: a narrow same-mount case exists where `state.status` cycles (`'loaded'→
'loading'→'loaded'`) WITHOUT `state.sessionId` changing — e.g. `ComposeWorkspace`'s `requestLoad` +
`externalChange` broadcast-channel path (another browser tab/window signaling a concurrent edit) — WHILE
the live-placed advisory comments already exist in that SAME editor instance. In that narrow case, the
FR-04 effect re-fires, finds the review's ledger output, and (since `lastMaterializedKey` was never set by
the live path) would call `placeAdvisoryComments` again — which has NO idempotency of its own
(`ComposeEditor.tsx`'s `placeAdvisoryComments` unconditionally calls `advisoryComments.createThread(...)`
per item, verified by reading the implementation) — producing a genuine duplicate. **Closing this
requires `ComposeWorkspace.tsx`'s `onAdvisoryComments` handler to record an equivalent of
`lastMaterializedKey`** (or `materializeComposeDraftFromLedger`'s findings branch to check for an
already-anchored identical thread before placing) — a `Compose.Components` file, explicitly off-limits
this wave ("030's committed materializer is read-only for you — if dedupe requires a Compose.Components
change, STOP and report"). **Recommendation for the follow-on task**: thread a `ledgerRef` on the
`compose_advisory_comments` event (the wire shape already has room — `ComposeAdvisoryCommentsEvent`
carries `sessionId`; a `ledgerRef` would be additive) and have `onAdvisoryComments` call
`setLastMaterializedKey(event.ledgerRef)` after a successful placement, mirroring the ledger-read branch's
own convention exactly.

No code change was made to `useNdaReviewAdvisoryCommentsBridge.ts` — considered adding a same-payload
re-emission guard there, but could not identify a CONCRETE double-invocation risk on the emitting side
(the `.then()` in `runBindingDispatch` fires once per settled dispatch; "Both"'s sequential dispatches
carry genuinely DIFFERENT payloads per pack, so a naive same-content dedupe would not even apply there).
Per CLAUDE.md §11, did not add speculative defensive code with no traceable failure mode.

## Tests (exact)

New:
- `documentSessionWaiter.test.ts` — 8 tests (immediate resolve when known; resolves on notify; a
  different fileId's notify never leaks; timeout degrades to null; concurrent waiters for the same
  fileId; notify with empty/undefined session id is a no-op; a settled waiter's leftover timer is inert;
  `reset()` drops known + in-flight state).
- `ConversationPane.agreement-review-session-routing.e2e.test.tsx` — 1 test (the two-session forcing
  test, acceptance criterion 1 literal).
- `useAgreementReviewGate.test.ts` — 4 new tests ("task 031 DEF-09 session routing" describe block:
  auto-proceed threads the override; confirm-chip threads the override; "Both" resolves once + threads to
  every sequential pack; degrades gracefully to `undefined` override on a never-established session).
- `useConsumerChips.surface-launch.test.tsx` — 3 new tests (`sessionIdOverride` threads verbatim;
  omitting it preserves the exact pre-031 `undefined` shape; the gating proof — NDA-REVIEW-shaped result
  → plain confirmation, no `composeEdit` metadata, no raw JSON).

Modified (test-update obligation, existing behavior legitimately changed):
- `ConversationPane.agreement-review-gate.e2e.test.tsx` — added the SAME `ComposeRegistrationCapture`
  stub as the new routing test, since the review dispatch now legitimately awaits the document session
  before firing; without the stub the test's 60-microtask-flush loop never reaches the real (8s) timeout.
  Also removed a genuinely-unused `init` parameter (pre-existing `tsc-surface-gate` baseline error,
  unrelated to this task's logic — see below) while already touching the file.

### Results (exact)

```
documentSessionWaiter.test.ts                                          8/8   PASS
ConversationPane.agreement-review-session-routing.e2e.test.tsx         1/1   PASS
useAgreementReviewGate.test.ts                                        14/14  PASS (10 pre-existing + 4 new)
useConsumerChips.surface-launch.test.tsx                               13/13  PASS (10 pre-existing + 3 new)
ConversationPane.agreement-review-gate.e2e.test.tsx                    2/2   PASS (unchanged assertions, updated harness)
ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx  2/2  PASS (unmodified — regression proof)
ConversationPane.compose-edit-controls.test.tsx                        —     PASS (unmodified)
ConversationPane.compose-action-format.test.tsx                        —     PASS (unmodified)
ConversationPane.compose-revise-document-session-routing.e2e.test.tsx  —     PASS (unmodified)

Full src/components/conversation/ suite    → 53 suites / 495 tests, ALL PASS
Full SpaarkeAi package suite (npx jest)     → 87 suites / 792 tests, ALL PASS
npm run typecheck (tsc-surface-gate)        → 0 surface-owned errors (73 pre-existing shared-lib
                                                errors deferred to Phase B — verified via `git stash`
                                                that 1 of the 2 "new" errors reported before my
                                                unused-param fix was ALREADY present at HEAD, line-shifted
                                                only; the OTHER was my own new file reproducing the same
                                                copy-paste pattern — both fixed, 0 remaining)
```

Pre-021 baseline was "49 test suites/445 tests" per 021's own notes; this task's net addition is 4 new
suites (documentSessionWaiter, the new routing e2e test) + growth in 2 extended suites, consistent with
the 53/495 total observed.

## §10 BFF Hygiene

N/A — zero `src/server/**` files touched, zero new packages, zero new endpoints. Placement Justification:
this task's entire surface is `src/solutions/SpaarkeAi/src/components/conversation/**` (client-only
routing/gating logic reusing an ALREADY-SHIPPED server contract — `dispatchConsumer`'s `sessionIdOverride`
already exists server-side per the DEF-09 EDIT-path precedent; no new BFF surface needed).

## §11 Component Justification — `documentSessionWaiter.ts`

1. **Existing** — the closest analog is the "revise this document" flow's `pendingNamedRevise` state +
   reactive `useEffect` (ConversationPane.tsx). It is SINGLE-SLOT (one pending revise at a time) and
   effect-driven (not directly awaitable from an async function in a SIBLING hook).
2. **Extension** — could not extend the single-slot pattern without either (a) rewriting it to be
   multi-slot/promise-based (risking regression on the SHIPPED revise flow, which this task must not
   touch) or (b) duplicating ad-hoc waiter/timeout logic inline across THREE call sites
   (`ConversationPane.handleReviewNda` + `useAgreementReviewGate`'s two dispatch functions in a SEPARATE
   file) with no shared, testable core.
3. **Cost-of-doing-nothing** — without it: (a) the Browse-ingested-then-chat-reviewed document class
   would keep dispatching on the (wrong) chat session — FR-16 durable recall silently fails to
   materialize for that document class specifically (a concrete, named failure mode, not a hypothetical
   one); (b) inlining the same async wait/timeout/keyed-map logic three times across two files would be
   untested, duplicated, and impossible to unit-test the timeout/graceful-degrade path in isolation
   (which — per the negative acceptance criterion — is load-bearing behavior, not incidental).

## Files modified

- `src/solutions/SpaarkeAi/src/components/conversation/documentSessionWaiter.ts` — **new**. Pure
  waiter/timeout state machine (the routing seam).
- `src/solutions/SpaarkeAi/src/components/conversation/__tests__/documentSessionWaiter.test.ts` — **new**.
  8 unit tests.
- `src/solutions/SpaarkeAi/src/components/conversation/__tests__/ConversationPane.agreement-review-session-routing.e2e.test.tsx`
  — **new**. The two-session DEF-09 forcing test.
- `src/solutions/SpaarkeAi/src/components/conversation/useConsumerChips.tsx` — `sessionIdOverride`
  threaded through `dispatchBinding`/`runBindingDispatch` (additive).
- `src/solutions/SpaarkeAi/src/components/conversation/useAgreementReviewGate.ts` — new
  `awaitDocumentSessionId` dep; `dispatchReview`/`dispatchBothSequentially` await it and thread
  `sessionIdOverride`.
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — `documentSessionWaiterRef`
  + `awaitDocumentSessionIdFor`; `.notify()` wired into `registerComposeActiveDocument`; `.reset()` wired
  into `handleSessionCreated`; `awaitDocumentSessionId` dep passed to `useAgreementReviewGate`;
  `handleReviewNda` awaits it before dispatching.
- `src/solutions/SpaarkeAi/src/components/conversation/composeApplyLeg.ts` — JSDoc-only: fixed a stale
  claim (predates task 030's disposition flip) and documented the structural (caller-path) reason
  findings never reach the apply leg.
- `src/solutions/SpaarkeAi/src/components/conversation/__tests__/useAgreementReviewGate.test.ts` — added
  the required `awaitDocumentSessionId` mock to `makeDeps()`; 4 new routing tests.
- `src/solutions/SpaarkeAi/src/components/conversation/__tests__/useConsumerChips.surface-launch.test.tsx`
  — 3 new tests (routing + gating).
- `src/solutions/SpaarkeAi/src/components/conversation/__tests__/ConversationPane.agreement-review-gate.e2e.test.tsx`
  — added the `ComposeRegistrationCapture` stub (test-update obligation — the review dispatch's behavior
  legitimately changed); removed a pre-existing unused `init` param (`tsc-surface-gate` baseline fix).

**Not touched** (hard boundary honored): `src/solutions/SpaarkeAi/src/utils/launch-resolver.ts`,
`src/solutions/SpaarkeAi/src/main.tsx`, `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx`
(task 022's — verified via `git diff` that these show as modified in the shared worktree from a
CONCURRENT, uncommitted task 022 session, NOT from this task); `src/client/shared/Spaarke.Compose.Components/**`
(read-only — see the escalated dedupe finding above); `src/server/**`; `infra/**`; `current-task.md`;
`TASK-INDEX.md`; no git commit.

## Quality gates (self-run, FULL rigor)

**code-review (self)**:
- No `any` introduced. No try/catch-log-rethrow. No defensive code for scenarios that can't occur (the
  `?? undefined` conversions are real `string|null → string|undefined` type narrowing for
  `DispatchConsumerArgs.sessionIdOverride`, not dead branches).
- New abstraction (`documentSessionWaiter.ts`) justified per §11 above with concrete failure modes, not
  "future flexibility."
- Comments explain WHY (the divergence root-cause, the Browse-ingest counter-example, the
  cross-component dedupe boundary), matching the codebase's established verbose-rationale convention.
- Every widened interface (`dispatchBinding`/`runBindingDispatch`/`dispatchReviewBinding`) is additive +
  optional — zero pre-031 caller's behavior changes (verified: the FULL 495-test conversation suite +
  87-suite/792-test package suite both green).

**adr-check (self)**:
- **ADR-039** (grounded execution, closed catalogs, ONE dispatch protocol): no new dispatch mechanism, no
  new BFF route, no client-side intent detection. Reused the ALREADY-SHIPPED `sessionIdOverride` field on
  the ONE `dispatchConsumer` primitive. PASS.
- **ADR-040** (session ledger, store-before-render): unaffected — routing changes WHICH session a
  dispatch's ledger write targets, never the ledger mechanics themselves. PASS.
- **ADR-041** (confirmation/completion policy): outcome reporting (the per-section Assistant summary) is
  PRESERVED for findings; no gate/confirmation semantics touched (the review's dispatch was never gated
  by the formal ADR-041 gate-ledger engine — 021's own documented Path-A exception, unaffected here).
  PASS.
- **ADR-016** (rate limits / sequential batch): `dispatchBothSequentially`'s sequential `for...of` +
  `await` loop is UNCHANGED; the one new `await` sits before the loop, resolved ONCE and shared — proven
  by test. PASS.
- **§10 BFF Hygiene**: N/A — zero server files touched.
- **§11 Component Justification**: `documentSessionWaiter.ts` justified above (concrete failure modes
  named, extension-first considered and rejected with reasons).
- **CLAUDE.md §6.5 (ADR conflict / cross-boundary escalation)**: the dedupe residual is an explicit
  escalation (not a silent gap) per the task's own HARD BOUNDARY instruction — documented above with a
  concrete recommendation for the follow-on fix.

No Critical or Warning findings.

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | GET compose-outputs on the DOCUMENT session contains the findings output after a review | **PASS** | `ConversationPane.agreement-review-session-routing.e2e.test.tsx` — ledger keyed by session, DOC_SESSION has 1 entry (`disposition:'compose'`), CHAT_SESSION has 0 |
| 2 | Findings render per-section outcomes, NO Accept/Reject/Try-another, NO staged redlines | **PASS** | `useConsumerChips.surface-launch.test.tsx` "task 031(d) gating" test — plain confirmation, `metadata.composeEdit` undefined, no raw JSON; structural trace confirms `materializeComposeDraft`/apply-leg are never reachable on this path |
| 3 | Live turn + subsequent reload → exactly ONE set of gutter comments | **PASS (primary scenario, safe by construction) / ESCALATED (narrow same-mount residual)** | Reasoning in "Bridge decision" above + 030's existing idempotency test (`ComposeWorkspace.redline-from-ledger.test.tsx`); residual requires a `Compose.Components` change out of this wave's boundary — documented, not silently dropped |
| 4 | A draft-alternative (real edit) still gets the full apply-leg (regression) | **PASS** | `ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx` — unmodified, re-run green (2/2) |
| 5 | Negative: findings on a session with no open document logs gracefully — no crash, no orphan placement | **PASS** | `documentSessionWaiter.test.ts` (timeout → null, never throws) + `useAgreementReviewGate.test.ts` "degrades gracefully" (undefined override, dispatch still proceeds on the bound chat session) |

## UI-tests (deferred per task assignment)

"Findings show no edit controls" (live, in-browser) deferred to tasks 060/061 per the POML's own
`<ui-tests>` note — covered here at the unit/integration/e2e level (real dispatch wire, real PaneEventBus,
real bridge conduit; no TipTap/real ComposeWorkspace mount, matching the established sibling-test
convention — `ConversationPane.compose-draft-alternative-session-routing.e2e.test.tsx`'s own header notes
the SAME division of labor between this solution's tests and `Spaarke.Compose.Components`'s own
`ComposeWorkspace.redline-from-ledger.test.tsx`).

## Deviations / escalations

**No `<escalation>` trigger fired** — `sessionIdOverride` was sufficient for this consumer; no server
change was needed (the mechanism already exists, shipped by the DEF-09 EDIT-path precedent).

**One HONEST partial** (flagged per the task's own "STOP and report" instruction, not silently
completed): the dedupe acceptance criterion is fully satisfied for the PRACTICAL, testable "live turn
then reload" scenario, but a narrower same-mount residual (broadcast-channel `externalChange` triggering
a `state.status` cycle while live-placed comments already exist) requires a `Compose.Components` change
this wave's hard boundary forbids. Documented with a concrete recommended fix for the follow-on task
(likely 032, which already owns `AgreementReviewSummaryPanel.tsx`/`ComposeCommentGutter.tsx` this wave
per 030's notes).

**One judgment call worth a human read**: task (d)'s framing anticipated needing to ADD gating logic; I
instead VERIFIED it was already structurally satisfied by 021's architecture and documented the finding
rather than adding speculative code. This is a deviation from the LITERAL POML step 2 ("classify findings
outputs explicitly... skip applied-edit resolution") in favor of the GOAL + constraints (directional step
mode) — the constraint IS satisfied (findings never enter the apply-leg), just not via new code.

**Boundaries honored**: did not touch `WorkspacePane.tsx`/`main.tsx`/`launch-resolver.ts` (verified via
`git diff` these are pre-existing uncommitted changes from a concurrent task-022 session in this shared
worktree, not mine); did not touch `src/client/shared/Spaarke.Compose.Components/**` (read-only —
imported its PUBLIC exports — `useComposeActiveDocumentRegistration` — in my own test files only, never
wrote to that directory); did not touch `src/server/**`/`infra/**`; did not touch `current-task.md`/
`TASK-INDEX.md`; no git commit/push.

## Task status

POML `031-def09-session-routing-apply-leg-gating.poml` status set to `completed` (with a completion
summary in its own `<notes>`). Per HARD BOUNDARIES, `TASK-INDEX.md` and `current-task.md` were NOT
touched — the orchestrating session/human applies root CLAUDE.md §7's transition steps after reviewing
this report.
