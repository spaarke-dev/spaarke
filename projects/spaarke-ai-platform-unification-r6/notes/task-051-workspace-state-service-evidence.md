# Task 051 — Evidence (R6 Pillar 6a / D-C-02)

> **Outcome**: ✅ COMPLETE — `WorkspaceStateService` shipped, tested, registered.
> **Date**: 2026-06-09
> **Branch**: `work/spaarke-ai-platform-unification-r6`
> **Companion**: [`task-051-placement-justification.md`](task-051-placement-justification.md)

---

## POML acceptance criteria — 9/9 PASS

| # | Criterion | Evidence |
|---|---|---|
| 1 | `IWorkspaceStateService` + `WorkspaceStateService` exist in `Services/Workspace/`; registered in `AnalysisServicesModule`; ZERO new Program.cs lines | Files created; one-line `services.AddScoped<IWorkspaceStateService, WorkspaceStateService>()` added inside `AddAnalysisServicesModule(...)`. `Program.cs` unchanged. |
| 2 | Redis cache key format `workspace:{tenantId}:{sessionId}`; per-tenant isolation verified by unit test | `WorkspaceStateService.BuildRedisKey` returns `workspace:{tenantId}:{sessionId}`. Tests `BuildRedisKey_IsolatesTenants_ForSameSessionId` + `UpsertTabAsync_WritesToTenantSpecificRedisKey` + `GetTabsAsync_ReturnsOnlyOwnTenantData` PASS. |
| 3 | Redis hot tier TTL = 24h | `WorkspaceStateService.RedisTtl = TimeSpan.FromHours(24)`. Test `UpsertTabAsync_SetsRedisTtlTo24Hours` PASS. |
| 4 | Pin operation writes through to Cosmos durable tier with matterId tag | `PinTabAsync` flips `IsPinned=true`, updates `MatterContext.MatterId`, calls `WriteDurableAsync`. Test `PinTabAsync_WritesThroughToCosmos_WithMatterIdTagAndIsPinnedTrue` PASS. |
| 5 | Close operation removes from Redis without touching Cosmos durable rows | `CloseTabAsync` only calls Redis methods; Cosmos `UpsertItemAsync`/`DeleteItemAsync` never invoked. Test `CloseTabAsync_RemovesFromRedis_DoesNotTouchCosmos` (with `Times.Never` verification) PASS. |
| 6 | Unit tests cover tenant isolation, TTL, pin write-through, close semantics | 14 tests across all 4 named acceptance areas + 2 bonus (idempotency, polymorphism round-trip). PASS. |
| 7 | Publish-size delta measured + recorded in task notes; respects ≤+5 MB R6 budget | 45.96 MB compressed (+0.31 MB vs Phase 5 baseline 45.65 MB). Well within ≤+5 MB R6 budget. |
| 8 | No HIGH-severity CVE; ADR-010/013/014/029 compliant | `dotnet list package --vulnerable` returns only pre-existing accepted-risk Kiota HIGH. ADR compliance documented in placement-justification note. |
| 9 | code-review + adr-check pass | Self-audit: see "Quality Gates" section below. |

---

## Architectural choice — Cosmos `memory` container reuse

Per POML hint, evaluated reuse-vs-new. **Decision: REUSE** the existing `memory` container with a `documentType: "workspace-tab"` discriminator + id prefix `workspace-tab_{tenantId}_{tabId}`. Full rationale in placement-justification note §"Cosmos container decision".

Co-existence proof:
- Existing `MatterMemoryService` doc id format: `{tenantId}_{matterId}` (no prefix)
- New workspace-tab doc id format: `workspace-tab_{tenantId}_{tabId}` (mandatory prefix)
- No collision possible; both share partition key `/tenantId`.
- Discriminator query: `WHERE c.documentType = "workspace-tab" AND c.sessionId = @sessionId` (added field on workspace-tab docs only; existing matter-memory docs lack this field so they are structurally excluded from workspace-tab queries).

---

## C# DTO polymorphism approach — `[JsonPolymorphic]` + `[JsonDerivedType]`

System.Text.Json native polymorphism on the abstract `WorkspaceTabWidgetData` base class:

```csharp
[JsonPolymorphic(
    TypeDiscriminatorPropertyName = "kind",
    UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
    IgnoreUnrecognizedTypeDiscriminators = false)]
[JsonDerivedType(typeof(SummaryTabWidgetData), typeDiscriminator: "Summary")]
[JsonDerivedType(typeof(DocumentViewerTabWidgetData), typeDiscriminator: "DocumentViewer")]
[JsonDerivedType(typeof(DashboardTabWidgetData), typeDiscriminator: "Dashboard")]
[JsonDerivedType(typeof(TableTabWidgetData), typeDiscriminator: "Table")]
public abstract class WorkspaceTabWidgetData
{
    [JsonIgnore]
    public abstract string Kind { get; }
}
```

