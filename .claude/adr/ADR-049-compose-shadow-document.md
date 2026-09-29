# ADR-049: Compose Shadow Document Architecture (Concise)

> **Status**: **Accepted**, amended three times — R4 (2026-07-22, `spaarkeai-compose-r4` task 001; accepted at the Phase-0 proof gate, task 006), R6 (2026-08-05, render-on-save), **R8 (2026-08-21, base re-projection + block copy-through — the current save contract)**. Read the R8 amendment before touching the save path: the two earlier amendments are each superseded in part.
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
  4. **The surgical `ComposeShadowPatchEngine` is retained ONLY for the transitional op-log path** (`ContentModel`-null saves): clean-apply for reopened authored docs, tracked apply for pre-cutover-client / legacy in-flight op-log saves, including the FR-08 stale-base re-anchor and best-effort recovery on that shape. It is removed from the normal (post-cutover) save path and is not the save mechanism; the whole transitional shape is Warning-logged for retirement telemetry (removal owned by tasks 013/090). *(Wording widened from "transitional clean-apply path" by task 012's adr-check — aligning the letter with the amendment's intent and the FR-08 bullet below; the reachable set was always the ContentModel-null umbrella.)*
- **Impact**: the save path is re-authored from "resolve retained baseline → surgical patch → replace content" to "render the canonical model → replace content (SPE makes the new version) → stamp eTag". `ComposeBaselineParaIdStamper`'s count-gate leaves the save path; `ComposeShadowPatchEngine` is demoted to the transitional clean-apply path only. The persistence (`ReplaceFileContentAsUserAsync`), Redis eTag stamp, and `AnnotationReanchorService` machinery carry over unchanged.
- **FR-08 imported-coverage change (recorded at retirement, task 012)**: the version-stamp + stale-base **re-anchor** machinery (`AnnotationReanchorService` on the save path) fires ONLY on the transitional op-log shape (`ContentModel`-null) — it re-anchors *operations*, and a model-path save carries none. Render-path saves (born-in-editor a0 + imported a1) deliberately skip it: their concurrency posture is **last-writer-wins with SPE version history as the safety net** (point 2 above), plus the unchanged 423 co-authoring lock and live-eTag fetch. This narrows FR-08's re-anchor coverage to the transitional shape by design; when that shape retires fully (post-transition), the re-anchor machinery retires with it while `AnnotationReanchorService` remains for its load/reload consumers.
- **Alternative considered (rejected)**: **Path A** (project-scoped exception — make the surgical patcher tolerant per document, the abandoned `compose-anchor-robustness-r1` framing) — rejected because a per-divergence tolerance patch is the treadmill itself; it cannot close the anchor bug *class*, only the current instance. **Path C** (comply — keep surgical byte-patch on the save path) — rejected because complying re-ships the exact 422 anchor-reconciliation failure R6 exists to eliminate.

### Scope guard (what this amendment does NOT touch)

- **I-7 (no text-search in the write path) remains in force — satisfied *trivially*.** Rendering from the model needs no text-search at all; there is nothing to locate. R6 does not reintroduce a write-path text search.
- **The R4.5 read/reference invariants F-1…F-5 are UNCHANGED.** One reader, deterministic numbering (`NumberingComputationEngine`), the `paraId → legal-number` reference layer + `CitationResolver`, and honest layout numbering all stand exactly as written and are reused by R6's canonical model (the read/reference-path use of I-4's byte-identity by the stateless browse projector is **not** superseded — this amendment is save-path only).
- **I-1/I-2 hold in spirit**: the server still authors all `.docx` bytes (the client never authors — R6 renders server-side; I-2 intact). The *authoritative model* shifts on the save path from "retained original OOXML + ops" to "the canonical document model rendered to a new version" — the I-1 supersession is scoped to that.
- **No auth/security/compliance ADR is touched**, and no unrelated ADR-049 section is modified. The amendment is confined to the Compose save-path fidelity decision.

