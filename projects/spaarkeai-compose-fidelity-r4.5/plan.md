# Spaarke Compose — Legal Fidelity (R4.5) — Implementation Plan

> **Source**: [`spec.md`](spec.md) · **Design**: [`design.md`](design.md)
> **Created**: 2026-07-28 · **Status**: Ready for execution
> **Branch**: `work/spaarkeai-compose-fidelity-r4.5` (already created; feature branch)

## Architecture Context

R4.5 builds **entirely on R4's existing projection + `paraId` machinery** — no new architectural paradigm, no byte-author changes. It extends `ComposeDocxProjectionBuilder` + `ComposeDocxProjection` (read side) and rewires the client mount so **one reader** (the server projection) serves every entry path.

### Discovered Resources

**Applicable ADRs** (read-side / BFF-hygiene relevant):
- **ADR-040** (session ledger / browse client-only) — WS-1 browse must not author bytes server-side; resolved via read-only stateless `project` endpoint (Tension T-2).
- **ADR-013** (AI facade) — WS-4 exposes data *to* the analysis layer via the projection contract; no `IOpenAiClient`/executor injection into `Services/Compose/`.
- **ADR-007** (`SpeFileStore` boundary) — projection stays `byte[]`-in/projection-out; no `Microsoft.Graph` above `SpeFileStore`.
- **ADR-038** (testing strategy) — fidelity harness = integration/seam vertical slice; KEEP-path tests; no `Mock<HttpMessageHandler>`/DI-registration/ctor-null tests.
- **ADR-039** (grounded execution, engine frozen) — R4.5 adds no AI dispatch; engine untouched.

