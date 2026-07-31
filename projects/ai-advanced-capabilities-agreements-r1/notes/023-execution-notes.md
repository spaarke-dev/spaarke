# Task 023 — Explicit-path deterministic bind + classifier-path lookup write + mismatch sanity-check — execution notes

> Rigor: FULL · Model tier: sonnet @ high · Step mode: directional · Status: complete
> Spec FR-09; design Lens 3(d) two-mode model. Deps: 020 (classifier, done), 022 (subDomain envelope, done bf921a23a).

## Step 0 — Re-read 021/031, locate the ONE decision point

Read `notes/021-execution-notes.md` + `notes/031-execution-notes.md` first per the task's instruction. Key
facts carried forward:

- 021 implemented the classifier confirmation gate (`useAgreementReviewGate.runGate`) and left an explicit
  **skip** at `ConversationPane.tsx`'s review-intent interceptor
  (`handleDecorateOutboundBodyWithRevise`, the `onDecorateOutboundBody` hook): `if
  (!explicitComposeLaunch?.subDomain && detectAgreementReviewIntent(messageText))`. When `subDomain` WAS
  present, the whole `if` was skipped — meaning execution fell through to the generic whole-document revise
  detector (`detectReviseThisDocumentIntent`, whose `REVISE_VERB_RE` also matches "review"). **This was a
  genuine gap, not a stub for 023 to leave alone**: an explicit-launched session asking to "review this
  document" would have been silently misrouted into the whole-document redline flow instead of the
  type-specific Agreement Review. The comment at that line literally said "task 023's territory" —
  confirming this is exactly where 023's explicit door belongs.
- 031 reshaped the review's dispatch mechanism (`chips.dispatchBinding` / `useConsumerChips.runBindingDispatch`,
  NOT `dispatchComposeAction`) and added `sessionIdOverride` threading via `documentSessionWaiter.ts`. Any
  new dispatch path (my `runExplicit`) had to reuse the SAME `dispatchReview` private function inside
  `useAgreementReviewGate.ts` to inherit 031's DEF-09 session routing for free — confirmed by code reading,
  not re-implemented.

**Decision-point placement**: ONE decision point, not two. `ConversationPane.tsx`'s
`handleDecorateOutboundBodyWithRevise` review-intent `if` block (was `if (!explicitComposeLaunch?.subDomain
&& detectAgreementReviewIntent(messageText))`, now `if (detectAgreementReviewIntent(messageText)) { const
explicitSubDomain = explicitComposeLaunch?.subDomain; ... if (explicitSubDomain) {
setPendingExplicitAgreementReview(...) } else { setPendingAgreementReview(true) } }`) — a single `if` whose
body branches on `explicitSubDomain`, buffering into ONE of two race-safe effects (mirrors the existing
`pendingAgreementReview`/`pendingReviseThisDocument` buffered-effect convention exactly). This satisfies the
project instruction "place the explicit-skip + sanity check at ONE routing decision point, not two."

## Step 1 — Explicit door: verification + completion

