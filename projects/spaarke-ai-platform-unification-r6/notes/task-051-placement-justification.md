# Task 051 — Placement Justification (R6 Pillar 6a / D-C-02)

> **Source rule**: [`.claude/constraints/bff-extensions.md` §A.1](.claude/constraints/bff-extensions.md) (binding — every BFF addition MUST include placement justification).
> **Source ADR**: [ADR-013 (refined 2026-05-20)](.claude/adr/ADR-013-ai-architecture.md) — Decision Criteria table.
> **Date**: 2026-06-09
> **Author**: Task 051 sub-agent

---

## Component

**`WorkspaceStateService`** — Q4 hybrid persistence for canonical R6 workspace tabs (Redis hot tier 24h TTL + Cosmos durable tier on pin/matter-attach).

Files:
- `src/server/api/Sprk.Bff.Api/Services/Workspace/IWorkspaceStateService.cs` (NEW)
- `src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceStateService.cs` (NEW)
- `src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceTab.cs` (NEW — C# mirror of TS canonical shape)
- `src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceTabWidgetData.cs` (NEW — 4-variant discriminated union)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` (MODIFIED — single unconditional `AddScoped<IWorkspaceStateService, WorkspaceStateService>()` registration)

---

## Decision: stays in BFF

Per the ADR-013 4-criteria decision matrix:

| Question | Answer | Rationale |
|---|---|---|
| Does it have a latency/TTFB budget against BFF state (<500ms)? | **YES** | Per-turn snapshot reads (task 053 chat agent factory) MUST complete inside the chat-message TTFB budget; a separate deployable would add network hops. |
| Does it write to BFF-managed session/audit/safety state in the same request lifecycle? | **YES** | Tab mutations from chat tools (Pillar 6b) coexist with the agent invocation that produced them; they share the chat request scope. |
| Does it require retroactive annotation of a streaming response? | **YES** | Pillar 6c execution-trace events (`workspace.tab_edited`, `workspace.user_selection`, etc.) are emitted ON the SSE stream that the chat tool is mutating in. |
| Is it event-driven with no synchronous user wait? | **NO** | All workspace-state operations are user-initiated and synchronous against the (Pillar 6a `GET /api/workspace/state`) endpoint OR the chat tool dispatch path. |

**Result**: 3 of 3 BFF-criteria answers are YES; the event-driven criterion is NO. Placement = BFF. ✅

---

## ADR-013 compliance — no AI-internal types injected

`WorkspaceStateService` is **workspace-state plumbing**, NOT AI capability. Its constructor dependencies are:

```csharp
public WorkspaceStateService(
    IDistributedCache cache,          // generic caching (ADR-009)
    CosmosClient cosmosClient,         // generic persistence
    IConfiguration configuration,      // BFF config
    ILogger<WorkspaceStateService> logger)
```

ZERO of the following appear:
- `IOpenAiClient` — NOT injected
- `IPlaybookService` — NOT injected
- `IPlaybookOrchestrationService` — NOT injected
- `IRagService` — NOT injected
- `IInsightsAi` / `IBriefingAi` / `IWorkspacePrefillAi` — NOT injected

Per refined ADR-013 facade boundary, this service may be **CONSUMED BY** AI code (task 053 `SprkChatAgentFactory` per-turn snapshot), but it does NOT consume AI code itself. The direction of dependency is correct: AI → workspace plumbing, never plumbing → AI.

---

## Cosmos container decision: REUSE `memory` container (no new container introduced)

Per the POML hint: "Cosmos durable rows go to either the existing `memory` container or a new `workspace_tabs` container (extend existing pattern; do NOT introduce new container if `memory` suffices)."

**Decision**: REUSE the existing `memory` container.

**Rationale**:
- The `memory` container is already provisioned with `partition key /tenantId`, 90-day retention, GDPR Art. 17 erasure semantics — all match workspace-tab needs.
- A discriminator field `documentType: "workspace-tab"` + id prefix `workspace-tab_{tenantId}_{tabId}` keeps workspace docs structurally distinct from `MatterMemoryService` docs (`{tenantId}_{matterId}`). No id collision possible.
- Query parity: `SELECT * FROM c WHERE c.documentType = "workspace-tab" AND c.sessionId = @sessionId` partitioned by `/tenantId` requires no new container index.
- Eliminates a new infra-provisioning task (would have added a Bicep change to `infra/cosmos/` per ADR-015 Tier 3 container convention).
- Aligns with ADR-014 + NFR-16 binding "scope keys by tenant" — partition-key isolation is preserved.

**Trade-off considered**: A dedicated `workspace_tabs` container would have given workspace tabs an independent TTL knob from matter-memory facts. Rejected because:
- The Q4 hybrid model says "Redis hot 24h + Cosmos durable on pin"; the Cosmos retention is governance-policy-driven (90 days), not workspace-state-driven.
- Workspace-tab queries by partition+sessionId are read-heavy on RU; sharing the partition with matter-memory amortizes RU provisioning across two domains rather than fragmenting.

---

## ADR-010 compliance — no new top-level Program.cs lines

Registration is a **single line** added inside `AnalysisServicesModule.AddAnalysisServicesModule(...)`:

```csharp
// R6 Pillar 6a (task 051, D-C-02) — WorkspaceStateService. ...
services.AddScoped<IWorkspaceStateService, WorkspaceStateService>();
```

ZERO modifications to `Program.cs`. The feature-module DI seam is preserved.

---

## ADR-014 + NFR-16 binding — per-tenant Redis isolation

Redis cache-key format: **`workspace:{tenantId}:{sessionId}`**.

- TenantId appears in EVERY cache key (binding per NFR-16).
- Cross-tenant reads are structurally impossible — same sessionId across two tenants produces two distinct keys.
- Unit test `BuildRedisKey_IsolatesTenants_ForSameSessionId` + `UpsertTabAsync_WritesToTenantSpecificRedisKey` + `GetTabsAsync_ReturnsOnlyOwnTenantData` lock this in.
- Additional defensive check in `UpsertTabAsync`: throws `InvalidOperationException` if `tab.TenantId != tenantId arg` (defends against caller-side bugs).

Cosmos durable tier: tenantId is the partition key (`/tenantId`) per ADR-015; doc id includes tenantId in the prefix; queries are partitioned. Same isolation guarantees.

---

## §F.1 Asymmetric-Registration Static-Scan Result — COMPLIANT

Per [`.claude/constraints/bff-extensions.md` §F.1](.claude/constraints/bff-extensions.md) (binding rule for new `*Module.cs` additions):

1. **Identify endpoint handlers that inject the service**:
   - Task 052 endpoint (next in Wave C-G1) will inject `IWorkspaceStateService` into `GET /api/workspace/state`.
   - Task 053 (`SprkChatAgentFactory` per-turn snapshot) will inject `IWorkspaceStateService` into the agent factory.
   - Future Pillar 6b chat tools (`send_workspace_artifact`, `update_workspace_tab`, `close_workspace_tab`) — will resolve via `IServiceProvider` per existing tool-handler pattern.

2. **Verify symmetric registration**:
   - `IWorkspaceStateService` is registered **UNCONDITIONALLY** inside `AnalysisServicesModule.AddAnalysisServicesModule(...)` — NOT wrapped in any `if (flag) { ... }` block.
   - Endpoint mapping (task 052) is also expected to be unconditional in `EndpointMappingExtensions.cs` — both surfaces are symmetric.

3. **Anti-pattern check**:
   - **NO** new `if (flag) { ... }` block introduced.
   - **NO** Null-Object peer needed — the service has only generic deps (`IDistributedCache`, `CosmosClient`), both of which are unconditional via `AddCacheModule` + `AddAiPersistenceModule`. If those modules are deactivated (e.g., during test fixture setup), the service constructor throws on missing config (`CosmosPersistence:DatabaseName`) — a contract-violation failure mode, not a kill-switch one.

**Conclusion**: §F.1 compliant. No ADR-032 P3 Fail-Fast Null peer required.

---

## ADR-029 (BFF publish hygiene) — size delta within budget

| Measurement | Value |
|---|---|
| Compressed publish size (this PR) | **45.96 MB** |
| Compressed publish size (master Phase 5 close baseline) | 45.65 MB |
| Delta vs Phase 5 baseline | **+0.31 MB** |
| R6 NFR-02 cumulative budget | ≤+5 MB across R6 |
| Per-task escalation threshold | ≥+5 MB single-task delta |
| ADR-029 hard ceiling | 50 MB compressed |

Status: **WITHIN BUDGET** (0.31 MB delta is negligible; ceiling slack 4.04 MB).

---

## CVE check — no NEW HIGH-severity vulnerabilities

`dotnet list package --vulnerable --include-transitive` returns:

- `Microsoft.Kiota.Abstractions 1.21.2 — High — GHSA-7j59-v9qr-6fq9`

This is **pre-existing accepted risk** per ADR-029: "Kiota HIGH is accepted risk pending separate Graph SDK 6.x upgrade project." NOT introduced by this PR.

Status: **NO NEW CVE.**

---

## Test obligation (binding per bff-extensions §F)

Per `[Test update obligation](.claude/constraints/bff-extensions.md#f-test-update-obligation-binding-per-fr-22--d-05)`, this PR modifies `src/server/api/Sprk.Bff.Api/Services/` and therefore MUST include unit test coverage.

Added: `tests/unit/Sprk.Bff.Api.Tests/Services/Workspace/WorkspaceStateServiceTests.cs` — 14 tests covering:

1. `BuildRedisKey_IsolatesTenants_ForSameSessionId` — per-tenant key isolation (NFR-16)
2. `UpsertTabAsync_WritesToTenantSpecificRedisKey` — two tenants do not collide
3. `GetTabsAsync_ReturnsOnlyOwnTenantData` — cross-tenant read isolation
4. `UpsertTabAsync_ThrowsOnTenantMismatch` — defensive tenantId enforcement
5. `UpsertTabAsync_SetsRedisTtlTo24Hours` — FR-32 TTL
6. `PinTabAsync_WritesThroughToCosmos_WithMatterIdTagAndIsPinnedTrue` — Q4 hybrid pin promotion
7. `PinTabAsync_PreservesHotTierAfterPromotion` — Redis row preserved post-pin
8. `PinTabAsync_ThrowsKeyNotFound_WhenTabNotPresent` — error handling
9. `CloseTabAsync_RemovesFromRedis_DoesNotTouchCosmos` — close semantics
10. `CloseTabAsync_PreservesOtherTabs_InSameSession` — partial-session close
11. `CloseTabAsync_IsIdempotent_WhenTabMissing` — idempotency
12. `GetTabsAsync_MergesHotAndDurable_HotWinsOnIdCollision` — merge semantics
13. `GetTabsAsync_ReturnsEmpty_WhenNoTabsExist` — empty case
14. `JsonPolymorphism_RoundTripsAllFourWidgetDataVariants` — 4-variant discriminator round-trip

All 14 pass. Full unit suite (6,900 tests) passes with no regressions.

---

## Pre-merge checklist (§A.1 of bff-extensions.md)

- [x] (1) Considered placement outside BFF — see decision matrix above
- [x] (2) Cited relevant ADRs — ADR-010, ADR-013, ADR-014, ADR-015, ADR-029
- [x] (3) Verified publish-baseline regression — +0.31 MB delta documented
- [x] (4) MUST NOT add new direct CRUD→AI dependency — service consumes ZERO AI internal types
- [x] (5) Follows feature-module DI conventions — single line in `AnalysisServicesModule`

Status: **READY TO MERGE.**
