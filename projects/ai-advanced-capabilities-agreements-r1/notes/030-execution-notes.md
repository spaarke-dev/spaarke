# Task 030 — Execution Notes: FR-16(a,b) Binding disposition→compose + findings materializer branch

> Rigor: FULL (code implementation on the ADR-040/043 ledger spine + ComposeWorkspace hot file; TEST-MODIFYING
> override — unconditional code-review + adr-check) · Model tier: opus @ high · Step mode: directional · Status: complete

## Summary

Made agreement-review findings **durable-by-disposition**:
- **(a)** Flipped the agreement-review Binding's `sprk_disposition` Informational (100000000) → Compose (100000006)
  in the mirror + deployed the SAME flip to spaarkedev1 (surgical, one field). Server code UNCHANGED — the compose leg
  is already store-then-pass-through, exactly as the design predicted.
- **(b)** Added a **FINDINGS branch** to `ComposeWorkspace.materializeComposeDraftFromLedger`: a compose-disposition
  ledger payload carrying `flaggedSections[]` re-materializes via `ComposeEditor.placeAdvisoryComments` (metadata intact),
  instead of falling into the draft/edits/comments branches. Reopening a reviewed document restores the gutter Review
  Notes deterministically with ZERO LLM calls.

## Step 0 — Seam re-verification (line refs were PRE-MERGE; re-verified against current worktree)

All HUB-R1-REVIEW §4 seams re-confirmed at their CURRENT locations (several files moved since the pre-merge refs):

| Seam | POML ref | Verified location (now) |
|---|---|---|
| Binding row to flip | mirror `nda-review` row | `infra/dataverse/sprk_playbookconsumer-rows.json` — consumerType `nda-review`, actionCode `agreement-review` (002 asymmetry, intentional), was `disposition: 100000000` |
| Compose store-then-pass-through | OutputRouter.cs :193-224,:280-281 | `DispositionRoutability.cs` L78 — `Compose` `Routable=true` (E-20). **NO registry change needed.** |
| compose-outputs projection | ChatEndpoints.cs :1312-1334 | (server unchanged — not touched) |
| FR-04 refresh effect → materializer | ComposeWorkspace.tsx :1609-1616 → :1385-1484 | `ComposeWorkspace.tsx` — FR-04 effect (`useEffect` on `[state.status, state.sessionId]`) → `materializeComposeDraftFromLedger`; branches on `edits[]` / single-edit / `comments[]` / rationale. `flaggedSections[]` matched **NO** branch → confirmed the gap. |
| placeAdvisoryComments return shape | ComposeEditor.tsx ~:2555+ | `ComposeEditor.tsx` L2642 — returns `{ placed, failed[{targetText, kind:'not_found'|'ambiguous'}] }` (task 012 precision fix present). |
| Live-path projection convention | `useNdaReviewAdvisoryCommentsBridge.ts` | `src/solutions/SpaarkeAi/src/components/conversation/useNdaReviewAdvisoryCommentsBridge.ts` — `projectFlaggedSectionsToAdvisoryComments` handles BOTH vintages (legacy `explanation` + post-split `flaggedClause`/`assessment`). |
| onAdvisoryComments live map | ComposeWorkspace ~:1765 | `ComposeWorkspace.tsx` `onAdvisoryComments` receiver maps event items → `placeAdvisoryComments` incl. flaggedClause/assessment. |

**Confirmed nothing already routes findings**: `flaggedSections[]` matched no existing materializer branch; grep confirmed
no other consumer re-materializes a review payload from the ledger. The gap was real.

## Step 0 — Routability evidence (ADR-043 escalation trigger did NOT fire)

`DispositionRoutability.cs` (the ONE disposition source, ADR-043 §3) registers
`BindingDisposition.Compose` with `Routable = true` (L78, shipped by E-20 for compose actions; `IsAdmissible` ⇔
`IsRoutable`). The Informational→Compose flip therefore needs **NO registry change** and no server change — it is pure
Binding-table DATA (ADR-039: disposition is catalog-declared data, never runtime-judged). The POML escalation trigger
("if the flip DOES require a DispositionRoutability registry change … STOP") **did not fire**. No overlap with
notification-spine-r1 (which edits the same file) — I made ZERO edits to `DispositionRoutability.cs`.

## Step 1 — Binding flip (mirror + env) — `-DiffOnly` before/after evidence

**Mirror** (`infra/dataverse/sprk_playbookconsumer-rows.json`): flipped the `nda-review` row `disposition` 100000000 →
100000006; updated the row's `$comment-agreements-r1-002` to record the flip landed (was "flip is task 030").
**Only the disposition field changed** — consumerType stays `nda-review`, actionCode stays `agreement-review`,
risk/captureMode/toolDescription untouched.

