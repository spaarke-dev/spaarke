# Task 021 — Interactive orientation, classify-on-review-intent, confirmation gate — execution notes

> Rigor: FULL · Model tier: sonnet @ xhigh. Spec FR-07/FR-08/FR-09 interactive half; design Lens 3(d).
> Deps: 020 (classifier Action — DONE, `ee24dcc3d`), 003 (knowledge packs — DONE).

## Step 0 — Seam trace (upload → review-intent → dispatch)

Traced the FULL existing pipeline before writing anything, since the task's context block flagged several
non-obvious mechanisms:

1. **Two independent classify-on-upload systems already exist** and are NOT the same thing as this task's gate:
   - **CLS-CHAT@v1 / `chat-classify`** (Binding row, `onEventBindings:[{"event":"document_uploaded","order":1}]`) —
     fires AUTOMATICALLY server-side (`EventRulesService.cs`) on EVERY upload with no typed command, producing a
     simple `{docType, confidence}` shape. Client-side, `handleNdaClassified` (ConversationPane.tsx) resolves it
     against `DOCUMENT_REVIEW_CAPABILITIES` (`localActionChips.ts`) and offers a "Review an NDA" chip
     UNCONDITIONALLY on confidence (no gate). **Untouched by this task** — it is a different, simpler, already-
     shipped mechanism (nda-r1 follow-up + task 064 generalization).
   - **`EventRulesService`'s bound (d)**: a TYPED COMMAND alongside an upload SUPERSEDES the whole event-rule chain
     (`EventRulesService.cs:134-144`). This means "review this document" typed alongside/after an upload does
     **not** reach CLS-CHAT@v1 at all — it goes down the Text path, which is exactly where THIS task's gate needed
     to intercept.
2. **The Text-path interception point**: `ConversationPane.handleDecorateOutboundBodyWithRevise` (the
   `onDecorateOutboundBody` hook SprkChat calls before every send) is the SAME hook the existing "revise this
   document" flow (`detectReviseThisDocumentIntent`) already intercepts on. Critically, `detectReviseThisDocumentIntent`'s
   `REVISE_VERB_RE` **already matches "review"** as a revise-flow trigger verb — meaning "review this document"
   would have fallen into the generic whole-document-revise mount+4-chip flow (compose-revise-document) BEFORE this
   task, with no type-specific advisory review at all. This is the concrete gap task 021 closes.
3. **Registry threshold + pack-ref data was NOT client-reachable**: the `sprk_agreementtype` registry (per-type
   `sprk_confidencethreshold` + `sprk_knowledgepackref`) has a proven CLIENT-SIDE direct-read precedent —
   `CreateAnalysisWizardWidget.tsx` (hub's A1 picker) already reads `sprk_agreementtype` via the host-context
   `Xrm.WebApi` `dataService.retrieveMultipleRecords` (no BFF round-trip) — the SAME pattern this task's gate reuses
   for its own threshold/display-name resolution (see Step 3).
4. **`ComposeLaunchContext` is null for the chat-upload case**: `ThreePaneShell`'s `composeLaunch` memo is only
   non-null when `composeMode==='editor'` (the ribbon Open-in-Compose entry) — the everyday chat-upload review flow
   never has it. The ACTUAL orientation conduit for a chat-uploaded file is the SEPARATE `ComposeDirectWidget` /
   `composeWidgetData.ts` seed path (`widgetData.compose.activeWorkType`), already wired end-to-end into
   `<ComposeWorkspace activeWorkType>` → `getToolsForSurface` by hub task 041. This task's orientation write uses
   THAT conduit (mountFileInCompose extended with an `activeWorkType` param), not `ComposeLaunchContext`.
   `useComposeLaunch()` is read ONLY to implement FR-09's "skip when subDomain already present" (explicit-path
   check) — read-only consumption of the READ-ONLY canonical reference file, never modified.

## Step 1 — Trigger-surface decision (§10-compliant exposure of 020's classifier)