**Key tuning decision**: the abstract `Kind` property is `[JsonIgnore]`-ed. System.Text.Json's polymorphism metadata layer emits the wire `"kind"` discriminator automatically based on the concrete subtype; if the model ALSO declared its own `Kind` property mapped to the same wire name, STJ throws `InvalidOperationException` with "property 'kind' that conflicts with an existing metadata property name." The `[JsonIgnore]` on the abstract property + concrete overrides preserves in-process pattern-matching ergonomics (`tab.WidgetData.Kind == "Summary"`) without conflicting with the metadata layer.

Round-trip is verified by `JsonPolymorphism_RoundTripsAllFourWidgetDataVariants` — all four concrete subtypes survive serialize → bytes → deserialize → concrete subtype preserved.

---

## File list

| File | Path | LOC |
|---|---|---|
| Service interface | `src/server/api/Sprk.Bff.Api/Services/Workspace/IWorkspaceStateService.cs` | ~95 |
| Service implementation | `src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceStateService.cs` | ~340 |
| DTO — root tab | `src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceTab.cs` | ~160 |
| DTO — widget data union | `src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceTabWidgetData.cs` | ~155 |
| DI registration (one line added) | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` | +22 lines (incl. context comment block) |
| Unit tests | `tests/unit/Sprk.Bff.Api.Tests/Services/Workspace/WorkspaceStateServiceTests.cs` | ~400 |
| Placement justification | `projects/spaarke-ai-platform-unification-r6/notes/task-051-placement-justification.md` | ~210 |
| Evidence (this file) | `projects/spaarke-ai-platform-unification-r6/notes/task-051-workspace-state-service-evidence.md` | ~160 |

---

## Test count

- **Added**: 14 unit tests in `WorkspaceStateServiceTests`
- **Passing**: 14/14
- **Full unit suite regression check**: 6,900 passed, 0 failed, 109 skipped (skips are pre-existing, unrelated)

---

## Build outcome

```
Sprk.Bff.Api -> deploy/api-publish/

Build succeeded.
    16 Warning(s)  (pre-existing — none in new code)
    0 Error(s)