**Canonical implementations to follow / extend:**
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs` — single doc-order walk (`:18-26`); `ListInfo`/`ResolveOrdered` (`:779-814`); `AppendEscaped` (`:689-693`); `w:noBreakHyphen` (`:702-703`); `w:tab` (`:695-698`); `AppendAlignment` (`:816-830`).
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs` — **write-side numbering mirror** (`LevelText`/`StartNumberingValue`) WS-3 must agree with.
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` (`:913-920` upload; `:1797-1804` record) — upload endpoint WS-1 extends.
- `src/server/api/Sprk.Bff.Api/Services/Compose/ParaIdPreParser.cs`, `AnnotationReanchorService.cs`, `DocxAnnotationReader.cs` — the `paraId` + doc-order-index re-anchor layer WS-4 extends.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` (`:1667`/`:1718`) — mammoth fallback branch WS-1 deletes.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` (`~:1891-1983`) — upload/browse client effects.
- `tests/fixtures/compose-corpus/` + `ComposeDocxProjectionBuilderTests.cs` — R4 corpus + projection test suite (no alignment/ordered-list/symbol tests today).

**Applicable skills:** `bff-deploy`, `code-page-deploy`, `conflict-check` (before every BFF PR), `ui-test` (WS-1/WS-3 render verification), `test-diet` (wrap-up).

**Coordination (hot-path):** `Services/Compose/**` overlaps `spaarkeai-compose-r1/r2/r3/r4` and `spaarke-ai-architecture-redesign-r2`. R4 merged to master 2026-07-24 (already in this branch). **`parallel-safe: false` on all `Services/Compose/` tasks; run `/conflict-check` before every BFF PR.** Open PRs to watch: **#690** (LFS corpus fixtures — coordinate WS-5 corpus adds), **#266** (`DocumentFormat.OpenXml` 3.4.1→3.5.1).

## Success Criteria (from spec)

1. One reader — `mammoth` zero Compose call sites (grep-proven).
2. Text-exact — character-for-character; `w:sym`/`w:cr` represented-or-warned; indentation preserved.
3. Numbering-exact — identical to Word (golden harness), incl. interrupted / multi-level / style-linked.
4. Referenceable — `paraId → {computed number, level}`; resolves "Section 4.2" / "4.2(b)(iii)" / "Sections 4–7"; survives edits.
5. Page/line honest — WS-5 decision + measured divergence; no over-claim.
6. Hygiene — build + suite + fidelity harness green; publish ≤60 MB; NetArch green; no new HIGH CVE; `/conflict-check` clean.

## Phase Breakdown

### Phase 0 — Foundation & characterization (unblock the harness)
- **001** Extend fidelity corpus with legal-numbering exemplars (NDA interrupted-clause, heading-style numbering, multi-level 1/1.1/1.1.1, `w:sym` §, line-numbered pleading) as LFS fixtures + manifest.
- **002** Extend the read-fidelity harness: **text-exactness** assertion (source runs == projected text, per paragraph) + **numbering-exactness** golden-value assertion scaffold; capture the *current* (broken) behavior as characterization baselines so regressions are visible.

### Phase 1 — WS-1 One reader everywhere (absorbs R5 G6) — the unblock
- **010** Upload path returns a projection — extend `POST /api/compose/upload` to run bytes through `ComposeDocxProjectionBuilder` and return `ComposeServerProjection`; client upload effect hydrates `projection`.
- **011** New stateless `POST /api/compose/project` (bytes → projection, no persist) for browse-local `.docx`; client browse effect hydrates `projection` (Tension T-2 — read-only, no authoring).
- **012** Open-in-Compose transient drafts projected server-side before mount (`mountTransient` no longer sets `projection: null`).
- **013** Delete the `mammoth` fallback branch in `ComposeEditor.tsx` + `docxToTipTapHtml` in `docxBridge.ts`; grep-prove zero Compose `mammoth` call sites (repo dep stays for SprkChat/Notepad).
- **014** (deploy) Deploy + UAT WS-1 — verify centered titles / alignment / tabs / comments / tracked-change recovery now correct on upload + browse.

### Phase 2 — WS-2 Harden the projection read (F-1)
- **020** Stop silent drops: `w:cr` → `<br>`/separator; `w:sym` → Unicode map (Symbol/Wingdings → §, etc.) or visible placeholder **+ warning**; intra-run glyph-loss warning mechanism (today's guard counts paragraphs, not glyphs).
- **021** Emit `w:ind` (left/first-line/hanging) as margin styles; apply `white-space:pre-wrap` on the editor surface for preserved whitespace.
- **022** Full OOXML run/block construct audit (enumerate + represent-or-warn each) + add the missing alignment / ordered-list / symbol projection tests.

### Phase 3 — WS-3 Deterministic numbering reconstruction (F-3) — the flagship
- **030** Numbering-model reader — parse `numbering.xml` (`w:num`→`abstractNumId`→`abstractNum`, per-level `w:numFmt`/`w:lvlText`/`w:start`/`w:lvlRestart`/`w:isLgl`/`w:lvlOverride`/`w:startOverride`) + **style-linked numbering** (pStyle carrying `w:numPr`).
- **031** Numbering computation engine — single doc-order walk, counter per `(abstractNumId, level)`, apply start/restart/override, format each level (decimal/lowerLetter/upperLetter/lowerRoman/upperRoman/legal), compose multi-level "4.2.1" + sub-item depth via `w:lvlText`.
- **032** Render the computed label as an **explicit non-editable number-atom** on the paragraph node (TipTap); the editor never relies on browser `<ol>` auto-count for a legal number. Read-time only — no auto-renumber on edit (R5 G3).
- **033** Round-trip agreement test — author via `ComposeDocumentRenderer` → read via WS-3 → identical labels.
- **034** (deploy) Deploy + UAT WS-3 — NDA/corpus numbers render identical to Word (golden harness green; no "1." collapse, no dropped heading numbers).

### Phase 4 — WS-4 Reference / citation layer (F-4)
- **040** Extend `ComposeDocxProjection` / `ParaIdMapEntry` with `computedNumber`, `numberingLevel`, `listPath`, `headingLevel` (+ existing `docOrderIndex` + `paraId`).
- **041** Persist the `paraId → legal-number` map **both** in the projection payload **and** the document session ledger; survives edits (stable `paraId`; new/split re-anchor per R4).
- **042** Citation resolver — resolve single label ("Section 4.2"), **sub-item depth** ("4.2(b)(iii)"), and **contiguous ranges** ("Sections 4–7") ↔ `paraId`(s); expose to the analysis/citation tool.

### Phase 5 — WS-5 Page/line numbering: research + decision (F-5) — parallel spike, can start early
- **050** LibreOffice-headless pagination prototype → page/line map; measure divergence from Word on the corpus (incl. the line-numbered pleading fixture).
- **051** Evaluate the Word-rendering-service (Graph/Office) path — ops/licensing/latency; NFR-03 licensing analysis (permissive-only; separate-process, not linked).
- **052** Decision record — ship-in-R4.5 vs fast-follow; write `notes/ws5-pagination-decision.md`. No page/line "100%" claim beyond the chosen engine's guarantee.

### Phase 9 — Wrap-up
- **090** Project wrap-up — README status → Complete; `notes/lessons-learned.md`; `/test-diet` reconciliation; publish-size final report; archive.

## Parallel Execution Strategy

Because nearly every task edits shared `Services/Compose/` files, **most tasks are `parallel-safe: false`** and run sequentially within a phase. WS-5 (research/notes) is the exception and can run in parallel with Phases 1–4.

| Group | Tasks | Prerequisite | parallel-safe | Notes |
|---|---|---|---|---|
| W0 | 001, 002 | none | true (different files) | Corpus fixtures + harness scaffold |
| W1 | 010 → 011 → 012 → 013 | 002 | **false** | Sequential — shared `ComposeEndpoints.cs` / `ComposeWorkspace.tsx` / `ComposeEditor.tsx` |
| W1-deploy | 014 | 013 | false | Deploy gate |
| W2 | 020 → 021 → 022 | 013 | **false** | Sequential — shared `ComposeDocxProjectionBuilder.cs` |
| W3 | 030 → 031 → 032 → 033 | 022 | **false** | Sequential — the flagship; shared builder + projection |
| W3-deploy | 034 | 033 | false | Deploy gate |
| W4 | 040 → 041 → 042 | 033 | **false** | Sequential — shared `ComposeDocxProjection` |
| W5 | 050 ∥ 051 → 052 | 001 (corpus) | true | Research spike — **can start early**, parallel to W1–W4 |
| W9 | 090 | all | false | Wrap-up |

**Critical path:** 002 → 010 → 011 → 012 → 013 → 020 → 021 → 022 → 030 → 031 → 032 → 033 → 040 → 041 → 042 → 090. WS-3 (030–033) is the flagship and the longest single stretch; WS-5 runs alongside.

## Timeline / Estimated Effort

~24 tasks across 6 phases + wrap-up. Read-fidelity work (WS-1..WS-4) is the bulk; WS-5 is a bounded spike. Rough order: WS-1 (unblock) small, WS-2 medium, **WS-3 large (flagship)**, WS-4 medium, WS-5 bounded research. Estimated effort surfaced per-task in each POML.

## References

- Spec: [`spec.md`](spec.md) · Design: [`design.md`](design.md)
- Root governance: `CLAUDE.md` §10 (BFF Hygiene), §11 (Component Justification), §6.5 (ADR Tensions)
- BFF checklist: `.claude/constraints/bff-extensions.md`
- Hot-path registry: `projects/INDEX.md`
- Sibling: `projects/spaarkeai-compose-r4/` (Shadow Document — the base R4.5 extends)