**Other tensions (Path C — comply, mention only)**: ADR-013 / ADR-007 (render-from-model stays `byte[]`/model-in → `byte[]`-out, no AI internals, no Graph above `SpeFileStore`), ADR-039 (engine frozen — render-on-save adds no AI dispatch), ADR-038 (seam + corpus round-trip harness DoD per FR-08), §10 BFF Hygiene (PDF intake + template part-merge get Placement Justifications; ≤60 MB publish).

---

## R8 Path-B Amendment — base re-projection + block copy-through (per CLAUDE.md §6.5)

> **Amendment 2026-08-21 · author `spaarkeai-compose-r8` · Path B (ADR amendment)** — owner-accepted
> 2026-08-21 ("ADR-049 is fine."). Drafted by task 031 on the evidence of the Phase-3 architecture gate;
> applied at the start of task 040 so that no Phase-4 code is written against the superseded rule. Scope is
> the **write/save path only**; the R4.5 read/reference invariants **F-1…F-5** and I-7 remain in force.
> Full reasoning: [`docs/adr/ADR-049-compose-shadow-document.md`](../../docs/adr/ADR-049-compose-shadow-document.md).

🔔 **ADR Conflict — Resolution (Path B, amendment)**

- **Rules challenged**: the **R6 amendment** ("save renders a new immutable version from the canonical model")
  *and* **I-4** as restored in intent ("untouched XML subtrees are byte-identical after save"). Both govern
  the save path; each prior amendment secured one of two non-negotiable properties by surrendering the other.
- **Conflict**: R4 took preservation and lost termination (surgical anchoring → the HTTP 422 treadmill);
  R6 took termination and lost preservation (whole-body rebuild from a model carrying `w:jc`, `w:b` and `w:i`
  — everything else in `w:pPr`/`w:rPr` is discarded at projection time). Measured on the 18-document corpus,
  master preserves **18.08%** of untouched blocks and **6.67%** of the near tier; on a 109-block patent-claims
  document, **one block** survives.
- **Resolution**: **the save renders from the model AND preserves untouched content. These are not
  alternatives.** At save time the renderer re-projects the retained baseline server-side, pairs its blocks
  against the posted model **by document order**, and dispatches per block:
  - **unchanged block** → the baseline's own `w:p` subtree is **cloned verbatim**, with zero property logic;
  - **changed block** → rendered from the model, with property inheritance from its baseline counterpart;
  - **unmergeable block** → thin render **+ warning**. Never a content refusal.
- **Impact**: R6's control flow is unchanged (ADR-049 **I-5** — one body author — is reinforced, not relaxed:
  the merge lives *inside* `ComposeDocumentRenderer`). What is added is the **base side** the render path
  never had. Measured on the corpus: overall preservation **18.08% → 100%**, near-tier **6.67% → 100%**, zero
  hard-fails, zero cumulative drift over 5 round trips, +2–19 ms per save, no new package.
- **Alternative considered (rejected)**: **Path A** (project-scoped exception) — wrong instrument; this is not
  a narrow deviation but a correction to the governing decision, and leaving the ADR as written would let a
  future project re-derive R6's mistake from a still-authoritative rule. **Path C** (comply with R6) — rejected
  because complying re-ships the silent fidelity loss this release exists to fix.

There is **no per-construct preservation logic and there must not be.** Properties survive because an
untouched block is **never re-derived** — preservation is a consequence of not rewriting, not a feature list.

### The seven standing invariants

1. **Every save terminates in a defined outcome** — never an undefined content refusal.
2. **Untouched blocks are preserved.**
3. **The projection is the only coordinate system** — nothing else independently resolves document positions.
4. **`paraId` is a hint in the *file*, authoritative within a *session*.** Duplicates are spec-legal across
   `mc:AlternateContent`; Word regenerates ids on save. Pair by document order; `paraId` corroborates, never keys.
