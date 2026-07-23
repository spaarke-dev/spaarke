# HEADS-UP → redesign-r2 (core): E-30 shipped 23 dispatch-contract tests RED on master (fixed by compose-r2) + an AuditLog flake

> **From**: spaarkeai-compose-r2 · **To**: spaarke-ai-architecture-redesign-r2 (core) · **Date**: 2026-07-09
> **Re**: while merging the Phase-E batch (`c300ab12d`) into compose-r2, caught two test issues on master.

## 1. E-30 ctor drift — 23 tests red on master (I fixed them; please absorb the lesson)
E-30 (coded-workflow dispatch seam, PR #606) added `ICodedWorkflowRegistry` to the `SessionDispatchOrchestrator` constructor but did **not** register it in two dispatch-contract test fixtures:
- `tests/integration/contract/Api/Ai/DispatchSessionEndpointContractTests.cs` — 14 tests (whole-class fixture-activation failure)
- `tests/integration/contract/Api/Ai/SummarizeSessionEndpointContractTests.cs` — 9 tests

**Verified pre-existing on pristine `origin/master`** (ran the class in a throwaway worktree — 14 fail / 1 pass) — not introduced by the compose merge. Error: `Unable to resolve service for type 'ICodedWorkflowRegistry' while attempting to activate 'SessionDispatchOrchestrator'`.

**Fix applied (compose-r2 commit `5f59cbc0b`, now on master)**: registered the real `CodedWorkflowRegistry` with an empty `ICodedWorkflow` set in both fixtures (the prompted-path contract tests don't exercise coded workflows; the ctor just requires the dep). Both classes green (15/15 + 12/12).

**The meta-point**: this is exactly the fixture-drift class ADR-043 §5's vertical-slice-seam **definition-of-done** (E-40) is meant to catch — a ctor/DI change that ships "green" at the contract-shape layer while the fixture is red. E-30 landed without the seam-gate catching it. Worth a look as you stand up E-40's KEEP-category enforcement so the next ctor change to the orchestrator can't ship its fixtures red.

## 2. Pre-existing flaky test (not ours, not E-30)
`Services/Ai/Audit/AuditLogServiceTests.LogInteractionAsync_PartitionsByTenantId` — **passes 3/3 in isolation, fails only under full-suite parallelism** (tenant-partition shared-state ordering). Pre-existing, unrelated to compose or Phase E. Flagging for your test-hygiene backlog (likely needs per-test isolation or a deterministic partition key).

## 3. Thank you — E-20 unblocked us
E-20's `DispositionRoutability` admits `Compose` exactly as you said (`IsAdmissible=IsRoutable`, `Compose Routable=true`); our hand-patch was cleanly superseded by your collapse. Compose dispatch is now unblocked end-to-end — we'll run the forcing-consumer validation (016 re-verify + the 084 seam slice) and report, which feeds your ADR-043 promotion gate.

*Contact: Ralph Schroeder.*
