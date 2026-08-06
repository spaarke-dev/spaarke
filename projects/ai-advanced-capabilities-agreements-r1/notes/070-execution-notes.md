# Task 070 — UAT2 review-depth selector — execution notes

> Rigor: FULL · Model tier: sonnet @ high · Status: complete (2026-08-03).
> Owner-approved UAT round-1 follow-up: user-selectable review depth (Quick ~20s / Thorough
> ~2-3min) with expected timing, per `notes/uat-round1-2026-08-03.md` item #1 (the 135s-review
> finding) and the parent brief. Studied `notes/021-execution-notes.md`, `023-execution-notes.md`,
> `031-execution-notes.md`, `033-execution-notes.md` first, per instruction.

## Summary

`reviewDepth: 'quick'|'thorough'` is a closed, client-authored per-run intent threaded through the
SAME dispatch-args wire shape task 021 already established for `subDomain` (`slots.reviewDepth`,
never a model/deployment name — ADR-039). Server-side, `SessionDispatchOrchestrator` maps it to an
`AiModelTier` override composed into the EXISTING task-011 precedence chain (no second routing
mechanism). Client-side, the interactive gate (021/023's `useAgreementReviewGate.ts`) inserts
exactly ONE extra chip turn per branch, never a double-ask; the wizard door (033's auto-run bridge)
carries the depth choice on its existing hand-off seed so the auto-run stays a true auto-run (no
post-open ask). A visible "Quick scan — not a full advisory review" caveat rides the review's
existing chat confirmation message when `reviewDepth==='quick'`.

## Step 0 — Seam trace (per-branch chip-flow design)

Traced the FOUR real dispatch call sites for the `nda-review`/`agreement-review` Binding (per 031's
notes): `ConversationPane.handleReviewNda` (the classic "Review an NDA" card, CLS-CHAT@v1 — a
SEPARATE, simpler, un-gated mechanism 021 explicitly left untouched), and the THREE call sites
inside `useAgreementReviewGate.ts` (`dispatchReview`'s auto-proceed/confirm branches,
`dispatchBothSequentially`'s composite "Both", and `runExplicit`'s deterministic bind). **Scoping
decision**: this task's depth selector covers the gate's three branches (confirm/auto-proceed/
explicit) + composite + the wizard door — mirroring 021's own precedent, `handleReviewNda`'s classic
card click is left OUT OF SCOPE this wave (same reasoning 021 used: a different, simpler,
already-shipped mechanism; adding depth there would need its own chip-turn design for a card-click
affordance, not a chat-turn). Documented here as a deliberate, considered call — not a silent gap.

## Chip-flow design per branch (CRITICAL UX RULE: never a double-ask)

| Branch | Pre-070 behavior | Post-070 behavior |
|---|---|---|
| **confirm** (below-threshold classify) | 2 chips: "Yes, review as {type}" / "Use the general review instead" | **3 chips, SAME turn**: "Review as {type} — Quick (~20 sec)" / "Review as {type} — Thorough (~2–3 min)" / "Use the general review instead" (unchanged, defaults Thorough — see rationale below) |
| **auto-proceed** (≥threshold classify) | Immediate dispatch, no chat question | **Insert ONE depth-choice turn** ("Ready to review as **{type}**. How deep should the review go?" + Quick/Thorough chips) before dispatching |
| **explicit door** (`runExplicit`, TEXT-path, no depth supplied) | Immediate deterministic dispatch, no chips | **Insert ONE depth-choice turn** (same message/chips as auto-proceed) before dispatching. The non-blocking classifier sanity-check still fires in parallel, unaffected. |
| **explicit door** (`runExplicit`, depth PROVIDED — wizard auto-run) | N/A (didn't exist) | **Dispatches IMMEDIATELY at the provided depth — no ask.** Inserting a post-open ask here would defeat FR-17 ("no manual re-upload, review auto-runs"). |
| **composite** (choice-of-lens / "Both") | Lens/Both chip → immediate dispatch | Lens/Both chip → **insert a FOLLOW-UP depth-choice turn** (own considered call — see below), then dispatch(es) at the picked depth |
| **non-agreement** (decline) | 1 chip: "Run a general review anyway" (→ general fallback) | Unchanged — general fallback defaults Thorough (same rationale as confirm's general chip) |
| **repeat/no-double-ask** (ADR-041 cached resolution) | Re-dispatches directly from cache | Re-dispatches directly from cache, **defaults to Thorough** (no re-ask; documented trade-off below) |
| **wizard door** (auto-run hand-off) | Auto-runs at (implicit) default | Small additive Review Depth radio on the "Analysis Details" step; hand-off seed carries the picked depth; auto-run dispatches at that depth immediately |

### Design decisions worth a human read

1. **General-review escape hatch NOT depth-split.** Both the below-threshold "pick-another" chip
   and the non-agreement decline's "Run a general review anyway" chip stay single, defaulting to
   Thorough. Rationale: both are rare pick-another/escape paths (the classifier's own suggestion was
   declined); splitting them would add a 4th/2nd chip for a path most users never take. Documented,
   not an oversight.
2. **Composite gets a FOLLOW-UP turn, not a combined one.** The task's literal text names only
   "gate"(confirm)/auto-proceed/explicit for the two named patterns ("same turn" vs "insert one
   turn"); composite isn't explicitly named. Combining depth into the lens-choice turn would multiply
   chips combinatorially (N lenses × 2 depths + "Both" × 2) for an already-dense turn — so composite
   follows the "insert one turn" pattern as a follow-up, after the lens/Both pick. This is my own
   considered call, applying the task's own rationale to an unnamed branch.
3. **Resolved-cache repeat dispatch defaults to Thorough, never re-asks.** Both `runGate`'s and
   `runExplicit`'s "already resolved" fast-path (ADR-041 no-double-ask) re-dispatch directly without
   any new question — including depth. This mirrors the PRE-existing "confirm" branch's own
   no-re-ask-anything-once-resolved timing exactly; a genuine per-file "remember the last depth too"
   enhancement is a nice-to-have follow-on, not required by the acceptance criteria (which are about
   the first kick-off flow).
4. **Depth resolution timing mirrors "confirm"'s existing pattern.** `resolvedRef`/
   `lastResolvedSubDomainKeyRef` are set only once a depth chip is actually answered (not at
   auto-proceed/lens-pick detection time) — consistent with how "confirm" already deferred
   `resolvedRef` until its own chip fired. A rapid double "review this document" while a depth choice
   is pending re-classifies once more (pre-existing "confirm" limitation, not newly introduced).

## Server: dispatch + resolver mapping

`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionDispatchOrchestrator.cs`:

- New `TryReadReviewDepth(JsonElement? args): string?` — mirrors `TryReadSubDomain`'s exact shape
  (tolerant string extraction, never throws).
- New `ResolveReviewDepthModelTierOverride(string? reviewDepth): AiModelTier?` — `"quick"` (case/
  whitespace-tolerant) → `AiModelTier.Standard`; `"thorough"` → EXPLICITLY `AiModelTier.Reasoning`
  (self-contained, not merely a catalog-default pass-through); anything else (absent/malformed) →
  `null` (no override — server-side reject/default, never a client-named model, ADR-039).
- Composed into the EXISTING task-011 precedence chain at the `effectiveModelTier` computation:
  `request.ModelTierOverride ?? reviewDepthModelTier ?? binding.ModelTierOverride ?? action.ModelTier`.
  `reviewDepthModelTier` sits ABOVE the maker-set Binding override and the Action's own catalog
  default (a genuine per-run user choice on the review kick-off UI should win over static config) but
  BELOW `request.ModelTierOverride` (the Assistant's separate global runtime picker, task 011 — a
  different UI surface that never fires alongside a `reviewDepth` arg in practice, but stays outermost
  if it ever does). `ModelTierDeploymentResolver` (inside `ActionRunner`) remains the ONE
  tier→deployment resolver — this only selects WHICH tier intent applies for this run, exactly as the
  task specified.
- Zero behavior change for every existing caller: `reviewDepth` is read from `request.Args`, which
  only the agreement-review chip flow populates — every other Binding's dispatch computes
  `reviewDepthModelTier = null` and is byte-identical to pre-070.

**ADR-016 / ADR-039 note per project constraint**: the catalog's default tier (Reasoning, per
`agreement-review.action.json`'s `modelTier: "Reasoning"`) still governs whenever `reviewDepth` is
absent or `"thorough"` — no ADR amendment needed (compatible per constraint text). ADR-039: closed
enum only, server validates, client never names models/deployments — `reviewDepth` is a two-value
product intent, not a tier/deployment name; the SERVER owns the tier mapping entirely.

## Server: tests (exact)

New file `tests/integration/seam/Ai/AgreementReviewDepthModelTierSeamTests.cs` (KEEP path
`tests/integration/seam/**`) — reuses `AgreementReviewKnowledgeScopeSeamFixture` (021's fixture,
§11 reuse-first; extended additively with a `CapturedAction` property + callback capture, zero
behavior change to 021's own 3 tests). Over the REAL app (`WebApplicationFactory<Program>`, real
`SessionDispatchOrchestrator`), 4 tests:

1. `reviewDepth:"quick"` → effective `AnalysisAction.ModelTier == Standard` (overrides a seeded
   Reasoning catalog default).
2. `reviewDepth:"thorough"` → effective tier `== Reasoning` (explicit, not merely pass-through).
3. No `reviewDepth` arg → effective tier `== Reasoning` (the seeded catalog default, untouched —
   additive-safety pin).
4. Invalid `reviewDepth` (`"blazing-fast"`) → dispatch still `200 OK`, effective tier `== Reasoning`
   (degrades to no override, never rejects the dispatch).

```
AgreementReviewDepthModelTierSeamTests (new)      4/4  PASS
AgreementReviewKnowledgeScopeSeamTests (021, extended fixture, unmodified assertions)  3/3  PASS
```

## Client: chip flow implementation

**`agreementReviewRouting.ts`** (pure, `agreementReviewRouting.test.ts` covers): `ReviewDepth` type,
`DEFAULT_REVIEW_DEPTH='thorough'`, `normalizeReviewDepth` (tolerant coercion — anything but the
literal `"quick"` → Thorough), `buildAgreementReviewDepthChoiceMessage`,
`buildAgreementReviewDepthChoiceChips` (2 generic Quick/Thorough chips), and
`buildAgreementReviewConfirmChips` rewritten to 3 chips (Quick-confirm / Thorough-confirm / general).

**`localActionChips.ts`**: `agreementReviewConfirm` (single id) SPLIT into
`agreementReviewConfirmQuick`/`agreementReviewConfirmThorough`; new
`agreementReviewDepthQuick`/`agreementReviewDepthThorough` (the standalone follow-up turn's ids,
shared by auto-proceed / explicit-door-ask / composite-post-pick).

**`useAgreementReviewGate.ts`**: `dispatchReview`/`dispatchBothSequentially` take a required
`reviewDepth: ReviewDepth` param, threaded into `slots: { fileIds, subDomain, reviewDepth }`. New
`pendingDepthRef` (mirrors `pendingRef`'s single-pending-decision-per-turn shape) holds the settled
target(s) awaiting a Quick/Thorough answer; `handleGateChipAction` checks it FIRST (before the
type-decision `pendingRef`). `runExplicit` gained an OPTIONAL 4th param (`reviewDepth?: ReviewDepth`)
— see the two-mode table above.

**`ConversationPane.tsx`**: `LOCAL_CHIP` switch cases updated for the split/new ids;
`pendingExplicitAgreementReview` state gained an optional `reviewDepth` field (TEXT door leaves it
undefined; the wizard listener sets it via `normalizeReviewDepth(seed.reviewDepth)`); the buffered
effect threads it into `runExplicitAgreementReview(...)`'s 4th arg.

**`composeWidgetData.ts`**: `ComposeWidgetSeed.reviewDepth?: 'quick'|'thorough'` — additive, alongside
`autoRunReview`.

**Wizard door (`CreateAnalysisWizardWidget.tsx`)** — assessed as SMALL and implemented (not
defaulted-and-documented): a `Field label="Review Depth"` with a Fluent `RadioGroup` (Quick /
"Thorough — recommended"), rendered on the EXISTING "Analysis Details" step directly below the
Agreement Type dropdown, gated on `workTypeValue === SprkAnalysisWorkType.AgreementAnalysis` (the
SAME condition already guarding `autoRunReview`). State defaults to `'thorough'`; threaded into the
finish-time compose seed as `reviewDepth: reviewDepthRef.current` alongside `autoRunReview` — the
SAME `useState` + mirroring-`useRef` idiom the file already uses for `selectedAgreementTypeId` (read
inside the async `onFinish` closure). Zero new wizard step, zero new network call, purely additive.

## Caveat seam (Item #4)

Investigated (background research agent) whether the Compose-side "standing banner" (the
not-legal-advice disclaimer constant) was reachable: `AgreementReviewSummaryPanel.tsx`'s
`overallRisk` prop is `@deprecated`/ignored since UAT round-5 — no live risk/disclaimer banner
renders there today (071-free/off-limits this wave, AND already structurally dead for this purpose).
The server never echoes the original dispatch args back (`SessionDispatchOrchestrator` reads
`reviewDepth` only to resolve the model tier, never persists or returns it) — `reviewDepth` is
CLIENT-ONLY knowledge.

**Seam chosen**: `useConsumerChips.tsx`'s `isNdaReview` branch (the confirmation message shown in
the chat transcript after a review dispatch completes) — `opts.slots.reviewDepth` is ALREADY in
closure scope (the same `opts` that supplies `opts.resultLabel` two lines above), zero new plumbing.
A one-line conditional prefix:

```ts
const quickScanCaveat =
  opts?.slots?.reviewDepth === "quick" ? "**Quick scan — not a full advisory review.** " : "";
```

prepended to both the labelled ("...under the **{label}** lens...") and unlabelled ("...reviewed the
NDA...") confirmation strings. Absent for every Thorough run (the default) — byte-identical wording
to pre-070. This is the least-invasive reachable seam (server round-trip would need a NEW echoed/
persisted field; Compose.Components is off-limits + already dead for banners).

**Memo threading — NOT trivially reachable, documented as follow-on** (per the task's own escape
hatch). The persisted `sprk_analysisoutput` payload is closed by JSON Schema
(`additionalProperties:false`, `agreement-review.action.json`'s output schema); `reviewDepth` never
persists anywhere server-side (transient, tier-resolution-only). Threading it into the memo would
require either a schema change (Action JSON + redeploy) or a new ledger/SessionOutput metadata field
— a legitimate but non-trivial follow-on, out of this task's scope.

## Client: tests (exact)

| File | Change | Result |
|---|---|---|
| `agreementReviewRouting.test.ts` | Confirm-chip test updated (3 chips); new depth-choice-chip + `normalizeReviewDepth`/`DEFAULT_REVIEW_DEPTH` tests | 33/33 PASS (was 33; net +9 new / re-shaped existing) |
| `localActionChips.test.ts` | Split/new chip-id assertions | 17/17 PASS |
| `useAgreementReviewGate.test.ts` | REWRITTEN: every branch test now exercises the depth-choice turn (auto-proceed/confirm/composite/explicit); new describe blocks for `runExplicit`'s two-mode contract | 46/46 PASS (was 43) |
| `ConversationPane.agreement-review-gate.e2e.test.tsx` (021) | Added a depth-chip click step (via the real `<ConsumerChips>` DOM button, `data-testid="consumer-chip-{bindingId}"`, rendered inside the stubbed SprkChat's `transcriptFooterSlot`) before asserting the review dispatch | 2/2 PASS |
| `ConversationPane.agreement-review-explicit-door.e2e.test.tsx` (023) | Same depth-chip-click addition, both tests | 2/2 PASS |
| `ConversationPane.agreement-review-session-routing.e2e.test.tsx` (031) | Same depth-chip-click addition | 1/1 PASS |
| `ConversationPane.wizard-auto-run.e2e.test.tsx` (033) | UNMODIFIED assertions still pass (wizard path bypasses the ask by design); +2 NEW tests proving `reviewDepth:"quick"` threads end-to-end and an absent value normalizes to Thorough | 8/8 PASS (was 6) |
| `CreateAnalysisWizardWidget.test.tsx` (AI.Widgets) | Existing task-033 hand-off assertion extended with `reviewDepth:'thorough'`; +1 new test driving the Quick radio via `fireEvent.click(screen.getByLabelText('Quick (~20 sec)'))` | 11/11 PASS (was 10) |
| `useConsumerChips.surface-launch.test.tsx` | +3 new tests: quick-caveat present, thorough-explicit no-caveat (exact byte match), absent-reviewDepth no-caveat (exact byte match) | 12/12 PASS (was 9) |

### Full suite runs

```
SpaarkeAi src/components/conversation (57 suites)      554/554 PASS
SpaarkeAi full package (npx jest)                       854/855 PASS (91/92 suites)
  1 pre-existing failure: HardSlashExecutor.test.ts "/save-to-matter POSTs ..." —
  a `< 100ms` elapsed-timing assertion; PASSES in isolation (43/43) — flaky under the
  full-suite's machine load, unrelated to this task (no file this task touched is in that
  suite's import graph).
SpaarkeAi typecheck (tsc-surface-gate)                   0 surface-owned errors (73
  pre-existing shared-lib errors, unrelated/deferred — unchanged baseline)
Spaarke.AI.Widgets CreateAnalysisWizardWidget.test.tsx   11/11 PASS
BFF dotnet build                                         0 errors
BFF dotnet test (full unit suite)                        9746/9747 PASS, 101 skipped
  1 pre-existing failure: NdaReviewDispatchEvalTests.NdaReviewBinding_ResolvesThroughThe
  RealRoutingService_ForBothClickAndTextPathCases — asserts the LIVE nda-review Binding's
  disposition is Informational; the environment's actual disposition is Compose (task 030's
  disposition flip, already deployed). Unrelated to this task — `SessionDispatchOrchestrator.cs`'s
  diff never touches `BindingDisposition`; this is an eval test reading live Dataverse state
  that drifted from the test's own expectation independent of this task.
```

## §10 BFF Hygiene / §11 Component Justification

- **Placement Justification**: no new endpoint, service, DI registration, or package. The two new
  static methods extend the EXISTING `SessionDispatchOrchestrator` class exactly like task 021's
  `TryReadSubDomain` did — same file, same pattern, same precedence-chain composition point task 011
  already established for `ModelTierOverride`.
- **§11 (component justification, root CLAUDE.md §11)**:
  1. *Existing* — `ModelTierDeploymentResolver` (tier→deployment) and the
     `request.ModelTierOverride ?? binding.ModelTierOverride ?? action.ModelTier` precedence chain
     (task 011) already exist and do 90% of the work.
  2. *Extension* — extended the precedence chain with ONE new composed value
     (`reviewDepthModelTier`) rather than inventing a second routing mechanism or a new resolver
     class; this satisfies the task's own instruction ("extend minimally").
  3. *Cost-of-doing-nothing* — without this, a user cannot pick a faster/cheaper review tier for a
     specific run; every review pays the full ~135s Reasoning-tier latency (the exact UAT #1
     complaint) with no escape hatch short of a global config change.
- **Publish size**: `dotnet publish -c Release` → **~46.87 MB** compressed (tar.gz proxy, incl.
  PDBs) — essentially flat vs. the 48.25 MB task-060/UAT-round-1 baseline (zero new packages; ~90
  lines of code across 2 files). Well under the 60 MB hard ceiling.
- **CVE**: `dotnet list package --vulnerable --include-transitive` → the only HIGH is the SAME
  pre-existing `System.Security.Cryptography.Xml 8.0.3` (transitive) every prior task in this project
  has reported. Zero new packages, zero new HIGH CVEs.
- **Test Update Obligation (§10 bullet 6)**: satisfied — new seam test file for the touched
  `Services/Ai/Chat/SessionDispatchOrchestrator.cs` logic.

## Quality gates (self-run, FULL rigor)

**code-review (self)**:
- No `any` introduced (client changes are fully typed; `opts?.slots?.reviewDepth` reads against
  `unknown` via a literal string comparison — no cast needed).
- No try/catch-log-rethrow; `TryReadReviewDepth` mirrors the existing tolerant-extraction pattern
  exactly (never throws for shape drift).
- No defensive code without a concrete failure mode — every new branch (`ResolveReviewDepthModelTierOverride`'s
  default case, `normalizeReviewDepth`'s fallback) names its own reason (malformed/legacy client
  values must never propagate a client-named tier).
- New abstractions (`pendingDepthRef`/`PendingDepthChoiceTarget`, the two new chip ids) are
  justified inline per §11 in the execution notes above and in-code doc comments, mirroring the
  established `pendingRef`/`PendingGate` shape exactly (not a novel pattern).
- Every widened interface (`dispatchReview`/`dispatchBothSequentially`'s new required param;
  `runExplicit`'s new optional param; `ComposeWidgetSeed.reviewDepth`) is additive or explicit at
  every call site — verified by the full 554/555-test conversation-module + 855-test package runs.
- Comments explain WHY (the two-mode `runExplicit` contract, the composite-follow-up-turn
  rationale, the general-chip non-split rationale, the resolved-cache-defaults-Thorough timing),
  matching the codebase's established verbose-rationale convention.

**adr-check (self)**:
- **ADR-039** (grounded execution, ONE dispatch protocol, closed catalogs): `reviewDepth` is a
  closed two-value CLIENT INTENT, never a model/deployment name; the server owns 100% of the
  tier→deployment mapping. No new dispatch mechanism — reuses `chips.dispatchBinding`/
  `dispatchConsumer` verbatim. PASS.
- **ADR-016** (rate limits / cost / backpressure): no new unbounded work; "Both"'s sequential
  `for...of` loop (ADR-016-compliant, task 021) is unchanged — the new `reviewDepth` param threads
  through unchanged loop mechanics, verified by the "Both" test asserting sequential order +
  uniform depth across both dispatches. Quick-tier reviews REDUCE cost/latency, never increase it.
  PASS.
- **ADR-041** (confirmation/completion policy, no double-ask): every new chip turn is a GENUINE new
  question with its own answer (never a re-ask of an already-answered question); the resolved-cache
  fast-path explicitly does NOT re-ask depth on a repeat call. PASS.
- **§10 BFF Hygiene**: see above — no new endpoint/service/package/DI; Placement Justification
  stated. PASS.
- **§11 Component Justification**: stated above for both server + client new abstractions. PASS.
- **CLAUDE.md §6.5** (ADR conflict resolution protocol): no conflict surfaced. The task's own
  in-code-comment instruction ("ADR-016 note in code comment: catalog default intent stands; this
  is a sanctioned per-run user intent — no ADR amendment needed") was satisfied literally — see the
  server-side doc comment on `ResolveReviewDepthModelTierOverride` / the precedence-chain edit.

No Critical or Warning findings.

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | Interactive review kick-off offers Quick/Thorough with timing labels; selection flows into dispatch args (assert reviewDepth in the request) | **PASS** | Static labels `"Quick (~20 sec)"`/`"Thorough (~2–3 min)"` on every chip (`agreementReviewRouting.ts`); `reviewDepth` asserted in `dispatchReviewBinding`'s `slots` at the hook level (`useAgreementReviewGate.test.ts`) and the real wire body (4 ConversationPane e2e tests) |
| 2 | Server maps thorough→Reasoning deployment, quick→Standard deployment (unit/seam assert on the resolver path); invalid values rejected/defaulted server-side | **PASS** | `AgreementReviewDepthModelTierSeamTests` 4/4 — quick→Standard, thorough→Reasoning (explicit), absent→catalog default (Reasoning), invalid→catalog default (never rejects the dispatch) |
| 3 | Quick-run findings render the caveat; thorough runs do not | **PASS** | `useConsumerChips.surface-launch.test.tsx` — 3 new tests: quick-caveat present, thorough/absent → byte-identical pre-070 wording |
| 4 | Gate + depth compose without double-ask; wizard door has a depth affordance OR documented default-thorough with rationale | **PASS** | Per-branch design table above (no double-ask verified at both hook + e2e level); wizard door HAS a small additive Radio affordance (not just defaulted), verified by 2 wizard-widget tests |
| 5 | Suites green (SpaarkeAi + BFF); publish-size note if server touched | **PASS (with 2 documented pre-existing failures, neither mine)** | SpaarkeAi 854/855 (91/92 suites); BFF 9746/9747 (101 skipped); publish ~46.87 MB (flat vs. 48.25 MB baseline) |

## Files modified/created

**Server**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionDispatchOrchestrator.cs` — `TryReadReviewDepth`,
  `ResolveReviewDepthModelTierOverride`, precedence-chain edit.
- `tests/integration/seam/Ai/AgreementReviewKnowledgeScopeSeamTests.cs` — additive `CapturedAction`
  capture on the shared fixture (021's file, extended not replaced).
- `tests/integration/seam/Ai/AgreementReviewDepthModelTierSeamTests.cs` — **new**, 4 tests.

**Client** (`src/solutions/SpaarkeAi/src/components/conversation/`, unless noted):
- `agreementReviewRouting.ts` — `ReviewDepth`, `DEFAULT_REVIEW_DEPTH`, `normalizeReviewDepth`,
  `buildAgreementReviewDepthChoiceMessage`, `buildAgreementReviewDepthChoiceChips`,
  `buildAgreementReviewConfirmChips` (3-chip rewrite).
- `localActionChips.ts` — chip-id split + 2 new ids.
- `useAgreementReviewGate.ts` — `pendingDepthRef`/`PendingDepthChoiceTarget`, `dispatchReview`/
  `dispatchBothSequentially` depth param, `runGate`'s auto-proceed rewrite, `handleGateChipAction`
  rewrite, `runExplicit`'s optional 4th param + two-mode contract.
- `ConversationPane.tsx` — chip-case switch update, `pendingExplicitAgreementReview.reviewDepth`,
  buffered-effect + wizard-listener threading.
- `useConsumerChips.tsx` — the Quick-scan caveat prefix in the `isNdaReview` branch.
- `../workspace/composeWidgetData.ts` — `ComposeWidgetSeed.reviewDepth`.

**Client (AI.Widgets)**:
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/CreateAnalysisWizardWidget.tsx` —
  Review Depth `RadioGroup` on the Analysis Details step; `reviewDepth`/`reviewDepthRef` state;
  threaded into the finish-time compose seed.

**Tests** (modified):
- `agreementReviewRouting.test.ts`, `localActionChips.test.ts`, `useAgreementReviewGate.test.ts`
  (rewritten), `useConsumerChips.surface-launch.test.tsx` (+3), `ConversationPane.agreement-review-
  gate.e2e.test.tsx`, `ConversationPane.agreement-review-explicit-door.e2e.test.tsx`,
  `ConversationPane.agreement-review-session-routing.e2e.test.tsx`,
  `ConversationPane.wizard-auto-run.e2e.test.tsx` (+2), `CreateAnalysisWizardWidget.test.tsx` (+1,
  1 assertion extended).

**Not touched** (HARD BOUNDARIES honored): `src/solutions/SpaarkeAi/src/components/shell/
ThreePaneShell.tsx`, `WorkspacePane.tsx`, `ReviewCompleteToast.tsx` (task 071's territory this wave —
confirmed via `git status` these show as modified/untracked from a CONCURRENT, uncommitted task-071
session in this shared worktree, not from this task); `src/client/shared/
Spaarke.Compose.Components/**` (`AgreementReviewSummaryPanel.tsx`/`ComposeEditor.tsx`/
`ComposeWorkspace.tsx` — 071-free/off-limits this wave, confirmed dead-for-this-purpose by the
research agent, not touched); `.claude/**`; `current-task.md`; `TASK-INDEX.md`. No git commit/push.

## Deviations / escalations

**No `<escalation>` trigger fired.** The task's design space was fully navigable with additive,
minimal-delta changes over already-shipped mechanisms (021/023/031/033's gate + dispatch spine, and
task 011's model-tier-override precedence chain).

**Judgment calls documented above** (not deviations from instruction — the task explicitly invited
"design it coherently, document the flow per branch"): the composite follow-up-turn placement, the
general-chip non-split, and the resolved-cache repeat-dispatch default. All three are reasoned,
minimal-footprint decisions consistent with the shipped codebase's own conventions, not
improvisation.

**Handle-review-NDA card scoping**: the classic "Review an NDA" card click (`ConversationPane.
handleReviewNda`) does NOT get a depth affordance this wave — matches 021's own precedent of leaving
that simpler, un-gated mechanism untouched. Documented, not silently dropped.

**Memo caveat threading**: assessed as NOT trivially reachable (closed output schema,
`reviewDepth` never persists server-side) — documented as a follow-on rather than built, per the
task's own explicit escape hatch.

## Task status

POML `070-review-depth-selector.poml` `<status>` set to `completed` with an inline `<notes>`
summary. Per HARD BOUNDARIES, `TASK-INDEX.md` and `current-task.md` were NOT touched — the
orchestrating session/human applies root CLAUDE.md §7's transition steps after reviewing this
report.