5. **Concurrency is last-writer-wins with a warning**, enforced by `If-Match` at the storage boundary.
6. **One edit-capture mechanism** — keystroke or model, the same anchor capture and rebasing.
7. **Deterministic information available at capture time MUST be carried, not re-derived.**

### The paired MUST (load-bearing — do not restate singly)

> **Invariants (1) and (2) are a PAIR. No future amendment may trade one away to obtain the other.**
> An amendment that improves termination at the cost of preservation, or preservation at the cost of
> termination, is rejected **by this rule alone**, regardless of its other merits.

Both prior amendments made exactly that trade. This clause exists so a fourth cannot.

### On invariant (7)

Stated as a **general rule**, not per surface. It is the rule beneath three of R8's four root causes: R6's
thin content model re-derived formatting it had been handed; the AI edit contract re-derived a location it
had already captured; and the demand for a fuzzy matcher was a consequence of the second. **If a design
re-derives something it already had, that is the bug** — and naming it once, generally, is how it stops
being rediscovered per surface.

### Mechanism MUSTs (normative — binding on Phase 4)

- **MUST** capture the retained baseline's **direct `w:body` children** before the swap. **MUST NOT** use
  `body.Descendants<Paragraph>()` — it interleaves `w:txbxContent` paragraphs into the body sequence and
  mis-pairs every block after the first text box. `mc:AlternateContent`, `w:txbxContent`, `mc:Choice` and
  `mc:Fallback` are **opaque**: carried whole, never entered.
- **MUST** decide "unchanged" against a **fresh server-side re-projection** of the baseline — never raw text,
  never the client's copy. Base and posted are then two values of the same type from the same builder, and
  their comparison is total.
- **MUST NOT** decide "unchanged" by text equality. Two paragraphs with identical text can differ in
  formatting, list level, comment anchors or revision state; a text shortcut clones a block the user *did*
  change, silently discarding their edit — a worse failure than the one being fixed. The comparison **MUST
  fail closed**: a block that cannot be compared is treated as changed and re-rendered.
- **MUST** fail open on an unavailable baseline: a baseline that cannot be re-projected yields no merge and
  the render proceeds as R6 does. A save is **never refused** because the base side was unavailable (invariant 1).

### Known residue (do not read this amendment as "fidelity solved")

- **The edited block is still rebuilt from the model.** The gate measures **untouched** blocks and excludes
  the edited one by construction. Property inheritance (R8 FR-A04, task 041) narrows this; it does not
  eliminate it, and 041 is **not optional and not deferrable**.
- **Reorder yields no benefit.** Document-order pairing cannot recognise a moved block; a reordered body
  degrades to R6's behaviour — never a failure, but no preservation.
- **`ComposeShadowPatchEngine` is NOT confirmed as subsumed.** It serves the op-log path, which this
  amendment does not touch. It **MUST NOT** be deleted on this evidence.

---

## Integration
ADR-013 (facade boundary — no AI internals in `Services/Compose/`) · ADR-007 (`SpeFileStore` — no Graph types above it) · ADR-009 (version/re-anchor state via `IDistributedCache`) · ADR-010 (Patch Engine = stateless concrete singleton) · ADR-028 (client fetches via `@spaarke/auth`) · ADR-038 (seam DoD; banned mock/DI/ctor tests) · ADR-039/040 (AI engine frozen; redline path envelope-only; no new dispatch).

**Full reasoning**: [`docs/adr/ADR-049-compose-shadow-document.md`](../../docs/adr/ADR-049-compose-shadow-document.md) (the extended record — currently the R8 third amendment in full) · `projects/spaarkeai-compose-r4/design.md` (§0 D1–D5, §3 invariants, §5 architecture, §9 ADR Tensions) + `spec.md` (R4) · `projects/spaarkeai-compose-fidelity-r4.5/` (R4.5 read/reference) · `projects/spaarkeai-compose-r6/` (R6 render-on-save) · `projects/spaarkeai-compose-r8/notes/{gate-contract,control-measurement,merge-prototype-results,gate-decision}.md` (R8 evidence).
