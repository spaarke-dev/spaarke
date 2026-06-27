# Task 065 evidence — PinnedContextRepository (D-C-18)

**Pillar / Spec ref**: R6 Pillar 7 / D-C-18 / FR-42 — Cosmos-backed repository for the
user-curated `PinnedContextItem` "memory anchor" entity. Pinned items NEVER drop from
system-prompt assembly per FR-42. Foundation for task 067 (hierarchical memory
composition); task 070 (R7) is the Q7 Pinned Memory UI consumer.
**Wave**: C-G2 gap-fill (model pre-existed; interface, impl, tests, DI added here).
**Date**: 2026-06-11.

## Implementation

Pre-existing:
- `src/server/api/Sprk.Bff.Api/Models/Memory/PinnedContextItem.cs` — model + `PinType` enum.

Added in this gap-fill:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/IPinnedContextRepository.cs` — contract.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PinnedContextRepository.cs` — Cosmos impl.
- DI registration in `Infrastructure/DI/AnalysisServicesModule.cs` (right after task 064).
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Memory/PinnedContextRepositoryTests.cs`.

## Key design decisions

- **Container reuse**: Cosmos container `memory` (same as `MatterMemoryService` and
  `WorkspaceStateService` durable rows). Partition key `/tenantId`. Document
  discriminator `documentType = "pinned-context"`. Co-exists with matter-memory
  (`{tenantId}_{matterId}`) and workspace-tab (`workspace-tab_{tenantId}_{tabId}`)
  documents on the same partition WITHOUT id collision — the `pinned-context_`
  id prefix is the disambiguator.
- **Document id format**: `pinned-context_{tenantId}_{pinId}`. Built via
  `PinnedContextRepository.BuildDocumentId(tenantId, pinId)` (internal helper for
  test access; pattern matches `MatterMemoryService.BuildDocumentId`).
- **Query API**: `GetByMatterAsync(tenantId, matterId)` and `GetByUserAsync(tenantId,
  userId)` — both partition-key-scoped SQL queries with `documentType = "pinned-context"`
  filter + the relevant secondary predicate.
- **Idempotent Delete**: 404 from Cosmos is swallowed (stale-handle race protection).
- **Length caps**: enforced at service layer per the model XML doc — title ≤200 chars,
  content ≤1000 chars. Mirrors the precedent of keeping POCO clean for Cosmos
  serialization (no DataAnnotations on the model).
- **Test-friendly internal ctor**: a second `internal` constructor accepts a Container
  directly so tests can mock the Cosmos container without a full CosmosClient mock
  graph. Pattern mirrors `MatterMemoryService`.

## Governance

- **ADR-010**: registered as `AddScoped<IPinnedContextRepository,
  PinnedContextRepository>()` inside `AnalysisServicesModule.AddAnalysisServices` —
  ZERO new `Program.cs` line. Interface seam justified (genuine substitution for
  task 067 / task 070 unit tests).
- **ADR-013**: lives under `Services/Ai/Memory/`. Injects `CosmosClient` +
  `IConfiguration` + `ILogger` only — NO AI-internal collaborators
  (`IOpenAiClient`, `IPlaybookService`). AI-internal callers consume this repository
  directly per the 2026-05-20 refined ADR-013 boundary rule.
- **ADR-014 / NFR-16**: every method takes `tenantId` and uses it as the Cosmos
  partition key. Cross-tenant reads are structurally impossible.
- **ADR-015**: pin `Content` is user-authored memory; persisted verbatim. The
  repository telemetry emits deterministic IDs (tenantId, userId, pinId, pinType)
  only — NEVER content bodies.
- **§F.1 asymmetric-registration**: registration is INSIDE the compound
  `(Analysis:Enabled && DocumentIntelligence:Enabled)` gate, matching the
  surrounding Memory services (MatterMemoryService, SummarizationCompressionService).
  Consumer (task 067) is inside the same compound gate.

## Test coverage

6 tests (all pass):
1. `CreateAsync_PersistsToCosmos_WithCorrectPartitionKey` — Create + verify id prefix +
   PartitionKey value.
2. `GetByMatterAsync_ReturnsMatchingPins` — by-matter query roundtrip.
3. `GetByUserAsync_ReturnsAllPinsForUser` — by-user query roundtrip.
4. `GetByUserAsync_UsesPartitionKey_ForTenantIsolation` — asserts
   `QueryRequestOptions.PartitionKey` equals the input tenant.
5. `DeleteAsync_IsIdempotent_WhenPinAbsent` — 404 swallowed; no exception escapes.
6. `DeleteAsync_TargetsCorrectId_LeavingOthersIntact` — deterministic id + partition.
7. `CreateAsync_Rejects_WhenContentExceedsCap` — `ArgumentException` thrown when
   Content length > MaxContentLength.

## Build status

- Source build: clean (0 errors).
- Test build: clean.
- Tests: 7/7 pass.
