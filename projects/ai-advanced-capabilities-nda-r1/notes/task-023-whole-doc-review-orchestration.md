# Task 023 — Whole-doc NDA review orchestration (Action run → disposition fan-out)

**Status**: complete · **Rigor**: FULL · **Tier**: opus · Base HEAD: `0fcf69322`

## Design decision: single prompted Action + client-side disposition fan-out (NO coded workflow, NO new production code)

Tracing the dispatch spine (`SessionDispatchOrchestrator.DispatchAsync` → `ActionRunner` →
`OutputRouter.RouteAsync`) shows the **existing generic spine already expresses the required fan-out**:

1. Runs the prompted Action **once** (`ActionRunner.RunAsync` → single `IOpenAiClient` call).
2. Writes the universal ledger entry **before any render** (`OutputRouter.RouteAsync`, ADR-040 store-before-render;
   key `{bindingId}@t{n}`).
3. Emits the full `{overallRisk, flaggedSections[]}` payload **verbatim** on the terminal `complete` chunk —
   `SessionDispatchOrchestrator.BuildResultChunk` routes any non-DocumentAnalysisResult object through
   `AnalysisChunk.CompletedRaw` (the pass-through path that already names `overallRisk` as a handled shape),
   rendering **from the stored entry** (`ProgressiveRenderGuard.EnsureStored`).

Both client dispositions derive from that **one ledgered result**:
- **(a) Summary payload** (task 030 review panel / Assistant) — from the terminal `complete` chunk.
- **(b) Advisory-comments payload** (task 031) — a **client-derived projection** of the *same* ledger entry's
  `flaggedSections[]`, materialized by a lightweight client event (the ADR-040 "derived-views" pattern), **not**
  a second routable server disposition and **not** a second LLM call.

### Why NOT a coded workflow (ADR-039 composite rule + §11)
NDA-REVIEW is a **single capability with two client views**, not an ADR-037 **composite** (which composes N
Action node outputs into one section-keyed payload). ADR-039's "author new composite capabilities as coded
workflows" therefore does not fire. §11 reuse-first forbids adding a `NdaReviewWorkflow` when the existing spine
already carries the one-run→two-payloads contract. Adding one would smuggle per-capability branching back onto the
dispatch seam (the r7 anti-pattern ADR-039 deletes). **Decision = single-Action-fan-out.**

### Recommended Binding shape for task 022
- `sprk_disposition = informational` — the review is **read-only advisory** (not `compose`/edit; not `overlay`,
  which is not-yet-routable and would require a new side-effect mechanism the task deliberately avoids by using a
  client event path for comments).
- `sprk_actionkind = Prompted`; `sprk_consumertype = nda-review` (carried on the terminal chunk so the client
  routes BOTH views); risk `None`.

## Deliverable
`tests/integration/seam/Ai/NdaReviewFanOutSeamTests.cs` — vertical-slice seam test (ADR-043 DoD), production types
(orchestrator/ContextBinder/ActionRunner/OutputRouter/ChatSessionManager) real; only the LLM + catalog data
boundaries doubled. Proves:
- **one run → both payloads / no second LLM call** — `CallCount == 1`.
- **store-before-render** — stored entry `{bindingId}@t1` present; terminal chunk payload == stored payload.
- **summary payload** — terminal chunk carries `overallRisk` + `flaggedSections[]` verbatim; disposition
  `informational`; consumerType `nda-review`.
- **comments payload** — re-read ledger `flaggedSections[]` (5 citation fields each) is byte-identical to the
  summary's `flaggedSections[]` → both views, one source.
- High/Critical attorney-review signal survives the ledger round-trip; clean-NDA (empty findings) still
  ledgers-once and renders.

3 tests, all green. No production `.cs` changed.

## §10 BFF Hygiene
- **Placement Justification**: this belongs in the BFF because it orchestrates an existing BFF AI capability on the
  ADR-043 dispatch spine (`Services/Ai/Chat`); the work reuses the in-zone spine and adds only a seam test — no new
  service, endpoint, DI registration, or package.
- **Publish size**: `dotnet publish -c Release` compressed = **47.49 MB** — **delta 0.00 MB** vs the 47.49 MB
  baseline (test-only change; ≤60 MB ceiling, well under). No new NuGet; no new HIGH CVE surface.
- **Hot-path**: BFF touched = tests only (no production BFF `.cs`); SpaarkeAi = N.

## Follow-ons / notes
- **Inline payload cap (ADR-040, 128 KB)**: a worst-case NDA output (schema `maxItems: 50`, each finding
  `quotedText`+`explanation` up to 2000 chars) could theoretically approach/exceed the 128 KB inline ledger cap and
  be replaced by a truncation marker — which would break both derived views. Typical reviews (a handful of findings)
  are far under. The ADR-040 blob/SPE-pointer offload is the designed upgrade path. Flagged for the deploy/eval
  gate (060/050); no code change here.
- Tasks 030 (panel), 031 (comments event), 041 (summary page) all consume this orchestration's single ledgered
  payload — no server change needed for them to fan out.
