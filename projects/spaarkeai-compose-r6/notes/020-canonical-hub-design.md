# Task 020 — Canonical document model hub: design (Steps 1–2) + Step-3 projector (implemented)

> **Date**: 2026-08-05 · **Task**: 020 (Phase-2 anchor) · **Rigor**: FULL · **Tier**: opus/high (executed on Fable 5)
> **Status**: Steps 0–3 complete (design + **projector implemented, 12/12 seam tests green, 0 regressions**). Next: Step 4 (SaveAsync render-out wiring — scoped, see §8) + widened-model seams.
> **Conflict-check**: CLEAN — no open PR and no sibling Compose branch (r5, fidelity-r4.5, r4, agreements-r1, analysis-hub-r1, fix-compose-launch-and-viz) has unmerged commits on `ComposeService.cs` / `ComposeDocxProjectionBuilder.cs` / `ComposeDocumentRenderer.cs`. Re-run before the BFF PR.

---

## 1. Code map — the two ends, confirmed at the type level

| Surface | Type | Shape | Source-agnostic? |
|---|---|---|---|
| **Render-OUT** (`ComposeDocumentRenderer.SynthesizeDocument:102`) consumes | `ComposeContentModel` | Thin, **mirror-first of TipTap**: `Paragraph/Heading/ListItem/Table` blocks; inline runs (`Bold/Italic/Underline/Href`); `Alignment`; numbering hints (`Level/Ordered/StartsNewList`). No headers/footers, tracked-changes, comments, sectPr, styles beyond Normal/Heading1-6/ListParagraph, text boxes, fields. | model shape is editor-shaped |
| **Read-IN** (`ComposeDocxProjectionBuilder.Build:92`) produces | `ComposeDocxProjection` | **HTML string** + `BlockAtoms` + `ParaIdMap` + `NumberingModel` + warnings. Rich OOXML traversal (field scan, numbering model, atoms) — but output is **HTML for the browse/read path**, NOT a `ComposeContentModel`. | reads docx, emits HTML |

**The gap (the dependency inversion, now type-confirmed):** there is **no `docx → ComposeContentModel` projector**. Read-in emits HTML; render-out consumes `ComposeContentModel`; nothing bridges them. This is exactly what blocked task 010 and forced the re-sequence — and it is task 020's core new work.

## 2. The decisive precedent — two authoring modes already exist

- `SynthesizeDocument:102` → `WordprocessingDocument.Create` = author a body onto a **blank** package. Everything not in the thin model is absent by construction. (Born-in-editor / Authored.)
- `AppendSection:191` → `WordprocessingDocument.Open` = the **same `RenderBlocks` engine** applied **onto an existing package**, detaching/re-attaching the trailing `sectPr` and **preserving every other part** (styles, numbering, headers/footers, theme, settings). Used for the NDA server-authored summary page.

`AppendSection` proves the renderer can author model-blocks onto a preserved package **without touching any other part and without anchoring**. Generalizing that from *append* to *replace-body* is the faithful imported-doc render.

## 3. Design — the canonical hub is an EXTENSION, not a parallel model

**Hub = `ComposeContentModel` (body, widened by 021–025) + a server-retained source package (the "carrier" = document-level parts).**

```
                         ┌───────────────── canonical hub ─────────────────┐
  source .docx  ──20──▶  │  ComposeContentModel  (body: blocks + runs)      │  ──11──▶  fresh .docx
   (carrier kept)        │  + carrier package    (styles/numbering/hdr-ftr/ │           (new SPE version)
                         │                        theme/settings/sectPr)    │
  PDF (Phase 4) ──40──▶  └──────────────────────────────────────────────────┘
```

**Render-on-save, unified across both `SaveAsync` branches (`ComposeService.cs:714`):**
- **Authored** (born-in-editor): carrier = blank → `SynthesizeDocument` (unchanged).
- **Imported**: carrier = the retained source package → open it, **replace the body** with the rendered `ComposeContentModel` body, **preserve all other parts** (generalized `AppendSection`). This is task **011** (generalize the renderer to accept a carrier); task **020** builds the model + projector it consumes.

