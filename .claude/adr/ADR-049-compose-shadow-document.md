# ADR-049: Compose Shadow Document Architecture (Concise)

> **Status**: Proposed (2026-07-22) — authored by `spaarkeai-compose-r4` task 001 (Phase 0 gate). Accepted at the Phase-0 proof gate (task 006: operation schema + applier spike on the CIPO doc + corpus byte-diff harness green).
> **Domain**: Compose — the AI-native legal drafting save/edit layer (`Services/Compose/`, `Spaarke.Compose.Components`).
> **Source**: `spaarkeai-compose-r4` design §0 (D1–D5), §3 (I-1…I-7), §5 (architecture), §9 (ADR Tensions); two external senior reviews (`notes/senior-reviews-2026-07-22.md`); prior-art catalog (`notes/bridge-prior-art.md`).
> **Why this ADR exists**: R1–R3 were unshippable for two structural reasons — **fidelity loss** on untouched content (the save re-derived the `.docx` from a lossy editor model) and **interior-location failures / HTTP 422** (annotations were placed by whole-document text-search). Both are consequences of the *translation/save layer*, not the feature set. No ADR governed how editor edits map to OOXML bytes; this ADR does, at the **principle level** (the property that kept ADR-028/039 durable while mechanism-shaped ADRs rotted).

---

## Decision

The OOXML `.docx` is the **server-authoritative source of truth**; the TipTap/ProseMirror editor is a **lossy view + controller**; every edit is a **step-level operation anchored by `(paraId, runIndex, run-local-offset)`** applied surgically to the retained OOXML by a **single unified byte-author** (`ComposeShadowPatchEngine`). **No text-search anywhere in the write path.**

Fidelity lives on the server (`DocumentFormat.OpenXml` + retained original bytes — faithful to all of OOXML by construction; unopened package parts copied verbatim). Editing tools live in the editor. The **bridge** — opaque atoms, offset→run mapping, structural ops — is the engineered core.

**Locked decisions (D1–D5)** — binding for R4:
- **D1** — Delta model = **step-level operational deltas** (ProseMirror steps → operations). NOT `getHTML()`, NOT paragraph-granularity diff.
- **D2** — Anchor = **`(paraId, runIndex, run-local-offset)`**. paraId is the durable coarse anchor; runIndex + run-local-offset the fine anchor, re-derived at patch time by split-run-at-offset.
- **D3** — `docx` end-to-end now; pdf/xlsx/pptx are explicit LATER phases (architecture extends, not built in R4).
- **D4** — SPE = store + open-in-Office launch surface; versioning + lock (HTTP 423) + concurrency protocol in scope; WOPI-embedded editor out.
- **D5** — **One** unified `ComposeShadowPatchEngine` replaces BOTH `DocxAnnotationWriter` (text-search) AND `ComposeParagraphRedlineSynthesizer` (paragraph-diff).

---

## Constraints

### ✅ MUST
- **MUST** hold the retained original OOXML as the one authoritative model (**I-1**); the server authors all `.docx` bytes, never the client (**I-2**).
- **MUST** give every editable `w:p` a persisted `w14:paraId` and reference edits by it (**I-3**); edits are surgical operations, and untouched XML subtrees are byte-identical after save (**I-4**).
- **MUST** route ALL save/annotation writing through the **single** `ComposeShadowPatchEngine` byte-author (**I-5, D5**); emit native `w:ins`/`w:del`/`w:comment`.
- **MUST** anchor durably by **`(paraId, runIndex, run-local-offset)`** (**D2**); resolve run boundaries at patch time by splitting the run at the offset.
- **MUST** treat the editor as a view + controller that renders the projection and emits operations (**I-6, D1**).
- **MUST** mutate the OpenXml DOM (never string-edit `document.xml`); handle paragraph-mark deletion (paragraph merge) via `w:del` on the para-mark glyph in `w:pPr/w:rPr`.
- **MUST** render non-renderable constructs (SDT/content controls, fields, complex/floating objects) as **opaque atom** placeholders carrying their paraId, so document order + patchability survive.
- **MUST** version-stamp every save (SPE eTag + projection schema version); on a stale base, re-anchor the operation log via `AnnotationReanchorService` (AUTO / REVIEW+ORPHAN) instead of failing.
- **MUST** feed born-in-editor (source-less) documents through the SAME operation/patch model (initial content = an insert-everything op set onto an empty shadow package) (**D1/D5**).
- **MUST** gate the hard-replace removal of the legacy paths behind the **Phase 0 proof gate** (operation schema + applier spike on the CIPO doc + corpus byte-diff harness green).