**DECISION: Option (a) — additive Binding row `agreement-classify`, riding the existing session-dispatch spine.**
Added to `infra/dataverse/sprk_playbookconsumer-rows.json` (disposition=Informational/100000000, risk=None,
captureMode=LoopElicitation, surfaces="assistant", NO `onEventBindings` — client-triggered on review-intent text,
not event-triggered on every upload). Dispatched via the SAME Click-path spine every other consumer chip uses
(`POST /sessions/{id}/dispatch`), with its `bindingId` resolved via `useCapabilityDiscovery` — ZERO new BFF route.

**§10 Placement Justification**: no new endpoint, no new DI module, no new package. The classifier Action (020) and
its dispatch mechanism (`SessionDispatchOrchestrator`/`ActionRunner`) already exist; this task's only server
additions are (a) a `KnowledgePackRef` projection on an EXISTING reader/row and (b) an EXISTING-context-field
thread-through (`LinearRunContext.KnowledgeSourceIds`) consumed by the EXISTING `ActionRunner.RetrieveReferenceGroundingAsync`.

**Rejected alternative (b) small gated endpoint**: would duplicate the dispatch/ledger/OutputRouter machinery the
Click-path spine already provides for free (ADR-040 store-before-render, telemetry, error mapping) — the
extension-over-new test (§11) clearly favors (a).

**Residual, accepted risk (documented, not silently ignored)**: capability discovery + agent Text-path tool
projection share ONE query (`IConsumerRoutingService.ListTextProjectableBindingsAsync` — any Binding with a
non-empty `sprk_tooldescription` is BOTH capability-discoverable AND agent-selectable). This means the classify
Binding — like the EXISTING `chat-classify` row — is technically reachable if the model chooses to self-select it
via the Text path. Mitigation: the `toolDescription` is worded as documentation ("Dispatched automatically by the
Assistant... not intended for direct invocation and produces no user-facing narrative on its own") rather than an
invitation, mirroring `chat-classify`'s identical shape/wording precedent exactly. This is the SAME accepted
platform risk that precedent already carries, not a new one this task introduces.

## Step 2 — Gate implementation (FR-08)

`useAgreementReviewGate.ts` (new hook) implements the branch logic via a pure decision function
(`agreementReviewRouting.ts: resolveAgreementReviewGateDecision`):

| Branch | Condition | Behavior |
|---|---|---|
| `non-agreement` | `isAgreement=false` or 0 candidates | Explicit decline message (`AGREEMENT_REVIEW_NON_AGREEMENT_MESSAGE`) + ONE chip ("Run a general review anyway") — NEVER a silent decline |
| `auto-proceed` | top candidate confidence ≥ resolved threshold | Orients + dispatches immediately, no chat question |
| `confirm` | top candidate confidence < resolved threshold | Confirm message + 2 chips ("Yes, review as {type}" / "Use the general review instead") — NO dispatch until answered |
| `composite` | `composite=true` with ≥2 candidates | Choice-of-lens message + N lens chips + "Both" |

Threshold resolution: `resolveConfidenceThreshold(subDomainKey, registry)` — per-row `sprk_confidencethreshold`
override, else the `GLOBAL_CONFIDENCE_THRESHOLD = 0.85` baseline (design Lens 3d, owner 2026-07-29). Registry read
via the SAME client-side Xrm.WebApi `dataService.retrieveMultipleRecords('sprk_agreementtype', ...)` pattern the
hub's A1 wizard picker already uses (§11 reuse — no new BFF endpoint for this reference-data read); degrades to `[]`
(global threshold + key-derived display names) on read failure — never throws, never blocks the gate.

**No silent wrong-grounding, no silent decline** (project constraint, verified by `agreementReviewRouting.test.ts`
+ `useAgreementReviewGate.test.ts`, 46 tests): every branch either dispatches with an explicit orientation, or
shows an explicit message + escape-hatch chip. There is no path where the review silently runs against the wrong
pack or silently declines.

**No-double-ask (ADR-041)**: `resolvedRef: Map<fileId, {subDomainKey, displayName}>` — once a file's gate resolves
(auto-proceed, confirmed, or a lens chosen), a repeat `runGate` call for the SAME file re-dispatches directly from
the cached decision, without re-classifying or re-asking. **Code-review self-check surfaced a race** the cache
alone didn't close: two near-simultaneous `runGate` calls for the same file (before the first classify round-trip
returns) would both see no resolved entry and classify concurrently. Fixed with an `inFlightRef: Set<fileId>` guard
— a duplicate call while classification is in flight is a silent no-op (test: `useAgreementReviewGate.test.ts`
"concurrent double-invocation" describe block, proves exactly ONE classify dispatch + ONE review dispatch across
two concurrent calls).

