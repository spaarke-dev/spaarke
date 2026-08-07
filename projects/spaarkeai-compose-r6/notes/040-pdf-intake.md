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

## Step 9.5 — verdict PASS-WITH-FINDINGS; ALL HIGH/MEDIUM fixed same-session

Review agent (combined code-review + adr-check) ran on `5ae5a4246` + `2d72046aa`. Compliance
verifications all clean (ADR-013/039/007, §F.1 incl. no-double-registration, thread-safety,
additivity). The "SourceFormat never crosses the wire" gap was found independently by BOTH the main
session (fixed pre-emptively in `2d72046aa` during the 041 survey) and the reviewer — the trailing-
optional placement was verified safe (single named-argument construction site).

### Findings → triage (fix commit `f0f9a34ec`)

| # | Finding | Resolution |
|---|---|---|
| HIGH-1 | Compound-OFF / failed PDF load surfaced as generic 500 (the endpoint catch-all swallowed the clear `InvalidOperationException` messages) | **FIXED** — `ComposePdfIntakeException` (Unavailable discriminator) + typed catches in the Load handler and `ExecuteSaveAsync` → 503 (intake unavailable) / 422 (document not projectable) ProblemDetails carrying the real message; catches placed BEFORE the InvalidOperationException catches (derivation order) |
| HIGH-2 | SaveAsync could write docx bytes over the `.pdf` SPE item (040 made a PDF session reachable; no server-side guard) | **FIXED** (server leg) — `GuardBaselineIsNotPdf` sniffs every resolved baseline at the `ResolveSaveBaselineAsync` choke point → typed 422. **041 (client leg, `c73055d33`) eliminates the mainline**: PDF-sourced docs route create-on-save exclusively. Residual: a rogue client posting docx bytes to a `.pdf` item's replace route with a valid docx baseline — accepted (indistinguishable from a legitimate save; the item's bytes were never PDF at that point) |
| MED-3 | Load returned the PDF item's VersionId (post-refresh save booby trap: re-fetch hands `%PDF-` to the OOXML engine) | **FIXED** — PDF loads skip the version-id lookup entirely (null → retained-bytes fast-path / create-on-save routing); the HIGH-2 guard also backstops it |
| MED-4 | Overlapping/duplicate table anchors dropped cell text silently; out-of-order anchors could leave a Restart without Continue | **FIXED** — reading-order anchor processing + overlap text CONSOLIDATED into the covering cell (new `pdf-intake-table-cell-consolidated` counter); Slot-based grid rebuild with owner tracking |
| MED-5 | Extension won over magic bytes: a docx misnamed `.pdf` took the lossy reflow path | **FIXED** — bytes first (`%PDF-` → PDF; `PK\x03\x04` → docx), extension is tiebreak only |
| MED-6 | The trickiest logic (`MapAnalyzeResultToLayout`) untested; projector missing adversarial cases | **FIXED** — +2 mapper tests via `DocumentIntelligenceModelFactory` (cell-paragraph dedup; role/offset interleave) + 4 projector adversarial tests (overlap consolidation, out-of-order anchors, oversized-span clamp, bare glyph). Suite 390/390 |
| LOW-7 | Intake warnings dropped when the synthesized-docx re-projection failed | **FIXED** — unconditional merge |
| LOW-8 | Failed-empty projection discarded its diagnostic counters | **FIXED** — counters ride on the Failed result; test locked in |
| LOW-9 | ~4 concurrent in-memory PDF copies on the intake leg (bounded by MaxFileSizeBytes) | **DEFERRED** → follow-ups ledger (MemoryMarshal.TryGetArray / BinaryData.FromStream would trim two) |
| LOW-10 | Cause-collapsing at the facade's null boundary (circuit-open vs timeout vs corrupt all → one message) | **DEFERRED** → follow-ups ledger (discriminated facade result; ties into the exception-type seam HIGH-1 introduced) |

## Task 041 (client wiring) — delivered same-session as `c73055d33`

