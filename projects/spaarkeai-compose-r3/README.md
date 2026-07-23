# Spaarke Compose R3 — Word-Feature Fidelity

> **Last Updated**: 2026-07-16
>
> **Status**: In Progress (pipeline-initialized — tasks generated, ready to execute)

## Overview

R3 makes the Compose Word round-trip **faithful**. Today any dirty save rebuilds the whole `.docx` from the editor's simplified view, silently dropping headers/footers, firm styles, multi-level clause numbering, hyperlinks, and embedded objects — even for content the user never touched. R3 inverts the save so the baseline is the **retained original** and edits apply as a **delta** onto it (via the MIT `Docxodus` `WmlComparer`), anchored by stable `w14:paraId` identity. It adds a grounding-tied confidence signal to AI suggestions, a credible editing toolset, and import of pre-existing Word revisions/comments.

**Theme: Fidelity.** All work on the MIT TipTap base — no TipTap product features (paid or unpaid); permissive licenses only (no AGPL).

## Quick Links

| Document | Description |
|----------|-------------|
| [Spec](./spec.md) | AI-optimized implementation spec (FR-01→26, NFR-01→10) — **source of truth** |
| [Design](./design.md) | Code-grounded design + July-2026 research + 6 passed pre-spec spikes |
| [Plan](./plan.md) | Phase/track WBS + critical path + parallel groups |
| [Task Index](./tasks/TASK-INDEX.md) | Task roster, dependencies, parallel groups, status |
| [OOXML fidelity findings](./ooxml-fidelity-findings.md) | E1/E2/E3 verdicts (grounded 2026-07-14) |
| [Seed README](./notes/seed-README.md) | Original 2026-07-01 seed (Scope Areas A–J) — superseded by spec |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development (pipeline-initialized) |
| **Progress** | 0% (0 tasks complete) |
| **Target Date** | — |
| **Completed Date** | — |
| **Owner** | Ralph Schroeder |

## Problem Statement

The moment the user edits anything, Save rebuilds the entire `.docx` from the editor's simplified view — so headers, footers, firm styles, multi-level clause numbering, hyperlinks, and embedded objects are silently dropped, even for the 99% of the document the user never touched. For a 40-page contract with a numbered clause hierarchy, a firm letterhead, and a signature block, this is make-or-break — the root of both the R1 UAT complaint ("Word loses its formatting on save") and R2 redline-fidelity pain.

## Solution Summary

Invert the save baseline: the persisted document is derived from the **load-time original OOXML** (fetched by SPE `versionId`), not a TipTap reconstruction. Only edited paragraphs (keyed by `w14:paraId`) are rebuilt and spliced into a copy of the original; `Docxodus` `WmlComparer` synthesizes the minimal `w:ins`/`w:del`. `docx.js` is dropped from the export path. `w14:paraId` becomes the stable anchor (carried through TipTap via MIT `@tiptap/extension-unique-id`), with the existing fuzzy re-anchor retained as the cross-Word-session fallback. AI redlines/comments continue via the existing `DocxAnnotationWriter`. E3 adds a server-derived grounding-tied confidence band; the toolset and import round-trip ride the same substrate.

## Graduation Criteria (G-R3 — browser-verified on spaarkedev1)

The project is **complete** when:

- [ ] Open a **formatted contract** (letterhead header/footer, multi-level clause numbering, custom styles, a table) in Compose — renders with structure intact.
- [ ] Edit **3 clauses** (incl. one bold/italic run change) and **accept one AI redline** — edits show as tracked changes; the AI redline shows rationale + a grounding-tied confidence band.
- [ ] Exercise the **toolset**: a find/replace and a table edit in the same session — both work; tracked-changes marks intact.
- [ ] **Save**, then **reopen** — header/footer/numbering/styles/table intact; the 3 edits + accepted redline present as tracked changes; each redline anchored to the correct paragraph by `paraId`.
- [ ] A doc that **already has Word revisions/comments** opens with them rendered in-editor and preserved across save (import round-trip).
- [ ] BFF publish ≤ 60 MB with Docxodus + SkiaSharp-excluded; no new HIGH CVE.
- [ ] ADR-013 NetArchTest green; through-the-wire slice test proves untouched OOXML preserved on a dirty save.
- [ ] Real-template hardening (NFR-09) passed before the delta-save cutover.