**Deploy method — surgical MCP update, NOT full Seed (deliberate):** a pre-deploy `-DiffOnly` revealed the worktree
mirror is **behind live on 16 unrelated fields** (env AHEAD of mirror on create-matter/create-task/draft-correspondence
disposition+chipTransitions+toolDescription, chat-classify/chat-summarize chipTransitions, and the nda-review
*toolDescription* — all from other projects' live edits + the pre-merge worktree lag noted in HUB-R1-REVIEW). A full
`Seed` (mirror→env) would have **clobbered those 16 live values with stale mirror data** — destructive and out of scope.
So I updated ONLY `sprk_disposition` on the one nda-review row via `mcp__dataverse__update_record`
(`sprk_playbookconsumerid = 683051bd-2989-f111-8077-7ced8ddc4a05`).

`-DiffOnly` evidence:

| | Total drifts | `nda-review :: disposition` |
|---|---|---|
| **BEFORE deploy** | 17 | **DRIFT** — mirror 100000006, env 100000000 |
| **AFTER deploy** | 16 | **GONE** — env == mirror == 100000006 (Compose) |

Post-deploy read confirms `sprk_disposition = 100000006` / `sprk_dispositionname = "Compose"` on the live row. The
remaining **16 drifts are ALL pre-existing** (env-ahead-of-mirror on rows I never touched) — a separate mirror-hygiene
item (needs an `-Export` by an owner), NOT introduced or altered by task 030. The `nda-review :: toolDescription`
drift is likewise pre-existing (env has the nda-r1/002 wording; my mirror has the older text) — I did NOT touch it
(disposition-only, per the task boundary).

## Step 2 — Findings branch (design)

Added to `ComposeWorkspace.tsx`:
1. `ComposeReviewFlaggedSection` (loose/optional interface — one flagged clause; both vintages) + `ComposeReviewPayload`
   (local superset of `ComposeDraftPayload` adding `overallRisk?` + `flaggedSections?`). Kept **local** to ComposeWorkspace
   (the payload's only consumer) rather than widening the shared edit vocabulary — a review result is not an edit — so
   the change is contained to the one file the task owns (ComposeEditor.tsx untouched).
2. `projectLedgerFindingsToAdvisoryComments(flaggedSections)` — exported pure helper mapping `flaggedSections[]` →
   `AdvisoryCommentInput[]`.
3. A branch inside `materializeComposeDraftFromLedger`, placed **before** the `edits[]`/single-edit branches and after the
   idempotency guard: detects `flaggedSections[]` structurally → projects → `editor.placeAdvisoryComments(...)` → surfaces
   unresolved anchors via `console.warn` (FR-19 "do not guess", same as the live path) → `setLastMaterializedKey` + `return`
   (so it never falls through to a redline).

**Both payload vintages** (POML requirement): the projection mirrors the SpaarkeAi bridge exactly — legacy entries carry
`explanation` verbatim; post-split entries (no `explanation`) compose the thread text / legacy-degrade source from
`[flaggedClause, assessment].join('\n\n')` AND carry the discrete `flaggedClause`/`assessment` through unchanged so the
gutter/export render the structured "Flagged clause / Assessment says" form with no string-parsing (task 052 seam).
Legacy `explanation` wins as the text source when both coexist (deterministic precedence).

**Metadata intact** (the point / §11 / CLAUDE.md): routed via `placeAdvisoryComments` (carries
riskLevel/sectionRef/standardRef/flaggedClause/assessment), NOT `registerAiReviewComments` (which drops
risk/section/standard). Confirmed against the two shipped conventions (bridge + onAdvisoryComments).

**Idempotency**: reuses the existing `lastMaterializedKey` guard (line ~1643, before the branch). My branch sets the key
and returns; a refresh / duplicate Flow-5 signal for the same key short-circuits at the guard → single placement.

**Graceful malformed skip**: an entry that is not an object / missing `quotedText` / missing all bodies is skipped by the
projection (never thrown). A findings-shaped payload that yields NO usable items logs
(`"…no usable flagged sections…", key`) + marks the key handled + returns — never crashes, never partial-places, never
falls through to a redline.

### §11 projection-helper decision (documented)

Wrote the ~15-line pure map **locally** in Compose.Components rather than hoisting a shared helper across the
SpaarkeAi→Compose.Components boundary. Reasoning: the bridge's `projectFlaggedSectionsToAdvisoryComments` is SpaarkeAi-side
and produces a **PaneEventBus event** shape (`ComposeAdvisoryCommentItem`); this reads the **raw ledger payload** and
produces the **ComposeEditor input** (`AdvisoryCommentInput`) directly. A cross-package hoist would introduce a new
SpaarkeAi→shared-lib coupling for a trivial pure function; the two copies share one **authored convention** (both handle
both vintages identically), cross-referenced in each JSDoc. Cost-of-doing-nothing (§11 Q3): without the branch, a
compose-disposition findings payload falls into the single-edit redline branch → `materializeComposeDraft` on
`{overallRisk, flaggedSections}` (no `new_text`/`target_text`) = a no-op redline + `registerAiEditReasonComment` no-op →
**reopening a reviewed doc shows NO gutter Review Notes** (concrete failure). The branch is justified; the helper extends
the existing convention rather than inventing one.

### Clean seams left (NOT implemented — other tasks)

- **Summary-panel restore**: my branch does NOT set `reviewSummaryFindings` (only the live `onAdvisoryComments` does) —
  summary-panel restore on reopen is **task 032**. Seam left clean (the gutter Review Notes restore; the summary panel
  stays empty until 032 wires it).
- **Session routing (DEF-09) + apply-leg gating**: **task 031**. My branch is the READ/reopen half; whether the LIVE
  dispatch also emits a `compose_advisory_comments` event (in-session double-emit) is 031's routing decision. The branch
  is idempotent per-key, so 031 can wire the live path without my branch double-placing on reopen.
- **Payload caps / truncation-marker surfacing**: **task 032** (the projection skips over a truncated payload's absent
  `flaggedSections` gracefully — the malformed-grace path).

## Step 3 — Tests (all green; pre-existing failures separated)

Extended `ComposeWorkspace.redline-from-ledger.test.tsx` (the redline-from-ledger family):
- **5 unit tests** on `projectLedgerFindingsToAdvisoryComments` — the authoritative metadata + both-vintages proof
  (legacy `explanation`; post-split `flaggedClause`+`assessment` composes+carries; legacy precedence; malformed-entry
  skip incl. non-object/null/blank; empty input).
- **2 integration tests** (real ComposeWorkspace + real ComposeEditor/TipTap):
  1. reopening a reviewed doc restores the advisory anchor from the ledger (`span[data-comment-id]` = the right clause),
     asserts **NO redline** (`[data-compose-mark="insertion"|"deletion"]` = 0 — the comment-anchor mark is a distinct
     `data-compose-mark="comment-anchor"`), **ZERO dispatch** (every network call is a GET — no POST/dispatch; the point
     of FR-16), and **idempotent** on a duplicate Flow-5 signal (still 1 anchor).
  2. malformed findings payload (no usable flagged sections) → `console.warn` + graceful skip, 0 placements, 0 redline,
     no crash.

**Test-strategy split (honest):** metadata-intact + both-vintages is proven at the **unit** level (the projection is the
only place metadata could be dropped on the reopen path — deterministic, no jsdom layout). The **integration** tests prove
the branch WIRES the projection to `placeAdvisoryComments` on reopen (placement, zero-dispatch, idempotency, malformed
grace). Gutter-card DOM assertion was deliberately NOT used — `ComposeCommentGutter` positions cards via `coordsAtPos`
(jsdom-fragile), the same reason the shipped DEF-11 comments test asserts a state count rather than gutter cards.

Results:
```
ComposeWorkspace.redline-from-ledger.test.tsx  → 10/10 PASS  (3 existing DEF-09/DEF-11 + 5 unit + 2 integration)
ComposeEditor.advisoryComments.test.tsx        → 7/7 PASS    (placeAdvisoryComments regression — unaffected)
tsc --noEmit                                    → 0 errors
npm run build (tsc)                             → 0 errors
Full package suite                              → 810 total / 795 pass / 15 fail across 5 suites
```
The **15 failures are the exact pre-declared pre-existing set** (`ComposeWorkspace.{bornInEditorSave,imports,
saveOpLogPreservation,search}` + `stepOperationInterceptor` — the mocked-`compose-editor-stub` mount/DI failure mode,
named in task 012's notes AND this task's brief). Proof they are not mine: the redline-from-ledger suite mounts the REAL
ComposeWorkspace + ComposeEditor and passes 10/10, so my source change does not break mounting; the 15 failing tests use a
MOCKED ComposeEditor and fail on their own mock wiring, unrelated to the findings branch.

## Step 4 — Quality gates (self-run, FULL + TEST-MODIFYING override)

**code-review (self):**
- §11: one new exported pure helper + 2 local types; justification concrete (cost-of-doing-nothing = no restored Review
  Notes on reopen); documented in JSDoc with grep-verified overlap to the bridge. PASS.
- AI code smells: none. No try/catch-log-rethrow (branch has no try; outer try is pre-existing). No `any` (used `unknown`
  + per-entry narrowing; the one `as ComposeReviewPayload` is a safe widening to an optional-superset, documented). Pure
  single-responsibility helper. Comments explain WHY (vintage handling, metadata-preservation, §11), not WHAT.
- Metadata preservation (the point): via `placeAdvisoryComments`, NOT `registerAiReviewComments`. PASS.
- **Tier-3 logging note (honest):** the failed-anchor `console.warn(..., result.failed)` logs `AdvisoryCommentFailure`
  (carries `targetText`, Tier-3) — this **mirrors the shipped live `onAdvisoryComments` convention verbatim** (same log
  shape). Not a new leak; if the live convention needs Tier-3 hardening it is a pre-existing, cross-cutting item, out of
  this task's scope. The "no usable flagged sections" warn logs only `target.key` (Tier-1, safe).

**adr-check (self):**
- **ADR-040** (ledger store-before-render; append-only): consumes the stored `ComposeLedgerOutput` AS-IS — no new ledger
  shape, no mutation, re-materializes from the durable ledger (not a client buffer). PASS.
- **ADR-043** (DispositionRoutability single-source): verified Compose already Routable=true — **zero registry change**;
  disposition stays catalog-declared DATA. PASS. Escalation trigger did not fire.
- **ADR-049** (no text-search when a deterministic paraId resolution exists): unchanged — `placeAdvisoryComments` still
  tries the deterministic sectionRef→paraId path first (task 011), legacy text fallback second; I only feed it items. PASS.
- **ADR-039** (closed catalog; disposition is data; ride the shipped dispatch seam): flip is Binding data, no new BFF
  route. PASS.
- **§10 BFF Hygiene**: NO server file touched (see below). N/A publish-size / CVE. PASS.
- **ADR-038** (never weaken a test): added tests, weakened none; the 5 pre-existing failing suites untouched. PASS.

No Critical or Warning findings.

## Deviations / choices

- **Deploy via surgical MCP update, not full `Seed`** — see Step 1. Full Seed would have clobbered 16 pre-existing live
  values with a stale worktree mirror. The task explicitly offered "Dataverse MCP … or the seed script"; MCP was the
  non-destructive choice. `-DiffOnly` still provides the before/after evidence (17→16, nda-review disposition drift gone).
- **Optional ComposeSummaryPageGenerator.cs comment cleanup — LEFT for wrap-up (my choice, per POML).** Rationale:
  touching any server file invokes the §10 BFF publish-size verification overhead for a zero-value comment fix; keeping my
  change surface entirely **client + Dataverse config** is the cleanest boundary for a durable-work-product change. Also,
  the `explanation` references in that file are still accurate (the server `NdaReviewFlaggedSectionInput` record still
  deserializes `explanation` — the server summary generator hasn't been updated for the 002 split; that is a separate
  server-side concern, not a pure comment fix). Only the `nda-review.action.json` filename reference is stale. Deferred.
- **Manual reload-network-assert (POML step 4) = UAT** — deferred to tasks 060/061 (deploy + e2e zero-LLM-reopen assert),
  per the POML. The in-repo proof is the integration test's zero-dispatch (GET-only network) assertion.

## Files modified

- `infra/dataverse/sprk_playbookconsumer-rows.json` — nda-review row `disposition` 100000000→100000006 + `$comment`
  update (flip-landed note). Env deployed to match (surgical MCP update on `683051bd-2989-f111-8077-7ced8ddc4a05`).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` — `AdvisoryCommentInput` import;
  `ComposeReviewFlaggedSection` + `ComposeReviewPayload` types; exported `projectLedgerFindingsToAdvisoryComments`;
  findings branch in `materializeComposeDraftFromLedger`.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.redline-from-ledger.test.tsx` — 5 unit + 2
  integration tests.

**Not touched:** any `src/server/**` file (server unchanged — the design's elegance held); `ComposeEditor.tsx`;
`AgreementReviewSummaryPanel.tsx` / `ComposeCommentGutter.tsx` (task 040 owns them this wave); `DispositionRoutability.cs`
(notification-spine-r1's surface — zero change needed); `current-task.md` / `TASK-INDEX.md` (hard boundary); no git commit.
