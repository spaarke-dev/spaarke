# Task 030 — Characterization Tests: Notes & Deviations

> **Status**: ✅ Completed 2026-07-21. 14 new seam tests, all green against pre-031 code. Zero production changes.
> **Purpose of these tests**: the behavior-neutrality safety net for task 031 (Layer-A action seam extraction). If these 14 tests pass **unmodified** after 031, the extraction changed nothing observable (FR-07 / Feathers characterization contract).

## Deliverables

| File | Tests | Pins |
|---|---|---|
| `tests/integration/seam/Ai/Nodes/CreateNotificationNodeExecutorSeamTests.cs` | 8 | Exact `appnotification` 9-field set; idempotency skip; iterate-items counts; unresolvable-recipient error; 4 exact Validate messages |
| `tests/integration/seam/Ai/Nodes/CreateTaskNodeExecutorSeamTests.cs` | 3 | Exact `task` 5-field set; degraded-success `Guid.Empty`; subject-required message |
| `tests/integration/seam/Ai/Nodes/UpdateRecordNodeExecutorSeamTests.cs` | 3 | Typed-mapping coercion (Choice/Boolean/Number); legacy `HeuristicParse` precedence; fail-loud Choice (FR-C1) |

Run: `dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~NodeExecutorSeamTests"` → **Passed 14 / Failed 0**.

## Design decisions (for task 031's author)

1. **Seam category, real `TemplateEngine`.** Per ADR-038 + POML constraint, tests use production `NodeExecutionContext` + a real `TemplateEngine(NullLogger)` (template rendering is part of the pinned behavior). Only the outermost Dataverse-boundary services are Moq doubles: `IGenericEntityService` (CreateNotification/CreateTask) and `IFieldMappingDataverseService` (UpdateRecord). This matches the existing executor unit-test mock convention.
2. **Config-string driven.** The executors parse `ConfigJson` strings; the `*NodeConfig` records are `internal`, so tests drive behavior via realistic ConfigJson (the honest characterization surface) rather than constructing internal types.
3. **Choice-metadata path** replicates the unit test's `UseMetadataFor` helper (real `MetadataService` + mocked `IDistributedCache` cache-HIT) — intentional duplication (POML forbids modifying the unit suite; a shared builder would couple the seam suite to it).

## Escalation trigger — NOT fired

The POML armed an escalation trigger: *stop if a characterized path looks like an unintended defect rather than a legitimate contract.* Three paths looked defect-adjacent but are **explicitly documented intentional contracts** in the production code, so they were pinned as-is (correct per the characterization constraint), NOT escalated:

- **CreateTask degraded success (`taskId = Guid.Empty` when `CreateAsync` throws)** — `CreateTaskNodeExecutor.cs:223-233` documents this as deliberate: "Return a degraded success — the task payload was assembled correctly but Dataverse rejected it." Intentional.
- **CreateNotification idempotency check swallows query errors and proceeds** — `CheckForDuplicateNotificationAsync` catch block documents "better to create a potential duplicate than to fail the entire node." Intentional (not pinned by a dedicated test since it's a fail-open on an infra error, not an observable output contract; the happy-path + duplicate tests bracket it).
- **UpdateRecord fail-loud on unmatchable Choice** — this is the *desired* FR-C1 contract, pinned positively (criterion 10).

No genuine defect surfaced → trigger correctly did not fire.

## Note for 031

031's acceptance criterion is that these 14 tests pass **unmodified** after the Layer-A seam extraction behind `*NodeExecutor.cs` (ADR-013 PublicContracts facade). If 031 must change any assertion here, that is a signal the extraction altered observable behavior — STOP and reconcile, do not "fix" the test to match.
