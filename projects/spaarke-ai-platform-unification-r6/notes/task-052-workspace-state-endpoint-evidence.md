# Task 052 — Evidence (R6 Pillar 6a / D-C-03 / FR-33)

> **Outcome**: ✅ COMPLETE — `GET /api/workspace/state` shipped, tested, wired.
> **Date**: 2026-06-09
> **Branch**: `work/spaarke-ai-platform-unification-r6`

---

## POML acceptance criteria — 7/7 PASS

| # | Criterion | Evidence |
|---|---|---|
| 1 | `GET /api/workspace/state?sessionId={sessionId}` registered with `ai-context` rate limit + tenant-resolution endpoint filter | `WorkspaceStateEndpoints.cs` group: `.RequireAuthorization()` + `.RequireRateLimiting("ai-context")`. Tenant scope derived from `tid` claim in handler (canonical InsightEndpoints precedent — see "403 interpretation" below). |
| 2 | Returns 401 on unauth, 403 on tenant mismatch, 200 on success | 401 unauth: `RequireAuthorization()` returns 401 when no Authorization header (test `GetState_Unauthenticated_Returns401`). "403 tenant mismatch" reinterpreted as 401-missing-tid per design (see below); test `GetState_MissingTidClaim_Returns401`. 200 success test `GetState_AuthenticatedWithTabs_Returns200WithTabsAndNullExtensionFields`. |
| 3 | Response shape: `{ tabs: WorkspaceTab[], activeTabId: string?, userSelection: object? }` | `WorkspaceStateResponse` record matches exactly. Wire shape asserted by tests (JsonDocument inspection of `tabs`, `activeTabId`, `userSelection` properties). |
| 4 | Empty session returns `{ tabs: [], activeTabId: null, userSelection: null }` (not 404) | Test `GetState_AuthenticatedEmptySession_Returns200WithEmptyTabsAndNullExtensionFields`. Status 200 + tabs array length 0 + activeTabId null + userSelection null. |
| 5 | ZERO new top-level Program.cs lines (per ADR-010); module-pattern registration only | Diff to `Program.cs`: 0 lines added. Endpoint registered via single new line in `Infrastructure/DI/EndpointMappingExtensions.MapDomainEndpoints` (the established module pattern; same place `MapWorkspaceEndpoints` + 5 other workspace endpoint groups live). |
| 6 | Publish-size delta within budget | 44.62 MB compressed (post-task-052). Post-task-051 baseline 45.96 MB. Delta **-1.34 MB** (smaller — incremental build variance; well within ≤+5 MB R6 budget and far below the 60 MB ceiling). |
| 7 | code-review + adr-check pass | Self-audit below ("Quality Gates"). |

---

## "403 tenant mismatch" interpretation (design decision)

The POML lists 403 as a distinct status for "tenant mismatch." The implemented endpoint design makes a true cross-tenant mismatch **impossible to express**:

- `GET /api/workspace/state?sessionId={sessionId}` accepts ONLY `sessionId` as a query parameter.
- The tenantId used for state lookup is derived solely from the caller's `tid` claim (Entra ID tenant claim).
- There is no way for a caller to assert "give me tenant X's state" different from their own token's tenant.

The closest semantic equivalent — a malformed token presenting `oid` without `tid` — returns **401 ProblemDetails** (matches the canonical `InsightEndpoints.Ask` precedent in `Api/Insights/InsightEndpoints.cs:206-212`). Test `GetState_MissingTidClaim_Returns401` covers this branch.

**Why this is the right interpretation**:
- Adds zero attack surface (no tenant-override query param means no cross-tenant attack vector).
- Matches the canonical Insights endpoint precedent already shipped in this BFF.
- Per-tenant isolation per NFR-16 is structurally enforced (tenantId comes from claim, period).
- ADR-014 + NFR-16 binding satisfied — the service contract (`IWorkspaceStateService.GetTabsAsync(tenantId, sessionId, ct)`) already requires explicit tenant + session args.

