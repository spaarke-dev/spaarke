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

## R4.5 Read/Reference Fidelity (companion — `spaarkeai-compose-fidelity-r4.5`, merged to master 2026-07-28)

R4 (above) made the **write/save** side principled; **R4.5** completed the **read/reference** side on the *same* projection + `paraId` machinery — no new paradigm, the two-author split stands. Read-side invariants **F-1…F-5** (companion to R4's I-1…I-7), all extending `ComposeDocxProjectionBuilder`/`ComposeDocxProjection` (pure OOXML, zero new runtime package, ADR-007/013 purity intact):

- **F-1 Text exactness** — run text emitted verbatim, character-for-character; any unrepresentable construct is **warned, never silently dropped**. Fixed the silent `w:sym`/`w:cr` drops (+ `w:pict`/`w:ptab`/footnote/endnote/`w:ruby`/page-break) and emits `w:ind` indentation. 8/8 corpus docs char-exact.
- **F-2 One reader** — exactly **one** docx→editor reader (the server projection); every entry path (stored-doc, **upload**, **browse**, open-in-compose) renders through it. The client **`mammoth` fallback + `docxToTipTapHtml` are deleted** from Compose (mammoth remains only for SprkChat/Notepad). New **stateless `POST /api/compose/project`** serves the browse door — read-only, no persist, no authoring (ADR-040 / R4 I-2 preserved; Tension T-2 Path A). A null/unreachable projection shows an explicit error state, never a blank editor or a second reader.
- **F-3 Deterministic numbering** — clause/section/heading/list numbers computed server-side from the OOXML numbering model (`NumberingComputationEngine`), **identical to Word** (24/24 golden), incl. interrupted / multi-level / style-linked / letter / roman / legal (`w:isLgl`). Rendered as an explicit **non-editable number-atom** (ProseMirror widget decoration), never the browser `<ol>` auto-count. Counter is **`numId`-instance-scoped** per ECMA-376 (restart-vs-continue). Read-time only; live renumber-on-edit is R5 G3.
- **F-4 Stable reference** — every paragraph carries `paraId` **and** its computed legal number + level (`ComputedNumber`/`NumberingLevel`/`ListPath`/`HeadingLevel`), persisted in the projection payload **and** the session ledger (reuses the `ChatSession`/`StoredSession` stack — no new store). `CitationResolver` (pure static) resolves "Section 4.2" / "4.2(b)(iii)" / "Sections 4–7" ↔ `paraId`. Survives edits.
- **F-5 Honest layout numbering** — page/line numbers are rendering artifacts; delivered only via an explicit pagination engine where in scope, never fabricated from OOXML. WS-5 = **spike + deferred** (fast-follow; LibreOffice-sidecar vs Graph-PDF, permissive-only per NFR-03 — the reachable ceiling is "Word-Online-identical", not "Word-desktop-100%").

**Narrative architecture**: [`docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md`](../../docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md) — entry paths, the numbering engine, the reference/citation layer, code inventory, and extension recipes. **Full reasoning**: `projects/spaarkeai-compose-fidelity-r4.5/{design,spec}.md` + `notes/` (WS-1..WS-5, incl. the numbering-engine + citation-resolver notes and the WS-5 pagination decision). Consumer wiring of `CitationResolver` (review-note citations) continues in `ai-advanced-capabilities-agreements-r1` (see its inbound handoff note).

---

## R6 Path-B Amendment — render-on-save supersedes surgical byte-patch **on the save path** (per CLAUDE.md §6.5)

> **Amendment 2026-08-05 · author `spaarkeai-compose-r6` · Path B (ADR amendment)** — MUST merge with or before the dependent R6 Phase-1 code (`spaarkeai-compose-r6` tasks 010/011/012). Source: `projects/spaarkeai-compose-r6/spec.md` "ADR Tensions" + `design.md` §11. Scope is the **write/save path only**; the R4.5 read/reference invariants **F-1…F-5** and I-7 remain in force (see Scope guard).

🔔 **ADR Conflict — Resolution (Path B, amendment)**

- **Rule challenged**: invariant **I-4** ("edits are surgical operations, and untouched XML subtrees are byte-identical after save") **and** the MUST NOT at line 40 ("MUST NOT re-derive the `.docx` from the editor model on save … violates I-1/I-2/I-4"). Both govern the **save path**.
- **Conflict**: R6 re-architects Compose around **render-on-save** — every save **renders a fresh `.docx` from a canonical document model into a new immutable SPE version**, which is precisely a "re-derive on save" and by construction does *not* keep untouched subtrees byte-identical. The R3→R5 treadmill was the *same anchor-reconciliation bug class* (reconciling `(paraId, runIndex, offset)` anchors between the editor model and the retained OOXML), surfacing reactively in UAT one divergence at a time; the latest (`AppligentNDA_Signed.docx`) hard-fails **HTTP 422** on interior text-boxes / `mc:AlternateContent` / duplicate paraIds. I-4 + line-40 were **guardrails against R1–R3 naive-re-render fidelity loss** — a failure mode R6 removes at the source by (a) a **widened canonical model + tiered format adapters** (near-term tier round-trips safely; hard tier accept-flattens with a warning, never 422) and (b) **version history as the fidelity safety net** (every save appends; the prior byte-perfect version stays retrievable). With nothing to anchor against on save, the 422 anchor bug class disappears **by construction**.
- **Proposed path**: **B (amendment)** — context changed; I-4 + line-40 are no longer correct *for the save path* as written. The surgical-anchor mechanism they protect is itself the treadmill's root cause.
- **Resolution** (codifies the four spec points; binding for R6 and forward, save path only):
  1. **Save renders a new immutable version from the canonical model — no surgical anchoring on the save path.** `ComposeService.SaveAsync` routes **all** saves (born-in-editor *and* imported) through render-from-model (`ComposeDocumentRenderer`); the `ComposeBaselineParaIdStamper` count-gate (the 422 root) and per-op anchoring are removed from the save path.
  2. **Version history is the fidelity safety net** — SPE versioning is append-only; a prior version is always retrievable (R6 FR-07 exposes an OBO list/open-prior-version read path). The render-on-save promise ("open v3 after v4 and get the exact bytes") depends on this.
  3. **Representative-corpus round-trip is a release gate** — a CI harness round-trips the fidelity corpus (seeded with `AppligentNDA_Signed.docx`) and fails the build on a hard-fail/regression (R6 FR-08), moving divergence discovery from UAT to CI.
  4. **The surgical `ComposeShadowPatchEngine` is retained ONLY for a transitional clean-apply path** — it is removed from the normal save path; any residual clean-apply use during the R6 transition is the sole remaining caller. It is not the save mechanism.
- **Impact**: the save path is re-authored from "resolve retained baseline → surgical patch → replace content" to "render the canonical model → replace content (SPE makes the new version) → stamp eTag". `ComposeBaselineParaIdStamper`'s count-gate leaves the save path; `ComposeShadowPatchEngine` is demoted to the transitional clean-apply path only. The persistence (`ReplaceFileContentAsUserAsync`), Redis eTag stamp, and `AnnotationReanchorService` machinery carry over unchanged.
- **Alternative considered (rejected)**: **Path A** (project-scoped exception — make the surgical patcher tolerant per document, the abandoned `compose-anchor-robustness-r1` framing) — rejected because a per-divergence tolerance patch is the treadmill itself; it cannot close the anchor bug *class*, only the current instance. **Path C** (comply — keep surgical byte-patch on the save path) — rejected because complying re-ships the exact 422 anchor-reconciliation failure R6 exists to eliminate.

### Scope guard (what this amendment does NOT touch)

- **I-7 (no text-search in the write path) remains in force — satisfied *trivially*.** Rendering from the model needs no text-search at all; there is nothing to locate. R6 does not reintroduce a write-path text search.
- **The R4.5 read/reference invariants F-1…F-5 are UNCHANGED.** One reader, deterministic numbering (`NumberingComputationEngine`), the `paraId → legal-number` reference layer + `CitationResolver`, and honest layout numbering all stand exactly as written and are reused by R6's canonical model (the read/reference-path use of I-4's byte-identity by the stateless browse projector is **not** superseded — this amendment is save-path only).
- **I-1/I-2 hold in spirit**: the server still authors all `.docx` bytes (the client never authors — R6 renders server-side; I-2 intact). The *authoritative model* shifts on the save path from "retained original OOXML + ops" to "the canonical document model rendered to a new version" — the I-1 supersession is scoped to that.
- **No auth/security/compliance ADR is touched**, and no unrelated ADR-049 section is modified. The amendment is confined to the Compose save-path fidelity decision.

**Other tensions (Path C — comply, mention only)**: ADR-013 / ADR-007 (render-from-model stays `byte[]`/model-in → `byte[]`-out, no AI internals, no Graph above `SpeFileStore`), ADR-039 (engine frozen — render-on-save adds no AI dispatch), ADR-038 (seam + corpus round-trip harness DoD per FR-08), §10 BFF Hygiene (PDF intake + template part-merge get Placement Justifications; ≤60 MB publish).

---

## Integration
ADR-013 (facade boundary — no AI internals in `Services/Compose/`) · ADR-007 (`SpeFileStore` — no Graph types above it) · ADR-009 (version/re-anchor state via `IDistributedCache`) · ADR-010 (Patch Engine = stateless concrete singleton) · ADR-028 (client fetches via `@spaarke/auth`) · ADR-038 (seam DoD; banned mock/DI/ctor tests) · ADR-039/040 (AI engine frozen; redline path envelope-only; no new dispatch).

**Full reasoning**: `projects/spaarkeai-compose-r4/design.md` (§0 D1–D5, §3 invariants, §5 architecture, §9 ADR Tensions) + `spec.md`. No `docs/adr/` twin yet — promote to a full ADR if the pattern generalizes beyond Compose (e.g. when D3's multi-format phase lands).