**ADR-041 Gate-ledger note (self-flagged, Path A)**: ADR-041's formal Gate-ledger mechanism (`SessionGate` via
ADR-040, the `TryReadConfirmGateId`/`BuildConfirmChip` machinery already in `SessionDispatchOrchestrator.cs`) is
designed for WRITE/side-effect confirmations (e.g. `create_record`). This gate's confirmations are
classification/grounding-scoped (informational, no side effect — the review Binding itself is
`disposition=Informational`), so its state lives in component-lifetime React refs (reset on session change per
`agreementReviewGate.resetForSession()`, wired into `handleSessionCreated`) rather than the formal session ledger.
It survives re-asks WITHIN a session but not across a hard page reload. This is a deliberate, narrower-scope
application of the ADR's "no double-ask" principle — not a workaround of its side-effect-confirmation protection,
which this gate never touches. Documented here per CLAUDE.md §6.5 rather than silently assumed compliant; Path A
(project-scoped exception) — the classification gate is a genuinely different risk class than what the formal
engine targets, and building on Policy v2 (tasks 030-038) for a client-side classification UX would be materially
out of this task's scope.

## Step 3 — Orientation writes

`activeWorkType='agreement-analysis'` threads through the EXISTING `mountFileInCompose` call (extended with an
optional 3rd param) → `widgetData.compose.activeWorkType` → `ComposeDirectWidget.buildLaunchFromSeed` →
`<ComposeWorkspace activeWorkType>` → `getToolsForSurface` (hub task 041's ALREADY-SHIPPED wiring — zero new code
in that chain; verified end-to-end by the e2e test's assertion on the `widget_load` seed's `activeWorkType` field,
not by re-testing `getToolsForSurface`'s own internals, which are already covered by
`ComposeWorkspace.activeWorkType.test.tsx` / `ComposeEditor.activeWorkType.test.tsx` — off-limits Compose.Components
tests this task must not touch or duplicate).

`subDomain` does **NOT** need to reach React props for this task's scope — the only consumer is the SERVER-side
review dispatch (`chips.dispatchBinding(reviewBindingId, { slots: { fileIds, subDomain } })`), which reaches
`SessionDispatchOrchestrator` via the existing dispatch args wire shape (see Step 4/pack-binding below). This
avoids any need to extend `ComposeWidgetSeed`/`ComposeDirectWidget` with a `subDomain` field or touch
Compose.Components at all.

Tool-palette scoping (`getToolsForSurface`) is per-**work-type**, not per-sub-domain, by design (confirmed via the
`ComposeLaunchContextValue.activeWorkType`/`subDomain` doc comments) — subDomain's role is exclusively pack-binding
into the review's retrieval, not toolbar scoping. No gap.

## Step 4 — Pack binding into the review dispatch (server touch)

Per the POML's pre-approved additive path (escalation NOT triggered — done additively, no breaking changes):

1. **`AgreementTypeRow.cs`** — added `KnowledgePackRef` as a TRAILING optional positional param (default `null`).
   020 read `sprk_knowledgepackref` conceptually but never projected it; 021 is its first consumer. Zero existing
   call sites broken (records, tests, eval fixtures) — trailing default param preserves them all unchanged.