`sourceFormat` lifecycle through the reducer (set at load + transientKey minted; cleared by every
fresh mount and by saveSucceeded, which also re-targets documentRef to the new docx identity,
re-baselines versionId from the save response, and swaps fileName `.pdf`→`.docx`); triggerSave routes
EVERY pdf-sourced save through the EXISTING create-on-save (never the replace URL; display name
`.docx`-swapped; the G7 transientKey dedups repeated saves onto ONE new record); "Opened from PDF"
info banner (Fluent v9 semantic tokens; per-mount dismissal — every fresh PDF open re-warns; no
"identical to source" claim; version-history safety net + new-Word-document expectation stated) +
friendly copy for the six `pdf-intake-*` codes. Deviation from the POML's file list: `ComposeEditor.tsx`
needed NO change — the synthesize-at-intake design mounts the PDF-derived model through the standard
docx pipeline (`docxBridge.ts` untouched by construction). Client evidence: tsc clean; reducer+banner
37/37; renderOnSave save-contract 11/11; 5 failing jest suites proven PRE-EXISTING at HEAD via stash
bisect (`bornInEditorSave` fails identically without these changes — mock/env failure class, plus the
known `stepOperationInterceptor` baseline). Step 9.5 review for `c73055d33` + triage-verification of
`f0f9a34ec` dispatched; result recorded in the 041 section of the close-out.

## 041 Step 9.5 — verdicts PASS-WITH-FINDINGS (both commits); triage in `48d17ac31`

The review verified all seven 040-triage fixes correct as-scoped, the full client state machine
(both save shapes create a NEW docx; the replace URL unreachable while PDF-sourced; saveFailed
retains the dedup key; the second-save posture matches the existing imported-transient flow — NOT a
staleness bug), ADR-021/ADR-040 compliance, and the eight-site non-PDF no-op audit. Findings → triage:

| # | Finding | Resolution (`48d17ac31`) |
|---|---|---|
| A-HIGH-1 | Version skew (pre-041 client + new BFF) could replace-save the synthesized docx over the `.pdf` item — baseline guard passes (bytes are valid docx), TARGET unchecked | **FIXED** — replace path refuses a `.pdf`-named target at the existing metadata choke point (typed 422, zero extra Graph calls) |
| A-MED-1 / B-MED-2 | Apply-template fully exposed to PDF mounts (server: %PDF- bytes → deep OOXML 500; client: button enabled) | **FIXED both legs** — server sniffs the downloaded bytes → typed 422 + endpoint catch; client disables with the same honest reason |
| B-MED-1 | Open-in-Word on a PDF doc opens the WRONG document (the C3 "id stable across flush" invariant breaks — the flush IS a create-on-save) | **FIXED** — Word actions disabled while PDF-sourced; re-enable after the first save (new docx identity) |
| A-MED-2 | Span-coverage collision over-widened rows (invalid Word grid) | **FIXED** — anchors clamp to the contiguous free run; S1 invariant test (sum(GridSpan)==columnCount) |
| A-LOW-1 | Out-of-grid anchor text counted as "consolidated" but actually dropped | **FIXED** — own `pdf-intake-table-cell-dropped` code + client copy |
| B-LOW-1/2/4 | triggerSave deps; missing banner copy for the table codes; fileName-undefined asymmetry | **FIXED** all three |
| B-MED-3 | New docx lands in the BU container (not the source PDF's container/matter) with no parent association | **DEFERRED — DECISION POINT for 042/operator UAT**: current behavior = the established create-on-save flow (BU container). Options: create in the source item's drive; or accept + document. Needs owner call |
| A-LOW-2 | Intake warnings reach the wire but never DISPLAY in the op-log-fallback case | **DEFERRED → 042** |
| B-LOW-3 | Redundant-but-defensive versionId re-baseline branch | **KEPT**; 042 pins the invariant with a test |

### 042 test plan (from the review — binding scope for task 042)
Reducer lifecycle (set/mint/clear/re-target/fileName-swap incl. `.PDF`/no-extension/undefined;
saveFailed retention; older-BFF omission), flow tests (dirty→Shape-2 create; clean→Shape-3
passthrough; second save→replace on the NEW item; forkNew fresh key; retry same key; NEGATIVE: never
`/documents/{pdfId}/save`), banner tests (render/dismiss/re-warn/retire; no "identical to source"),
server endpoint tests (replace-refusal on `.pdf` target; apply-template 422; 503-vs-422 mapping),
B-LOW-3 versionId invariant, A-LOW-2 display fix.

### Follow-ups ledger additions (→ notes §23 ledger)
- LOW-9 buffer-copy trim on the intake leg; LOW-10 discriminated facade result (cause preservation).
- Pre-existing client jest failures (4 × ComposeWorkspace suites "Element type is invalid" + timeouts
  under full-suite load) — owning-project fix candidate; NOT introduced by 040/041 (stash-bisect proof).
- 042 must add reducer-lifecycle tests for `sourceFormat` (set/clear/save-retarget) per the review scope.
- Operator UAT items for 041 (dark-mode ui-tests are manual): PDF opens with the notice in light+dark;
  edit → save creates a NEW .docx document (original PDF untouched); second save updates that docx.
