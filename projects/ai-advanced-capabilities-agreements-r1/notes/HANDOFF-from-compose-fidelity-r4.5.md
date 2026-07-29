# Handoff — from `spaarkeai-compose-fidelity-r4.5` → `ai-advanced-capabilities-agreements-r1`

> **Created**: 2026-07-28 by the R4.5 project (Compose Legal Fidelity — read/reference).
> **Why here**: `nda-r1` is **closed**; these two items are AI-Advisory-Review / agreements-review concerns (outside R4.5's read/reference scope), so they route to the agreements-r1 successor per owner direction (2026-07-28).
> **Reference commits (branch `work/spaarkeai-compose-fidelity-r4.5`)**: task 043 (review-note label fix), WS-4 tasks 040/041/042 (reference/citation layer).

---

## Context you inherit (already shipped in R4.5)

R4.5 built + deployed the **read/reference fidelity** foundation the agreements review should build on:

- **WS-3 numbering engine** — every numbered paragraph/heading now carries its exact Word legal number, computed deterministically server-side (`ComposeDocxProjectionBuilder.NumberingComputationEngine`) and exposed as:
  - `ComposeDocxProjection.ParaIdMap[].ComputedNumber` (+ `NumberingLevel`, `ListPath` ordinal chain, `HeadingLevel`) — projection payload **and** session ledger (`ChatSession.ReferenceMap`).
  - client node attribute `computedNumber` on `paragraph`/`heading` (via `composeNumberAtomExtension.ts`, from `data-computed-number`).
- **WS-4 `CitationResolver`** (`Services/Compose/CitationResolver.cs`) — pure resolver: citation string ↔ `paraId`, covering single ("Section 4.2"), sub-item ("4.2(b)(iii)"), and range ("Sections 4–7"). **Not yet wired to a consumer** — the agreements review is the natural consumer.
- **task 043** — `deriveClauseLocationLabel` (`ndaClauseLocation.ts`) now cites the clause's **computed legal number** ("Sec 2") instead of falling back to doc-order ("Para 3"). This is document-agnostic (see item 2).

Deployed to dev: `spaarke-bff-dev` (BFF) + `sprk_spaarkeai` on `spaarkedev1` (client), 2026-07-28.

---

## ITEM 1 — DEF-01: advisory-comment **placement** target-resolution bug (needs a fix)

**Symptom**: `ComposeEditor.advisoryComments.test.tsx` fails — `placeAdvisoryComments` returns `placed = 2` where the test expects `placed = 1` (a should-be-**ambiguous** target gets placed instead of being reported as `not_found`/`ambiguous`).

**Location**: `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` — `placeAdvisoryComments` (~`:2519`); test `ComposeEditor.advisoryComments.test.tsx` (test: *"a unique target resolves + materializes a comment thread; not_found/ambiguous targets are reported, not dropped"*, sessionId `session-nda-review-031`).

**Status / provenance**:
- **Pre-existing on master** — confirmed failing at R4.5 task 012 *before* any R4.5 change touched that test file; present identically before/after (`git stash` A/B verified).
- **NOT the numbering issue** — R4.5's WS-3 numbering + the task-043 location-label fix do **not** touch this code path (verified 2026-07-28: the label fix leaves `placed` unchanged at 2). Originally mis-hypothesized as "same root cause as the label"; that was disproven.
- **Distinct root cause**: target-resolution **precision** in `placeAdvisoryComments` — the match/ambiguity logic materializes a comment for a target that should have been surfaced as ambiguous. This is an AI-Advisory-Review correctness concern (which clause an advisory note anchors to), squarely in the agreements-review domain.

**Why it matters for agreements**: if a should-be-ambiguous advisory target gets placed, a review note can anchor to the **wrong clause** — a correctness risk for legal review across all agreement types, not just NDAs.

**Recommended approach**: investigate `placeAdvisoryComments`' target match + ambiguity thresholds; a target that matches >1 location (or below a confidence bar) must be reported (`not_found`/`ambiguous`), not silently placed. Now that WS-4 gives a stable `paraId → computedNumber` map + `CitationResolver`, anchor advisory targets by **computed clause number** (deterministic) rather than fuzzy text/position where the AI supplies a section reference — that likely removes the ambiguity class entirely. Re-enable the test (don't weaken the assertion) as the exit criterion.

---

## ITEM 2 — Generalize `ndaClauseLocation.ts` naming NDA → agreements (cosmetic, but do it)

**The concern (owner, 2026-07-28)**: "this change needs to be part of our general 'agreement' (and any document) fidelity review — is the fix NDA-only because it's in `ndaClauseLocation.ts`?"

**Answer**: The **logic is already document-agnostic** — `deriveClauseLocationLabel` / `findGoverningHeading` / `computedNumberAt` have **zero** NDA-specific branching. Every "nda" reference is a comment, the filename, or the name of the review *model* (`NDA-REVIEW`) that supplies `sectionRef`. The function reads the computed legal number + headings + page/para from **any** document and is invoked by the shared review-note renderers (`ComposeCommentGutter.tsx`, `ComposeEditor.tsx`). So the task-043 fix benefits **every** agreement/document that produces review notes — it is not NDA-gated.

**What agreements-r1 should do**:
1. **Rename for clarity** — `ndaClauseLocation.ts` → e.g. `clauseLocation.ts` (and `NdaReviewSummaryPanel` naming as appropriate), updating the imports in `ComposeCommentGutter.tsx`, `ComposeEditor.tsx`, and tests. Pure rename, no logic change. Removes the "is this NDA-only?" confusion permanently.
2. **Confirm the review *trigger* is general** — the location-label logic is general, but verify the AI Advisory Review itself runs for all agreement types (not gated to an NDA `consumerType`). That generalization is agreements-r1's core deliverable; the label + reference layer are ready for it.
3. **Consume WS-4** — wire the review-note anchoring + citations to `ParaIdMap.ComputedNumber` / `CitationResolver` (see Context above), which is what makes item 1's ambiguity fixable deterministically.

---

## Pointers
- R4.5 branch: `work/spaarkeai-compose-fidelity-r4.5` · project: `projects/spaarkeai-compose-fidelity-r4.5/`
- R4.5 defer-issues (source of DEF-01): `projects/spaarkeai-compose-fidelity-r4.5/notes/defer-issues.md`
- WS-4 reference layer + resolver: tasks 040/041/042 notes in the same dir.
- Label fix: task 043 (`ndaClauseLocation.ts` + `.test.ts`).