### ❌ MUST NOT
- **MUST NOT** re-derive the `.docx` from the editor model on save (the R1–R3 fidelity-loss root cause) — violates **I-1/I-2/I-4**.
- **MUST NOT** use **text-search in the write path** (**I-7**). Fuzzy content-match survives ONLY as a below-threshold "surface-as-comment" last resort on reload / cross-Word-session re-anchor — never as a placement mechanism.
- **MUST NOT** use run-*ids* or absolute editor positions as the durable anchor (they do not survive Word round-trips / concurrent same-paragraph edits) — **D2**.
- **MUST NOT** keep two write paths — the split byte-authors (`DocxAnnotationWriter` + `ComposeParagraphRedlineSynthesizer`) are retired; exactly one engine writes the package (**D5**).
- **MUST NOT** reach AI internals from `Services/Compose/` (ADR-013 Tier-1 NetArchTest) or take a `Microsoft.Graph` dependency above `SpeFileStore` (ADR-007); the Patch Engine is `byte[]`-in / `byte[]`-out.
- **MUST NOT** introduce a new AI dispatch endpoint or change any AI catalog row — the AI redline path is envelope-only, engine frozen (**ADR-039**). AI returns JSON operations referencing paraId; every returned anchor is validated before apply.
- **MUST NOT** add any commercial / per-seat / AGPL / TipTap-Pro (`@tiptap-pro/*`) component, and MUST NOT take a runtime dependency on EigenPal (official repo is a closed facade; the frozen Apache-2.0 fork is study-reference only) — **NFR-03**.

---

## Path-B Amendment — supersede the R3 paragraph-diff project decision (per CLAUDE.md §6.5)

🔔 **ADR Conflict — Resolution (Path B, amendment)**

- **Decision in question**: The `spaarkeai-compose-r3` project decision (r3 FR-02 / design §4.2) that codified **`ComposeParagraphRedlineSynthesizer`** (paragraph-granularity diff onto the retained original) as *the* delta engine. This was a **project-scoped decision**, not a doc-ADR — a survey of `.claude/adr/` + `docs/adr/` at authoring time found **no ADR document codifying the paragraph-diff choice** (so this amendment stays a project-decision supersession, not a doc-ADR change).
- **Rule challenged**: "dirty save = paragraph-diff synthesizer onto retained original."
- **Conflict**: paragraph-diff is paragraph-coarse — it re-diffs runs, **cannot express structural edits** (split/merge/insert/delete paragraph), and leaves a **second text-search write path** (`DocxAnnotationWriter`) alive alongside it (the two-path drift that caused r3's Bug A).
- **Proposed path**: **B (amendment)** — context changed; the paragraph-diff decision is no longer correct as written.
- **Resolution**: The **step-level operational delta model (D1)** applied by the single `ComposeShadowPatchEngine (D5)` **SUPERSEDES** paragraph-diff. `ComposeParagraphRedlineSynthesizer` is retired (task 032); its behavior is subsumed by operations. This is binding for R4.
- **Impact**: r3's paragraph-diff engine + `DocxAnnotationWriter` text-search write path are removed (hard-replace, gated by the Phase 0 proof). One byte-author remains.
- **Alternative considered (rejected)**: Path A (project-scoped exception keeping paragraph-diff) — rejected because paragraph-diff cannot satisfy FR-05 (structural edits) or I-7 (no text-search) at all; a narrow exception can't close the defect classes. Path C (comply with the R3 decision) — rejected for the same reason: complying would re-ship the two-path drift.

**Other tensions (Path C — comply, mention only)**: ADR-013 (AI facade — op validation/apply is pure; AI dispatch stays behind PublicContracts), ADR-007 (Graph isolation — engine is `byte[]`-in/out), ADR-038 (integration-heavy — seam slice + corpus round-trip harness for every save/load change), NFR-03 (MIT base only — raw ProseMirror step/decoration APIs are MIT; no Pro extension).

---

## Integration
ADR-013 (facade boundary — no AI internals in `Services/Compose/`) · ADR-007 (`SpeFileStore` — no Graph types above it) · ADR-009 (version/re-anchor state via `IDistributedCache`) · ADR-010 (Patch Engine = stateless concrete singleton) · ADR-028 (client fetches via `@spaarke/auth`) · ADR-038 (seam DoD; banned mock/DI/ctor tests) · ADR-039/040 (AI engine frozen; redline path envelope-only; no new dispatch).

**Full reasoning**: `projects/spaarkeai-compose-r4/design.md` (§0 D1–D5, §3 invariants, §5 architecture, §9 ADR Tensions) + `spec.md`. No `docs/adr/` twin yet — promote to a full ADR if the pattern generalizes beyond Compose (e.g. when D3's multi-format phase lands).
