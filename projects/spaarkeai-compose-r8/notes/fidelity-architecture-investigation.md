# Compose R8 — Render-on-Save Fidelity Architecture: Investigation & Findings

> **Status**: 🔬 Investigation (pre-spec). This document seeds `spaarkeai-compose-r8`. It captures the
> audit findings that motivate the project and candidate fix directions — but R8's FIRST job is a **full
> investigation + research pass** to confirm the root cause and choose the CORRECT solution before any build.
> **Created**: 2026-08-18 (from `spaarkeai-compose-r7` UAT + proactive hidden-issue audit)
> **Source of findings**: `projects/spaarkeai-compose-r7/notes/uat-issues.md` (UAT-07a, UAT-15..20, and the
> architectural anchor items UAT-24/25). **Do not lose those links** — they are the evidence base.

---

## 1. Why this project exists (the problem in one paragraph)

Compose's "render-on-save" writes an edited document back to SharePoint Embedded by **re-authoring the entire
`<w:body>` from a thin in-memory model** (`ComposeContentModel`) whose run type carries only
Text + Bold/Italic/Underline/Href + tracked-change facts. Everything else a real legal document contains —
fonts, sizes, colors, footnotes, cross-reference fields, paragraph spacing, content controls, complex
objects — is **not in the model, so it is lost on the first save of any imported document**. The original is
retained in SPE version history (ADR-049 safety net), but the *live* document that reopens in Word is
degraded. Many of these losses are **SILENT** (no warning). This makes Compose unusable as a legal-drafting
surface for real documents. Fixing it is not a "widener" patch — it is a **render-on-save architecture
change**, which is why it is its own project (R8) rather than an R7 UAT fix.

**R7's job (in flight)** = make Compose **HONEST + SAFE** (never silently drop or mis-place; surface every
loss). **R8's job** = make Compose **FAITHFUL** (actually preserve the formatting/structure through the save).

---

## 2. The architectural root cause (confirmed by audit)

- Live save path: `ComposeDocumentRenderer.RenderIntoCarrier` (`src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs:399`)
  calls `body.RemoveAllChildren()` (`:449`) and rebuilds every paragraph/run from `ComposeContentModel`.
- The model's run type `ComposeInlineRun` (`Services/Compose/.../ComposeContentModel.cs:276`) carries only
  `Text / Bold / Italic / Underline / Href` + tracked-change facts. There is **no opaque-rPr carry**.
- Carrier-level assets (styles.xml, numbering.xml, headers/footers) survive because they live in other parts;
  but anything expressed as **direct/inline formatting or as an in-body structure** the model can't represent
  is dropped when the body is re-authored.

**Consequence**: fidelity loss is systemic to the write model, not a set of missing special-cases.

---

## 3. Findings carried from R7 (the FAITHFUL-bar backlog R8 owns)

Severity uses ⛔ BLOCKER = "not usable / legally wrong for real documents". All are cross-referenced to the
R7 register (`projects/spaarkeai-compose-r7/notes/uat-issues.md`).

| From R7 | Loss | Severity | Silent? | Evidence |
|---|---|---|---|---|
| UAT-07a | Widener family: indentation, paragraph styles, tables, section breaks, tabs, line breaks, internal links flattened | ⛔ BLOCKER | warned (loud) | R6 defer-register §C; `ComposeDocxProjectionBuilder.cs` warn sites |
| UAT-15 | **Direct character formatting** — font family/size/color, highlight/shading, super/subscript, caps, underline style+color, char spacing — stripped to Normal | ⛔ BLOCKER | **SILENT** | `ComposeDocxProjectionBuilder.cs:2644-2677`; `ComposeContentModel.cs:276` |
| UAT-16 | **Footnotes / endnotes** dropped from flow (orphaned parts; text invisible) | ⛔ BLOCKER | warned-cryptic | `ComposeDocxProjectionBuilder.cs:2748-2753, 866-869` |
| UAT-17 | **Word fields** — cross-refs (`REF`), TOC, page/section refs, DATE — flattened to STATIC text (live reference lost) | ⛔ BLOCKER | warned-cryptic | `ComposeDocxProjectionBuilder.cs:2414, 2445` |
| UAT-18 | **Paragraph spacing** — line spacing, space before/after, shading, borders, keepNext, tab-stops — dropped | ⛔ BLOCKER (court filings) | **SILENT** | `ComposeDocxProjectionBuilder.cs:1935-2090` |
| UAT-19 | Content controls / SDT flattened (form fields, dropdowns, repeating sections) | MAJOR (BLOCKER for templates) | warned-cryptic | `:316, 1892, 1885, 2520, 410` |
| UAT-20 | strikethrough dropped; numbering-unresolved loses number; complex/floating objects dropped + text-boxes flattened; comment rich-content + reply-thread flattened / 4-part threaded comments unrepresentable | MAJOR | warned-cryptic | `ComposeContentModel.cs:45-46`; `IComposeService.cs:571-572`; various |