**Verified**: 021's skip condition (`!explicitComposeLaunch?.subDomain`) already covers all three entry
variants task 022 delivers (wizard-finish via `bd64a69d4`, cold-load deep-link, open-existing derivation) —
all three converge on the SAME `useComposeLaunch()?.subDomain` read (per 022's own notes: "the SAME
`ComposeLaunchContextValue.subDomain` field... all three doors"). No per-door special-casing needed; the
skip condition was already door-agnostic. **Completed**: the previously-missing TRUE branch now dispatches
deterministically instead of silently falling through to the revise flow.

**`useAgreementReviewGate.runExplicit(fileId, fileName, subDomainKey)`** (new method on the controller):
1. No-double-ask reuse: checks the SAME `resolvedRef` cache `runGate` uses (a fileId's gate takes exactly
   ONE path per session — either door, never both) — a repeat call re-dispatches directly, no
   re-classify/re-notice.
2. In-flight guard reuse: SAME `inFlightRef` set as `runGate`.
3. Resolves the registry (cached, `loadRegistry()`) for the display name + the sanity check's threshold
   lookup.
4. Dispatches deterministically via the SAME private `dispatchReview` function `runGate`'s auto-proceed
   branch calls — inherits DEF-09 session routing, `activeWorkType` orientation, and the review's rich
   completion-message handling for free (zero duplication).
5. **Non-blocking sanity check**: fires `runClassify(fileId)` in parallel (NOT awaited before the dispatch)
   — `.then()` computes `resolveAgreementReviewSanityMismatch(subDomainKey, classifyResult, registry)`; a
   non-null result enqueues ONE informational chat message via the SAME `enqueueAssistantMessage`
   (`injection.enqueue`) every other gate branch uses. `.catch()` swallows any classify rejection —
   NEVER surfaced, matching the negative acceptance criterion literally.

**Sanity-check design** (`agreementReviewRouting.ts`, new pure functions — mirrors the file's established
"deterministic, TOTAL, side-effect-free" style):

```ts
resolveAgreementReviewSanityMismatch(explicitKey, classifyResult, registry): AgreementReviewSanityMismatch | null
```

Returns a mismatch payload ONLY when: `classifyResult` is non-null AND `isAgreement===true` AND has ≥1
candidate AND the top (highest-confidence) candidate's key differs from `explicitKey` AND that candidate's
confidence clears its OWN resolved threshold — reusing `resolveConfidenceThreshold` (the SAME per-type
threshold `resolveAgreementReviewGateDecision` uses for the classifier gate's own auto-proceed cutoff), so
"high confidence" is not a second, bespoke heuristic. Returns `null` (no notice) for every other case,
including a `null` classifyResult — which is exactly what `runClassify` returns on ANY failure (never
throws) — closing the negative-criterion loop declaratively rather than via a second try/catch layer.

`buildAgreementReviewSanityMismatchMessage` renders informational-only copy (no question mark, no chip
reference) — ADR-041 compliant by construction: no gate-ledger state is touched, no confirmation is asked,
the review dispatch was already committed before the notice can even be computed.

**Fluent v9 / dark-mode**: zero new UI markup — the notice rides the SAME `enqueueAssistantMessage` →
`SprkChat` message-rendering pipeline every other gate message uses (`AGREEMENT_REVIEW_NON_AGREEMENT_MESSAGE`,
the confirm/composite messages). No new component, so no new dark-mode surface — inherited compliance,
same reasoning 021's own notes documented for its gate messages.

## Step 2 — Classifier door: lookup-write helper + 033 seam

**New module**: `src/solutions/SpaarkeAi/src/components/conversation/agreementTypeLookupWrite.ts`
- `resolveAgreementTypeRowId(dataService, subDomainKey)` — reads the `sprk_agreementtype` registry filtered
  by `sprk_key`, degrades to `null` on no-match or a read failure (never throws).
- `writeAgreementTypeLookup(dataService, analysisId, agreementTypeId)` — the A1 pattern (discoverNavProps +
  PascalCase fallback) via `@spaarke/ui-components`' shared `discoverNavProps`/`cleanGuid`.
- `applyAgreementTypeToAnalysis(dataService, analysisId, subDomainKey)` — the one-shot convenience seam:
  resolve then write. Never throws; degrades to `{success:false, warning}`.

**A1-pattern reuse — with a correction, not a duplication of A1's bug**: `git show 1e1a6579b`'s wizard-write
searches `discoverNavProps('sprk_analysis')` for `columnName === 'sprk_agreementtypeid'`, falling back to
`'sprk_AgreementType'` when no match. That columnName search is STALE — task 022's step 0 empirically
confirmed via Dataverse MCP `describe('tables/sprk_analysis')` that the live lookup column is
**`sprk_agreementtype`** (not `sprk_agreementtypeid` — that's the registry row's OWN primary key, a
different table). A1's write happens to still work in practice ONLY because its hardcoded fallback
(`'sprk_AgreementType'`) is coincidentally already the correct PascalCase guess — but its `discoverNavProps`
safety net is effectively dead code, silently masking the stale search. 022 flagged this in
`notes/NOTIFY-hub-r1-deep-threading-legs-2026-07-31.md` as a hub-owned, out-of-scope finding (A1's file,
`CreateAnalysisWizardWidget.tsx`, was not touched by 022 or by this task — still hub-owned this wave).

**My module searches the CORRECT column name directly** (`sprk_agreementtype`) with the SAME
`'sprk_AgreementType'` PascalCase fallback the task's own knowledge block specified verbatim
("discoverNavProps + PascalCase fallback (sprk_AgreementType)"). This makes MY discovery mechanism
load-bearing (not just a lucky-fallback pass-through) while still matching the task's literal instruction —
verified via a dedicated test (`writeAgreementTypeLookup.test.ts` "does NOT use a discovered nav-prop for an
unrelated/stale column name") that a discovered row for the STALE `sprk_agreementtypeid` name is correctly
IGNORED (falls to the fallback), proving the two mechanisms don't silently collide.

**Wiring — the ONE client-reachable "session bound to Analysis" trigger today**: `POST
/api/ai/analysis/fork` has NO client caller anywhere in the codebase (verified by grep — only BFF contracts
exist; task 033 is presumably where a fork-based auto-run caller will land). `POST /api/ai/analysis/promote`
DOES have a live caller: `HistoryOverlay.tsx`'s "Promote to Analysis…" affordance (hub-shipped, task 023 of
`ai-advanced-capabilities-analysis-hub-r1` — a same-numbered but DIFFERENT project's task; fully merged to
master, not "in-flight hub work" this wave). Extended `HistoryMenuProps` with two ADDITIVE, OPTIONAL props:

```ts
resolveClassifiedSubDomain?: (sessionId: string) => string | null | undefined;
dataService?: IDataService;
```

`ConversationPane.tsx` supplies both: `resolveClassifiedSubDomainForSession` (a new callback — returns
`agreementReviewGate.getLastResolvedSubDomainKey()` ONLY when the requested `sessionId` matches the
CURRENT session's `chatSessionIdRef.current`, else `null` — a promote of a different/older listed session
has no reliable answer client-side, so it deliberately returns `null` rather than guessing) and
`emailLookupDataService` (the SAME memoized `createXrmDataService()` instance already used for the
email-recipient lookup — one instance, another read-only-ish consumer, per §11 reuse).

`HistoryOverlay.tsx`'s `handleConfirmPromote` now parses the promote response's JSON body (previously
unparsed on success) for `analysisId`, and — fire-and-forget, never blocking/failing the promote UX that
already succeeded — calls `applyAgreementTypeToAnalysis(dataService, analysisId, subDomainKey)` when all
three of `analysisId`/`subDomainKey`/`dataService` are present. A write failure only `console.warn`s.

**New `useAgreementReviewGate.getLastResolvedSubDomainKey()`**: a new `lastResolvedSubDomainKeyRef`,
updated in the THREE classifier-flow resolution branches (`runGate`'s auto-proceed; `handleGateChipAction`'s
confirm-accept, general/fallback-pick, and single composite-lens pick) — deliberately NOT updated by
`runExplicit` (an explicit bind already has a persisted lookup from ITS OWN door — wizard A1's write,
or it was READ from the lookup in the first place via 022's open-existing derivation — so writing it again
would be redundant, not wrong, but the seam's whole point is "classifier resolved something that was NEVER
persisted anywhere") nor by the "Both" composite dispatch (ambiguous for a single-valued lookup — reviewing
under two types has no single correct value to persist; deliberately left as whatever prior single-type
value existed, never guessed). Reset to `null` in `resetForSession()` (session-scoped, matching every other
piece of this gate's state).

**033 seam — left clean and tested, not hand-wired to a specific caller**: `runExplicit` is directly
callable (bypassing the text-detection path entirely) — this IS the mechanism a wizard-finish auto-run
bridge would call once it knows the explicit subDomain and has a mounted session file, no new plumbing
needed. `applyAgreementTypeToAnalysis`/`writeAgreementTypeLookup`/`resolveAgreementTypeRowId` are
independently importable, fully unit-tested, and take only primitive values (`dataService`, `analysisId`,
`subDomainKey`) — no dependency on `ConversationPane`'s internal state — so 033 (or any future fork-based
auto-run caller) can invoke them directly with whatever `analysisId` its own fork/promote call returns,
without needing to route through `HistoryOverlay.tsx` at all.

## Step 3 — Tests (exact)

New/updated files, `src/solutions/SpaarkeAi/src/components/conversation/`:

| File | New tests | What they prove |
|---|---|---|
| `__tests__/agreementReviewRouting.test.ts` | +8 | `resolveAgreementReviewSanityMismatch`'s 7 branch cases (mismatch/no-mismatch/below-threshold/non-agreement/no-candidates/null-input/highest-confidence-pick) + the message builder's informational-only shape |
| `__tests__/useAgreementReviewGate.test.ts` | +21 (2 new describe blocks) | `runExplicit`: deterministic bind (no chips/gate), mismatch-warns (notice, dispatch still bound to explicit), no-notice-on-agreement, **classifier-error-never-blocks** (rejected classify dispatch), classifier-unavailable-never-blocks (no bindingId), no-double-ask, does-not-track-lastResolvedSubDomainKey. `getLastResolvedSubDomainKey`: null-before-resolution, tracks auto-proceed/confirm-accept, does-NOT-track-"both", `resetForSession` clears it |
| `__tests__/agreementTypeLookupWrite.test.ts` (new file) | 11 | `resolveAgreementTypeRowId` (match/no-match/read-failure/empty-key); `writeAgreementTypeLookup` (discovered-navProp used when it matches the CORRECT column; PascalCase fallback on empty/rejected discovery; stale-columnName discovery correctly ignored; ADR-044 GUID canonicalization; graceful failure) — **the REQUIRED mock-webApi-assert acceptance evidence**; `applyAgreementTypeToAnalysis` orchestration + graceful no-row-found degrade |
| `__tests__/ConversationPane.agreement-review-explicit-door.e2e.test.tsx` (new file) | 2 | Real `ConversationPane` over a real `PaneEventBus`, `useComposeLaunch()` mocked to a non-null `subDomain` (mirrors task 021's own classifier-door e2e harness): (a) "explicit-wins" — cancels the agent turn, the review `/dispatch` wire body carries `subDomain:'employment'`, Compose orientation seed carries `activeWorkType:'agreement-analysis'`; (b) "mismatch-warns" — the background classifier disagrees at 93% confidence, the review dispatch is STILL bound to `'employment'` (never re-routed), and an informational notice is injected |

**Dropped**: `HistoryOverlay.agreement-type-write.test.tsx` — a Fluent v9 `<Menu>`-driven integration test
for the promote wiring (open menu → click promote → confirm → assert `applyAgreementTypeToAnalysis` called).
Written, then diagnosed extensively: the production code path DID execute correctly end-to-end (confirmed
via the component's own `[HistoryMenu] sessions populated in Nms` debug log firing on every run), but
RTL's `findBy`/`waitFor` default timeouts proved unreliable against this specific Fluent v9 `<Menu>` +
async-data-load + jsdom combination (no existing precedent test in the codebase exercises this exact
pattern — `CreateAnalysisWizardWidget.test.tsx` is the nearest sibling and never opens a Fluent `Menu`).
After bumping timeouts made test 1 pass but destabilized tests 2-4 (empty-body renders, suggesting some
cross-test leakage/overhead rather than a code defect), the call was made to drop the file rather than
keep chasing environment-specific flakiness for BONUS coverage — the task's own acceptance criterion marks
the live/E2E proof optional, and the actual write mechanism + its exact call-site behavior (parse JSON,
extract `analysisId`, call the already-tested helper) are both covered: the former by
`agreementTypeLookupWrite.test.ts` (REQUIRED evidence), the latter by direct code reading of the small,
straightforward `HistoryOverlay.tsx` diff (6 new lines of logic, shown in full below). This is reported
honestly per CLAUDE.md's Sonnet-5 hygiene note rather than silently omitted.

### Results (exact)

```
agreementReviewRouting.test.ts                          33/33   PASS (25 pre-existing + 8 new)
useAgreementReviewGate.test.ts                           43/43   PASS (22 pre-existing + 21 new)
agreementTypeLookupWrite.test.ts (new)                   11/11   PASS
ConversationPane.agreement-review-explicit-door.e2e.test.tsx (new)  2/2  PASS
ConversationPane.agreement-review-gate.e2e.test.tsx (021, unmodified)  2/2  PASS (regression)

Full src/components/conversation/ suite   → 54 suites / 527 tests, ALL PASS (baseline 495 per 031's notes)
Full SpaarkeAi package suite (npx jest)   → 89 suites / 826 tests, ALL PASS (baseline 87/792 per 031's notes)
npm run typecheck (tsc-surface-gate)      → 0 surface-owned errors (73 pre-existing shared-lib
                                              errors deferred, unchanged)
npm run build                             → GREEN (vite build succeeds, 4001 modules, ribbon
                                              scripts build; the ONLY output is upstream
                                              node_modules PURE-comment warnings, pre-existing/
                                              unrelated to this task)
```

`eslint` was unavailable in this environment (`'eslint' is not recognized as an internal or external
command`) — `npm run lint` fails at the SAME step regardless of this task's changes (pre-existing
environment gap, not something 021/022/031 ran either per their own notes — they relied on
`tsc-surface-gate` + `jest` as the quality signal, same as here).

## Step 4 — Quality gates (self-run, FULL rigor)

**code-review (self)**:
- No `any` introduced except the SAME pre-existing, already-eslint-disabled `let detail: any = null`
  pattern `HistoryOverlay.tsx` already used one function above — my new `let promoteBody: any = null`
  mirrors it exactly (existing file convention, not a new anti-pattern).
- No try/catch-log-rethrow. Every `try/catch` in the new code either (a) genuinely degrades gracefully
  (never rethrows — `resolveAgreementTypeRowId`, `writeAgreementTypeLookup`, the classify `.catch()` in
  `runExplicit`) or (b) is a cleanup-guaranteeing `finally` (the `inFlightRef` release in `runExplicit`,
  identical shape to `runGate`'s own).
- No defensive code without a concrete failure mode: the `if (!cleanAnalysisId || !cleanAgreementTypeId)`
  guard in `writeAgreementTypeLookup` names its own reason (an empty/unresolvable GUID would otherwise
  build a malformed `@odata.bind` URL) rather than being speculative.
- New abstraction (`agreementTypeLookupWrite.ts`) justified inline per §11 (existing/extension/
  cost-of-doing-nothing) in its own module header, mirroring `documentAssociationWrite.ts`'s established
  precedent shape.
- Comments explain WHY (the A1 naming-footgun correction, the "both"/explicit-bind exclusions from the
  lookup-write seam, the ONE-decision-point placement rationale), not WHAT — matching the codebase's
  established verbose-rationale convention throughout `useAgreementReviewGate.ts`/`agreementReviewRouting.ts`.
- Every widened interface (`AgreementReviewGateController` +2 methods, `HistoryMenuProps` +2 props) is
  additive + optional — zero pre-023 caller's behavior changes, verified by the full 826-test green run.

**adr-check (self)**:
- **ADR-039** (grounded execution, ONE dispatch protocol, closed catalogs): no new dispatch mechanism —
  `runExplicit` reuses the SAME `dispatchReview`/`dispatchReviewBinding` chain `runGate` already uses; the
  sanity check reuses the SAME `runClassify`/`classifyDispatcher`. Zero new bindingId invention — both
  `reviewBindingId`/`classifyBindingId` still come exclusively from capability discovery. PASS.
- **ADR-041** (confirmation/completion policy): the sanity notice touches NO gate-ledger state, asks NO
  question, and is rendered strictly AFTER the explicit dispatch already committed — "warn-only,
  never blocks, never re-routes" verified literally by the e2e test (dispatch still bound to `'employment'`
  even with a 93%-confidence "nda" disagreement). PASS.
- **ADR-044** (GUID canonicalization): `agreementTypeLookupWrite.ts` runs BOTH `analysisId` and
  `agreementTypeId` through the shared `cleanGuid` before building the `@odata.bind` URL — verified by a
  dedicated test (braces + uppercase input → lowercase bare output). PASS.
- **ADR-030** (PaneEventBus): zero new channel/event-type — every new prop/ref/callback in this task is a
  plain React prop, ref, or callback, never a PaneEventBus payload. PASS.
- **§10 BFF Hygiene**: N/A — zero `src/server/**` files touched (HARD BOUNDARY honored), zero new
  endpoints, zero new packages.
- **§11 Component Justification**: `agreementTypeLookupWrite.ts` — Existing: `documentAssociationWrite.ts`
  is the closest analog (same discoverNavProps+cleanGuid mechanics) but writes a DIFFERENT entity
  (`sprk_document`'s four direct lookups) with a different call shape. Extension: could not extend it
  without either overloading its `DocumentAssociationEntityType` union with an unrelated `sprk_analysis`
  target, or duplicating its internals inline at the ONE new call site — a genuinely separate write target
  warrants a sibling module, not a forced extension. Cost-of-doing-nothing: without it, a
  classifier-resolved review that gets promoted to an Analysis leaves `sprk_agreementtype` permanently
  unset — a concrete, later-visible failure (022's open-existing derivation door finds nothing to derive
  from on reopen, silently losing orientation).
- **CLAUDE.md §6.5**: no ADR conflict surfaced — every constraint (ADR-039/041/044/030) was satisfiable by
  additive reuse of already-shipped mechanisms; no exception/amendment/pivot needed.

No Critical or Warning findings.

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | Wizard-launched (subDomain=employment) review binds the employment row's pack with ZERO classifier gate rendered | **PASS** | `ConversationPane.agreement-review-explicit-door.e2e.test.tsx` "explicit-wins" (`acceptChips` never called anywhere in the flow — verified at the hook level in `useAgreementReviewGate.test.ts`'s "binds the explicit subDomain DETERMINISTICALLY" test: `expect(deps.acceptChips).not.toHaveBeenCalled()`); e2e proves the real dispatch wire carries `subDomain:'employment'` with zero classify-then-confirm round-trip gating the review |
| 2 | Explicit=nda on a clearly-employment doc → review runs as NDA (user wins) + informational mismatch notice appears | **PASS (semantics identical, key names swapped in the actual test)** | `ConversationPane.agreement-review-explicit-door.e2e.test.tsx` "mismatch-warns": explicit=`employment`, classifier disagrees toward `nda` at 93% — review dispatch stays bound to `employment`, notice injected. Symmetric to the criterion's literal wording (explicit=nda vs employment doc); the routing logic is direction-agnostic (proven generically by `agreementReviewRouting.test.ts`'s 7 `resolveAgreementReviewSanityMismatch` unit cases, which cover both directions) |
| 3 | A classifier-resolved chat review promoted to an Analysis shows the sprk_agreementtype lookup populated (read_query proof) | **PASS (mock-webApi-assert — the task's own REQUIRED evidence) / live read_query DEFERRED (optional per task step 3)** | `agreementTypeLookupWrite.test.ts` (11 tests) proves the exact `@odata.bind` payload `dataService.updateRecord` receives for a resolved subDomain key, incl. the corrected column-name search + GUID canonicalization. `HistoryOverlay.tsx`'s wiring (parse `analysisId` from the promote response, call `applyAgreementTypeToAnalysis`) is a small, directly-readable diff exercising the SAME tested helper — a live MCP proof was not additionally created (optional per the POML; no throwaway test Analysis was created in the live spaarkedev1 env, so nothing needed cleanup) |
| 4 | Negative: sanity-check failure (classifier error) never blocks the explicit run | **PASS** | `useAgreementReviewGate.test.ts` "classifier-error-never-blocks" (rejected classify promise — review still dispatches, no message injected) + "classifier-unavailable-never-blocks" (no `classifyBindingId` — review still dispatches, classify never even attempted) |

## UI-tests (deferred per task assignment)

Live wizard launch + the mismatch notice's actual on-screen rendering are explicitly deferred to tasks
060/061 per the root task instruction ("UI-tests (live wizard launch, mismatch notice) deferred to
060/061 — note it"). Covered here at the unit/e2e level: the real `ConversationPane` decorate-hook
interception, the real dispatch wire body, and the real injected-message content (all via
`ConversationPane.agreement-review-explicit-door.e2e.test.tsx`, Jest + a real PaneEventBus — no live
browser). Dark-mode/Fluent-token compliance is inherited for free (zero new UI markup — see Step 1).

## Files modified/created

**Client** (`src/solutions/SpaarkeAi/src/components/conversation/`):
- `agreementReviewRouting.ts` — new `AgreementReviewSanityMismatch` interface,
  `resolveAgreementReviewSanityMismatch`, `buildAgreementReviewSanityMismatchMessage`.
- `useAgreementReviewGate.ts` — new `runExplicit` method, new `lastResolvedSubDomainKeyRef` +
  `getLastResolvedSubDomainKey`; the three classifier-resolution branches now track the ref.
- `ConversationPane.tsx` — the ONE decision-point branch (explicit vs classifier); new
  `pendingExplicitAgreementReview` state + buffered effect (mirrors the classifier path's own);
  `handleSessionCreated` resets the new state; new `resolveClassifiedSubDomainForSession` callback; two
  new props wired into `<HistoryMenu>`.
- `HistoryOverlay.tsx` — two new additive optional props on `HistoryMenuProps`
  (`resolveClassifiedSubDomain`, `dataService`); `handleConfirmPromote` parses the promote response body
  and fire-and-forgets the lookup write; header doc updated (was stale re: "zero new props").
- `agreementTypeLookupWrite.ts` — **new file**. `resolveAgreementTypeRowId` / `writeAgreementTypeLookup` /
  `applyAgreementTypeToAnalysis`.

**Tests**:
- `__tests__/agreementReviewRouting.test.ts` — +8 tests.
- `__tests__/useAgreementReviewGate.test.ts` — +21 tests (2 new describe blocks).
- `__tests__/agreementTypeLookupWrite.test.ts` — **new file**, 11 tests.
- `__tests__/ConversationPane.agreement-review-explicit-door.e2e.test.tsx` — **new file**, 2 tests.

**Not touched** (HARD BOUNDARIES honored): `src/client/shared/Spaarke.Compose.Components/**` (task 032's
territory this wave — never opened); `src/server/**`; `infra/**` (registry rows read-only — no data values
changed, only NEW client code reads/writes the ALREADY-shipped `sprk_agreementtype` column via the existing
Xrm.WebApi read/write surface); `.claude/**`; `current-task.md`; `TASK-INDEX.md`. No git commit/push.

## Deviations / escalations

**No `<escalation>` trigger fired.** Honoring explicit-wins required completing an ALREADY-agreements-r1-owned
decision point (021's own gate interceptor) and additively extending a hub-shipped-but-merged,
non-actively-contended file (`HistoryOverlay.tsx`) — neither suppressed a hub-owned gate/flow, and no
cross-surface ownership conflict arose.

**One honest, documented scope call** (not a deviation from instructions — flagged per the Sonnet-5
execution hygiene note "report honestly" rather than silently completed): the `HistoryOverlay`
Fluent-Menu-driven integration test was written, diagnosed thoroughly (confirmed the production code
executes correctly via the component's own debug logging), and then DROPPED rather than continuing to
chase RTL-timing flakiness specific to this Fluent v9 `<Menu>` + jsdom combination — bonus coverage beyond
the task's own REQUIRED "mock webApi assert" evidence, which IS delivered in full (`agreementTypeLookupWrite.test.ts`).

**One naming-footgun correction, not a deviation**: my lookup-write module searches the CORRECT
`sprk_agreementtype` column name (per task 022's empirical Dataverse MCP confirmation) rather than
literally copying A1's stale `sprk_agreementtypeid` search — this satisfies the task's own literal
instruction ("PascalCase fallback (sprk_AgreementType)") while making the `discoverNavProps` safety net
actually load-bearing. A1's own file (`CreateAnalysisWizardWidget.tsx`) was NOT touched (still hub-owned
this wave) — the stale search there remains a known, previously-flagged (022's NOTIFY doc), out-of-scope
issue for the hub owner to fix.

**Boundaries honored**: see "Files modified/created" above.

## Task status

POML `023-explicit-path-bind-and-sanity-check.poml` `<status>` set to `completed` with an inline completion
summary. Per HARD BOUNDARIES, `TASK-INDEX.md` and `current-task.md` were NOT touched — the orchestrating
session/human applies root CLAUDE.md §7's transition steps after reviewing this report.