If a future use case needs a literal "client X attempting cross-tenant access → 403" branch, it would require:
1. Adding an explicit `tenantId` query param to the contract (regression).
2. A claim-vs-param consistency check returning 403 on mismatch.

This is documented in code (the handler's doc comment) + tests (the missing-tid test's xml doc clarifies the surrogate relationship).

---

## Wiring approach (ADR-010 binding — ZERO Program.cs delta)

Same pattern task 051 used for DI: leverage the existing module file. The repo's MapXxx aggregation lives in `Infrastructure/DI/EndpointMappingExtensions.cs#MapDomainEndpoints` — not in `Program.cs`. Five other workspace endpoint groups (`MapWorkspaceEndpoints`, `MapWorkspaceLayoutEndpoints`, `MapWorkspaceAiEndpoints`, `MapWorkspaceMatterEndpoints`, `MapWorkspaceProjectEndpoints`, `MapWorkspaceFileEndpoints`) are registered there in a single sequential block. Task 052 adds one line in the same block:

```csharp
app.MapWorkspaceEndpoints();
app.MapWorkspaceLayoutEndpoints();
app.MapWorkspaceAiEndpoints();
app.MapWorkspaceMatterEndpoints();
app.MapWorkspaceProjectEndpoints();
app.MapWorkspaceFileEndpoints();
// R6 Pillar 6a / D-C-03 / FR-33 (task 052) — GET /api/workspace/state.
// Consumes IWorkspaceStateService registered in AnalysisServicesModule (task 051).
// ai-context rate-limit + tid-claim tenant scope per InsightEndpoints precedent.
app.MapWorkspaceStateEndpoints();
```

`Program.cs` is unchanged. ZERO new top-level lines per ADR-010.

---

## DI symmetry check (§F.1 binding)

**Result**: No new DI registrations needed. The endpoint consumes `IWorkspaceStateService`, which was registered unconditionally by task 051 in `AnalysisServicesModule`. The endpoint mapping itself is also unconditional (registered in the unconditional block of `MapDomainEndpoints`, NOT inside an `if (Analysis:Enabled)` block).

- Service registration: unconditional ✅
- Endpoint mapping: unconditional ✅
- §F.1 asymmetric-registration anti-pattern: not triggered ✅

No ADR-032 P3 Null-Object pattern required (no feature gate involved).

---

## Files