**Architectural anchor items that R8 should also weigh** (they interact with the write model):
- UAT-24 — strict-only resolver (no fuzzy); UAT-25 — ContentModel save bypasses stale-base concurrency check.
  (R7 addresses the *safety/honesty* of these; the deeper "how edits map to the doc" may inform the R8 model.)

---

## 4. Candidate fix directions (to be RESEARCHED in R8, not assumed)

The audit suggested directions; **R8 must investigate + validate before committing**. Options on the table:

1. **Opaque-rPr / opaque-pPr carry** — extend `ComposeContentModel` runs/paragraphs to hold the ORIGINAL
   `w:rPr`/`w:pPr` XML verbatim and re-emit it on render for untouched runs; only edited runs are re-authored.
   Preserves everything on unedited content; smallest conceptual change. Risk: the model grows; "edited vs
   untouched" boundary must be exact.
2. **Diff/patch the original OOXML instead of whole-body re-author** — apply edits as targeted byte/step
   patches onto the ORIGINAL `document.xml` (the R4 write-path direction `ComposeShadowPatchEngine` already
   points at), so untouched content is byte-identical. This is the "one byte-author, no whole-body rebuild"
   model — likely the correct long-term architecture; the biggest change. Relate to ADR-049.
3. **Hybrid** — patch-engine for loaded docs (fidelity), thin-model render only for born-in-editor/new docs
   (where there's no original to preserve).
4. **Structure-aware model** — add footnotes/fields/SDT/objects as first-class model nodes. Necessary
   regardless of 1–3 for content the user actually EDITS.

**Research questions R8 must answer** (the "build the correct solution" mandate):
- What is the true edited-vs-untouched granularity Compose needs (run / paragraph / block)?
- Can the existing `ComposeShadowPatchEngine` (op-log, `(paraId,runIndex,offset)` anchored) be the single
  byte-author for the load path, eliminating `RenderIntoCarrier`'s whole-body rebuild?
- Which OOXML features must round-trip losslessly for legal docs (numbering, cross-refs, footnotes, tables,
  tracked-changes, comments) vs. which are acceptable to simplify — validated against a REAL worst-offender
  corpus (the R6 §C / Corteva NDA corpus)?
- How does this interact with the tracked-changes + comments model (UAT-20/22) and the resolver (UAT-24)?
- Fidelity acceptance: define a measurable "round-trips clean" gate on the corpus (seam tests).

---

## 5. What R8 is NOT

- Not an R7 UAT fix — R7 owns the honest/safe signal layer (surface losses, no mis-placement).
- Not a "warning copy" change — that's UAT-07b (already done in R7).
- Not a guess-and-build — R8 opens with a full investigation/research pass to choose the model, then specs it.

---

## 6. Entry-points (evidence base for the R8 investigation)

- Write model: `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs` (`RenderIntoCarrier` @399/449),
  `ComposeContentModel.cs` (`ComposeInlineRun` @276), `ComposeDocxProjectionBuilder.cs` (projection/warn sites),
  `ComposeShadowPatchEngine.cs` (the op-log byte-author — candidate single author).
- Read model / fidelity: `docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md`, `.claude/adr/ADR-049-compose-shadow-document.md`.
- Backlog evidence: `projects/spaarkeai-compose-r6/notes/r6-defer-register-consolidated.md` §C (widener corpus + UAT volumes),
  `projects/spaarkeai-compose-r7/notes/uat-issues.md` (UAT-07a, 15–20, 24, 25 with file:line).
- Corpus: R6 §C worst-offender rows + the Corteva NDA (confidentiality sign-off pending) — the fidelity gate target.

---

## 7. Next steps for R8 (when scheduled)

1. `/design-to-spec` seeded by THIS document → confirm scope + the research questions in §4.
2. Full investigation/research pass (the model decision: patch-engine vs opaque-carry vs hybrid) with a
   proof-of-concept round-trip on the worst-offender corpus BEFORE committing an architecture.
3. `/project-pipeline` → tasks. Sequence AFTER R7's honest/safe batch lands.
