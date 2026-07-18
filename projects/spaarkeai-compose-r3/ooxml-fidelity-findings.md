# Compose R3 — OOXML Fidelity Findings & Recommendations (E1/E2/E3)

> **Created**: 2026-07-14
> **Origin**: Grounded gap-check run during `spaarkeai-compose-r2` UAT close-out, comparing the shipped Compose save/redline path against the `sdap-WORD-studio-r2/design.md` sibling design. Feeds the R3 `design.md`.
> **Status**: Findings + recommendations (pre-design). Aligns with and refines the R3 seed [`notes/seed-README.md`](notes/seed-README.md) Scope Areas A–J and the prior R1/R2 feedback.
> **Constraint carried forward (BINDING)**: **NO TipTap product features — paid OR unpaid** (IP / lock-in). All three enhancements are our-code on the MIT TipTap base. Track-changes/comments already ship home-grown (no Pro) — see §Current State.

---

## Why this doc

The R3 seed README (2026-07-01) scoped the fidelity gap from R1 UAT ("Word loses its formatting on save"). Since then **R2 shipped substantial fidelity machinery** (native `w:ins`/`w:del`/`w:comment` via `DocxAnnotationWriter`; a true retained-original delta path in `PushAnnotationsAsync`). This doc grounds the R3 scope in **what the running build actually does today** (with file:line evidence), states the three enhancements (E1/E2/E3) with verdicts, and reconciles the now-stale parts of the seed. It is the input to `design.md`.

---

## Current state (grounded 2026-07-14) — what R2 actually shipped

| Capability | Reality today | Evidence |
|---|---|---|
| Track changes | **Built home-grown, shipped.** Our marks → `DocxAnnotationInput` → server `DocxAnnotationWriter` emits **native OOXML `w:ins`/`w:del`**. No TipTap Pro. | `DocxAnnotationWriter.cs`, `useComposeWordShuttle.ts`, `usePendingRedline.ts` |
| Comments | **Built home-grown, shipped.** Anchored annotations → native `w:comment`. | `DocxAnnotationWriter.cs`, `anchoredAnnotationsToDocxAnnotations` |
| Retained-original delta | **Partially exists.** The "Push to Word" flow (`PushAnnotationsAsync`) **re-fetches the original .docx from SPE and annotates those bytes** (true delta). BUT the **main editor Save does NOT** use it. | `ComposeService.cs:880-921` (delta) vs `ComposeService.SaveAsync` |
| Main editor Save | **Reconstruction (lossy).** Any *dirty* Save rebuilds the whole .docx from the TipTap view (`tipTapJsonToDocxBytes`), dropping headers/footers, multi-level numbering, styles, hyperlinks, embedded objects. Original bytes are used byte-identically ONLY for an **unedited** save. | `docxBridge.ts:210-348`; `ComposeWorkspace.tsx` `triggerSave` ~997-1029 |
| Redline suggestion payload | rationale + source ids carried AND rationale **surfaced** in the accept/reject popover. No confidence, no explicit offsets. | `ComposeDraftPayload` (`ComposeEditor.tsx:260-284`); `redlineLabelText` (~750-755) |
| Anchoring | Text-pattern match + best-effort paragraph **index** + a Levenshtein re-anchor scorer. No stable OOXML `paraId`. mammoth import discards paraIds. | `AnnotationReanchorService.cs:94-217`; `compose-contracts.ts:104-112` |

**Net:** the R3 seed's B (track changes) and C (comments) "build from scratch / consider TipTap Pro" framing is **superseded** — those are done home-grown. The live gap is (1) **round-trip fidelity on save** and (2) **importing** existing Word revisions/comments into the editor.

---

## The three enhancements

### E1 — Retained-original OOXML + delta save (THE fidelity keystone) — Scope A
- **Verdict: GENUINELY MISSING for edited saves.** Only an untouched doc round-trips cleanly today; any edit triggers a full lossy rebuild from TipTap.
- **End-user impact:** the difference between "my contract came back exactly as it went in, with my edits marked" and "my header block, clause numbering, and firm styles got mangled." Make-or-break for legal drafting. This is the probable root of the R1 *and* R2 fidelity complaints.
- **Approach (maps to README A.1 "Original-DOCX preservation + patch"):** keep the original OOXML (server-retained / SPE re-fetch) and apply edits as a **delta**. The machinery partly exists (`PushAnnotationsAsync`).
- **Central design fork (the key `design.md` decision):**
  - **(a) Everything-is-a-tracked-change:** capture *direct* typing (not just AI redlines) as `w:ins`/`w:del` deltas onto the retained original. Reuses the existing annotation pipeline; for legal drafting, "every edit is tracked" is often *desirable*. Harder part: representing free typing as tracked-change marks.
  - **(b) Text-diff → OOXML patch:** diff edited paragraphs vs the original and synthesize minimal OOXML edits (README A.1). Highest fidelity, hardest algorithm (paragraph add/remove/reorder).
  - (c) / (d) fallbacks from README (read-only Compose; annotations-only) — lower value, listed for completeness.