**The docx→canonical-model projector (020's core new work):**
- Walk the source docx body → `ComposeContentModel` blocks. **Reuse** `ComposeDocxProjectionBuilder`'s existing OOXML traversal + `NumberingComputationEngine:1357` for numbering labels (do NOT re-implement).
- Retain the source package as the carrier (server-side, per ADR-040 session ledger — the client never sends/sees it; mirrors today's retained-baseline for imported docs).
- **TOTAL / lenient by construction**: unrecognized constructs project to their nearest editable form or are dropped (fidelity deferred to 021–026), and the projector **never throws**. Because render-on-save renders *from the model*, "not in the model" = flatten-by-omission — so the NDA **saves (no 422)** in 020; its rich constructs gain fidelity in 021–026 before the 010 cutover flips imported docs onto this path.

## 4. Why this does NOT trip task 020's escalation triggers

- **Trigger #1 (no parallel model type — extension only):** the **body** has exactly one representation (`ComposeContentModel`, widened with optional back-compat fields). The carrier is a **companion payload** (like the existing `NumberingModel` already carried alongside the projection), not a second body model. ⇒ extension, does **not** fire.
- **Trigger #2 (no text-search / surgical anchoring on the save path):** the body is re-rendered **wholesale** from the model; carrier parts are preserved **wholesale**. Nothing is located-and-patched. ⇒ does **not** fire. (ADR-049 Path-B satisfied trivially.)

## 5. What 020 builds vs. what the downstream tasks add

| Task | Adds THROUGH this hub |
|---|---|
| **020 (this)** | The hub shape (widened `ComposeContentModel` + carrier); the `docx→ComposeContentModel` projector (lenient/total); numbering via reused engine; wire `SaveAsync` both branches to render the model out; seam slice proving docx→model→render round-trips + NDA saves (no 422). PDF-ready by *shape* only. |
| 011 | Generalize `SynthesizeDocument` to render onto a carrier (replace-body-preserve-parts), not just a blank package. |
| 021–025 | Widen numbering/lists, tables, headers/footers+page-breaks, hyperlinks+comments, tracked-changes as model data that survives the round-trip. |
| 026 | Hard-tier (text boxes/drawings/fields/content controls) accept-flatten + warning — never 422. |
| 010/012 | Flip imported saves onto the render path; retire the surgical count-gate from the save path. |
| 027 | Per-feature fidelity seam suite over the shipped path. |

## 6. ADR posture (per project CLAUDE.md)

- **ADR-049 Path-B**: render from the model; carrier preserved wholesale; version history is the safety net. ✅ by design.
- **ADR-007**: `byte[]`-in / projection-out; carrier is in-memory OPC bytes; no `Microsoft.Graph` above `SpeFileStore`. ✅
- **ADR-013**: no AI-internal types in `Services/Compose` (Tier-1 NetArchTest). Projector is pure OOXML. ✅
- **ADR-039**: no new AI dispatch endpoint. ✅ (projection/render concern)
- **ADR-040**: carrier persisted via the existing session-ledger channel, not a new one. ✅
- **ADR-038**: seam slice under `tests/integration/seam/Compose/`; no banned mock/DI/ctor shapes. ✅

## 7. Open decisions (surfaced for the operator, none blocking Step 3)

1. **Carrier persistence size.** Retaining the full source package per session is bytes-in-ledger (ADR-040). NDA-class docs are small; a large-doc ceiling may be worth a follow-up. *Default:* retain full package (matches today's retained-baseline).
2. **Widened-field staging.** 020 adds the hub + projector + numbering; the inline-rich fields (tracked-changes/comments as model data) land in 024/025. 020's `ComposeContentModel` additions are the structural seams (optional, back-compat) those tasks populate. *Default:* add only the seams 020 needs; 021–025 extend.

## 8. Step 3 — implemented (2026-08-05, this session)

**What landed** (BFF builds green; 12/12 new seam tests pass; full Compose suite 407/409 with the 2 fails pre-existing — see below):

- **`ComposeDocxProjectionBuilder.BuildContentModel(ReadOnlyMemory<byte>)`** — the docx→`ComposeContentModel` projector, a new region on the SAME class (extension, no new component). Total/lenient: never throws (OCE excepted); unreadable/empty/over-cap → `Failed` envelope with empty model. Mirrors the read walk's traversal (heading/list classification, field-scan, SDT boundary rule, symbol-glyph map, hyperlink allowlist via the now-shared `ResolveHyperlinkHref(h, MainDocumentPart)`), classifies ordered-vs-bullet through the R4.5 `NumberingModel` (override-aware), and carries source `w14:paraId`s (renderer dedups/mints).
- **`ComposeCanonicalModelProjection`** (in `ComposeDocxProjection.cs`) — status/warnings ENVELOPE only, not a second body model.
- **Flatten rules (ADR-049 Path-B accept-flatten baseline; 021–026 retire these one by one):** field → cached result text (`field-flattened-to-text`) · opaque SDT → display text (`hard-tier-sdt-flattened`) · tracked ins/del → settled prose KEPT (`tracked-*-flattened*`; deletion kept = no-text-loss default, 025 models revisions first-class) · drawings/objects/pictures **and `mc:AlternateContent` wrappers** → dropped loudly (`complex-object-dropped`) · line breaks → space (`line-break-flattened`) · plus the read path's F-03 parity guard (`unrendered-paragraphs`).
- **Seam slice** `tests/integration/seam/Compose/ComposeCanonicalModelRoundTripSeamTests.cs`: every corpus doc projects → renders → re-projects with a STABLE top-level block-kind sequence (the hub is a fixed point); the NDA flattens with warnings, never refused; the NDA's rendered output carries a **unique paraId on every paragraph** (the count-gate's mismatch condition cannot exist on this path); unreadable sources fail closed. Pure-component style per the `ComposeReadFidelityHarnessSeamTests` precedent; no banned mock/DI/ctor shapes.

**Finding 1 — `mc:AlternateContent` was a silent drop.** The NDA's text-box signature blocks are NOT direct `w:drawing` children of runs — they're wrapped in `mc:AlternateContent`, which `IsComplexObjectRun` doesn't see. First projector cut dropped them silently (caught by the NDA seam test). Fixed in the MODEL walk (explicit cases + counted warning + F-03 guard). The READ walk has the same blind spot (relies only on its unrendered-paragraphs count guard) — deliberately NOT changed here (read-path behavior change = out of 020 scope on a contested surface); routed to task 026.

**Finding 2 — 2 PRE-EXISTING corpus-harness fails on the NDA (empirically confirmed at HEAD via stash, §F.3).** Since task 004 added the NDA to the auto-discovered corpus: (a) `ComposeSummaryPageSeamTests.AppendSection_LeavesEveryOriginalParagraphOuterXmlUnchanged` — the NDA's DUPLICATE `w14:paraId`s (Choice + Fallback branches share ids) trip `AppendSection`'s `AssignParaIds` dedup, which re-mints ids inside an untouched paragraph → byte-identity violated (a real, pre-existing I-4 violation on the summary-page path); (b) `ComposeReadFidelityHarnessSeamTests.TextExactness` — the NDA's text-box runs are in source but not in projected HTML. Both are the harness correctly flagging the NDA against the CURRENT paths — the exact bug class R6 retires. **Routed to: 026 (hard-tier surface, incl. the AppendSection dup-paraId dedup scope) + 027 (post-cutover suite).** Not fixed in 020 (out of scope; no regression introduced — this branch's suite was already red on these 2 since task 004).

**Step-4 scoping (directional deviation, per the re-sequence):** 020 does NOT flip `SaveAsync`'s Imported branch — that is the 010 cutover (gated on 011 carrier-render + 026 hard-tier). 020's POML criterion "NDA saves no-422" is satisfied at the component seam (unique-paraId + no-refusal proofs above); the through-the-wire save proof lands at 010/013 as re-sequenced. Remaining 020 work: model-shape seams for 021–025 (only as needed), publish-size gate, Step 9.5.

## 9. Gate results (2026-08-05, post-Step-3 commit `f3179b819`)

| Gate | Result | Evidence |
|---|---|---|
| **Publish size (§10.4 / NFR-01)** | ✅ **46.88 MB compressed incl. PDBs** (145.14 MB uncompressed; PDBs 2.13 MB) — **−1.37 MB vs task-003 baseline 48.25 MB**, 13.12 MB headroom to the 60 MB ceiling. Delta is measurement-noise/master-drift, not growth — this diff adds only code to an existing assembly. | `dotnet publish -c Release` → `deploy/api-publish/`, zip-deflate measure |
| **No new HIGH CVE (§10.5)** | ✅ Only the pre-existing task-003 baseline: `System.Security.Cryptography.Xml` 8.0.3 (5 High, transitive). **No NEW entries.** | `dotnet list package --vulnerable --include-transitive` |
| **Tier-1 ArchTests** | ✅ for this task: **ADR-013 "no AI internals in Services/Compose" PASSES** (among 24 green). ⚠️ 4 fails are **PRE-EXISTING from master** (ADR-007 Graph types in `Services.Communication.*`/Office errors; ADR-010 1:1-interface ceiling 76→146; options-pattern) — none of the violating types are in this diff (commit touches only Compose + tests + notes; Communication types last touched by email-r5 merge `e26b66c2f`). Flagged for repo-level follow-up, out of 020 scope. | `dotnet test tests/Spaarke.ArchTests/` 24/28 |
| **Conflict-check (re-run)** | ✅ CLEAN — no open PR touches the changed files; compose-r5 / fidelity-r4.5 / fix-compose-launch-and-viz / agreements-r1 / analysis-hub-r1 all have zero unmerged commits on `Services/Compose/` or `seam/Compose/`. | PR file-list scan + per-branch `git log origin/master..origin/{b}` |
| **Step 9.5 code-review + adr-check** | ✅ **adr-check: PASS** (9/9 compliant, 0 violations, 3 Low/Info warnings — all closed: publish-size evidence is this table; docxBridge.ts path is `src/utils/` not `src/widgets/`; seam-category fit by established precedent). ✅ **code-review: APPROVE-WITH-MINORS** — triage + fixes below. | two independent read-only audit agents on `f3179b819`; fixes in the follow-up commit |

### Step 9.5 code-review triage (24 findings → fixed / routed / accepted)

**Fixed in the follow-up commit (10):**
- **R1/R2 (Major)** — `ListContinuity` doc claimed interrupted ordered runs keep continuity; the renderer demonstrably restarts them. Rewrote the semantics honestly (`PrevOrderedNumId` mirrors the renderer's clear-on-every-non-ordered-block) + added counted `ordered-list-continuity-lost` when a numId re-appears after an interruption/interleave. Renderer-side continuation → **task 021**.
- **R3 (Major, partial)** — uncounted flattens now counted: `tab-flattened`, `indentation-dropped`, `heading-direct-numbering-dropped`. (Run-formatting beyond b/i/u — color/size/fonts — remains uncounted: routed to **021–025** widening; accepted for the 020 baseline.)
- **R6 (Major)** — unterminated/container-spanning field no longer silently discards its result text: flushed as plain run + `field-unterminated`.
- **R7 (Major)** — style-linked numbering on non-Heading styles (FR-12 firm templates): now counted `style-linked-numbering-dropped`; faithful projection → **task 021**.
- **R8 (Minor)** — ilvl fallback now probes lower THEN higher levels, closing the walk-disagreement window vs the read walk's FirstOrDefault.
- **R10 (Minor, partial)** — `w:customXml` block + inline wrappers now recurse transparently (text kept). (`w:smartTag`/`w:dir`/`w:bdo` remain default-skipped — read-walk parity, rare legacy; routed to **026** inventory.)
- **R11 (Minor)** — deleted paragraph mark now counted `tracked-paragraph-mark-flattened`.
- **R12 (Minor)** — zero-row `w:tbl` no longer emitted (renderer skips them → would break the fixed point): dropped + `empty-table-dropped`.
- **R13/R14 (Minor/Major-low)** — `ClampText`: warn-once flag (Count stays 1) + surrogate-pair backoff at the clip boundary (a lone high surrogate would make the rendered package unserializable).
- **R15/R16 (Minor)** — `Warnings` doc contradiction fixed; corpus theory hardened with `Blocks.NotBeEmpty()` (no vacuous pass).

**Routed for operator sign-off (2 flatten-tier decisions):**
- **R4 (Major)** — ins+del replacement pairs flatten to BOTH texts ("bar"+"foo" → "barfoo"): each half individually defensible (insert kept; deletion rejected = no-text-loss), combined output is neither accepted nor rejected. Both warnings fire. **→ task 025 scope note: model revisions first-class; until then imported docs with pending replacements stay on the surgical path (they do today — the 010 cutover gate).**
- **R5 (Major)** — unmapped `w:sym` persists U+FFFD into saved bytes (destructive vs the read path's display-only placeholder). Warned (`unmapped-symbol-char`). **→ task 026 hard-tier decision: extend `KnownSymbolGlyphMap` per corpus, or refuse-flatten for unmapped syms.**
- (Also for R4/R5 context: the model path has NO production caller yet — R23 — so neither behavior ships to users before 010/011 wire it, by which time 025/026 land first per the re-sequenced critical path.)

**Accepted as-is (documented):** R9 row/cell-level wrappers (pre-existing shape parity with `RenderTable`; caught by unrendered-paragraphs guard) → 026 inventory · R17 kind-only stability assertion (Ordered/Level/interior = 021/027 oracles) · R18 comment framing · R19/R20/R21/R22 clean · R23 staged dead code until 010/011 · R24 pre-existing disposal pattern (harmless).

## 10. Task 011 — RenderIntoCarrier (2026-08-05, commits `e24ceefbc` + Step-9.5 fix commit)

**Delivered:** `ComposeDocumentRenderer.RenderIntoCarrier(carrier, model, author)` — the imported-doc render-on-save author (replace-body-preserve-parts, generalizing `AppendSection`); collision-safe numbering merge (`NumberingPlan(firstNumId)` above carrier max + `MergeNumberingDefinitions` remapped abstracts); carrier styles win; trailing sectPr + metadata preserved. Seam slice `ComposeCarrierRenderSeamTests`: non-body parts BYTE-IDENTICAL across the swap on every corpus doc; block-kind + **visible-text** fixed point; collision-free merge proven; unique paraIds; degenerate cases.

**Step 9.5 (independent agents on `e24ceefbc`):** adr-check **PASS** (11/11, 0 violations). code-review **APPROVE-WITH-MINORS** — triage:

*Fixed:* **M-1** heading style-linked numPr in the no-styles-carrier branch could dangle or capture carrier numId 1 → catalog now authored WITHOUT heading numbering in carrier mode (`AddStyleDefinitions(includeHeadingNumbering:false)`) · **P-1** pPr-nested final sectPr promoted (clone) when body-level absent — kills a UAT-#1A shape on third-party generators · **P-3** `numIdMacAtCleanup` stays last in the merge (CT_Numbering order) · **P-8** localized/custom paragraph-style identity loss now a counted projector flatten (`paragraph-style-flattened`; localized heading-id mapping → 021/026) · **T-1** cycle-unsafe part recursion replaced with the task-004 comparer's cycle-safe enumerator (widened to internal; §11 reuse) · **T-2** numbering.xml byte-identity exempted ONLY when the render allocates lists — **this hardening immediately caught a real bug**: merely READING the carrier's Numbering DOM for max ids caused autoSave to re-serialize an untouched numbering.xml on 4 list-free corpus docs → fixed by gating the inspection on `ModelContainsListItem(model.Blocks)` · **T-3** sectPr oracle asserts (no silent skip) · **T-4** null-safe stats · **T-5** visible-text concat round-trip oracle added corpus-wide · **P-4/P-7/P-9** documented degradations added to remarks (dangling-abstract capture; styles-present-but-missing-Heading/ListParagraph → Normal-look; header REF fields/bookmark links orphaned by the body swap).

*Deferred/accepted:* **P-2** triple-duplicated open-package preamble → extract when 012 touches this area · **P-5** int-overflow ids (theoretical) · **P-6** `mc:Ignorable` w14 on pre-2010 carriers (strict-validator only) · **P-10** the write-path text-search audit needs a scoped sentinel carve-out AT the 010 cutover (noted for 010) · **T-6** OpenXmlValidator pass (real Word carriers carry pre-existing validation noise — flaky) · file-length (pre-existing).

**Suite:** 333/335 (same 2 pre-existing NDA reds). Publish 46.88 MB (unchanged). ADR-013 ArchTest unchanged-green.

---

## 11. Task 021 — numbering/lists through the canonical model (2026-08-06)

**Design: carry the IDENTITY, reference the SCHEME.** The model gains ONE additive field —
`ComposeBlock.NumId` (the source `w:numPr/w:numId`, ListItem only, null for born-in-editor). The
numbering SCHEME (abstractNum levels, numFmt/lvlText) stays in the carrier per the hub design (§3:
model = body, carrier = styles/numbering/…). The `NumberingComputationEngine` is REUSED untouched —
no parallel numbering path (POML constraint + §11 reuse).

**Renderer (`ListRenderState`, document-scoped):**
- `NumId` present in the carrier ⇒ the rendered `numPr` references that instance DIRECTLY — Word's
  per-instance counters reproduce the source labels (golden parity BY CONSTRUCTION: continuity,
  multi-level composition, style/glyph). A fully carrier-referencing render allocates nothing →
  `numbering.xml` never touched → BYTE-IDENTICAL (upgrades 011's "numbering.xml may be merged"
  carve-out for the pure round-trip).
- `NumId` unknown to the target (blank-package synthesize / foreign carrier) ⇒ per-DISTINCT-source-id
  map to allocated instances — identity therefore continuity preserved under the renderer's own scheme.
- `NumId` null (born-in-editor) ⇒ the `StartsNewList` CONTRACT is now honored (020-R1 CLOSED): the
  renderer no longer clears its current ordered instance on every non-ordered block; `StartsNewList=true`
  remains the explicit restart. Safe for the live client: `docxBridge.buildContentModel` flags every
  distinct top-level ordered list `startsNewList=true`. BONUS FIX: a nested bullet inside an ordered
  list no longer restarts the parent run (live-client bug under the old clear-on-interrupt).
- Carrier numbering inspected via a SEPARATE READ-ONLY open of the carrier bytes (`ScanCarrierNumbering`)
  — the editable package's Numbering DOM is never read unless a merge actually happens, eliminating the
  autoSave-rewrite hazard class (011-T2) instead of gating around it.

**Projector:** captures `NumId`; `StartsNewList` = first appearance of the numId; `ListContinuity`
reduced to the seen-set; **`ordered-list-continuity-lost` RETIRED** (continuity is now carried, not
lost). `heading-direct-numbering-dropped` / `style-linked-numbering-dropped` (custom non-Heading
styles) / `paragraph-style-flattened` remain — custom/localized STYLE identity is 026's scope (021
routes the 020-R7 comment there explicitly).

**Client contract:** `compose-contracts.ts` gains optional `numId?: number` (server-set; mapper never
sets it; preserve-on-repost documented). `docxBridge.ts` untouched (NEVER deleted).

**Seam slice** — `ComposeNumberingCanonicalModelSeamTests.cs` (new) + updates to the R4.5 agreement
file `ComposeNumberingRoundTripSeamTests.cs`:
- **THE golden oracle (owed since 020/011):** carrier round-trip computed-label SEQUENCE == manifest
  §1.5 golden Word labels for all four golden exemplars (rows 9/10/11/13) — source anchored to golden,
  rendered equal to golden. Sequence assertion is stronger than the per-paragraph R4.5 Theory (no
  numbered paragraph may appear/disappear).
- Label-sequence STABILITY over all five §1.5 exemplars incl. `symbol-section-mark.docx` (Wingdings
  bullet glyph — no golden constant by design, equality-only).
- `numbering.xml` BYTE-IDENTITY over all five exemplars.
- Blank-package synthesize of the interrupted-clauses model keeps clauses 1..6 continuous via the
  identity map (the heading's own "1" mid-sequence is the synthesize scheme's FR-27 style-linked
  heading number — documented divergence from carrier mode, where carrier styles govern).
- Projector capture facts + retired-warning fact + nested-bullet no-restart fact.
- R4.5 agreement file §3 UPDATED to the new write-side contract: continuation when
  `StartsNewList=false` + explicit-restart companion test (both green — read/write counter models agree).

**Gates (2026-08-06):** BFF build 0 errors. Compose seam+unit 688/691 — 3 reds ALL pre-existing,
stash-verified at HEAD per §F.3 (2 known NDA seam reds + `ComposeBaselineParaIdStamperTests.MintAndPersist_
AcrossTheFidelityCorpus` — same NDA dup-paraId class, in the RETIRING count-gate component; routed
026/027 with the others). ArchTests 24/28 — same 4 pre-existing master fails (ADR-007/010,
Communication surface); ADR-013 Compose facade green. Publish **46.88 MB** incl. PDBs (−1.37 vs 48.25
task-003 baseline; ≤60 ✓). CVE: no NEW HIGH (pre-existing `System.Security.Cryptography.Xml` 8.0.3
×5 High; master `0455d8658` already patches to 8.0.4 — resolves on merge). Client: contracts file
parses; worktree-wide TS module-resolution errors are pre-existing (unbuilt workspace deps), untouched
by the additive optional field.

**Placement Justification (root §10, citing `.claude/constraints/bff-extensions.md`):** modify/extend
of existing `Services/Compose` files only — no new service, no new endpoint, no DI change, no package.
§11 three questions: existing = `NumberingPlan`/`RenderBlocks`/`BuildContentModel` (extended in place);
extension chosen over a new component; cost-of-doing-nothing = golden-label parity impossible (an
imported numbered doc re-renders with renderer-scheme numbering — wrong legal labels ⇒ wrong legal
references).

**Deviations / routed:**
- Custom/localized paragraph-style-linked numbering (non-Heading styles; 020-R7) → **026** (style
  identity is the carrier concept it rides on; the §1.5 exemplar surface — the task's acceptance
  oracle — is fully covered without it).
- Localized heading-id mapping (011-P8) → **026** (unchanged routing; heading-style parity for
  standard `Heading1..6` ids IS proven here via row 10).
- Client mapper preservation of server-set `numId` on re-post → **010/012** (the cutover tasks own the
  imported-doc client edit loop; documented in the TS contract comment).

---

### §11.1 Task 021 Step 9.5 triage (2026-08-06, commit `cc9ac812b` + fix commit)

**adr-check: PASS 8/8** (independent re-run of the ADR-013 facade arch test included; test-replacement
question resolved — old restart scenario preserved verbatim by the explicit-restart companion, coverage
widened not narrowed).

**code-review: REQUEST-CHANGES → all findings resolved:**
- **F1 (Major, live-path) FIXED** — nested sibling ordered lists merged under the scalar no-clear
  contract (the mapper flags only TOP-level lists; nested boundaries are conveyed by LEVEL transitions).
  Fix: `RenderBlocks` per-level run state — a list item closes deeper runs; a bullet also closes the run
  AT its level; non-list blocks close nested runs but keep level 0 continuable (020-R1 contract); a
  NumId-less nested ordered item INHERITS the nearest shallower active instance (Word's
  one-instance-deeper-ilvl idiom → parent continues after the nested list; re-entered nested list
  restarts via Word's own deeper-level reset). Seam: nested-sibling restart + ordered-in-ordered facts.
- **F2 (Major, forward-looking / task-030 foreign carriers) FIXED** — coincident carrier numId of the
  WRONG KIND no longer binds: `CarrierNumberingScan` now classifies (instance, level) ordered-vs-bullet
  (abstract levels + w:lvlOverride full-lvl redefinitions; tolerant lower-then-higher probe mirroring
  `ResolveOrderedFromModel`); unknown classification defaults to compatible (same-source trust). Seam:
  wrong-kind fallback fact on multilevel-1-1-1 as carrier.
- **F3 (Minor, documented deferral)** — client numId carry-through on edit-and-repost stays routed to
  010/012 (contract comment already instructs preservation). No action here.
- **F4 (Minor) FIXED** — `ScanCarrierNumbering` wraps part-parse failures in
  `ComposePatchException(MalformedDocument)` (lazy package open meant bytes passing the editable open
  could still fail the DOM parse unwrapped).
- **F5 (Minor) FIXED** — `AppendSection`'s defensive list path now mirrors `RenderIntoCarrier`:
  gated target scan, allocation above target max, Add-or-Merge (was: blank-package ids + add-only →
  latent dangle/capture on targets with an existing numbering part).
- **F6 (Minor) FIXED** — `StartsNewList` doc now states per-container scoping vs document-scoped NumId
  continuity (consumers must key continuity on NumId).

**Post-fix gates:** Compose seam+unit 691/694 (same 3 pre-existing reds). Build 0 errors.
**Publish-size note (measurement honesty):** clean-worktree fresh publish of this task's commit =
**46.89 MB incl. PDBs** vs parent commit 46.89 MB — single-task delta **+0.01 MB** (DLL +4 KB). The
main local worktree's fresh publish reports 50.92 MB solely because its `Sprk.Bff.Api.pdb` comes out
6.3 MB vs 2.1 MB in a clean checkout — a LOCAL environment artifact (verified per-file diff: pdb is
the entire delta), not commit payload; CI/deploy build from clean checkouts. Baseline comparison:
−1.36 MB vs the 48.25 MB task-003 baseline; ≤60 ceiling ✓.

---

## 12. Task 022 — tables through the canonical model (2026-08-06)

**R5-reuse interpretation (directional):** the R5 tracked-table work (`ComposeShadowPatchEngine.ApplyTableOperation`
— tracked InsertRow/DeleteRow/InsertColumn/DeleteColumn/SetCellContent/SetTableProps) is an EDIT-OPERATION layer
on the op-log path; it is REUSED AS-IS there (untouched by this task; its representation continues to serve
tracked edits until 025 models revisions). What render-on-save needs — and what FR-04 "tables round-trip
without hard-fail" actually requires — is the MODEL carrying table structure, since tables live in the body
being replaced (unlike numbering, no carrier part to reference). Table handling was NOT re-built: the existing
`ComposeTable`/`ProjectTable`/`BuildTable` surfaces were extended in place.

**Closed structural set (task-022 widening):**
- `ComposeTable`: `StyleId` (tblStyle — carrier styles keep the styled look), `Width` (tblW), **`Borders`
  (TRI-STATE: null = born-in-editor → legacy single-border/100% chrome bit-stable for the live client;
  non-null = source-faithful, only present edges emitted; all-edges-null = BORDERLESS — a legal
  signature-block layout table no longer grows borders on save, the biggest visible table-fidelity bug)**,
  `GridColumnWidthsTwips` (tblGrid), `LookHex` (tblLook).
- `ComposeTableRow`: `RepeatAsHeaderRow` (trPr/tblHeader — distinct from the cosmetic cell `IsHeader`).
- `ComposeTableCell`: `GridSpan`, `VMerge` (None/Restart/Continue), `Width` (tcW), `VerticalAlignment`
  (projector always explicit — source value else Word-default "top"; null keeps the legacy center chrome).
- Out-of-set chrome flattens LOUDLY: one counted `table-formatting-flattened` per dropped construct
  (tblpPr floating, jc, shading table+cell, tblInd, tblCellMar, tblCellSpacing, trHeight, tcBorders,
  tcMar, textDirection). Widening = 026/follow-up.
- Grid computation fix: width-less grids now size to the widest row's TOTAL SPAN (gridSpan-aware), not
  the raw cell count.
- SDK 3.x gotcha: the OOXML enum STRUCTS' `ToString()` is NOT the XML token — capture via
  `IEnumValue.Value` ("single"/"pct"), re-mint via the struct's string ctor.

**Seam slice** — `ComposeTableCanonicalModelSeamTests.cs`: SDK-authored rich source (borderless
signature table w/ explicit grid + gridSpan row; styled table w/ partial borders, tblLook, repeat-header
row, vMerge restart/continue, tcW, bottom vAlign) → capture facts → carrier round-trip reproduces the
structure in rendered OOXML → **OpenXmlValidator: no NEW schema errors** (the "Word-valid markup"
acceptance made mechanical) → corpus-wide table-shape fixed point (rows/cells/span/merge survive
model→docx→model for every corpus doc with tables) → loud-degradation count pin (5 constructs = 5) →
born-in-editor legacy-chrome pin (live client look unchanged).

**Gates:** suite 705/708 (same 3 pre-existing NDA-class reds). ADR-013 facade green. No package/DI/endpoint
change (CVE surface unchanged; publish measured post-commit in a clean worktree per §11.1's measurement note).
Client contract: additive optional fields on the table types (server-set; preserve-on-repost documented).

**Placement Justification (root §10):** modify/extend of existing `Services/Compose` files only — no new
service/endpoint/DI/package. §11: existing = ComposeTable model + ProjectTable + BuildTable (extended in
place); cost-of-doing-nothing = imported tables lose merges/widths and grow borders on save (Word-visible
structural corruption of legal signature/schedule tables).

---

### §12.1 Task 022 Step 9.5 triage (2026-08-06, commit `cef2cd988` + fix commit)

**code-review: REQUEST-CHANGES → all warranted findings resolved:**
- **F1 (Major, live endpoint) FIXED** — client-supplied border token unvalidated (`new BorderValues(garbage)`
  → schema-invalid XML; JSON `"val": null` → ArgumentNullException 500). Fix: `AppendEdge` validates via
  `IEnumValue.IsValid`, coerces null/garbage → `single`.
- **F2 (Major, F-1 contract) FIXED** — the loud-degradation enumeration was incomplete (hMerge, tblLayout,
  noWrap, cantSplit, tblPrEx, bidiVisual… dropped SILENTLY). Fix: CATCH-ALL counting — every tblPr/trPr/tcPr
  child outside the modeled set counts `table-formatting-flattened`, plus row-level `tblPrEx`. Carrying
  hMerge/tblLayout typed → 026.
- **F3 (Major) FIXED** — boolean-attrs-only `tblLook` (strict authoring) was silently dropped, killing style
  banding. Fix: `ProjectLookHex` synthesizes the hex bitmask from the six booleans when `@w:val` absent.
- **F4 (Major — reviewer right, my design wrong) FIXED** — forced explicit `vAlign=top` OVERRODE
  table-style-inherited alignment. Fix: model carries the DIRECT source value or null; renderer emits
  nothing for null in source-faithful mode (style chain governs; `Borders != null` is the mode
  discriminator, threaded to `BuildCellProperties`), legacy center only in editor mode.
- **F5 FIXED (docs)** — the Borders tri-state is documented as the mode discriminator (StyleId/Width/LookHex
  honored only source-faithful) in model XML docs + TS contract.
- **F6 FIXED** — type-less width with `@w:w` = legacy dxa idiom, kept (was silently dropped).
- **F7 FIXED** — partially-widthed grid discard now counted.
- **F8 FIXED** — case-insensitive vAlign/width-type mapping; unknown coerces (documented).
- **F9/F10/F11 FIXED (tests)** — validator diff is a per-description MULTISET; corpus theory vacuity-guarded
  against raw body-table count; `TableShapeFacts` fold extended to ALL carried facts (style/width/borders/
  grid/look/header/vAlign/cell-width) corpus-wide.
- **F12 (documented deferral)** — client-mapper preservation of server-set table facts on re-post → 010/012
  (same routing class as 021-F3 numId preservation; TS contract states the obligation).

**Post-fix:** suite 705/708 (same 3 pre-existing). Publish (clean-worktree): **46.89 MB incl PDBs, task
delta 0.00** (measured on `cef2cd988`; fix commit is code-only).

---

## 13. Task 023 — headers/footers + page breaks through the canonical model (2026-08-06)

**Rigor note:** POML authored STANDARD; executed at **FULL** per task-execute Step 0.5 (modifies `.cs` on the
BFF hot-path, `bff-api` tags — and Step 9.5 caught live-path Majors on both prior wideners on this surface).

**Two mechanisms (directional interpretation):**
- **Headers/footers ride the CARRIER, not the model.** RenderIntoCarrier already preserves header/footer
  PARTS byte-identically and re-attaches the trailing sectPr (which carries the headerReference/
  footerReference relationship ids). 023's contribution is the PROOF: parts byte-identical + references
  RESOLVE post-round-trip (relationship integrity), seam-pinned per-doc and corpus-wide. Editing header
  CONTENT is not an editor capability; template-chrome part-merge is Phase 3 — carrying header content in
  the model would be scope creep against both.
- **Page breaks are MODEL data** (the actual gap: a manual `w:br type="page"` degraded to a SPACE via
  `line-break-flattened` before this task): run-level breaks project to a dedicated
  `ComposeInlineRun.IsPageBreak` at the EXACT inline position (ProjectRun flush-split — text before/after
  in separate runs), `w:pPr/pageBreakBefore` projects to `ComposeBlock.PageBreakBefore` (OnOff semantics
  via IsOn); the renderer authors both back out (`BuildRun` break-run; `ApplyPageBreakBefore` in CT_PPr
  order across all three paragraph builders). Soft line/column breaks remain warned flattens.
- **Interior section breaks (pPr-nested sectPr) flatten LOUDLY** via the NEW counted
  `section-break-flattened` warning (content joins the FINAL section — a real pagination/header-scope
  change, honestly counted). Full multi-section modeling deferred (corpus manifest row 8 = placeholder;
  no multi-section corpus doc exists). Trailing-section preservation unaffected.

**Seam slice** — `ComposeHeaderFooterPageBreakSeamTests.cs` (13 tests): exact-position break capture
(3-run split, warning-free) · carrier round-trip (parts byte-identical + references resolve + breaks
re-authored in-paragraph + multiset validator) · break-fact fixed point · interior-section loud flatten +
no-hard-fail round-trip (landscape final section preserved) · corpus-wide reference-resolution +
page-break-count stability theory.

**Gates:** suite 718/721 (same 3 pre-existing NDA-class reds). ADR-013 facade green. No package/DI/endpoint
change. Publish: clean-worktree measurement post-commit (per §11.1 convention).

**Placement Justification (root §10):** modify/extend of existing Services/Compose files only. §11:
existing = ProjectRun/ProjectParagraph/BuildRun/paragraph builders (extended in place); cost-of-doing-nothing
= a legal document's manual page breaks become SPACES on save (agreement signature pages flow into the
preceding clause — visible structural corruption).

---

### §13.1 Task 023 Step 9.5 triage (2026-08-06, commit `5951f173b` + fix commit)

**adr-check: PASS 8/8** (incl. confirmation the FULL-rigor escalation over the POML's authored STANDARD is
the correct Step 0.5 protocol reading; re-ran ADR-013 + the seam class independently).

**code-review: APPROVE-WITH-MINORS → applied:**
- **F1 (Medium) FIXED** — `section-break-flattened` falsely fired on the 011-P1 promotion shape (final
  sectPr parked in the LAST body paragraph, no body-level sectPr — the renderer PROMOTES it, nothing
  flattens). Fix: `IsPromotedTrailingSectPr` predicate mirrors the renderer's promotion condition; new
  seam fact proves no-warn + actual promotion.
- **F2 (Low, documented)** — explicit `w:pageBreakBefore w:val="false"` style-override-off collapses to
  absent (bool, not tri-state) → paragraph regains a carrier style's break; accepted degradation noted on
  the model field (026-shaped if it surfaces).
- **F3 (Low) FIXED (comment)** — a page break inside a FIELD result / SDT atom display text stays a space
  under that construct's own warning (deliberately not IsPageBreak; 026 owns the surface).
- **F4 (Low) FIXED** — page-break runs are no longer emitted once the output-text budget is exhausted
  (`ModelWalkContext.HasOutputBudget`) — a clipped projection must not trail blank pages.
- **F5-F8 (Info, recorded)** — a mid-hyperlink break splits the hyperlink into two wrappers on re-render
  (semantically equivalent; hyperlink identity not model data); `{isPageBreak + text/href}` client posts
  collapse to a bare break by documented contract; TS preservation obligation remains 010/012's (with
  021-F3/022-F12); the corpus break-count theory proves model→rendered equality (source-side losses in
  dropped constructs are covered by those constructs' own warnings).

**Post-fix:** suite 719/722 (same 3 pre-existing). Publish clean-worktree: **46.89 MB incl PDBs, task
delta 0.00**.

---

## 14. Task 024 — hyperlinks + comments through the canonical model (2026-08-06)

**Rigor:** FULL (Step 0.5 override UP from authored STANDARD — same reading adr-check confirmed for 023).

**Hyperlinks — R5 representation REUSED, silent-loss gaps closed:** external http/https/mailto links
already flowed through the model (Href → BuildRun sentinel → external relationship). 024 adds loudness:
INTERNAL bookmark links ("#anchor") flatten with counted `internal-link-flattened` (bookmark targets are
not model data — bookmarks + internal links routed to **026**; the renderer previously unwrapped them
SILENTLY on the absolute-Uri check); unresolvable/protocol-neutralized rel targets count
`hyperlink-target-dropped` (was silent). Read path (#anchor HTML links) untouched.

**Comments — were SILENTLY LOST, now model data (the 024 core):** body anchors fell through default
cases; the comments part survived the carrier byte-copy but nothing anchored it. Now:
- `ComposeContentModel.Comments` — the part's projection (id/author/initials/RAW date InnerText/plain
  text, paragraphs joined 
; rich comment content flattens per the near-term tier).
- ANCHOR MARKER runs (`ComposeInlineRun.CommentAnchor` Start/End — the IsPageBreak mechanism) at exact
  inline positions; the End marker FOLDS the `w:commentReference` (Word's canonical adjacency); a bare
  reference (point comment) projects as an adjacent Start+End pair (`ctx.CommentRangesSeen` discriminates).
- Renderer: `AssembleParagraph` emits rangeStart / rangeEnd+reference from markers;
  `EnsureCommentsPart` authors `word/comments.xml` ONLY when the target has none — CARRIER mode leaves
  the source part authoritative + BYTE-IDENTICAL and re-authors only the anchors against its ids (the
  numbering identity pattern); blank-package synthesize authors the part from the model.
- Loud degradations: block-level range anchors (rare shape) + non-decimal comment ids →
  `comment-anchor-flattened` / `comment-flattened`.

**Seam** — `ComposeHyperlinkCommentSeamTests` (13): capture facts (anchors at exact positions incl.
fold + point-pair; raw date; internal-link loudness) · carrier round-trip (part byte-identical; full
range markup re-authored in bracket order; external link resolves to the same URI through a real
relationship; multiset validator) · fixed point (anchors/comments/hrefs) · synthesize-mode part
authoring · corpus-wide stability theory (part bytes + anchor multiset + link-target sequence).

**Gates:** suite 732/735 (same 3 pre-existing). ADR-013 green. No package/DI/endpoint change. Publish:
clean-worktree post-commit.

**Placement Justification (root §10):** modify/extend of existing Services/Compose only. §11:
cost-of-doing-nothing = reviewer comments on an imported agreement VANISH on first save (silent
data-loss — the POML's escalation class, resolved by modeling rather than escalation since the
representation fits the established marker-run + carrier-identity patterns).

---

### §14.1 Task 024 Step 9.5 triage (2026-08-06, commit `a5a979d29` + fix commit)

**adr-check: PASS 8/8** — scrutinized the anchor mechanism directly (markers are model data by id, no
positional coupling to retained bytes — NOT inherited-XML anchoring); confirmed the POML itself
pre-authorized the FULL escalation ("may escalate to FULL if a silent-loss risk is found" — it was).
Two forward-looking tension candidates recorded: comment EDITING would collide with the
byte-identical-part rule (future resolution: identity-diff part re-authoring per the numbering
precedent); dangling-anchor robustness (026-shaped) — the latter fixed outright below.

**code-review: REQUEST-CHANGES → all findings resolved. The recurring client-input class (021-F1,
022-F1) bit a THIRD time — now Critical:**
- **F1 (Critical) FIXED** — `EnsureCommentsPart` wrote client-controlled Author/Initials/Text UNSANITIZED
  (XML-illegal control chars ⇒ part unserializable ⇒ save throws on the live path) and arbitrary Date
  InnerText into an xsd:dateTime attribute. Fix: `SanitizeText` on every string; Date emitted only when
  it parses round-trip; seam fact feeds REAL control bytes and asserts them stripped.
- **F2 (Major) FIXED** — orphan anchors: renderer now computes the valid-id set (carrier part ids scanned
  READ-ONLY from the bytes / deduped model ids for blank-package) and `FilterCommentAnchors` DROPS
  unmatched or out-of-range-Kind markers (text always kept); duplicate `ComposeComment.Id`s collapse
  first-wins; `AppendSection` strips all anchors (manages no part).
- **F3 (Major, routed)** — the client mapper still drops every 021-024 server-set field on rebuild:
  explicitly recorded as a **010/012 CUTOVER OBLIGATION** (was implicit) — comments/anchors/numId/table
  facts/page breaks all silently vanish on an edited re-post until the mapper preserves them.
- **F4 (documented)** — id normalization: OOXML `w:id` is ST_DecimalNumber (integer VALUE semantics;
  "01" == "1") — canonical emission is value-correct; noted in `ScanCarrierCommentIds` docs.
- **F5 (Minor) FIXED** — range ids PRE-SCANNED before the walk (order-independent fold; a bare reference
  before its range no longer duplicates anchors).
- **F6 (Minor) FIXED** — block-level range elements suppress their id ATOMICALLY (`SuppressedCommentIds`)
  — the inline partner flattens counted, never an orphan start/end.
- **F7 (Minor) FIXED** — positive tests added: dangling r:id + neutralized `javascript:` target
  (hyperlink-target-dropped ×2) and a cross-paragraph range round-trip.
- **F8 (Minor) FIXED** — `ProjectComments` joins the walk's resource discipline (cancellation per
  comment; Text through the shared output budget).
- **F9 (Minor) FIXED** — docLocation-only hyperlinks join the loudness guard.
- **F10 (Minor) FIXED** — CRLF normalized before the part split.
- **F11 (Info)** — Word's cosmetic rStyle/annotationRef omitted (tolerated, default styling); out-of-range
  enum Kind folded into the F2 filter.

**Post-fix:** suite 735/738 (same 3 pre-existing). Publish clean-worktree: **46.90 MB incl PDBs, task
delta +0.01**.

**Pattern note for 025/026:** three consecutive tasks had their top finding in the same class —
CLIENT-SUPPLIED MODEL DATA REACHING OOXML AUTHORING UNVALIDATED (021-F1 border tokens, 022-F1 repeat,
024-F1 comment strings). 025 (tracked-changes: client-posted authors/dates!) must sanitize at the point
of authoring FROM THE START, not wait for review.

---

## 15. Task 025 — tracked-changes (redlines) through the canonical model (2026-08-06)

**Commit `1f27e0291`** (+ Step-9.5 fix commit if any — see §15.1). Spec FR-04 / 020-R11. The pivot: the
model walk no longer SETTLES revisions — redlines are model data with real accept/reject in Word after a
render-from-model save.

**Mechanism ("carry the identity, mint the id"):**
- `ComposeInlineRun.Revision {Kind Inserted|Deleted, Author, Date(raw)}` — captured by threading a
  revision context through `ProjectInline`/`ProjectRun` (default-param recursion, same shape as `href`).
  Pending-deleted text is ordinary model `Text`; the renderer authors it as `w:delText` inside `w:del`.
- Renderer GROUPS consecutive same-identity runs (record value equality kind+author+date) into ONE
  `w:ins`/`w:del` wrapper. Revision `w:id` is ALWAYS server-minted (`ListRenderState.NextRevisionId()`),
  seeded ABOVE the carrier's max via `ScanCarrierRevisionIdSeed` (read-only side open — the R5
  `SeedRevisionId` analog; skipped for revision-free models).
- Paragraph-MARK revisions: `ComposeBlock.MarkRevision` → `w:pPr/w:rPr/w:ins|w:del` (Deleted = accepting
  merges with next paragraph). RETIRES `tracked-paragraph-mark-flattened`.
- Formatting-change history: `ComposeFormatChange {Author, Date, PreviousPropertiesXml}` for
  `w:pPrChange` (block) / `w:rPrChange` (run — first-flush-only on page-break splits). The previous
  properties are an OPAQUE server-set XML carry — a typed carry would mis-state the reject target (the
  old pPr can hold indentation/spacing/tabs far outside the thin model). Render gate
  `TryParsePreviousProperties<T>`: typed SDK parse (never string injection) + LocalName/namespace check +
  `OpenXmlValidator` subtree validation + 32 KB clamp; ANY failure drops the whole change record
  (= accepting the formatting change; renderer-side loud counter routed to 026 with dangling-anchor).
- Downgrades (LOUD, counted): `tracked-move-downgraded` (moveFrom→del / moveTo→ins; move identity lost,
  accept/reject semantics preserved); `tracked-nested-revision-simplified` (stacked ins⊃del → innermost
  wins — the **R4 "barfoo" warned-flatten baseline, operator sign-off still pending**);
  `tracked-format-change-flattened` (mark rPrChange). Table revisions (trPr/tcPr ins/del/cellIns/cellDel)
  stay in 022's `table-formatting-flattened` catch-all — 026 owes typed carry.
- RETIRED warning codes: `tracked-insert-flattened`, `tracked-delete-flattened-kept`,
  `tracked-paragraph-mark-flattened`.
- Marker interplay: page-break markers CARRY revision (a break can be inside an insertion); comment
  anchors NEVER do (emitted at paragraph level outside wrappers — wrapper content-model caution + the
  anchor id contract).

**§14.1 pattern note APPLIED FROM THE START** (021-F1/022-F1/024-F1 class): `SanitizeRevisionAuthor`
(control chars stripped, 255 clamp, empty→"Unknown" — `@w:author` schema-required), `TryValidRevisionDate`
(parse-gate, raw kept — the 024 comments-part recipe), previous-props XML SDK-parse+validator-gated,
ids never client-controlled. The seam slice pins all of it (`Render_SanitizesHostileClientRevisionInput`).

**Client contract mirrors** (additive): `revision`/`formatChange` on runs, `markRevision`/
`propertiesChange` on blocks — all server-set, preserve-untouched-on-re-post. **010/012 cutover
obligation extended**: the mapper must now ALSO preserve revision facts or an edited re-post silently
SETTLES every redline (the exact data-loss class this task closes).

**Seam slice:** `ComposeTrackedChangesSeamTests` — 15 green (capture incl. mark/pPrChange/rPrChange;
move+nested downgrades loud; Word-valid carrier round-trip w/ delText-only under w:del, unique minted
ids > carrier max, validator multiset; fixed point; grouping split-on-identity; hostile-input hardening;
corpus theory w/ revision-fact stability).

**Gates:** BFF build clean · full suite 9770/9874 (only the 3 pre-existing NDA reds) · ArchTests
unchanged (ADR-013 Compose PASS) · client tsc 28 errors before AND after (stash-verified; all
pre-existing workspace resolution) · publish clean-worktree **46.90 MB incl PDBs, task delta ±0.00** ·
no NEW HIGH CVE (Crypto.Xml HIGHs pre-existing; patched on master, resolves at pre-PR merge).

### 15.1 Step-9.5 review (commits `1f27e0291` → fix `ea2cdce2a`)

Two parallel agents on the committed SHA. **adr-check: PASS 8/8** (render-from-model preserved; scans
read-only; additive widening; the opaque PreviousPropertiesXml carry judged COMPLIANT — outside-the-thin-
model content, ADR-049 opaque-atom precedent, server-set/client-opaque, injection-gated; R5 representation
faithfully reused incl. landing the R5-deferred rPrChange refinement; both engines untouched — 0 hunks).
**code-review: REQUEST-CHANGES** — the reviewer EMPIRICALLY reproduced every Major in a scratch harness:

- **F1 (Major) FIXED** — a revised linked run rendered `w:ins ⊃ w:hyperlink` (schema-INVALID; CT_RunTrackChange
  does not admit hyperlink). Now Word's canonical `w:hyperlink ⊃ w:ins ⊃ w:r` (wrapper inside the link;
  hyperlink boundary breaks grouping). Seam test added.
- **F2 (Major) FIXED** — paragraph-mark `w:moveFrom`/`w:moveTo` (whole-paragraph move) vanished UNCOUNTED.
  Now downgrades to Deleted/Inserted mark revision + `tracked-move-downgraded`, symmetric with run level.
- **F3 (Major) FIXED** — the date gate (DateTime.TryParse, the 024 recipe) admitted culture formats
  ("08/01/2026") that are schema-INVALID as `@w:date`. Now an xsd:dateTime LEXICAL gate (TryParseExact +K).
  ⚠️ The 024 comments-part gate has the same hole — routed to 026 (loud-counter batch) rather than
  touching shipped 024 behavior in this commit.
- **F5 FIXED** — dates normalize through the same gate at CAPTURE (model canonical; empty/junk→null) so
  the fixed point holds for degenerate source attribution; oracle distinguishes null vs empty.
- **F4 FIXED** — LocalName/ns check was dead code (typed elements always report their class name); the SDK
  typed-parse ctor (throws on wrong root, prohibits DTD — reviewer-proven) is the real gate; comments fixed.
- **F7 FIXED** — validator-gate branch now exercised (well-formed schema-invalid rPr fixture) + F3 date fixture.
- **F8 FIXED** — seed scan extended: comments part + SectionPropertiesChange/TblPr/TrPr/TcPr/TblGridChange.
- **F9 (documented)** — AppendSection takes no revision seed; callers author settled AI content (noted inline).
- **F10 FIXED** — per-call OpenXmlValidator (instance thread-safety not contractual; subtree validation cheap).
- **F6 (routed → 027)** — the corpus contains ZERO revision markup (even the "track changes" PAT doc is a
  settled comparison result) — the corpus theory pins nothing tracked-changes-specific; 027 should add a
  genuine multi-author redlined fixture (tracked hyperlink + paragraph move included).

Reviewer-proven CLEAN: pPr/rPr child order; delText-only under del; anchors-outside-wrappers loses nothing
(anchors ARE legal in ins — positional coverage identical); table-cell revision threading; budget clamp
covered; grouping fixed point; no client path to w:id; DTD prohibited; id-smuggling via previous-props
blocked by the validator gate.

**Post-fix:** seam slice 17 green; Compose scope 952/955 (same 3 pre-existing NDA reds).

---

## 16. Task 026 — hard-tier graceful degradation (2026-08-06)

**Commits `0d1a78a9c` + Step-9.5 fixes `3857ce542`.** Spec FR-04's graceful-degradation GUARANTEE — the
NDA breakers (text boxes / drawings / fields / content controls) accept-flatten with a surfaced warning,
NEVER a 422. Much of the loud-flatten machinery already existed from 020-025; 026 closed the CONTENT gap
and built the warning SURFACE:

- **Text-box visible text is extracted** (`ExtractTextBoxDisplayText` + 4 projector sites: complex run /
  inline AC / run-nested AC / block-level AC). The NDA's signature blocks ("For: Appligent, Inc.",
  "signature", "______________") land as degraded runs/paragraphs. `mc:AlternateContent` extracts exactly
  ONE branch (Choice preferred) — dedups both the text AND the NDA's duplicate-paraId class (the dup ids
  ARE the Choice/Fallback duplication). Per-run nearest-paragraph assignment (F1) prevents nested-shape
  doubling; unchosen-branch paragraphs count as visited (F2 — no false unrendered-paragraphs); mixed
  transitional runs keep their direct w:t text (F3). Text-free objects keep `complex-object-dropped`.
