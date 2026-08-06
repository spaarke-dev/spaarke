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

---

*Steps 1–3 artifact. Checkpoint in `current-task.md`.*
