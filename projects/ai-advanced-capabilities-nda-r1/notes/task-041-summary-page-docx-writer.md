# Task 041 — Summary-Page DOCX writer (TL;DR + flagged overview + recommendations)

**Status**: complete · **Rigor**: FULL · **Tier**: sonnet @ xhigh

## Design decision: additive, NON-TRACKED content via `ComposeDocumentRenderer` (NOT `ComposeShadowPatchEngine`)

The Summary Page is SERVER-AUTHORED, FINAL content — not a proposed user edit. Routing it through
`ComposeShadowPatchEngine`'s operation log would emit tracked `w:ins` revisions (a pending suggestion a
reviewer would accept/reject), which is the wrong semantic for a system-generated appendix. Instead:

- **`ComposeSummaryPageGenerator`** (new, pure) builds the Summary Page as a plain `IReadOnlyList<ComposeBlock>`
  — the SAME content model `ComposeDocumentRenderer` already knows how to materialize (task 026/027,
  `RenderBlocks`). Every block is `ComposeBlockKind.Paragraph` with unstyled/unnumbered runs (bold only via
  `ComposeInlineRun.Bold`; a literal `"• "` prefix for the flagged-section overview, not a real `w:numPr` list
  item) — deliberately style/numbering-INDEPENDENT so the writer never needs to merge into (or collide with)
  the target document's own `StyleDefinitionsPart` / `NumberingDefinitionsPart`.