- **Effort: HIGH.** This is the WORD-studio design's #1 risk. Deserves the bulk of R3.

### E2 — Paragraph / position identity (`paraId` anchoring) — Scope A/E
- **Verdict: GENUINELY MISSING.** Anchoring is fuzzy text+index today (robust-ish, not identity-based).
- **End-user impact:** redlines/comments land in exactly the right place on save and survive edits elsewhere; hardens the anchoring bug-class (the same class as the R2 round-8 #3 stale-selection bug).
- **Coupling:** requires a `paraId`-preserving **import path** (mammoth discards paraIds), so E2 is really "phase 2 of the OOXML-identity work" alongside E1 — treat them as ONE workstream, not standalone.

### E3 — Enriched redline contract (reason + confidence + offsets) — Scope G/H
- **Verdict: PARTIALLY present.** rationale + sources already carried and the **rationale is already surfaced** in the accept/reject UI. Missing: a **confidence** signal and explicit character offsets/paraId (offsets belong with E2).
- **End-user impact:** the lawyer sees *why* + *how confident* alongside each suggested change, for informed accept/reject — the "offer a suggestion the user knowingly accepts with track changes" model. The valuable half (the "why") already ships.
- **Effort: LOW** for the confidence signal alone; offsets ride with E2.

---

## Prioritization recommendation
1. **E1 + E2 as one "Compose OOXML fidelity" core** — E1 is arguably a *defect to schedule* (root of fidelity pain), E2 is the identity substrate it needs. This is the heart of R3 and where the design fork above must be resolved.
2. **E3-confidence** — cheap add, fold in opportunistically when touching the redline UI. E3-rationale is already done.
3. **Import round-trip** (reading existing Word revision marks + comments INTO the editor) — the remaining half of the seed's B/C, now that authoring them is done.

---

## Alignment: E1/E2/E3 ↔ R3 seed scope ↔ prior feedback

| Enhancement | R3 seed Scope Area | Prior feedback it answers |
|---|---|---|
| **E1** retained-original delta | **A** (Preserving Formatting on Save — highest priority) + E (headers/footers/TOC) + F (theme) | R1 UAT 2026-07-01 "loses formatting on save"; R2 round-7 redline/fidelity pain |
| **E2** paraId anchoring | A.1 (TipTap↔OOXML paragraph mapping) + E (structural) | R2 round-8 #3 anchoring bug-class |
| **E3** reason+confidence+offsets | **G** (selection-scoped AI) + **H** (insert AI response) | R2 robust-bridge edit model ("offer a suggestion, insert with track changes") |
| (already done in R2) home-grown track changes | **B** — supersedes "build/Pro" framing | R1 "track changes is Word-only" |
| (already done in R2) home-grown comments | **C** — authoring done; import is the gap | R1 "comments as w:comment deferred" |

---

## Corrections to fold into the R3 seed README
1. **B (Track Changes) & C (Comments):** authoring is **shipped home-grown in R2** (native `w:ins`/`w:del`/`w:comment`, no TipTap Pro). R3's remaining work is **import round-trip**, not building from scratch. Remove the "license TipTap Pro" option (constraint reaffirmed: no TipTap product features, paid or unpaid).
2. **A (Save fidelity):** note the partial machinery already exists (`PushAnnotationsAsync` delta path) — R3 extends it to the main Save, not greenfield.
3. **G/H:** the redline suggestion contract exists and surfaces rationale today; R3 adds confidence + offsets.

---

## Next step
Write `design.md` for R3 with **E1+E2 (the OOXML fidelity core, resolving the design fork) as the spine**, **E3-confidence** as a low-cost rider, and **import round-trip** for B/C. Everything on the MIT TipTap base — no TipTap product features.
