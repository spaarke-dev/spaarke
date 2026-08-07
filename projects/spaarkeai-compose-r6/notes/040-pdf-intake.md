# Task 040 — PDF → canonical model (FR-06) — implementation record

> Commit: `5ae5a4246` · 2026-08-07 · FULL rigor (opus-tier task, executed on Fable 5)

## What was built

PDF becomes the canonical hub's **second intake source** by construction: the PDF is parsed once
(Azure DI `prebuilt-layout` — first caller of the layout model; `SemanticDocumentChunker` already
documented it as the sanctioned layout source), projected into the ONE `ComposeContentModel`, rendered
through the ONE `ComposeDocumentRenderer.SynthesizeDocument`, and from that point the load **IS a docx
load** — the entire existing pipeline (paraId ingest stamp, HTML projection, canonical re-projection,
annotation reader, session binding) runs unchanged on the synthesized bytes.

### Layering (extend, not fork)

| Layer | Change |
|---|---|
| `ITextExtractor` | additive `ExtractLayoutAsync` as a **default interface method** (Failed result) — zero breaking implementers/test fakes; `NullTextExtractor` overrides with the loud `FeatureDisabledException` (P3 posture) |
| `TextExtractorService` | real `ExtractLayoutAsync`: same config/size-limit/timeout/**shared circuit breaker** as the flat-text path; `prebuilt-layout`; `MapAnalyzeResultToLayout` (internal static): span-ordered paragraph+table interleave, **table-cell paragraph dedup** (layout reports cell text twice — as paragraphs AND cells) |
| `DocumentIntelligenceService` | `ParseDocumentLayoutAsync` — thin delegate, throw-on-failure (mirrors `ParseDocumentAsync`) |
| `DocumentParserRouter` | `ParseDocumentLayoutAsync` — router stays the single parse entry; routes to Azure DI ALWAYS (LlamaParse emits markdown text, not the structured contract; Azure DI natively OCRs scanned PDFs on layout) |
| `PublicContracts` | neutral `DocumentLayout` DTO (+ paragraph roles, table anchor cells) + `IComposePdfIntakeSource` facade; real impl `Services/Ai/ComposePdfIntakeSource` (never throws → null + loud log); `NullComposePdfIntakeSource` compound-OFF peer (§F.1, 032-F1 precedent) |
| `Services/Compose` | `ComposePdfModelProjector` — DocumentLayout → `ComposeCanonicalModelProjection`; `ComposeService.LoadAsync` PDF branch (extension **OR** `%PDF-` magic) + `LoadComposeDocumentResult.SourceFormat="pdf"` (additive, ADR-040) |
| DI | real facade inside the compound gate (AnalysisServicesModule, beside AddAiModule); Null peer in `AddNullObjectsForCompoundOff`; projector unconditional singleton in ComposeModule |

### Honest-lossiness mapping (`pdf-intake-*` warning codes)

- `pdf-intake-fixed-layout-reflowed` (count = source page count) — **always emitted**; a PDF projection
  is Partial at best BY DESIGN. Drives 041's banner copy.
- `pdf-intake-page-chrome-dropped` — pageHeader/pageFooter/pageNumber paragraphs dropped (repeat-per-page
  chrome would corrupt the flow-document body).
- `pdf-intake-footnote-inlined` / `pdf-intake-formula-flattened` — text preserved, apparatus not.
- `pdf-intake-list-approximated` — leading bullet glyphs (closed set •◦▪●‣·⁃) → bullet ListItems.
  **Numbered/dashed prose is deliberately NOT converted** — legal numbering ("1.2", "(a)") stays literal
  text; no fake auto-numbering.
- `pdf-intake-table-style-approximated` — tables emit in born-in-editor mode (Borders=null → renderer
  default chrome; PDF border styling not extractable). Row/column spans reconstructed: anchor GridSpan +
  vMerge Restart, synthesized Continue cells, analysis holes → empty cells (grid stays rectangular).
- `pdf-intake-empty` — the ONLY Failed outcome (nothing projectable; never mount an empty editor over a
  non-empty PDF). Unavailability (compound-OFF / parse failure) throws a clear message, never a DI 500.

### Judgment calls (the opus-tier decisions)

1. **Synthesize-at-intake**: rather than teaching the client/save-path a PDF mode, the branch converts
   PDF → docx at load; downstream invariants hold trivially. The save-side semantics for a `.pdf`-named
   SPE item (new docx document vs version) are **041's scope** — server contract exposes
   `SourceFormat="pdf"` for the client to key on.
2. **No synthetic page breaks**: reflow is honest; per-page breaks would fracture the flow document.
   Counted once via the reflow warning instead.
3. **Conservative list detection**: glyph-only. Mis-fired numbering conversion in legal text is worse
   than a literal glyph (operator principle: best fidelity on common cases, degrade loudly on rare).
4. **Router routes layout to Azure DI only**: LlamaParse's markdown output cannot satisfy the structured
   contract; documented in the router so future structured parsers slot in there.

## Placement Justification (root §10, Path A — stated for the PR)

PDF intake **extends the existing Azure DI intake path** (`DocumentIntelligenceService` +
`DocumentParserRouter` + `ITextExtractor`) — a managed service, no new NuGet, no binary weight
(NFR-01). The Compose-side projector lives in existing `Services/Compose/` (deterministic mapping, no
AI type). New surface = ONE PublicContracts facade (`IComposePdfIntakeSource`) + its Null peer —
justified per §11: (existing) no current surface exposes structured layout to CRUD consumers;
(extension) `IComposeTemplateSource` is storage/variables, not parse — wrong seam; (cost-of-nothing)
Success Criterion 2 (PDF opens in Compose) is unreachable without a facade, and direct
`DocumentParserRouter` injection into Compose would violate ADR-013.

## Gate evidence

- Build: 0 errors. Projector tests **10/10** incl. the hub round-trip
  (Project → SynthesizeDocument → BuildContentModel — proves "same canonical model as docx" end-to-end).
- Compose unit suite **384/384**. ADR-013 facade arch guards green; 4 arch-test failures
  (ADR-007 Graph ×2, ADR-010 ×2) are **pre-existing branch state** — verified none reference the new
  types (surfaces untouched by this task).
- Publish **47.00 MB compressed incl. PDBs** (Δ ≈ 0 vs 46.94–47.00 baseline; ceiling 60 MB).
- `dotnet list package --vulnerable --include-transitive`: clean.
- `/conflict-check`: soft-pass — no open PR overlaps the touched files (#690 is CI/seam-test surfaces;
  rest docs/deps); cross-worktree Compose contention governed by the project serialize rule.

## Step 9.5

Combined code-review + adr-check agent dispatched on `5ae5a4246`; findings + triage appended below.