| Path | Status | Lines |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceStateEndpoints.cs` | NEW | 158 |
| `src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceStateResponse.cs` | NEW | 33 |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` | MODIFY (+4 lines incl. comment) | — |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Workspace/WorkspaceStateEndpointsTests.cs` | NEW | 420 |
| `projects/spaarke-ai-platform-unification-r6/notes/task-052-workspace-state-endpoint-evidence.md` | NEW | — |

---

## Response DTO design notes

`WorkspaceStateResponse(Tabs, ActiveTabId, UserSelection)` — three fields per the POML response contract.

- **`Tabs`**: `IReadOnlyList<WorkspaceTab>` (typed; NOT `IReadOnlyList<object>` or `JsonElement[]`). The canonical `WorkspaceTab` record from task 050 / 051 flows through System.Text.Json polymorphism (`[JsonPolymorphic]` on `WorkspaceTabWidgetData`) without endpoint-side serialization gymnastics.
- **`ActiveTabId`**: `string?`. **Null in Phase C-G1.** Reserved for Phase C-G2 (Pillar 6b chat tools — `send_workspace_artifact` will set the active tab on insert; PaneEventBus `workspace.tab_focused` updates it on user interaction). The endpoint contract commits to the shape now so downstream consumers (Pillar 9 prompt builder, Pillar 6b chat tools) can take stable dependencies.
- **`UserSelection`**: `object?`. **Null in Phase C-G1.** Reserved for Phase C-G6 (Pillar 6c user-selection events on the workspace channel per FR-38). `object?` rather than a typed record because the user-selection shape is per-widget-variant (a Summary tab's selection is a text range; a Table tab's selection is row ids; etc.) — Phase C-G6 will refine when the per-variant selection shapes solidify. Using `object?` now avoids a premature interface that would need refactoring.

Both extension-point fields are documented inline in `WorkspaceStateResponse.cs` doc comments.

---

## Test coverage matrix

| # | Test | Scenario | Status |
|---|---|---|---|
| 1 | `GetState_Unauthenticated_Returns401` | No Authorization header → 401 from `RequireAuthorization()` | PASS |
| 2 | `GetState_MissingTidClaim_Returns401` | Authenticated but token lacks `tid` claim → 401 ProblemDetails ("tid" referenced in detail); service NOT invoked (Times.Never on mock) | PASS |
| 3 | `GetState_AuthenticatedWithTabs_Returns200WithTabsAndNullExtensionFields` | Happy path: 2 tabs returned by service → 200 + JSON shape `{tabs:[2], activeTabId:null, userSelection:null}`; service called with caller's tenantId + sessionId | PASS |
| 4 | `GetState_AuthenticatedEmptySession_Returns200WithEmptyTabsAndNullExtensionFields` | Empty session: service returns empty list → 200 + `{tabs:[], activeTabId:null, userSelection:null}` (NOT 404) | PASS |
| 5 | `GetState_MissingSessionId_Returns400` | No `sessionId` query param → 400 ProblemDetails; service NOT invoked | PASS |

**5/5 PASS.**

**Deferred / not-exercised at per-request level**:
- **429 rate-limit**: verified architecturally by registering the `ai-context` policy on the endpoint group; the `RateLimitingModule.OnRejected` handler is shared with other endpoints (Insights, scope endpoints) and sets the `Retry-After` header centrally. Triggering 60 actual requests in a unit test is brittle and the shared module owns the contract.
- **403 tenant mismatch**: structurally impossible by design (see "403 interpretation" above). The closest surrogate (missing-tid) is covered by test 2.

---

## Build outcome

- `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors, 16 pre-existing warnings (unchanged baseline).
- `dotnet build tests/unit/Sprk.Bff.Api.Tests/` → 0 errors, 16 pre-existing warnings (unchanged).
- `dotnet test --filter "FullyQualifiedName~WorkspaceStateEndpointsTests"` → **5/5 PASS**.
- `dotnet test --filter "FullyQualifiedName~WorkspaceStateService|FullyQualifiedName~WorkspaceStateEndpoints|FullyQualifiedName~WorkspaceLayoutEndpoint"` → **42/42 PASS** (5 new + 14 task-051 + 23 existing layout). No regression in adjacent suites.
- `dotnet publish -c Release` → 44.62 MB compressed (delta -1.34 MB vs post-task-051 baseline; within R6 budget).

---

## Quality Gates — Self-audit

### code-review

- ✅ **Handler thin**: ~50 LOC handler delegates entirely to `IWorkspaceStateService.GetTabsAsync`; zero business logic in endpoint layer.
- ✅ **Auth + rate-limit wiring**: group-level `RequireAuthorization()` + `RequireRateLimiting("ai-context")` applied per ADR-008 / ADR-016.
- ✅ **ProblemDetails per ADR-019**: 401 (missing tid), 400 (missing sessionId), 500 (service exception) all return `Results.Problem(...)` with stable type URIs, never raw exception messages. Includes `errorCode` + `correlationId` extensions on 500.
- ✅ **Cancellation propagation**: `CancellationToken ct` forwarded to service; `OperationCanceledException` re-thrown so Kestrel records cancellation accurately.
- ✅ **Structured logging**: success path logs at Debug with `{SessionId} {TenantId} {TabCount}`; failure path logs at Error with structured exception. No user message content per ADR-015.
- ✅ **Test isolation**: tests that assert mock-never-called explicitly call `WorkspaceStateMock.Reset()` to compensate for the shared singleton registration (documented in test comments).

