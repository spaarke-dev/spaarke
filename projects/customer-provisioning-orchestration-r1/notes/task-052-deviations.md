# Task 052 — H9 BFF Deploy Handler — Deviation Notes

**Date**: 2026-08-17
**Task**: `052-implement-h9-bff-deploy-handler.poml`
**Model / Effort**: Sonnet 5 / xhigh
**Rigor**: FULL
**Status**: ✅ Complete

## Executive summary

H9BffDeployHandler + 5 seams + 5 production impls + 33 test cases landed cleanly. L2 build: 0/0 warnings/errors. L2 tests: 486/486 pass (35 new + 451 pre-existing; no regressions). Program.cs DI hunk applied post-057 (task 057 committed b8dcdfaeb before this task; no logical conflict — additive registration alongside its endpoint mapping).

No POML-scope-expanding deviations. Two Path C (pivot-to-comply) deviations documented below, per CLAUDE.md §6.5.

## Deviation 1 (Path C) — r3-era gate verifier: Skipped for pending-artifact gates

**POML step 2 wording**: "Invoke r3-era gate verifier: analyzers-as-errors + god-class ratchet + 4 ArchTests + naming-conformance + Graph app-role parity. Block on any failure with distinct code per failing gate."

**Reality (per POML mandatory pre-work #8)**: not all gate artifacts have landed:

| Gate | Artifact | Status |
|---|---|---|
| Analyzers-as-errors | Directory.Build.props `TreatWarningsAsErrors=true` | **Landed** (r3 task 041) |
| God-class ratchet | `tests/Spaarke.ArchTests/GodClassGuardTests.cs` | **Landed** (r3 task 040) |
| 4 new ArchTests I1–I5 | `tests/Spaarke.ArchTests/TenantIsolation*Tests.cs` | **Pending** (r1 task 064 — in `tests/Spaarke.ArchTests/TenantIsolation/` directory per git status; not yet as top-level `TenantIsolation*Tests.cs` files) |
| Naming-conformance | `scripts/naming-conformance-check.ps1` | **Pending** (r3 task 063) |
| Graph app-role parity | `tests/Spaarke.ArchTests/GraphAppRoleParity*Tests.cs` | **Pending** (r3 task 062 / r1 task 067; queued behind CI-wiring per task 088) |

**Chosen path**: Path C (pivot-to-comply). The production `DotnetR3GateVerifier` treats missing gate artifacts as `R3GateStatus.Skipped` (logged prominently but does NOT block the deploy). The handler only blocks on `R3GateStatus.Failed`. Once the pending artifacts land, the verifier auto-picks them up via filesystem-based discovery (no code change required).

**Rejected alternatives**:
- **Block on missing gates**: would pre-emptively block every H9 deploy until r1 tasks 064/067 + r3 task 063 land — that's a hard dependency ordering the POML deliberately did NOT declare (`<deps>036, 044, 047</deps>` does not include 064/067/063).
- **Escalate to main-session and wait**: main-session guidance implicitly ruled this out via the POML's explicit "Skip missing gates rather than block" allowance in the "r3-era gate verification — the abstraction" section.

**Where documented**: `IR3GateVerifier.cs` file header + `DotnetR3GateVerifier.cs` file header + Program.cs DI comment ADR Tension citation.

## Deviation 2 (Path C) — Deploy-BffApi.ps1 invocation without -UseSlotDeploy

**POML step 3 wording**: "Invoke hardened Deploy-BffApi.ps1 with customerId-driven target."

**Reality**: the shipped Deploy-BffApi.ps1 does NOT expose a `-SkipSwap` switch. `-UseSlotDeploy` performs ALL of steps 1–7 (build + zip + deploy + verify staging + swap + verify prod + rollback) — that would duplicate the handler's swap + smoke + rollback ownership.

**Chosen path**: Path C (pivot-to-comply). `DeployBffApiScriptRunner` invokes `Deploy-BffApi.ps1` WITHOUT `-UseSlotDeploy` so the script performs a direct-deploy path, and targets the STAGING App Service (`{AppServiceName}-{SlotName}`) rather than production. This confines the script to its steps-1-3 scope (build + zip + deploy + verify staging /health) while giving the handler ownership of the swap + prod smoke test + rollback in-process for tight Cosmos state coupling.

**Rejected alternatives**:
- **Add `-SkipSwap` switch to Deploy-BffApi.ps1**: pulls script hardening into H9's scope; task 013 already covered PS-side hardening. Adding another switch expands blast radius unnecessarily.
- **Duplicate script steps 4–6 in the handler AND run the script's own steps 4–6**: catastrophic double-swap semantics.

**Where documented**: `DeployBffApiScriptRunner.cs` file header + `IBffDeployRunner.cs` file header "SCOPE DIVISION".

## Not-a-deviation: Deploy-Release.ps1 hardening

**POML mandatory pre-work #6 asked**: "READ scripts/Deploy-Release.ps1 Phase 4 — check whether 'customerId-driven, no spaarkedev1 hardcode' hardening has been applied. If NOT, this is a Phase B script hardening dependency that may not be done yet — flag in escalation."

**Result**: Task 013 (Phase B) is ✅ complete per TASK-INDEX.md. `grep spaarkedev1 scripts/Deploy-Release.ps1` returns zero matches. `-CustomerId` is Mandatory with `[ValidatePattern('^[a-z0-9][a-z0-9-]{1,63}$')]` and there is no fallback default (spaarkedev1 or otherwise). Gap 2 hardening is in place.

The handler still performs a defense-in-depth `spaarkedev1` scan on Deploy-Release.ps1 (POML criterion 5) — this guards against a future regression, not a current one. The scan is tolerant of script-not-found (task 013's hardening is the primary defense; the handler-side scan is a belt-and-braces secondary guard).

## Not-a-deviation: idempotency key format

Per POML constraint: `bff-{customerId}-{buildId}`. Implemented verbatim in `H9BffDeployHandler.BuildIdempotencyKey(customerId, buildId)`. Buildn is required at parameter-guard time (MissingBuildId Resumable failure if absent).

## Not-a-deviation: NFR-01 publish-size gate

Per POML acceptance criterion 4 + spec.md NFR-01: report absolute + delta; ≥+5 MB single-task delta fails deploy with explicit-justification requirement. Implemented: `FileBffPublishSizeReporter` returns `PublishSizeReport` with `ExceedsDeltaThreshold` + `ExceedsAbsoluteCeiling` flags; handler REJECTS with `PublishSizeDeltaExceeded` (QuarantineRequired) when either flag trips. Baseline 44.96 MB (2026-08-13 net10 framework-dependent linux-x64) per CLAUDE.md §10 bullet 4 + `dotnet-10-upgrade-r1` task 031.

## Not-a-deviation: Both-slots-bad escalation (POML escalation trigger 2)

Handler distinguishes:
- Smoke test failure + rollback SUCCESS → `RetryableWithCleanup` + `SmokeTestFailedRolledBack` (production safe)
- Smoke test failure + rollback FAILURE → `QuarantineRequired` + `SmokeTestFailedRollbackAlsoFailed` (BOTH SLOTS BAD; diagnostic message includes "POML escalation trigger 2")
- Rollback throws exception mid-swap → `QuarantineRequired` + `RollbackInfraFault` (same escalation semantics)

All three paths have dedicated test coverage (AC-13a / AC-13b / AC-13c / AC-13d).

## Files added / modified

### New (13 files)

1. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/BffDeployOptions.cs`
2. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/BffDeployRejectionCodes.cs`
3. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/IR3GateVerifier.cs`
4. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/DotnetR3GateVerifier.cs`
5. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/IBffDeployRunner.cs`
6. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/DeployBffApiScriptRunner.cs`
7. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/IAppServiceSlotSwapper.cs`
8. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/AzCliAppServiceSlotSwapper.cs`
9. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/IHealthProbe.cs`
10. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/HttpHealthProbe.cs`
11. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/IBffPublishSizeReporter.cs`
12. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/FileBffPublishSizeReporter.cs`
13. `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/BffDeploy/H9BffDeployHandler.cs`

### New (tests)

14. `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/H9BffDeployHandlerTests.cs` — 35 test cases (AC-1 through AC-18)

### Modified

15. `src/server/services/Sprk.Provisioning.ControlPlane/Program.cs` — 1 `using` line + DI registration hunk after task-073 H14 registration. No conflict with task 057's `MapRunsEndpoints()` / `MapRunLogsEndpoints()` mapping (task 057 committed b8dcdfaeb before this task).
16. `projects/customer-provisioning-orchestration-r1/tasks/TASK-INDEX.md` — row 052 status 🔲/⏸ → ✅.

## Verification

| Check | Result |
|---|---|
| `dotnet build src/server/services/Sprk.Provisioning.ControlPlane/` | ✅ 0 warnings / 0 errors |
| `dotnet build src/server/services/Sprk.Provisioning.ControlPlane.Tests/` | ✅ 0 warnings / 0 errors |
| `dotnet test src/server/services/Sprk.Provisioning.ControlPlane.Tests/` | ✅ 486 pass / 0 fail (35 new H9 tests + 451 pre-existing) |
| `dotnet test --filter "H9BffDeployHandlerTests"` | ✅ 35/35 pass in 115 ms |
| Deploy-Release.ps1 spaarkedev1 scan | ✅ no hits (task 013 hardening intact) |
| Idempotency key format | ✅ `bff-{customerId}-{buildId}` per POML constraint (AC-15 verified) |
| Rollback re-swap on smoke-test failure (AC-13a) | ✅ Verified via FakeSlotSwapper.SuccessThenSuccess |
| Both-slots-bad escalation (AC-13b/13c) | ✅ Verified via FakeSlotSwapper.SuccessThenFailure + SuccessThenThrows |

## Publish-size impact

H9 adds ~1500 LOC to L2 App Service (7 seams + handler + 5 impls). L2 App Service is NOT the BFF; the BFF publish-size ceiling (NFR-01, ≤60 MB) does not apply to L2. No BFF changes in this task — BFF publish size unchanged (44.96 MB baseline; PR description need not include a BFF-side delta).

## Coordination

**Task 057 (L2 REST endpoints — shared Program.cs)**: coordinated per Wave 3 pattern. Task 057 committed b8dcdfaeb before this task; late-read Program.cs at commit time and applied narrow additive DI hunk alongside 057's endpoint mapping. Both hunks are additive; no logical conflict.

**Task 064 (I1–I5 ArchTests)**, **Task 067 (Graph app-role parity)**, **Task 063 (naming-conformance)**: pending. Handler tolerates their absence via `R3GateStatus.Skipped` (see Deviation 1). Once landed, verifier auto-picks up via filesystem discovery.