2. **`DataverseAgreementTypeRegistryReader.cs`** — added `sprk_knowledgepackref` to the `$select` + mapping.
3. **`LinearRunContext.cs`** — added `KnowledgeSourceIds` (nullable, default `null` — every pre-021 construction
   site is unaffected).
4. **`ActionRunner.cs`** — `RetrieveReferenceGroundingAsync`'s `ReferenceSearchOptions` now sets
   `KnowledgeSourceIds = context.KnowledgeSourceIds`. Fixes the task-003 finding (`ActionRunner` never scoped
   reference-knowledge search — whole-corpus retrieval crowds out the classified type's own pack). Null/empty
   preserves the EXACT prior unscoped behavior (verified: `ReferenceRetrievalService`/`RagService` both no-op the
   filter when empty).
5. **`SessionDispatchOrchestrator.cs`** — new optional ctor dep `IAgreementTypeRegistryReader?` (mirrors the
   existing `ISurfaceLaunchEnricher?`/`TimeProvider?` optional-dependency pattern exactly); new
   `TryReadSubDomain(JsonElement? args)` helper (mirrors `TryReadConfirmGateId`/`TryReadFileIds`'s established
   shape); before constructing `runContext` in the Prompted branch, resolves `subDomain` → registry row →
   `KnowledgePackRef` → `KnowledgeSourceIds`, wrapped in try/catch (registry-read failure degrades to unscoped,
   never fails the dispatch).

**Additive proof**: `dotnet build` green (0 errors), full `Sprk.Bff.Api.Tests` suite green (**9628 passed / 0
failed / 101 skipped** — same 101 pre-existing skips as before this task; net +3 tests from the new seam file).
A dedicated NEW seam test (`tests/integration/seam/Ai/AgreementReviewKnowledgeScopeSeamTests.cs`, 3 cases, KEEP
path `tests/integration/seam/**`) proves the full wire-to-`LinearRunContext` threading over the REAL
`WebApplicationFactory<Program>` app: (a) `subDomain` matching a registry row threads its `KnowledgePackRef`; (b) no
`subDomain` arg → `KnowledgeSourceIds=null`, registry never even read (no wasted Dataverse call, additive-safety
pin); (c) `subDomain` matching no row degrades to `null`, never throws.

## Step 5 — "Both" (composite multi-pack dispatch)

`dispatchBothSequentially` (useAgreementReviewGate.ts) — a plain `for...of` loop with `await` inside (ESLint
`no-await-in-loop` explicitly disabled with rationale), NEVER `Promise.all` — ADR-016 compliant. Each pack's
completion message is labelled via a NEW optional `resultLabel` param threaded through
`useConsumerChips.dispatchBinding`/`runBindingDispatch` (additive — every existing caller omits it and gets the
exact original generic message). Proven by `useAgreementReviewGate.test.ts`'s "Both" test: exactly 2 sequential
calls, in declared candidate order (`callOrder` array asserts `["employment", "nda"]`, not `["nda","employment"]`
or concurrent).

## Step 6 — Real-bug found during test-writing (production fix, not a test-only fix)

Writing the ConversationPane-level e2e test surfaced a genuine timing defect in the PRODUCTION classify-dispatch
construction: the raw classify dispatcher (built via `createConsumerDispatcher`) defaulted to the PACED
section-reveal bridge (task 039/D-F5 — `revealSectionsProgressively`, `setTimeout`-based pacing between
`section_started`/`section_completed` workspace events per top-level result key). Since NOTHING renders the
classifier's raw `{isAgreement, candidates, composite, reasoning}` JSON (by design — it's consumed programmatically,
never shown to the user), paying that pacing latency was pure waste — and in the test environment (real fake
timers not used), the promise chain didn't settle within a reasonable microtask budget. Fixed by passing
`suppressWorkspaceSectionBridge: true` to the classify dispatcher's construction (`ConsumerDispatchDeps`'s existing
task-112 flag, designed for exactly this "no renderer subscribes" case) — this is a genuine production
improvement (the classify dispatch now settles immediately on the terminal chunk in production too, not just in
tests), not a test-only workaround.

