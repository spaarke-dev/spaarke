# Task 032 — Execution Notes: FR-16 completion — summary-panel restore + 128KB payload budget + supersede/coexistence protection

> Rigor: FULL · Model tier: sonnet @ xhigh · Step mode: directional · Status: complete

## Summary

Closed all three FR-16 gaps client-side, entirely inside `Spaarke.Compose.Components` (no server changes, no
escalation fired, no sibling-owned file touched):

1. **Summary-panel restore** — reopen now repopulates `AgreementReviewSummaryPanel`'s rows + risk data (gutter
   AND panel, not just gutter), mirroring the live `onAdvisoryComments` capture.
2. **128KB budget** — chose **Leg B (explicit visible notice, not chunking)**. Chunking needs a server write-seam
   change (splitting one Action turn's output into multiple ledger entries before the cap applies) — out of the
   read-only `src/server/**` boundary, exactly as the POML pre-empted. Implemented two detection legs so a
   truncated/skipped review is never silently absent.
3. **Coexistence + supersede protection** — a findings output is never evicted by a later edit output (the
   ORIGINAL bug: the untargeted materializer picked only the single globally-highest-turn compose output).
   Verified server-side supersede is BindingId-scoped by construction, so it cannot touch a findings output.
4. **031-residual dedupe guard** — closed the same-mount double-placement race 031 escalated (a `Compose.Components`
   change explicitly deferred to this task).

## Step 0 — Payload measurement + seam re-verification

**Realistic worst-case payload vs. the 128KB cap** (`SessionLedgerEntries.cs:50`, `InlinePayloadCapBytes = 128 *
1024`): estimated per-finding JSON size for a `flaggedSections[]` entry (`quotedText` + `flaggedClause` +
`assessment` + `sectionRef` + `riskLevel` + `standardRef` + field-name/JSON overhead) at ~1.5–2.0 KB for a
moderately long quoted clause + two-sentence assessment. A 50-section agreement with EVERY section flagged (an
unusually thorough review — most reviews flag a subset) lands at **~75–100 KB**, under the cap. A genuinely
plausible over-cap case needs either more findings (60–70+) or longer quoted clauses (full-paragraph quotes,
1500–2000 chars) — matching the POML's own framing ("plausibly exceeds," not "always exceeds"). Conclusion:
over-cap is a real but not dominant case — worth closing, but a full server-side chunking redesign for a
minority case is disproportionate; the client-only Leg B (explicit notice) is the right-sized fix.

**Seam re-verification** (all POML line refs were pre-merge/stale; re-verified against the current worktree):

| Seam | POML ref | Verified current location |
|---|---|---|
| Summary-panel feed | `ComposeWorkspace.tsx:1577-1586` | `onAdvisoryComments` receiver inside `useComposeWorkspaceReceivers({...})`, ~line 2108 pre-edit; `setReviewSummaryFindings`/`setReviewSummaryFailedCount` calls |
| 128KB cap | `SessionLedgerEntries.cs:50` | `SessionLedger.InlinePayloadCapBytes = 128 * 1024` (unchanged since 030) |
| Truncation skip | `ChatEndpoints.cs:1326-1329` | `ProjectComposeOutputs` — `if (SessionLedger.IsTruncationMarker(output.Payload)) continue;` (confirmed: the entry is dropped from the response ENTIRELY, not flagged) |
| Highest-turn selection | `ComposeWorkspace.tsx:1420-1422` | `materializeComposeDraftFromLedger`'s untargeted branch — `composeOutputs.reduce((a,b) => b.turn>a.turn?b:a)` (the exact bug; now replaced) |
| Supersede endpoint | `ChatEndpoints.cs:1337-1408` | `SupersedeComposeOutputAsync` / `SupersedeComposeOutput` — confirmed **BindingId-scoped** via `ComposeDisposition.ResolveCurrent(outputs, prior.BindingId)` (`ComposeDisposition.cs:148-160`) |
| Live wire `ledgerRef` availability | POML: "thread a `ledgerRef` on the event" | `PaneEventTypes.ts` already declares `ledgerRef?: string` as reusable across ANY `WorkspacePaneEvent`, including `compose_advisory_comments` — but `useNdaReviewAdvisoryCommentsBridge.ts`'s `emitFromResult` (the ONLY emitter) does **not** set it. Confirmed by reading the emitter (read-only; SpaarkeAi is off-limits this wave). Designed the dedupe guard to NOT depend on it. |

## Step 1 — Design decisions (documented deliberately)

### Cap strategy: Leg B (explicit notice), not chunking

The POML explicitly pre-authorized this fallback: *"if your audit shows the ONLY way to chunk is server-side,
evaluate the alternative: keep single payload + explicit visible truncation notice."* Confirmed via code read:
`SessionLedger.CapInlinePayload` truncates **at the write seam**, replacing the payload with a marker BEFORE the
client ever sees the original content — chunking would require splitting one Action turn's output into multiple
ledger entries in the SERVER write path (`OutputRouter`/dispatch), which is out of the read-only `src/server/**`
boundary. No escalation was raised because the POML's own "if X, do Y" clause already resolved the choice.

**Two detection legs implemented** (both client-only):
- **Directly observable**: a findings-shaped compose output IS present in the `GET /compose-outputs` response but
  every entry fails `projectLedgerFindingsToAdvisoryComments`'s guard (0 usable items) → immediate degraded
  notice (`reason: 'malformed'`). No marker needed — this is visible in the SAME response.
- **Not directly observable** (the server fully SKIPS a truncated entry — `ChatEndpoints.ProjectComposeOutputs`):
  a same-tab `sessionStorage` marker (`spaarke.compose.reviewFindingsMarker:{sessionId}`), refreshed on every
  successful findings restore (live or ledger-driven), records the last known-good finding count. If a LATER
  untargeted materialize pass sees ZERO findings-shaped outputs but the marker says otherwise, surfaces a
  degraded notice (`reason: 'skipped'`).

**Honest scope of Leg B**: `sessionStorage` is tab-lifetime. A genuinely new browser tab/device after the
truncation has no marker to compare against and will show nothing at all (not even a degraded notice) — the
SAME "silent absence" the acceptance criterion names, just narrowed to a rarer sub-case (cross-device reopen of
a genuinely-truncated review, with no intervening same-tab visit). Closing that fully needs either (a) a
server-side truncation-marker passthrough (the projection would need to expose "an entry was truncated here"
instead of dropping it — a `src/server/**` change), or (b) piggybacking the marker onto the FR-29
`anchoredAnnotations` durable store (considered and rejected below). Documented as a residual, not silently
dropped — a natural follow-on for whichever task next touches `ChatEndpoints.ProjectComposeOutputs`.

**FR-29 layer considered and rejected for the marker**: the POML suggested the FR-29 `anchoredAnnotations`
layer as a candidate "second durability layer." Read the mechanism (`ComposeWorkspace.tsx` gap-4.3 persist
effect, `POST /api/compose/sessions/{id}/annotations`): it is a REAL durable (Redis+Cosmos-backed) store, but it
carries `AnchoredAnnotation` records used by `registerAiEditReasonComment`/`registerAiReviewComments` (DEF-11/
DEF-13 — flag-risks comments and AI-edit-reason comments) — a STRUCTURALLY DIFFERENT store from the TipTap-native
advisory-comment threads `placeAdvisoryComments` creates for agreement-review findings (confirmed: findings never
touch `anchoredAnnotations` at all, in either the live or ledger path). Extending `AnchoredAnnotation.type`'s
closed union to add a "review marker" entry would give genuine cross-device durability, but risks unintended
interaction with any OTHER consumer that iterates all `anchoredAnnotations` (e.g., a future annotations sidebar,
or Word-export code) — a materially larger blast radius than a purpose-built sessionStorage key that carries no
content, just a count. Chose the narrower, contained mechanism; the wider one is a legitimate future upgrade if
cross-device Leg-B coverage becomes a real complaint.

### Coexistence: multi-select materialization (chosen) vs. FR-29-backed (rejected)

Chosen: **replay ALL findings outputs + the latest edit-shaped output, independently** (`isFindingsShapedComposeOutput`
partitions `composeOutputs`; the findings loop has no turn-ordering constraint, the edit branch keeps the
EXISTING highest-turn-among-edits semantics unchanged). This is the textually SIMPLEST fix that meets the closed
guarantee: it directly reverses the bug (the untargeted `reduce()` picking one global-highest-turn output) with
a partition + two independent selections, reusing 100% of the EXISTING edit-materialize code path (extracted
verbatim into `materializeEditOutput`, unchanged behavior — proven by the full pre-existing 10/10
DEF-09/DEF-11/DEF-16(030) suite passing UNMODIFIED after the refactor).

FR-29-backed alternative (rejected): could have made `placeAdvisoryComments`' output ALSO live in
`anchoredAnnotations` so a reopen replays from THAT durable store instead of re-reading `compose-outputs`. Not
chosen — this would DUPLICATE the source of truth for advisory comments (they already have a durable
representation: the ledger's `flaggedSections[]` payload, re-projected identically on every restore per ADR-040
render-follows-store) and complicate `placeAdvisoryComments`'s own idempotency story further. The ledger IS
already durable and already authoritative; multi-select materialization is a pure SELECTION fix, not a new
storage layer.

### Supersede protection: verified structurally safe, not defensively re-coded

Read `ChatEndpoints.SupersedeComposeOutput` (`ChatEndpoints.cs:1436-1493`): the "current head" resolution
(`ComposeDisposition.ResolveCurrent`, `ComposeDisposition.cs:148-160`) filters by `entry.BindingId ==
bindingId` — supersede can ONLY find + retract entries sharing the SAME `bindingId` as the referenced ref. Since
the agreement-review Binding and the compose-draft-alternative Binding are verified DIFFERENT bindings (task
031's own finding — the review is `chips.dispatchBinding`, edits are `dispatchComposeAction`'s 6
`DEFAULT_ACTIONS`), a findings output is **structurally unreachable** by any edit-binding's supersede call — no
new code needed server-side (confirmed read-only), and no client-side defensive code needed either (the
CLIENT-side coexistence fix above already ensures findings restore regardless of how many turns an edit binding
accumulates). Proved with a same-file client test simulating a two-turn "Try another" sequence (v1 superseded,
v2 current) alongside an untouched findings output at a lower turn — findings restore, only the CURRENT edit
text renders, the superseded v1 text never appears.

### 031-residual dedupe guard

Root cause (re-confirmed): the LIVE `compose_advisory_comments` event carries no `ledgerRef` on the wire today
(`useNdaReviewAdvisoryCommentsBridge.ts`'s `emitFromResult` never sets it — read-only verification, SpaarkeAi is
off-limits this wave). So `onAdvisoryComments` cannot record an exact ledger key the way the ledger-read path
does via `lastMaterializedKey`. The narrow race: a **Save** operation (`requestSave`→`saving`→`saveSucceeded`
→`loaded`) cycles `state.status` without unmounting `ComposeEditor` (`showEditor = status==='loaded' ||
status==='saving'` covers BOTH) — the FR-04 effect's `[state.status, state.sessionId]` dependency re-fires on
BOTH transitions, and once back at `'loaded'` it re-runs the untargeted materialize pass in the SAME (never
unmounted) editor instance that already holds the live-placed comment. (Verified this is the concrete trigger,
not the `externalChange`/`requestLoad` reload path 031's prose literally named — that path's own code comment
says the editor is "already remounted transparently," i.e. IT unmounts+remounts, which would wipe the live
comment BEFORE any re-placement could race it. Save is the mundane, high-frequency, SAME-instance case that
actually matches "the SAME editor instance" from 031's notes.)

**Fix**: `computeAdvisorySignature` (order-independent, content-based — sorted+lowercased `targetText` join) is
computed by BOTH the live path (`onAdvisoryComments`, after a successful placement) and the ledger path
(`materializeFindingsOutput`, before placing). Both record/check against a SHARED `materializedFindingsKeysRef`
Set, session-scoped (`{sessionId}::sig:{signature}` / `{sessionId}::key:{ledgerKey}` tokens — ledger turn numbers
are session-local, so a bare key could collide across two DIFFERENT sessions without the scope prefix). A
genuinely different review (different clauses) gets a different signature and is NOT suppressed.

**Empirically verified load-bearing** (not a vacuously-passing test): temporarily disabled the signature check
(`if (false && ...)`), re-ran the dedupe test — the assertion flipped from **1 → 2** advisory anchors (a genuine
duplicate), confirming the guard is the thing preventing the bug, then restored the fix and re-confirmed green.

## Step 2 — Summary-panel restore (implementation)

`materializeFindingsOutput` (new, extracted from the pre-032 inline findings branch) now, on a successful
placement, ALSO calls `setReviewSummaryFindings`/`setReviewSummaryFailedCount`/`setReviewSummaryOverallRisk` —
mirroring `onAdvisoryComments`'s existing capture, but AGGREGATING (append, not replace) across multiple findings
outputs in the SAME untargeted pass (coexistence). The `reviewSummaryFindings`/`reviewSummaryOpen`/
`reviewSummaryFailedCount` state declarations were MOVED earlier in `ComposeWorkspace.tsx` (were declared AFTER
`materializeComposeDraftFromLedger`; now declared before it) so the restore path can reference them — a pure
relocation, no behavior change to the declarations themselves.

**`overallRisk` data-path completion** (not a UI resurrection): `ComposeReviewPayload.overallRisk` was typed by
030 with the comment "carried for the summary panel (restore is task 032)" but nothing consumed it — even the
LIVE path dropped `event.overallRisk` (the field exists on the wire, just unused). Threaded it through: live
event → `reviewSummaryOverallRisk` state (worst-of via the ALREADY-EXPORTED `deriveOverallRisk` — reused
verbatim, §11) → `ComposeEditor`'s `reviewSummary.overallRisk` (new additive field) → `AgreementReviewSummaryPanel`'s
EXISTING (currently inert per UAT round-5 #2's own removed-banner decision) `overallRisk` prop. Deliberately did
**NOT** resurrect the removed banner — that was a prior, still-standing UX decision, out of this task's scope to
reverse. The data is now correct and complete for whenever/if a future consumer needs it; today it is a
compile-verified no-visible-change threading (0 tsc errors), not a new rendered element.

## Step 3 — 128KB budget + notice (implementation)

See Step 1's design section above for the FULL rationale. Implementation: `ComposeReviewFindingsDegraded` type
(`ComposeWorkspace.types.ts`, to avoid a `ComposeWorkspace.tsx` ⇄ `ComposeBannerStack.tsx` circular import),
`reviewFindingsDegraded` state in `ComposeWorkspace.tsx`, a new banner row in `ComposeBannerStack.tsx` mirroring
the EXISTING `partialApply` dismiss/reshow-on-new-instance convention verbatim (same hook shape, same
`data-testid` naming convention).

## Step 4 — Coexistence + supersede (implementation)

`materializeComposeDraftFromLedger`'s untargeted branch: `composeOutputs.filter(isFindingsShapedComposeOutput)`
→ loop, materialize EACH (never evicted). `composeOutputs.filter(o => !isFindingsShapedComposeOutput(o))` → pick
highest-turn (unchanged semantics) → materialize via the EXTRACTED (unchanged-behavior) `materializeEditOutput`.
**Important fix mid-implementation**: the ORIGINAL draft had `if (composeOutputs.length === 0) return;` BEFORE
the targeted/untargeted branch split — this would have silently skipped the degraded-notice check for the exact
scenario it needs to catch (a fully-truncated entry means `composeOutputs` is COMPLETELY EMPTY). Moved the
empty-bail to be scoped ONLY to the targeted branch; the untargeted branch now runs its reset + degraded-check
logic even on an empty list.

## Step 5 — 031-residual dedupe guard (implementation)

See Step 1 above. `materializedFindingsKeysRef` (a `React.useRef<Set<string>>`, not state — no re-render needed)
shared between `onAdvisoryComments` (live, write-only bookkeeping after a successful placement) and
`materializeFindingsOutput` (ledger path, read-then-write).

## Step 6 — Tests

**New integration tests** (`ComposeWorkspace.redline-from-ledger.test.tsx`, real `ComposeWorkspace` + real
`ComposeEditor`/TipTap, network mocked at the `@spaarke/auth` boundary):
1. **Reopen-full-restore** — gutter anchor (1) + summary-panel row (opened via the toolbar toggle, correct
   takeaway text + risk badge) + zero non-GET network calls.
2. **Malformed/corrupted degraded notice** — a findings-shaped output present but 0 usable items → visible
   banner (`reason: 'malformed'`), no crash, no placement, no redline (acceptance criterion 5, positive half of
   Leg B, directly observable — no marker needed).
3. **Truncated/skipped degraded notice via the marker** — two sequential mounts (unmount/remount, same
   `DOC_SESSION`): first restores cleanly + writes the marker; second (empty ledger response, simulating a
   truncation-skip) surfaces the notice with the marker's expected count (`reason: 'skipped'`).
4. **Findings + edit coexistence** — a findings output (turn 1) and a LATER edit output (turn 2) both restore
   simultaneously on reopen — the exact pre-032 bug (findings would have been silently dropped).
5. **Supersede protection** — findings output + a two-turn edit-binding sequence (v1 superseded, v2 current) —
   findings restore, only v2's text renders, v1's text never appears.
6. **031-residual dedupe guard** — live placement (1 anchor) → simulated Save status-cycle (`loaded`→`saving`→
   `loaded`, SAME editor instance, verified via the reducer's `showEditor` gate) → still exactly 1 anchor.
   Empirically verified load-bearing (see Step 1).

**New unit tests** (`ComposeBannerStack.test.tsx`): 6 tests for the `reviewFindingsDegraded` banner row —
'skipped' message + count, 'malformed' message (no false count claim), omitted/null renders nothing, dismiss,
reshow-on-new-instance, dark mode (no hardcoded hex, ADR-021).

### Results (exact)

```
ComposeWorkspace.redline-from-ledger.test.tsx  → 16/16 PASS  (10 pre-existing (DEF-09/DEF-11/DEF-16-030) + 6 new task-032)
ComposeBannerStack.test.tsx                    → 18/18 PASS  (12 pre-existing + 6 new task-032)
ComposeEditor.advisoryComments.test.tsx        →  7/7  PASS  (unaffected — regression check)
tsc --noEmit (Compose.Components)              →  0 errors
npm run build (Compose.Components)             →  clean
Full package suite (npx jest, no filter)       → 822 total / 807 pass / 15 fail across 5 suites
```

**The 15 failures are the EXACT pre-declared pre-existing set** named in the POML brief:
`ComposeWorkspace.{bornInEditorSave,imports,saveOpLogPreservation,search}` + `stepOperationInterceptor` — the
"mocked `compose-editor-stub` mount/DI failure mode." Verified NOT mine: `git status` shows I touched only
`ComposeBannerStack.tsx`, `ComposeEditor.tsx`, `ComposeWorkspace.tsx`, `ComposeWorkspace.types.ts`,
`ComposeWorkspace.redline-from-ledger.test.tsx` — none of the 5 failing test files. My source change did not
break mounting (the redline-from-ledger suite mounts the REAL `ComposeEditor` and passes 16/16); the 5 failing
suites use a MOCKED `ComposeEditor` and fail on their own mock wiring, unrelated to this task.

**UI-tests (deferred per task assignment)**: "Zero-LLM reopen restore" (network trace) and "Dark mode" — both
`<ui-tests>` in the POML — deferred to tasks 060/061 (deploy + e2e), per the POML's own note. The in-repo proof
is: (a) the zero-non-GET-calls assertions in tests 1 and 4 above (the network-trace half), (b) the
ComposeBannerStack dark-mode unit test (the color-token half — the summary panel's own dark-mode compliance is
pre-existing/unchanged, task 030's territory).

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | Reopen: gutter + summary-panel rows + overallRisk all restore; zero LLM calls | **PASS** (gutter+panel: DOM-proven) / **PASS-documented leg** (overallRisk: data-path complete, compile-verified end-to-end; the dedicated banner stays intentionally unrendered per the standing UAT round-5 #2 decision — see Step 2) | Test 1 (reopen-full-restore); `tsc --noEmit` 0 errors proves the type-safe threading |
| 2 | A synthetic >128KB findings set restores via chunking OR shows an explicit notice — never silent absence | **PASS — Leg B chosen** (chunking needs a server write-seam change, out of the read-only boundary per the POML's own pre-authorization) | Tests 2 + 3 (malformed + marker-based skipped notice); honest residual documented (cross-device Leg-B gap, Step 1) |
| 3 | Review → accept a draft-alternative → reload: findings AND edit state both restore | **PASS** | Test 4 (findings+edit coexistence) |
| 4 | Supersede an edit output → findings unaffected (ledger + UI proof) | **PASS** | Test 5 (supersede protection); server-side BindingId-scoping verified by code read (`ComposeDisposition.ResolveCurrent`) |
| 5 | Negative: a corrupted/partial chunk set surfaces a visible degraded-restore notice, not a crash | **PASS** | Test 2 (malformed → 'malformed' reason banner, 0 crash, 0 placement) |

## §10 BFF Hygiene

N/A — zero `src/server/**` files touched (read-only verification only), zero new endpoints, zero new packages.
Placement Justification: this task's entire surface is `src/client/shared/Spaarke.Compose.Components/**`.

## §11 Component Justification (new abstractions)

- **`isFindingsShapedComposeOutput`** — extraction of a pre-existing inline check (030's own structural guard),
  reused across 3 call sites. No new capability; pure refactor-for-reuse.
- **`computeAdvisorySignature`** — Existing: no dedupe mechanism exists for the live path (`lastMaterializedKey`
  is edit-only, scalar). Extension: overloading `lastMaterializedKey`'s single-scalar semantics with a Set would
  be a breaking behavior change to well-tested existing edit-path code — a separate, purpose-built store is
  cleaner. Cost-of-doing-nothing: EMPIRICALLY PROVEN (Step 1's disable-and-rerun) — a genuine duplicate advisory
  comment thread.
- **`readReviewFindingsMarker`/`writeReviewFindingsMarker`** — Existing: no durability-detection mechanism exists
  client-side; the FR-29 layer was considered and rejected (different store, wider blast radius — Step 1).
  Extension: N/A (no existing marker mechanism to extend). Cost-of-doing-nothing: acceptance criterion 5's
  negative case (never silent absence) would be unmet for the server-side-skip case, per the ARCHITECTURAL FACT
  that `ChatEndpoints.ProjectComposeOutputs` drops a truncated entry entirely (verified by code read, not
  hypothesis).
- **`ComposeReviewFindingsDegraded` banner row** — a new ROW in the EXISTING `ComposeBannerStack`, not a new
  panel/component; mirrors the shipped `partialApply` pattern verbatim (§11 reuse-first).

## Quality gates (self-run, FULL rigor)

**code-review (self)**: No `any` introduced (the one `as ComposeReviewPayload` cast is the SAME pre-existing
widening pattern 030 already used, unchanged in kind). No try/catch-log-rethrow. Comments explain WHY (design
choices, rejected alternatives, the empirical dedupe-guard proof) not WHAT. Every extracted function
(`materializeEditOutput`/`materializeFindingsOutput`/`materializeSingleOutput`) preserves the EXACT pre-032
behavior for its slice (proven: 10/10 pre-existing DEF-09/DEF-11/030-FR-16 tests pass UNMODIFIED). PASS.

**adr-check (self)**: ADR-040 (store-before-render, append-only, 128KB cap) — PASS, no new store, no ledger
mutation, the sessionStorage marker carries a COUNT only (never content, never authoritative). ADR-030
(PaneEventBus) — PASS, zero new channel/event-type, reused two ALREADY-DECLARED optional fields
(`overallRisk`/`sessionId`) the emitter already sends. ADR-021 (Fluent v9/dark mode) — PASS, new banner reuses
`MessageBar`/tokens, dark-mode-tested. ADR-039 (grounded execution/closed catalogs) — N/A, no new dispatch
mechanism. §10 — N/A, zero server files. §11 — all four new abstractions justified above with concrete,
evidence-backed failure modes (one EMPIRICALLY proven via a disable-and-rerun test). No Critical or Warning
findings.

## Deviations / escalations

**No `<escalation>` trigger fired.** The POML's own escalation trigger ("if the closed guarantee cannot be met
without changing the server projection/selection...STOP") was PRE-EMPTED by the POML's own fallback clause for
the specific case that DID arise (chunking is server-only-reachable) — the POML explicitly authorized choosing
Leg B in that exact scenario, so no live escalation round-trip was needed; documented the choice + rationale
per CLAUDE.md §6.5-adjacent transparency instead.

**One honest, narrowed residual** (flagged, not silently dropped): the 128KB Leg-B degraded notice is same-tab
(`sessionStorage`)-scoped; a genuinely NEW tab/device reopening a review that was truncated server-side, with no
intervening same-tab visit, sees nothing (not even a degraded notice) — the residual "silent absence" case,
narrowed from "any truncated review" to "a truncated review reopened cross-device with no same-tab marker."
Closing it fully needs either a server-side truncation-marker passthrough or an FR-29-layer marker (both
considered, both out of this task's read-only-server / contained-blast-radius boundaries — see Step 1).
Recommended follow-on: whichever future task next touches `ChatEndpoints.ProjectComposeOutputs` should consider
projecting a lightweight `{key, truncated:true}` stub (no content) instead of a full skip, closing this
client-side detection gap without any content-size regression.

**One re-attribution correction**: 031's notes framed the dedupe residual as triggered by the `externalChange`/
`requestLoad` reload path ("the SAME editor instance"). Read the reducer + render logic: that specific path's own
code comment ("A CLEAN editor was already remounted transparently") indicates `ComposeEditor` UNMOUNTS +
REMOUNTS on that transition (`showEditor` is false during `'loading'`), which would actually WIPE the live-placed
comment before any race could occur. The `requestSave`→`'saving'`→`saveSucceeded`→`'loaded'` cycle is the one
that genuinely preserves the SAME editor instance (`showEditor` covers BOTH `'loaded'` and `'saving'`) — that is
what the fix (and its test) targets. The fix itself is IDENTICAL either way (content-signature dedupe, session-
scoped) — only the concrete reproduction scenario differs from 031's prose; documented for the record.

## Files modified

- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` — `deriveOverallRisk` import;
  `isFindingsShapedComposeOutput`/`computeAdvisorySignature`/`readReviewFindingsMarker`/
  `writeReviewFindingsMarker` module helpers; review-summary state block relocated + extended
  (`reviewSummaryOverallRisk`, `reviewFindingsDegraded`, `reviewSummarySessionRef`,
  `materializedFindingsKeysRef`); `materializeEditOutput`/`materializeFindingsOutput`/`materializeSingleOutput`
  extracted; `materializeComposeDraftFromLedger` refactored (targeted vs. untargeted split; untargeted now
  replays ALL findings + latest edit + degraded-check, never bails on an empty list); `onAdvisoryComments`
  bookkeeping additions; render wiring (`reviewFindingsDegraded` → `ComposeBannerStack`, `overallRisk` →
  `reviewSummary` bundle).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.types.ts` — new
  `ComposeReviewFindingsDegraded` exported type.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeBannerStack.tsx` — new
  `reviewFindingsDegraded` prop + dismissable banner row (mirrors `partialApply`).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` — `reviewSummary.overallRisk`
  additive field, forwarded to `AgreementReviewSummaryPanel`'s existing (inert) prop.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.redline-from-ledger.test.tsx` — Save
  endpoint mock branch; `window.sessionStorage.clear()` in `beforeEach`; 6 new integration tests.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeBannerStack.test.tsx` — 6 new unit tests.

**Not touched** (hard boundaries honored): any `src/server/**` file (read-only verification of
`SessionLedgerEntries.cs`/`ChatEndpoints.cs`/`ComposeDisposition.cs`); `src/solutions/SpaarkeAi/**` (read-only
verification of `useNdaReviewAdvisoryCommentsBridge.ts`/`PaneEventTypes.ts`); `AgreementReviewSummaryPanel.tsx`
(its render body is UNCHANGED — only its ALREADY-DEFINED `overallRisk` prop now receives a real value from
above); `.claude/**`; `current-task.md`; `TASK-INDEX.md`; no git commit.