- **Render-side degradation sink**: `ListRenderState.Warn` → optional out-collection on
  `SynthesizeDocument`/`RenderIntoCarrier`. Counts: `comment-anchor-dropped` (024-routed),
  `tracked-format-change-dropped` (025-F4/F7-routed), `hyperlink-target-dropped`,
  `comment-duplicate-dropped` (F7). SaveAsync surfaces them as SUCCESS-WITH-WARNINGS:
  `SaveComposeDocumentResult.DegradationWarnings` → response `degradationWarnings` (optional trailing,
  mapped to the `ComposeProjectionWarningResponse` wire DTO — F6) → the client's existing dismissible
  banner (`ComposeWorkspace`).
- **Comments date gate → xsd lexical** (025-F3 same-class hole closed; carrier parts unaffected).
- **No-422 proof**: seam slice `ComposeHardTierDegradationSeamTests` (16 facts + corpus theory) — NDA
  projects/renders/re-projects without hard-fail, exact one-branch oracle ("For: Appligent, Inc." ×1),
  UNIQUE rendered paraIds (the 422's dup-id trigger is unreachable on this path), sink counts, corpus
  no-hard-fail floor. `ComposeCanonicalModelRoundTripSeamTests` NDA expectation updated to 026 posture.

**Step 9.5**: adr-check **PASS 8/8** — notably: FR-04's "user-visible warning" clause judged COMPLIANT for
imported docs TODAY via the load-path projection-warning banner (text-box-flattened reaches the user at
open); the save-side surface joins at the 010 cutover. code-review **APPROVE-WITH-MINORS**, 3 empirically-
proven Mediums (F1/F2/F3 above) all FIXED + pinned; F6/F7 fixed; F5 routed.

**Gates**: suite 9787/9891 full + 968/971 Compose (same 3 pre-existing NDA reds; one unrelated flaky
passed on re-run) · ArchTests same 4 pre-existing · client tsc 28 before/after · BannerStack jest 18/18
(14 ComposeWorkspace suites fail PRE-EXISTING @spaarke/auth worktree resolution) · publish clean-worktree
**46.90 MB incl PDBs, delta ±0.00** · CVE: no package changes.

**ROUTED / REMAINING:**
- **010 (CUTOVER OBLIGATIONS, extended)**: wire the `degradations` out-collection into `RenderIntoCarrier`
  from `SaveAsync` at the imported-save cutover (adr-check recommendation — otherwise the imported half of
  FR-04's warning clause silently regresses); + the standing client-mapper preservation list (021-025).
- **012**: client warning-family separation (F5 — save degradations vs load import warnings share one
  reducer slot; clean-save doesn't clear; raw codes need friendly copy).
- **Notes/backlog (fidelity wideners beyond FR-04's closed criteria — post-R6 or 027-adjacent)**:
  custom-style-linked numbering (020-R7) · localized heading ids (011-P8) · hMerge/tblLayout typed carry
  (022-F2) · bookmarks + internal links (024) · typed move + table-revision carry (025) · pageBreakBefore
  tri-state (023-F2) · field-result box text + SmartArt doc note (026-F4).
- **Operator sign-offs still pending**: R4 "barfoo" (025 warned baseline) · R5 U+FFFD.

---

*Steps 1–3 artifact + gates + tasks 020/011/021/022/023/024/025/026 records. Checkpoint in `current-task.md`.*
