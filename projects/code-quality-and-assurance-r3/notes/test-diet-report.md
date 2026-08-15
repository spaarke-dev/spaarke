# Test diet report — code-quality-and-assurance-r3

**Run date**: 2026-08-14 (project-close gate, task 090; root CLAUDE.md §7 / spec FR-B09)
**Branch**: work/code-quality-and-assurance-r3
**Scope**: tests touched between merge-base `da6b989ad` and HEAD
**Classifier**: ADR-038 §7 build-vs-maintain (17-ban B1–B17)

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP — confirmed) | 6 added + 13 modified | confirmed, no action |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 0 | — |
| PATH-VIOLATION (wrong KEEP path) | 0 (1 by-convention note below) | — |
| **Total test files touched** | **19** | — |

**Outcome: clean diet — zero scaffolding, zero deletions.** This is the expected result for a quality
program whose test deltas are forcing-functions (ArchTest fitness functions), a contract test, and a
startup-validation behavior test — not coverage scaffolding. Task 031 already ran a conservative
scaffolding-removal sweep DURING the program (committed `1d885d6c9`), so the modified pre-existing files
were dieted in-flight.

## Files ADDED by r3 (the real new-test scope) — all MAINTAIN

| File | KEEP path / category | Why MAINTAIN | Bans hit |
|---|---|---|---|
| `tests/Spaarke.ArchTests/ADR013_LinearConsumerBoundaryTests.cs` | ArchTest fitness fn (task 040) | Enforces ADR-013 AI-facade boundary; fails on real architectural drift | none |
| `tests/Spaarke.ArchTests/DataverseServiceClientDowncastTests.cs` | ArchTest fitness fn (task 040) | Bans the downcast pattern collapsed to 1 site (task 028); fails on regression | none |
| `tests/Spaarke.ArchTests/GodClassGuardTests.cs` | ArchTest fitness fn (task 040) | God-class LOC ceiling guard (baseline 4950); fails when a class grows past bar | none |
| `tests/Spaarke.ArchTests/LayerDependencyTests.cs` | ArchTest fitness fn (task 040) | Encodes true layer direction (Dataverse base, acyclic; no shared lib → BFF app) | none |
| `tests/integration/contract/Api/Finance/FinanceRollupEndpointsContractTests.cs` | `integration/contract/**` (canonical KEEP) | Contract test for the task-023 Finance auth closure (unauthenticated → 401) | none |
| `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/AgentServiceOptionsValidationTests.cs` | DI/startup-invariant (task 061) | Boots a real host; asserts fail-fast startup naming the key + gated boot; behavior, not wiring | none |

**Path note (not a violation)**: `AgentServiceOptionsValidationTests.cs` sits under
`tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/` rather than one of the six canonical KEEP paths. This
is the BFF's established convention for DI/config/startup-invariant tests — it co-locates with
`DiGraphValidationTests` (explicitly blessed MAINTAIN/KEEP in the net10 handoff) and
`AnalysisServicesModuleGatingTests`/`CacheModuleTests`. These are fitness-function-adjacent maintain-class
tests; no move recommended. Reviewer may relocate to a seam path if preferred.

## Files MODIFIED by r3 (pre-existing — MAINTAIN, no new scaffolding introduced) — 13

Mechanical/behavioral touches during the program, not new scaffolding:

- `EndpointGroupingTests.cs` — task 023 updated 3 assertions to the bare-401 auth contract (behavioral).
- `Phase1StableIdMigrationSuite.cs`, `PlaybookByIdIntegrationTestFixture.cs`, `StubPlaybookLookupService.cs`,
  `AnalysisServicesModuleGatingTests.cs`, `Phase2IntegrationTests.cs`, `EmailAnalysisIntegrationTests.cs`,
  `FinancialCalculationToolHandlerTests.cs`, `ContainerTypeEndpointsTests.cs`,
  `RegisterContainerTypeTests.cs`, `UpdateContainerTypeSettingsTests.cs`, `WorkspaceFileEndpointsTests.cs`,
  `ScheduledJobRegistryTests.cs` — namespace migration (task 025 Endpoints→Api), downcast-helper
  adoption (task 028), and the task-031 conservative scaffolding sweep. No new B1–B17 shapes added.

## Delete commands

_None — zero scaffolding._

## Path-move commands

_None._

## Count delta

- New test files added during r3: 6 (all MAINTAIN)
- Classified SCAFFOLDING: 0
- Net post-diet expected count: unchanged (no deletions)

## Industry citation

Build-vs-maintain per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior;
Google test-sizes; DHH less-tests). 17-ban classifier B1–B17. Task 031 performed the in-flight sweep;
this project-close pass confirms the deltas are all maintain-class.