- **`ComposeDocumentRenderer.AppendSection(byte[] docxBytes, IReadOnlyList<ComposeBlock> blocks)`** (new
  public method) opens the existing package, detaches the body-level trailing `SectionProperties` (which
  OOXML requires to be the LAST direct child of `w:body`), appends a manual page break
  (`<w:br w:type="page"/>` in its own paragraph — deliberately NOT a new `w:sectPr` section break, to avoid
  forking page setup / clashing with the source doc's own section scheme), calls the EXISTING private
  `RenderBlocks` to materialize the Summary Page paragraphs, re-attaches the trailing `SectionProperties`,
  and mints fresh `w14:paraId`s (`AssignParaIds`, idempotent — every existing id untouched). `byte[]`-in /
  `byte[]`-out, purely additive; a no-op (empty blocks) returns the input unchanged.

### Why NOT the retired `DocxAnnotationWriter` / a new package
`DocxAnnotationWriter.cs` stays retired — untouched, unreferenced. No new NuGet package: `AppendSection`
reuses the already-referenced `DocumentFormat.OpenXml` 3.5.1 and the SAME private helpers
(`BuildParagraph`/`BuildRun`/`RenderBlocks`/`AssignParaIds`) `SynthesizeDocument` already uses — this is the
same authoring engine applied additively to an existing package instead of a blank one.

### Content model (derived from the ledgered result — no second LLM call)
`NdaReviewSummaryPageInput` / `NdaReviewFlaggedSectionInput` mirror the `nda-review` Action's closed
`outputSchema` EXACTLY (`infra/dataverse/actions/nda-review.action.json`:
`{overallRisk, flaggedSections[{sectionRef, quotedText, riskLevel, explanation, standardRef}]}`, task 020) —
`System.Text.Json.Deserialize<NdaReviewSummaryPageInput>` on the SAME ledgered JSON (task 023's
`{bindingId}@t{n}` store-before-render entry) reconstructs this type with zero remapping. Every sentence
`ComposeSummaryPageGenerator.Build` emits is a deterministic template/count over those fields:

- **TL;DR** — finding count + severity breakdown (Critical/High/Medium/Low) + `overallRisk` (or a
  "no material deviations" sentence when `flaggedSections` is empty).
- **Flagged Sections** — one line per finding: `[riskLevel] sectionRef — truncated explanation (Standard: standardRef)`.
  `quotedText` is carried on the input type for schema fidelity but NOT rendered here (kept concise; the
  verbatim excerpt lives in the advisory comment thread, tasks 031/040).
- **Recommendations** — a fixed template keyed off `overallRisk` (Critical/High/Medium/Low-or-clean).
- A one-line "AI-generated advisory ... not legal advice" caption.

No `IOpenAiClient` / executor / routing type appears anywhere in `ComposeSummaryPageGenerator` or
`ComposeDocumentRenderer.AppendSection` — by construction there is no model call in this path.

## Wiring

`IComposeService.cs`: new optional `SaveComposeDocumentRequest.SummaryPage` (`NdaReviewSummaryPageInput?`).
`ComposeService.SaveAsync`: when non-null, calls `ComposeSummaryPageGenerator.Build` +
`_documentRenderer.AppendSection(contentToPersist, summaryBlocks)` AFTER the existing operation-log/comment
patch-engine block and BEFORE the SPE upload steps — so the Summary Page always lands as the true tail of
what gets persisted, regardless of whether the save also carried edits. Null/absent (every non-NDA-REVIEW
Compose save) → unchanged behavior; zero new dependency (`_documentRenderer` was already injected).

**Sequencing note vs task 040**: 040 ("comment-export wiring fix") is a **client-side** fix per its own task
notes — "server side WORKS + is tested... the GAP is client-side" (wrong field/shape sent to the ALREADY
working `ComposeShadowPatchEngine.ApplyComment` path). It should require zero changes to the `SaveAsync`
block this task added. This task was sequenced ahead of 040 on operator instruction; verified no functional
collision (040 touches the client save payload + the EXISTING comment-application code path, not the new
Summary Page block).

## Deliverable
`tests/integration/seam/Compose/ComposeSummaryPageSeamTests.cs` — 8 tests against the real
`tests/fixtures/compose-corpus/` fixtures (same corpus `ComposePatchEngineSaveSeamTests` /
`ComposeShadowPatchEngineByteDiffSeamTests` use — no new fixture, §11 reuse). Proves:
- Well-formed OOXML after append (SDK re-opens the result; every original paragraph still resolves).
- A manual page break (`w:br type="page"`) precedes the appended content.
- TL;DR / flagged-section overview / recommendations text is present, derived from the hand-authored
  ledgered-shape JSON (no LLM call anywhere in the test or the production types under test).
- The trailing `SectionProperties` remains the LAST body child, and there is still exactly ONE (never a
  second section).
- Appended paragraphs carry unique, minted `w14:paraId`s; existing paragraph `OuterXml` is BYTE-IDENTICAL
  (the "untouched content stays untouched" I-4 spirit).
- Every OTHER package part (styles, numbering, headers/footers, theme, media, ...) is byte-identical —
  reuses the existing `ComposeOoxmlPackagePartComparer` (task 004), the same NFR-01 instrument
  `ComposePatchEngineSaveSeamTests` / `ComposeShadowPatchEngineByteDiffSeamTests` use (§11 reuse — added
  after self-review per code-review Step 5.5 to match the sibling seam tests' rigor).
- Zero `w:ins` tracked-change marks (this is final content, not a proposed edit).
- A clean-NDA (zero findings) ledgered result still yields a positive "no material deviations" summary.
- An empty block list is a byte-identical no-op passthrough (mirrors `ComposeShadowPatchEngine.Apply`'s
  contract).

8/8 green. Full Compose seam suite (52 tests across `tests/integration/seam/Compose/`) green — no
regression. 525/525 tests matching `~Compose` across the unit test project green.

## Quality Gates (Step 9.5)

- **code-review**: no Critical/Warning findings. No new interface-with-single-implementation, no
  try/catch-log-rethrow, no code-restating comments. Null-checks on the new public entry points
  (`ComposeSummaryPageGenerator.Build`, `ComposeDocumentRenderer.AppendSection`) mirror the EXISTING
  `ArgumentNullException.ThrowIfNull` / `is null` precedent already used by the sibling
  `ComposeShadowPatchEngine.Apply` for the same class of public byte[]/model-in entry points — not a new
  pattern. No new NuGet, no new endpoint, no new DI registration.
- **adr-check**: ADR-013 (facade boundary) / ADR-007 (Graph isolation) / ADR-010 (DI minimalism, stateless
  singleton) — compliant, no new interface, no AI-internal type touched. **ADR-049 tension surfaced and
  resolved (Path C — comply, not a violation)**: `AppendSection` deliberately does NOT route through
  `ComposeShadowPatchEngine` — D5's "single unified byte-author" rule governs the EDIT/annotation write
  path (tracked `w:ins`/`w:del`/`w:comment` over EXISTING content); the Summary Page is NEW, server-authored
  FINAL content, which is exactly the use case ADR-049 already assigns to the SIBLING engine
  `ComposeDocumentRenderer` (the born-in-editor whole-document author, task 026/027). Appending via the
  same engine's `RenderBlocks` machinery is a correct, in-scope use of the ADR's own second sanctioned
  engine, not a bypass of the first. ADR-038 (testing) — new test lives at the correct
  `tests/integration/seam/Compose/` KEEP path; no banned antipattern (B1-B17) present; reuses the existing
  corpus fixture + package-part comparer per §11.
  

- **Placement Justification**: this is server-side OOXML authoring for the Compose save path
  (`Services/Compose/`) — the SAME zone `ComposeDocumentRenderer`/`ComposeShadowPatchEngine` already own;
  no new endpoint, no new DI registration (both engines were already injected into `ComposeService`), no new
  package.
- **Publish size**: `dotnet publish -c Release` compressed (zip of `deploy/api-publish/`, incl. PDBs) =
  **51.29 MB** vs the **47.49 MB** last-recorded project baseline (current-task.md, post-task-023) — **delta
  +3.80 MB**. This worktree carries other already-landed-but-uncommitted parallel-wave work (022 bindings,
  031 advisory-comments receiver wiring, 042 test-only, plus in-flight 011/013/020 subagents) whose IL is
  included in this measurement; task 041 itself adds only two small server files (`ComposeSummaryPageGenerator.cs`,
  the `AppendSection` method + one field) — a few KB of IL, not multiple megabytes. Well under the 60 MB HARD
  ceiling and the 55 MB cumulative-review flag either way. No new NuGet package (uses the already-referenced
  `DocumentFormat.OpenXml` 3.5.1). No new HIGH CVE surface (no package graph change).
- **Hot-path**: BFF touched = YES (`Services/Compose/ComposeDocumentRenderer.cs`, new
  `ComposeSummaryPageGenerator.cs`, `Services/Compose/ComposeService.cs`, `Services/Compose/IComposeService.cs`);
  SpaarkeAi = N; ci-workflows = N; skill-directives = N; root-CLAUDE = N.

## Files changed
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeSummaryPageGenerator.cs` (new)
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocumentRenderer.cs` (added `AppendSection`)
- `src/server/api/Sprk.Bff.Api/Services/Compose/IComposeService.cs` (added `SaveComposeDocumentRequest.SummaryPage`)
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs` (wired `SummaryPage` in `SaveAsync`)
- `tests/integration/seam/Compose/ComposeSummaryPageSeamTests.cs` (new, 8 tests)

## Follow-ons / notes
- Works with 040 (comment baking) to produce the fully-annotated export — both consume the same
  `contentToPersist` pipeline in `SaveAsync`, applied in sequence (comments/operation-log first, Summary
  Page last), so a save that both bakes comments AND appends the Summary Page in the same request is already
  supported by construction.
- No client wiring in this task (out of scope per hard rule — `Spaarke.Compose.Components`/`SpaarkeAi` client
  files are owned by parallel tasks). The client caller (a future task, e.g. 030's review panel or a
  dedicated "export" action) supplies `SummaryPage` on the Save request once it has the ledgered
  `{overallRisk, flaggedSections[]}` result in hand — no server change needed for that wiring.
- The 128 KB inline-ledger-payload cap flagged by task 023's notes (worst-case `maxItems: 50` findings) is
  the SAME upstream concern for this writer's input — if the ledger entry were ever truncated, the Summary
  Page would summarize the truncated (not full) finding set. No code change here; same deploy/eval-gate
  follow-on task 023 already flagged (060/050).
