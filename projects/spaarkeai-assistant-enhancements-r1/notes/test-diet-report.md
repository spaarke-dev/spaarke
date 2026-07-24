# Test diet report — spaarkeai-assistant-enhancements-r1

**Run date**: 2026-07-23
**Branch**: work/spaarkeai-assistant-enhancements-r1
**Classifier**: ADR-038 §7 build-vs-maintain (17-ban B1–B17)

## Scope note (incremental-merge project)

R1 merged to master **incrementally** throughout the project (per the standing "merge each batch to master" pattern), so `git merge-base HEAD master` = `ee0e2bda1` (this session's earlier merge) and the branch-vs-master `.cs` test delta is a single file. That undercounts the project's true test surface, so this diet reconciles the **full R1 test delta** (identified by R1 task markers / catalog surfaces), not just the post-merge delta. Every file below already passed its task's **Step 9.5 (code-review + adr-check)** gate before merge.

## Summary

| Class | Count (files) | Action |
|---|---|---|
| MAINTAIN (KEEP behavior, no ban) | 8 enumerated + siblings | confirmed — no action |
| SCAFFOLDING (DELETE candidate) | **0** | — |
| AMBIGUOUS (reviewer judgment) | **0** | — |
| PATH-OBSERVATION (repo-structural, pre-existing) | unit files under `tests/unit/Sprk.Bff.Api.Tests/**` | no R1 action (see below) |

**Net: zero deletions, zero moves.** R1 added no scaffolding-class tests.

## Evidence (mechanical scan of the R1 test delta)

- **B1** `Mock<HttpMessageHandler>` / `Mock<IServiceClient>`: **none**.
- **B3** DI-registration assertions (`GetRequiredService<>()…Should` / `Assert.NotNull(...GetRequiredService)`): **none**.
- **B4** ctor `ArgumentNullException` tests: **none**.
- The dispatch-spine tasks (010/021/022/044) deliberately used the `tests/integration/seam/**` vertical-slice-seam DoD (ADR-038 seam category) and module-boundary stubs (not transport mocks) — the pattern ADR-038 prescribes.

## MAINTAIN — confirmed (no action)

| File | Facts | KEEP category | Why maintain |
|---|---|---|---|
| `tests/integration/contract/Eval/AssistantEnhancementsR1EvalTests.cs` (051) | 6 | contract/Eval | Eval-gate inventory + honest catalog grounding + FR-E4 profile non-flip + AC3 incoherent-combo; joined to `Category=GoldenUtteranceEval` merge gate (NFR-06). Behavioral, non-vacuous. |
| `tests/integration/seam/Ai/AgentToolProjectionGroundingSeamTests.cs` (044) | 2 | seam | Grounding PreFilter seam DoD — "Create matter" hidden inside a matter, offered with no host. |
| `tests/unit/…/Services/Ai/ConstrainedFieldMatcherTests.cs` (010) | 10 | unit (domain-logic) | Pure constrained-field ladder — calculation/mapping behavior, no I/O. |
| `tests/unit/…/Services/Ai/ConstrainedFieldResolverTests.cs` (010) | 6 | unit | Resolver behavior over stubbed metadata boundary (no transport mock). |
| `tests/unit/…/Services/Ai/Context/StatedProfileReaderTests.cs` (030) | 5 | unit | Stated-profile read behavior. |
| `tests/unit/…/Services/Ai/Context/StatedProfileRendererTests.cs` (032) | 8 | unit | Byte-stable render + envelope-budget behavior (NFR-02). |
| `tests/unit/…/Services/Ai/Context/StatedProfileSecurityTests.cs` (052) | 4 | unit | Profile authZ / delimiting / prompt-injection (NFR-05). |
| `tests/unit/…/Services/Ai/Chat/PreferenceNotPermissionInvariantTests.cs` (031) | 6 | unit | preference≠permission structural invariant (FR-E4) — red-before-green calibrated guard. |

Siblings in the same R1 surface (e.g. `ContextBinderStatedProfileTests`, `StatedProfileRendererTests`, `ContextEnvelopeRendererTests`, dispatch/gate seam tests) follow the same shape and classify MAINTAIN by the same criteria.

## 051 eval family — method-level (authored this task)

All 6 `[Fact]`s MAINTAIN: `Suite_Loads_WithNamespacedUniqueCaseIds_AndClosedVocabularies`, `EachR1CatalogChange_HasEvalCoverage_AtItsFloor`, `EveryDispatchConsumerType_IsGroundedPerItsDeclaredCatalogStatus`, `ListTasksBindingRow_DeclaresSurfaceLaunch_AndCarriesTheViewVsCreateCue`, `ProfileInjection_DoesNotFlipTheGroundedR1CapabilitySet_OperationalEvalInMergeGate`, `IncoherentPracticeAreaMatterType_CannotCommit_ModelEmitsIndependentLabelsNotClosedSetValues`. Each defends a concrete gate behavior; names follow `{Method}_{Scenario}_{ExpectedResult}`; non-vacuous guards present.

## PATH-OBSERVATION (repo-structural, NOT an R1 action)

R1's unit tests live under `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/**`, not the canonical `tests/unit/domain/**`. This is the **repo-wide** BFF test-project layout (thousands of tests); `tests/CLAUDE.md` notes the KEEP-path reorganization is a future, separate effort ("after task 050 path reorganization completes" — repo-wide, not this project). R1 followed the de-facto repo convention; moving these is out of scope for this project's diet and should ride the repo-wide reorganization, not a one-project `git mv`.

## Count delta

- R1 scaffolding-class tests: **0** → no `git rm`.
- R1 path-moves for this project: **0** → no `git mv` (repo-wide reorg is separate).
- Post-diet expected count = unchanged.

## Industry citation

Build-vs-maintain per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1–B17. Verdict: **clean — nothing to delete.**