## Scope

### In Scope
- **E1** — Retained-original delta save (drop `docx.js`; adopt `Docxodus` `WmlComparer`).
- **E2** — `w14:paraId` identity (server pre-parse/mint; TipTap carry; paraId-primary anchoring).
- **E3** — Grounding-tied confidence band + formatted AI insertions (additive `ComposeDraftPayload`).
- **Editing toolset** — find/replace, basic tables, sticky toolbar, one-line bubble menu, dismissible simplification warning, styles pane (apply existing), richer comment-thread UI.
- **Import round-trip** — read existing `w:ins`/`w:del` + `w:comment` on Load; preserved across save.

### Out of Scope
- Any **TipTap product feature** (paid or unpaid); AGPL code.
- New AI dispatch endpoint (ADR-039) or new AI catalog rows — **engine frozen**; E3 is server-derived.
- Recreating Word: pagination, footnote/endnote numbering, complex numbering *authoring*, cross-reference/TOC computation, print fidelity, full style *management* → deferred to Open-in-Word.
- Multi-user co-editing / CRDT (R5+).
- True byte-identity of untouched content (Approach B splice-back) — MVP accepts WmlComparer cosmetic re-serialization (Approach A).

## Key Decisions

| Decision | Rationale | Ref |
|----------|-----------|-----|
| E1 = hybrid retained-original + Docxodus redline + existing writer (D1) | Untouched paragraphs preserved; maximum reuse of R2 annotation pipeline | design §0/§4.2 |
| Full R3 scope: fidelity core + E3 + toolset + import (D2) | Owner directive; fidelity core sequences first, import + toolset parallel | design §0 |
| E3 = grounding-tied qualitative band, rationale-first (D3) | 2026 HCI research: numeric scores drive over-reliance | design §6.2 |
| Text + run-level formatting fidelity (D4) | WmlComparer Format-Change Detection | design §0 |
| Baseline = load-time SPE version by `versionId` (S4) | Authoritative, refresh-safe, zero new storage | design §4.3 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Docxodus publish-size / SkiaSharp pull-in | Med | Low | Exclude SkiaSharp assets (S3 validated 2.44 MB net); never call `HtmlToWml`/`FormattingAssembler`; measure per BFF task |
| WmlComparer fails on real firm templates | High | Med | NFR-09 hardening gate — re-run S1/S1b on 2–3 real templates before delta-save cutover |
| `SpeFileStore` cannot fetch a version's content (FR-06) | Med | Low | Validate the Graph route early (Phase 0); fallback = Redis cache of load-time original |
| Hot-path overlap with `spaarkeai-compose-r2` / `spaarke-ai-architecture-redesign-r2` | Med | Med | Consume `PublicContracts` seams, no fork of `Services/Ai/`; `/conflict-check` before each BFF PR; confirm R2 merged/frozen before cutover |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| R1 + R2 merged to master | Internal | Ready | Compose service/layout/endpoints, native-OOXML writer/reader, slice-safe marks present |
| `Docxodus` 7.1.0 (NuGet, MIT) | External | Ready | Redline engine; SkiaSharp excluded |
| `@tiptap/extension-unique-id` 3.28.0 (npm, MIT) | External | Ready | paraId carry/minting |
| SPE driveItem version-content fetch (Graph) | External | Validate | Small `SpeFileStore` addition (FR-06) |
| `.NET 10` SDK (Docxodus target) | External | Ready | Verified S1 |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Ralph Schroeder | Overall accountability, UAT |
| Developer | AI Agent (Sonnet 5 / Opus 4.8 per task tier) | Implementation |
| Reviewer | code-review + adr-check gates | Code + ADR review |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-07-16 | 1.0 | Pipeline-initialized: canonical README, plan, tasks generated | project-pipeline |

---

*All work on the MIT TipTap base — no TipTap product features; permissive licenses only (no AGPL). Source of truth: [`spec.md`](./spec.md).*