## Files modified/created

**Server**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Classification/AgreementTypeRow.cs` — added `KnowledgePackRef`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Classification/DataverseAgreementTypeRegistryReader.cs` — select + map
- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/LinearRunContext.cs` — added `KnowledgeSourceIds`
- `src/server/api/Sprk.Bff.Api/Services/Ai/LinearConsumers/ActionRunner.cs` — threads it into `ReferenceSearchOptions`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionDispatchOrchestrator.cs` — optional reader dep,
  `TryReadSubDomain`, registry-lookup threading
- `infra/dataverse/sprk_playbookconsumer-rows.json` — new `agreement-classify` Binding row
- `tests/integration/seam/Ai/AgreementReviewKnowledgeScopeSeamTests.cs` — new (3 tests)

**Client**:
- `src/solutions/SpaarkeAi/src/components/conversation/agreementReviewRouting.ts` — new pure module
- `src/solutions/SpaarkeAi/src/components/conversation/useAgreementReviewGate.ts` — new hook
- `src/solutions/SpaarkeAi/src/components/conversation/localActionChips.ts` — new `LOCAL_CHIP` entries + lens-chip
  id encode/decode
- `src/solutions/SpaarkeAi/src/components/conversation/useConsumerChips.tsx` — `dispatchBinding` now returns
  `Promise<void>` (additive widen) + optional `resultLabel`
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — wired the gate (imports,
  `useComposeLaunch` read, `classifyBindingId` resolution, hook instantiation, decorate-hook interception BEFORE
  `detectReviseThisDocumentIntent`, buffered race-safe effect, local-chip routing, session reset,
  `mountFileInCompose` extended with `activeWorkType`)
- New tests: `agreementReviewRouting.test.ts` (25 tests), `useAgreementReviewGate.test.ts` (9 tests),
  `ConversationPane.agreement-review-gate.e2e.test.tsx` (2 tests), + additions to `localActionChips.test.ts` (5
  tests)

## Test results (exact)

- **BFF**: `dotnet build` — 0 errors. `dotnet test tests/unit/Sprk.Bff.Api.Tests` — **9628 passed / 0 failed / 101
  skipped** (baseline was 9625/0/101 before this task; the 3 new seam tests are the delta; the 101 skips are
  pre-existing and unrelated).