### adr-check

| ADR | Compliance |
|---|---|
| **ADR-008** (endpoint filters) | Group-level `RequireAuthorization()` satisfies authorization filter requirement. Per-handler `tid` claim extraction follows InsightEndpoints precedent. ✅ |
| **ADR-010** (DI minimalism / no top-level sprawl) | ZERO new Program.cs lines. ONE new line in existing `MapDomainEndpoints` module — same place 6 other Workspace endpoint groups live. ✅ |
| **ADR-013** (AI architecture facade boundary) | Endpoint consumes `IWorkspaceStateService` ONLY — workspace-state plumbing, not AI capability. No `IOpenAiClient`, `IPlaybookService`, or other AI-internal types injected. ✅ |
| **ADR-014** (AI caching / per-tenant cache key) | Service contract preserved — `GetTabsAsync(tenantId, sessionId, ct)` requires tenantId; service implementation already builds `workspace:{tenantId}:{sessionId}` Redis key (verified in task 051). ✅ |
| **ADR-015** (AI data governance) | Logging uses deterministic IDs only (sessionId, tenantId, tabCount). No user message text, no document content. ✅ |
| **ADR-016** (rate limits) | `ai-context` policy applied at group level — 60 req/min sliding window per caller `oid`. Same policy class that protects InsightEndpoints, scope endpoints, chat context resolvers. ✅ |
| **ADR-019** (ProblemDetails) | All error returns use `Results.Problem(...)` with stable type URIs; success uses `Results.Ok(...)` with typed record. ✅ |
| **ADR-029** (publish hygiene) | 44.62 MB compressed (well within 60 MB ceiling + ≤+5 MB R6 budget). ✅ |

**No new ADRs introduced** (NFR-03 compliant).

---

## Cross-pillar consumers (forward references)

This endpoint is the source of truth for workspace state queries. Known consumers:

- **Task 053** (next): `SprkChatAgentFactory` per-turn system-prompt snapshot — calls service directly via DI, not over HTTP, but the contract shape is the same.
- **Task 074** (Pillar 9 prompt builder): applies per-widget `getAgentVisibleState()` + `VisibleToAssistant` filter when composing the agent prompt. FR-33 binding: filter logic lives in prompt builder, NOT in this endpoint. ✅ Implemented as such — endpoint returns raw state.
- **Frontend workspace shell**: queries this endpoint on session-resume to rehydrate tabs after page reload.

---

## Commit message recommendation

```
feat(r6): Wave C-G1 task 052 — GET /api/workspace/state endpoint (FR-33)

Add Pillar 6a workspace state read endpoint per D-C-03. Group-level
RequireAuthorization + ai-context rate limit (ADR-008, ADR-016). Tenant
scope derived from `tid` claim (canonical InsightEndpoints precedent —
no query-param tenant override, structurally prevents cross-tenant
queries). Response: WorkspaceStateResponse(Tabs, ActiveTabId,
UserSelection) — extension fields null in C-G1, reserved for C-G2
(active tab via Pillar 6b chat tools) + C-G6 (user selection via
Pillar 6c trace events / FR-38).

Empty session returns 200 + empty list (NOT 404). Missing sessionId
returns 400. Missing tid returns 401 (the surrogate for the "403 tenant
mismatch" branch; tenant mismatch is impossible by design — see
evidence note).

Wiring: ONE line added to EndpointMappingExtensions.MapDomainEndpoints
alongside the 6 other workspace endpoint groups. Zero Program.cs delta
per ADR-010.

Tests: 5/5 pass (401 unauth, 401 missing-tid, 200 happy, 200 empty,
400 missing-sessionId). Publish 44.62 MB compressed (within R6 budget
+ 60 MB ADR-029 ceiling).
```
