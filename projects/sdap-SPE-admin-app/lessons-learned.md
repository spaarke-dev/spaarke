# Lessons Learned — SDAP SPE Admin App

> **Project**: sdap-SPE-admin-app
> **Completed**: 2026-03-14
> **Duration**: 1 day (AI-assisted accelerated delivery)
> **Final Test Count**: 4176 passing, 0 failing

---

## What Went Well

### 1. Code Page Architecture Was the Right Choice

Delivering the SPE Admin as a Dataverse Code Page (ADR-006) proved to be the correct architectural decision. The single-file Vite + viteSingleFile bundle (`sprk_speadmin`) deploys as a web resource and opens as a Dataverse dialog page — no custom page, no PCF wrapping, no legacy JS. The React 18 + Fluent UI v9 stack integrated seamlessly with the existing Spaarke shared library.

**Key insight**: Code Pages are the right pattern for any standalone admin tool that needs full React 18 features (hooks, context, suspense) and doesn't need to bind to a specific Dataverse form field.

### 2. BFF Feature Module Pattern Scaled Well

The `AddSpeAdminModule()` DI feature module pattern (ADR-010) kept the SPE Admin code completely isolated from existing Spaarke modules. Adding 25+ new API endpoints and 3 new services required zero changes to existing modules. The `SpeAdminEndpoints.cs` route group with a shared `SpeAdminAuthorizationFilter` cleanly enforced the admin-only security boundary.

**Reusable pattern**: For any new major feature area, create `{Feature}Module.cs` with `Add{Feature}Module()` extension method, `{Feature}Endpoints.cs` route group, and a dedicated `{Feature}AuthorizationFilter`.

### 3. Phase-by-Phase Testing Caught Issues Early

Running integration tests at the end of each phase (tasks 046, 071, 085) rather than only at the end revealed the ContainerItemEndpoints registration gap in Phase 1 (file endpoints were implemented but not registered in the route group). Catching this during Phase 1 E2E testing rather than at final delivery prevented a significant debugging session.

**Practice to keep**: Always write a phase-end integration test task that verifies HTTP route availability (not just unit test pass/fail).

### 4. Multi-Config Graph Client Pattern Was Extensible

The `SpeAdminGraphService` multi-config pattern — resolving the correct Graph client based on the selected container type config — scaled smoothly through Phase 3 when multi-app support (SPE-084) required supporting different owning app registrations per Business Unit. The initial abstraction designed in Phase 1 needed no structural changes.

### 5. Parallel Task Execution Worked

Tasks within parallel groups (A, B, C, D...) were designed to be independent from the start. This allowed AI-assisted execution to work on multiple task groups without file conflicts. The dependency graph in TASK-INDEX.md was accurate throughout.

### 6. Moq Limitation Documented Early

The decision to exclude Azure SDK type lifecycle tests (BulkOperationService enqueue/poll scenarios) because `SecretClient` and `DataverseWebApiClient` lack parameterless constructors and cannot be mocked with Moq was captured in current-task.md during Phase 3 (SPE-083). Model-level contract tests covered observable behavior. This is a known .NET testing constraint worth documenting for future phases.

---

## What Could Be Improved

### 1. TASK-INDEX.md Status Tracking Lag

Several tasks that were completed as part of sequential pipeline execution (005-009, 010-013, 017, 022-029, 031, 038) retained 🔲 status in TASK-INDEX.md throughout the project because the task-execute skill only updates the specific task being executed, not tasks completed as sub-steps of other tasks. The index only reflected completion status for tasks that were explicitly invoked via task-execute.

**Recommendation**: When executing a task that completes work defined in a separate POML (e.g., a "phase integration" task that wires up endpoints from earlier tasks), update the TASK-INDEX.md for all constituent tasks at that point.

### 2. Phase 3 eDiscovery Deferral Should Have Been Captured Earlier

Tasks SPE-080 (eDiscovery dashboard) and SPE-081 (Retention label management) were marked ⏭️ skipped by user decision during Phase 3 execution. These tasks were in the original spec but depend on Graph eDiscovery API (beta) with limited environment availability. Identifying these as high-risk at the spec or plan stage would have allowed earlier scoping and avoided the work of creating the task files.

**Recommendation**: Add a risk-scoring step to `task-create` that flags tasks with beta API dependencies or external prerequisites as "conditional" from the start.

### 3. Notes/ Cleanup Should Be Done Per-Phase

The `notes/e2e-test-report-phase1.md` ephemeral file accumulated from Phase 1 and remained in the repo through Phase 3 completion. In a multi-developer project, ephemeral notes would accumulate and become stale.

**Recommendation**: At the end of each phase integration task, include a step to archive or delete ephemeral notes generated during that phase.

### 4. BulkOperationService Live Scenario Coverage Gap