```

---

## Publish-size delta

| Measure | Value |
|---|---|
| Compressed | **45.96 MB** (target ≤50 MB ADR-029 ceiling) |
| Uncompressed | 139.08 MB (target ≤150 MB ADR-029) |
| File count | 260 |
| Delta vs Phase 5 baseline (45.65 MB) | **+0.31 MB** |
| Within ≤+5 MB R6 budget | ✅ YES (4.04 MB slack remaining to ceiling) |

---

## CVE check outcome

```
$ dotnet list package --vulnerable --include-transitive
> Microsoft.Kiota.Abstractions 1.21.2 — High (GHSA-7j59-v9qr-6fq9)
```

**Status**: NO NEW HIGH CVE introduced. The single HIGH entry is pre-existing Kiota accepted-risk per ADR-029.

---

## DI symmetry check (§F.1 binding)

**Result**: ✅ COMPLIANT — anti-pattern does NOT apply.

`WorkspaceStateService` registration is **unconditional** (NOT inside any `if (flag) { ... }` block). Consumers (task 052 endpoint, task 053 chat factory) are expected to be likewise unconditional. No Null-Object peer required because the service has zero AI-internal constructor deps — only generic `IDistributedCache` + `CosmosClient` + `IConfiguration` + `ILogger`. Full §F.1 static-scan result in placement-justification note.

---

## Quality Gates (Step 9.5 FULL rigor)

### Self-audit: code-review

- **Naming**: ✅ `IWorkspaceStateService` (interface seam), `WorkspaceStateService` (concrete), `WorkspaceTab` (DTO), `WorkspaceTabWidgetData` (union base) — clear, consistent with `MatterMemoryService` / `SessionPersistenceService` conventions.
- **Dependency injection**: ✅ Scoped lifetime matches consumers; deps are all framework-level (`IDistributedCache`, `CosmosClient`) — no AI-internal types; constructor null-guards present.
- **Error handling**: ✅ Redis read/write errors are caught + logged at Warning + swallowed (parity with `SessionPersistenceService`). Cosmos write errors are logged + swallowed. `ArgumentException.ThrowIfNullOrWhiteSpace` on every public method. `InvalidOperationException` on tenant mismatch (defensive). `KeyNotFoundException` on pin against missing tab (explicit contract).
- **JSON polymorphism**: ✅ System.Text.Json `[JsonPolymorphic]` + `[JsonDerivedType]` × 4 variants; round-trip verified by test.
- **Test coverage**: ✅ 14 tests; all 6 acceptance categories + 3 bonus (idempotency, defensive tenantId, empty case).

### Self-audit: adr-check

- **ADR-010 (DI minimalism)**: ✅ ZERO new `Program.cs` lines. Single registration line inside existing `AnalysisServicesModule`.
- **ADR-013 (AI architecture)**: ✅ Workspace-state plumbing, not AI capability. No injected AI-internal types. CRUD-side consumers (Pillar 6b) would reach this via dedicated `IWorkspaceStateService` interface — facade boundary preserved.
- **ADR-014 (AI caching)**: ✅ Per-tenant cache key `workspace:{tenantId}:{sessionId}` (binding per NFR-16). TTL 24h. Centralized key builder (`BuildRedisKey` static method). No inline string keys.
- **ADR-015 (data governance)**: ✅ Provenance `createdBy` documented as deterministic IDs only (userId GUID, agentId, playbookId — never user message text). Cosmos partition `/tenantId` enforced. Cosmos discriminator + id prefix prevent doc-type confusion.
- **ADR-029 (publish hygiene)**: ✅ +0.31 MB delta. No new packages. No HIGH CVE introduced. Within budget.
- **NFR-08 (11 node executors preserved)**: ✅ Not touched.
- **NFR-13 (safety pipeline preserved)**: ✅ Not touched.
- **NFR-07 (pre-fill flow preserved)**: ✅ Not touched.
- **NFR-16 (per-tenant isolation)**: ✅ Verified by 3 unit tests + defensive throws.
- **FR-32 (24h TTL + pin write-through)**: ✅ Verified by 2 unit tests.

---

## Escalations

**None.** Task completed within time budget; no surprises. The architectural decision (Cosmos `memory` container reuse with discriminator + id prefix) is documented in the placement-justification note for code-review visibility — surfacing as INFORMATIONAL to main session, NOT as a stop-and-confirm trigger.

---

## Commit message recommendation

```
feat(r6): Wave C-G1 task 051 — WorkspaceStateService (Pillar 6a, Q4 hybrid)

R6 D-C-02 — `WorkspaceStateService` implementing Q4 hybrid persistence for
canonical workspace tabs:
- Redis hot tier with key `workspace:{tenantId}:{sessionId}` (ADR-014 + NFR-16)
  and 24h sliding TTL (FR-32)
- Cosmos durable tier on pin/matter-attach, reusing existing `memory` container
  with `documentType: "workspace-tab"` discriminator + id prefix
  `workspace-tab_{tenantId}_{tabId}` (no new container; co-exists with
  MatterMemoryService docs)

C# DTOs mirror the R6 task 050 canonical TS `WorkspaceTab` interface:
- `WorkspaceTab` root record (matterContext, sourceProvenance, isPinned, etc.)
- `WorkspaceTabWidgetData` 4-variant discriminated union via System.Text.Json
  polymorphism (Summary | DocumentViewer | Dashboard | Table)

DI registration: single unconditional line in AnalysisServicesModule (no
Program.cs changes per ADR-010). §F.1 asymmetric-registration anti-pattern
not applicable — service has ZERO AI-internal deps.

Acceptance: 9/9 POML criteria PASS. 14 unit tests (per-tenant isolation, TTL,
pin write-through, close semantics, merge, polymorphism round-trip). Full
unit suite: 6,900 passed / 0 failed. Publish-size delta: +0.31 MB
(45.96 MB compressed; budget +5 MB R6; ceiling 50 MB ADR-029). No new CVE.

Files:
- src/server/api/Sprk.Bff.Api/Services/Workspace/IWorkspaceStateService.cs (NEW)
- src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceStateService.cs (NEW)
- src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceTab.cs (NEW)
- src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceTabWidgetData.cs (NEW)
- src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs (MODIFIED — registration + comment)
- tests/unit/Sprk.Bff.Api.Tests/Services/Workspace/WorkspaceStateServiceTests.cs (NEW)
- projects/spaarke-ai-platform-unification-r6/notes/task-051-placement-justification.md (NEW)
- projects/spaarke-ai-platform-unification-r6/notes/task-051-workspace-state-service-evidence.md (NEW)
```