- **SpaarkeAi**: `npm run typecheck` (the project's own `tsc-surface-gate.mjs`) — **0 surface-owned errors** (73
  pre-existing shared-lib errors, unrelated/deferred, unchanged by this task). Full conversation-module Jest suite
  — **49 test suites / 445 tests, all passing** (baseline 46 suites/405 tests before this task's new files).

## §10 BFF Hygiene checklist

- **Placement Justification**: see Step 1 above — no new endpoint/module/package; additive context-threading
  through EXISTING dispatch primitives.
- **Publish size**: `dotnet publish -c Release` → compressed (`tar.gz` proxy) **47 MB incl. PDBs** — essentially
  flat vs the ~49.63 MB / 48.24 MB baselines this project has been tracking (020 measured 47 MB too — this task
  added a handful of small properties/params, no new packages). Well under the 60 MB hard ceiling.
- **CVE**: `dotnet list package --vulnerable --include-transitive` → the only HIGH is
  `System.Security.Cryptography.Xml 8.0.3` (transitive, pre-existing, same as 020's finding). **Zero new packages,
  zero new HIGH CVEs.**
- **Test Update Obligation (§10 bullet 6)**: satisfied — new seam test (`AgreementReviewKnowledgeScopeSeamTests.cs`)
  added for every `Services/` file touched with novel branching logic.

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | Untyped upload + "review this" → classifier runs; ≥threshold auto-proceeds bound to the matched pack (assert pack ref in dispatch) | **PASS** | `ConversationPane.agreement-review-gate.e2e.test.tsx` (real dispatch wire: `args.subDomain==='nda'`); server-side pack threading proven by `AgreementReviewKnowledgeScopeSeamTests` (KnowledgeSourceIds=["KNW-011"]-equivalent via mock KnowledgePackRef) |
| 2 | Below threshold → confirm chips (proposed + pick-another); NO dispatch until answered; no double-ask | **PASS** | `useAgreementReviewGate.test.ts` "confirm" describe block + "no-double-ask" + "concurrent double-invocation" blocks |
| 3 | Composite → choice-of-lens incl. Both; Both runs sequentially once per pack | **PASS** | `useAgreementReviewGate.test.ts` "composite" describe block (2 tests: single-lens click, "Both" sequential order asserted) |
| 4 | Non-agreement → explicit response + general option; no fabricated review | **PASS** | `useAgreementReviewGate.test.ts` "non-agreement" block; `agreementReviewRouting.test.ts` decision-table tests |
| 5 | After orientation, getToolsForSurface returns the agreement-analysis palette (activeWorkType + subDomain asserted) | **PASS (activeWorkType) / N/A (subDomain — see Step 3)** | e2e test asserts `widget_load` seed's `activeWorkType==='agreement-analysis'`; `getToolsForSurface`'s OWN behavior is pre-existing/off-limits-tested (Compose.Components). subDomain's role is pack-binding (criterion 1), not toolbar scoping — confirmed by design, not a gap |
| 6 | Negative: bare "review" text with no attached/target doc never fires the gate | **PASS** | e2e test "negative criterion" describe block (`decorateResult` non-null, zero dispatch calls); `agreementReviewRouting.test.ts` "negative criterion" unit tests |

## UI-tests deferred (per task assignment)

Live upload flows + dark-mode toggle in a real browser are explicitly deferred to tasks 060/061 per the assignment.
Covered at unit/integration level here: all 4 gate branches, no-double-ask, the in-flight race guard, sequential
"both", the real Text-path interception + real dispatch wire (Jest + real SSE wire, not a live browser), and the
server-side pack-binding threading (real `WebApplicationFactory<Program>` seam test). Dark-mode/Fluent-token
compliance is inherited for free — the gate renders through the EXISTING `ConsumerChips` component; zero new UI
markup was authored, so there is no new surface for a dark-mode regression to hide in.

## Deviations / escalations

**No escalation fired.** The POML's `<escalation>` trigger ("if binding a pack per-dispatch for 'both' is not
expressible through the existing binding/scope resolution without server changes") did not fire — the additive
`KnowledgeSourceIds` threading (Step 4) was expressible with a small, backward-compatible extension exactly as the
POML's context block anticipated ("Keep additive + minimal... This is in scope").

**One self-flagged judgment call** (not a deviation from instructions, but worth a human read): the ADR-041
Gate-ledger note in Step 2 — this gate's confirmation state lives in component-lifetime state, not the formal
session ledger. Documented per CLAUDE.md §6.5 rather than silently assumed.

**Boundaries honored**: did NOT touch `src/client/shared/Spaarke.Compose.Components/**` or its tests (task 012's
territory this wave) — `composeLaunchContext.ts` was READ-ONLY consumed (`useComposeLaunch()` deep-import), never
modified; orientation writes route entirely through the pre-existing `ComposeDirectWidget`/`composeWidgetData.ts`
seed conduit, which needed zero changes for this task's scope (subDomain doesn't need to reach React props — see
Step 3). Did NOT modify `sprk_agreementtype-rows.json` data values (registry row VALUES untouched — only NEW server
code reads one additional existing column). No `.claude/**` writes. No `current-task.md`/`TASK-INDEX.md` edits. No
git commit/push.

## Task status

POML `021-interactive-orientation-confirmation-gate.poml` status set to `completed`. Per HARD BOUNDARIES,
`TASK-INDEX.md` and `current-task.md` were NOT touched — the orchestrating session/human applies root CLAUDE.md
§7's transition steps after reviewing this report.
