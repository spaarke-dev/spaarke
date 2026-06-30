# Wave 11 T116 — Narrator Spike Plan

> **Started**: 2026-06-30
> **Spike type**: Architecture validation (NOT production refactor)
> **Owner**: T116 session
> **Reversibility**: Feature-flagged; current playbook path preserved as fallback

---

## Hypothesis being tested

**The `/narrate` workflow can be implemented as ~80 lines of direct C# (a "narrator class") that calls existing AI services directly, while preserving 100% of operator value (prompts, models, tuning still editable in Dataverse Action rows).**

If true, this delivers:
- Working `/narrate` content (currently empty due to interpreter bugs P1/P2)
- Same operator workflow as today for prompt edits
- ~40× reduction in runtime code surface area
- Compiler-enforced data flow (no template substitution bugs possible)

If false, we fall back to the P1/P2 tactical fixes documented in the systematic assessment.

## Architecture under test

```
┌─────────────────────────────────────────────────┐
│  OPERATOR-VISIBLE (UNCHANGED)                   │
│  sprk_analysisaction rows in Dataverse:         │
│    • BRIEF-NARRATE-TLDR  (prompt, schema, temp) │
│    • BRIEF-NARRATE-CHANNEL                      │
│    • BRIEF-VALIDATE-ENTITY-NAMES                │
│  Operator edits prompts → effective immediately │
└─────────────────────────────────────────────────┘
                      ↑
              loaded by ActionCode
                      │
┌─────────────────────────────────────────────────┐
│  CODE LAYER (NEW — this spike)                  │
│  DailyBriefingNarrator.cs (~80 lines):          │
│    NarrateAsync(req) → calls LLM 1+N times      │
│    Runs validator inline                        │
│    Returns DailyBriefingNarrateResponse         │
│  NO orchestrator, NO templates, NO aggregator   │
└─────────────────────────────────────────────────┘
                      ↑
                   uses
                      │
┌─────────────────────────────────────────────────┐
│  REUSED COMPONENTS (UNCHANGED)                  │
│  • IOpenAiClient.GetStructuredCompletionRawAsync│
│  • AnalysisActionService (Action loader)        │
│  • EntityNameValidator algorithm                │
│  • Auth, telemetry, circuit breaker             │
└─────────────────────────────────────────────────┘
```

The existing playbook engine (PlaybookOrchestrationService, executors, template engine, aggregator) **stays running** for chat-summarize and any other consumer that uses it. This spike only changes the `/narrate` execution path.

## Feature flag

`Features:NarrateUseCodeBasedNarrator` (default `false`)

- Flag OFF → current playbook path runs (unchanged behavior; regression-safe)
- Flag ON → DailyBriefingNarrator path runs

Toggleable via App Service config `Features__NarrateUseCodeBasedNarrator=true` (no redeploy needed to flip).

## Coexistence guarantee

- No deletion of existing code
- No modification to playbook engine or executors
- No modification to Action rows
- No modification to playbook rows in Dataverse
- Only additions: new narrator class + DI registration + feature-flag branch in endpoint

If spike fails or reveals problems, we set flag to false and the system reverts entirely to today's behavior.

## Step list

| # | Step | Output | Status |
|---|---|---|---|
| 0 | Inspect existing services (IAnalysisActionService, IOpenAiClient signatures, EntityNameValidator algorithm, feature flag pattern) | Notes confirming reuse plan | pending |
| 1 | Write `DailyBriefingNarrator.cs` (~80 lines) | New file under `Services/Ai/Narrators/` | pending |
| 2 | Extract EntityNameValidator algorithm into reusable service IF the executor entangles it with Dataverse | Pure-C# scrubber callable from narrator | pending |
| 3 | DI registration in `AnalysisServicesModule` | One-line add | pending |
| 4 | Feature-flag the `HandleNarrate` endpoint | `if (flag) narrator.NarrateAsync(); else existing` | pending |
| 5 | `dotnet build` + `dotnet test` (regression-safe with flag off) | Clean build, all tests pass | pending |
| 6 | Deploy BFF (flag off by default) | Live in dev, behavior unchanged | pending |
| 7 | Toggle flag on in dev App Service, smoke `/narrate` | Non-empty tldr + channelNarratives | pending |
| 8 | Write comparison doc (playbook vs code path) at `notes/spikes/narrator-vs-playbook-comparison.md` | One-page table + observations | pending |
| 9 | Decision-gate report to operator | Operator decides: ship-as-default / keep both / revert | pending |

## What this spike PROVES if successful

✅ Code-based runtime delivers same functional contract as playbook path
✅ Operator value preserved (Action edits still hot)
✅ `/narrate` returns non-empty content (first time this session)
✅ Lines-of-code delta is real and measurable
✅ The "playbook as source format, compiled to runtime" architecture is viable

## What this spike does NOT decide

❌ Whether to build a compiler / source generator (separate decision; depends on number of future narrative consumers)
❌ Whether chat-summarize should also migrate (each consumer assessed separately)
❌ Whether to delete the existing playbook engine code (engine stays running for other consumers regardless)

## Risk assessment

| Risk | Mitigation |
|---|---|
| New narrator misses an edge case the playbook handled | Smoke test with same payload used for playbook path; compare outputs |
| EntityNameValidator algorithm is hard to extract from executor | Extract carefully; keep executor working for any other consumer that uses it |
| Feature flag wiring breaks existing path | Test flag-off case first before flipping flag-on |
| Deploy fails or breaks regression | Reversible — toggle flag off; if catastrophic, revert deploy |

## Decision gate outputs

After Step 8, deliver to operator:
1. Working `/narrate` with non-empty content (demonstrable)
2. One-page comparison doc
3. Recommendation: ship as default / keep both for a release / revert

Operator decides next action. Spike does NOT auto-promote to default behavior.

## Budget

~4 hours of focused work + 1 BFF deploy + 1 feature-flag toggle.

---

*This is a SPIKE. The goal is empirical answer to an architectural question. Outputs feed into a separate decision; the spike itself does NOT change production behavior unless the operator explicitly approves flipping the feature flag default.*
