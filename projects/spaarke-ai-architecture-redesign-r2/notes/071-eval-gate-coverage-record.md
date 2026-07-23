# AIR2-071 — Eval-Suite-Green Merge Gate Coverage Record

> **Task**: 071 — Eval-suite-green merge gate (golden-utterance + resourcefulness + origin-classification families)
> **Spec refs**: FR-D-02, NFR-02, FR-B-05, FR-B-10
> **Date**: 2026-07-10

## Finding: the gate was already substantively closed

Before this task, an audit of the CI eval-gate and its constituent test files found that **all three FR-D-02 in-scope families, plus the FR-B-05 budget-breach hook and the FR-B-08/FR-30 capture-recall family, were already joined to the merge-blocking `eval-gate` job** in `.github/workflows/sdap-ci.yml` via the `[Trait("Category", "GoldenUtteranceEval")]` xUnit trait — the mechanism this project's tasks 026/031/033/054/057 established specifically so that joining a new family requires **no CI YAML change** (the trait IS the registration).

### Inventory (file:line, trait status)

| Family | Task | File | Trait line | Status before 071 |
|---|---|---|---|---|
| Golden-utterance suite (P0/P1) | r1-011/026 | `tests/integration/contract/Eval/GoldenUtteranceEvalSuiteTests.cs:69` | `[Trait("Category","GoldenUtteranceEval")]` | ✅ Gated |
| P2 loop/injection/compound | r1-037 | `tests/integration/contract/Eval/P2LoopInjectionEvalSuiteTests.cs:74` | same | ✅ Gated |
| D-F0(e) Resourcefulness | r2-031 | `tests/integration/contract/Eval/ResourcefulnessEvalSuiteTests.cs:48` | same | ✅ Gated |
| Origin-classification (Policy v2 E-1..E-6) | r2-033 | `tests/integration/contract/Eval/OriginClassificationEvalSuiteTests.cs:45` | same | ✅ Gated |
| ContextEnvelope budget-breach-fails-eval | r2-054 | `tests/integration/contract/Eval/ContextBudgetBreachEvalTests.cs:37` | same | ✅ Gated |
| Memory-write capture→recall | r2-057 | `tests/integration/contract/Eval/MemoryWriteCaptureRecallEvalTests.cs:32` | same | ✅ Gated |
| Daily Briefing accuracy (separate project, shares the mechanism) | daily-update-service-r5 | `tests/integration/contract/Eval/BriefingAccuracyEvalSuiteTests.cs:36` | same | ✅ Gated (not R2 scope; noted for completeness) |

**Memory-poisoning family**: searched the repo (`grep -r "poison"` across `tests/`) — no memory-poisoning eval family exists. Correct: FR-B-10 defers it to the governance project; it MUST NOT be added here.

CI mechanism verified: `.github/workflows/sdap-ci.yml` `eval-gate` job runs
```
dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj -c Debug --filter "Category=GoldenUtteranceEval"
```
with no `continue-on-error` at the job or step level — any family red fails the workflow run (the repo's only mechanical merge signal while branch protection is disabled).

## Genuine gap found and closed

The only thing NOT already satisfying FR-D-02's acceptance criteria was **documentation**, specifically the negative criterion: *"an explicit comment cites the governance-project deferral (grep confirms absence)."* Before this task:

- The `sdap-ci.yml` eval-gate comment block (added at r1 task 026, extended at r2 task 031) enumerated only 2 of the 6 gated families (golden-utterance suite + resourcefulness) — origin-classification, budget-breach, and capture-recall were gated in practice (via the trait) but undocumented in the block.
- **No comment anywhere in `.github/workflows/sdap-ci.yml` or `tests/integration/contract/Eval/README.md` explicitly stated that memory-poisoning families are deferred** — the deferral existed only in `spec.md` item 38/46 and `notes/defer-issues.md`-adjacent context, not at the gate itself where a future contributor would look before adding a family.

### Changes made (smallest closure — no new infrastructure)

1. **`.github/workflows/sdap-ci.yml`** (comment-only, additive, no functional/YAML-structure change): extended the "Families joined to this gate" bullet list to include origin-classification (033), budget-breach (054), and capture-recall (057); added an explicit paragraph citing FR-D-02/NFR-02 as the full-scope obligation and FR-B-10 as the memory-poisoning deferral, instructing future contributors not to add such a family to this trait/gate.
2. **`tests/integration/contract/Eval/README.md`**: added a "Eval-suite-green merge gate — full family scope (r2 task 071 / FR-D-02, NFR-02)" section immediately before "Deletion-safety", restating the full family list and the explicit memory-poisoning exclusion + deferral citation, so the family-authoring doc (the one a BA/engineer reads before adding a case) also carries the boundary.
3. **This note** — the coverage record + deferral note named in task 071's `<outputs>`.

No test code, no CI YAML mechanism, and no `Sprk.Bff.Api.Tests.csproj` changes were needed — the gate was already correctly wired; only its self-documentation was stale/incomplete.

## Local gate-run verification

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj -c Debug --filter "Category=GoldenUtteranceEval"
```

Result: **Passed! Failed: 0, Passed: 83, Skipped: 0, Total: 83** (Debug config, matches the "83 cases as of today" state-of-the-world baseline). Build: 0 errors (pre-existing nullable/obsolete warnings only, unrelated to this task).

Per-file `[Fact]` inventory (informational — case counts, not xUnit-discovered test counts, since some methods hold multiple assertions per case set):

| File | `[Fact]` count |
|---|---|
| GoldenUtteranceEvalSuiteTests.cs | 18 |
| P2LoopInjectionEvalSuiteTests.cs | 20 |
| ResourcefulnessEvalSuiteTests.cs | 14 |
| OriginClassificationEvalSuiteTests.cs | 12 |
| ContextBudgetBreachEvalTests.cs | 8 |
| MemoryWriteCaptureRecallEvalTests.cs | 2 |
| BriefingAccuracyEvalSuiteTests.cs | 1 Fact + 4 `[Theory]` (Daily Briefing project; not R2 scope) |

## Acceptance criteria — status

| Criterion | Status | Evidence |
|---|---|---|
| CI eval-gate runs golden-utterance + resourcefulness + origin-classification; any family red fails merge | ✅ Already satisfied (pre-existing trait wiring) | `eval-gate` job, no `continue-on-error`; local run confirms all 3 families execute under one filter |
| ContextEnvelope per-slice budget breach fails the eval run (FR-B-05) | ✅ Already satisfied | `ContextBudgetBreachEvalTests.cs` gated via same trait; negative cases assert breach-detected-not-truncated |
| Gate extends the existing sdap-ci.yml eval-gate — no second gate mechanism | ✅ Satisfied (no new mechanism introduced; comment-only edit) | Single `eval-gate` job unchanged in structure |
| No `Mock<HttpMessageHandler>`, no DI-registration assertions (ADR-038) | ✅ Verified | Grepped all `tests/integration/contract/Eval/*.cs`; no matches |
| NEGATIVE: memory-poisoning families NOT present; explicit comment cites governance-project deferral | ✅ Closed by this task | Added to `sdap-ci.yml` + `README.md`; grep for `poison` across `tests/` still returns no family (correct — deferred, not present) |

## Deviations

None from the POML's directional steps. Step 1 ("Wire families") and Step 2 ("Budget hook") required no code change because the wiring already existed; the actual work was Step 3 (exclude deferred, make it explicit) plus documenting Step 0's inventory finding so the next reader doesn't have to re-derive it.