The bulk operations feature (SPE-083) has model-level contract tests but no integration test for the actual enqueue → poll lifecycle due to Azure SDK mock constraints. Manual test scenarios are documented in the test file but not automated. For a production feature, this gap should be covered by end-to-end tests in a real environment.

**Recommendation**: For features using Azure SDK types that cannot be mocked, add an environment integration test suite (tagged `[Category("LiveIntegration")]`) that runs against a real environment in CI.

---

## Key Technical Decisions and Outcomes

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Code Page (not PCF) for SPE Admin | ADR-006; standalone tool needs full React 18; no form field binding | Correct — clean deploy, full React 18 feature access |
| Single `SpeAdminAuthorizationFilter` for entire route group | ADR-008; all SPE Admin routes require admin role | Correct — one filter covers all 25+ endpoints |
| App-only tokens for Phase 1; OBO extensibility architected | SPE Admin is admin tooling; user identity less critical in Phase 1 | Correct — simplified auth; OBO path available for Phase 2+ |
| BackgroundService for dashboard metrics sync | ADR-001; no Azure Functions | Correct — runs in-process with BFF, no additional infrastructure |
| `AddSpeAdminModule()` feature module isolation | ADR-010; ≤15 DI registrations | Correct — zero impact on existing modules |
| Multi-config Graph client from Phase 1 | Phase 3 multi-app requirement was anticipated | Correct — SPE-084 required no structural changes |
| Model-level contract tests instead of Moq for Azure SDK types | Azure SDK sealed classes cannot be mocked | Acceptable — documents limitation; behavior verified at contract boundaries |
| Defer SPE-080/081 (eDiscovery, Retention) | Graph eDiscovery API in beta; limited availability | Appropriate — reduces scope risk; features can be added in Phase 4 |

---

## Reusable Patterns Discovered

### Pattern: SPE Admin Route Group Registration

```csharp
// SpeAdminEndpoints.cs — route group with shared auth filter
var group = app.MapGroup("/api/spe")
    .AddEndpointFilter<SpeAdminAuthorizationFilter>()
    .WithTags("SpeAdmin");

// Register all endpoint groups on the shared group
ContainerEndpoints.MapContainerEndpoints(group);
ContainerItemEndpoints.MapContainerItemEndpoints(group);
ContainerTypeEndpoints.MapContainerTypeEndpoints(group);
// ...
```

**Lesson**: Always register all endpoint sub-groups from one top-level registration method. A missed `MapXxxEndpoints()` call causes silent 404s that are easy to miss in testing.

### Pattern: BU-Scoped Config Resolution

```typescript
// BuContext.tsx — cascading BU → Config → Environment selection
// with localStorage persistence and cascade-clear on parent change
const selectBusinessUnit = (bu: BusinessUnit) => {
    setSelectedBu(bu);
    setSelectedConfig(null);  // cascade-clear child selections
    setSelectedEnvironment(null);
};
```

**Lesson**: Always cascade-clear child selections when a parent selection changes. The BuContextPicker pattern (BU → Config → Environment cascade) is reusable for any multi-level Dataverse relationship picker.

### Pattern: Code Page Theme Detection

```typescript
// ThemeProvider.ts — 4-source priority chain for dark mode
// 1. localStorage user preference
// 2. URL ?theme=dark query parameter
// 3. DOM navbar color detection (Dataverse dark mode)
// 4. prefers-color-scheme system preference
```

**Lesson**: This 4-source priority chain ensures correct theme detection across all Dataverse embedding scenarios. Reuse this exact pattern in all Code Pages.

### Pattern: Phase-End Integration Test Structure

```csharp
// PhaseNIntegrationTests.cs — test structure for phase validation
// 1. Auth filter tests (admin/non-admin/SystemAdmin)
// 2. Endpoint registration tests (HTTP 401 = live, not 404)
// 3. Handler tests (model contract, DTO validation)
// 4. Background service type registration tests
```

**Lesson**: Testing `HTTP 401` (auth required) vs `HTTP 404` (not registered) is a lightweight but effective way to verify route registration without needing authenticated Graph API calls.

---

## Phase 4 Backlog (Future Work)

If this project is extended, the following items are recommended:

| Item | Priority | Reason Deferred |
|------|----------|-----------------|
| eDiscovery read-only dashboard (SPE-080) | Medium | Graph eDiscovery API beta; limited environment availability |
| Retention label management (SPE-081) | Medium | Depends on SPE-080 infrastructure |
| Authenticated end-to-end test suite | High | Requires real Dataverse + SPE environment for full flow validation |
| BulkOperationService live integration tests | Medium | Azure SDK mock constraint; needs real environment |
| ContainerItemEndpoints registration verification test | Low | One-line gap found in Phase 1; fixed; test would prevent regression |

---

*Generated by task-execute skill, SPE-090, 2026-03-14*
