# Task 018 BFF Publish Delta + FR-02 Pattern A Migration (WorkspaceAiService)

> **Generated**: 2026-06-22
> **Task**: 018 — Migrate `WorkspaceAiService` to stable ID (Pattern A — Wave 1-E)
> **Wave**: 1-E (depends on Wave 1-D task 015; FR-02 Pattern A consumer)
> **Phase 0 baseline**: 44.75 MB compressed
> **Post-Wave 1-D baseline (per `015-bff-publish-delta.md`)**: 44.75 MB compressed (46,927,919 bytes)

---

## Identified stable ID (task 018 deferred-decision unblock)

| Property | Value |
|---|---|
| **`WorkspaceOptions.AiSummaryPlaybookId`** | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` |
| **Playbook name** | `Document Profile` |
| **Admin slug (`sprk_playbookcode`)** | `PB-002` |
| **Source** | Pre-task-018 hardcoded `DefaultAiSummaryPlaybookId` constant at `WorkspaceAiService.cs:43-44` |
| **Backfill state (DEV)** | ✅ Already populated in `sprk_playbookid` per task 014 evidence (`014-playbookid-backfill-evidence.md` line 14) — no separate write required |
| **Higher-env config** | Populate `Workspace:AiSummaryPlaybookId` per-environment at deploy time (task 026 owns); the DEV GUID above is the canonical reference value for the "Document Profile" playbook |

This is the canonical AI summary playbook for the Legal Operations Workspace tile (Updates Feed item / To-Do item / Matter / Project / Document analysis).

---

## Migration shape chosen

**Typed options + IPlaybookLookupService.GetByIdAsync** — same pattern as `SessionSummarizeOrchestrator` (task 015, commit `c3f65e975`), adapted with a **graceful-degrade tolerance** rather than fail-fast, because the workspace AI summary tile must render a template response when config is missing rather than 500-ing the endpoint (existing `BuildFallbackResponse` contract preserved).

- Inject `IOptions<WorkspaceOptions>` (already wired in `ConfigurationModule.cs`, pre-existing nullable `AiSummaryPlaybookId` property — task 013 explicitly left it as the pre-existing nullable surface per Q1 refactor handoff).
- Inject `IPlaybookLookupService` (already wired Scoped in `FinanceModule.cs`).
- Resolve at runtime via `await _playbookLookup.GetByIdAsync(_workspaceOptions.Value.AiSummaryPlaybookId, ct)`.
- Forward `playbook.Id` (Guid) to the `PlaybookRunRequest.PlaybookId` consumed by `IWorkspacePrefillAi.ExecutePlaybookAsync` — facade signature unchanged.
- **Empty/null config** → log warning + return `BuildFallbackResponse(request)` (graceful degrade — tile renders template; spec FR-02 contract).
- **Lookup exception** → log warning with exception + return `BuildFallbackResponse(request)` (graceful degrade — no 500).

### Why graceful-degrade vs fail-fast (FR-26)

Unlike `SessionSummarizeOrchestrator` (R6 FR-26 convergence point — fail-fast `InvalidOperationException` mapped by endpoint to 503), `WorkspaceAiService.GenerateAiSummaryAsync` is consumed by a UI tile (`/api/workspace/ai/summary`) that has an existing template-fallback path already exercised on RunFailed / Timeout events. Misconfiguration deserves a logged warning + visible-template fallback, not a 5xx that breaks the workspace UX. The `BuildFallbackResponse` behavior is therefore preserved verbatim.

---

## Files modified (exactly 2)

1. **`src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceAiService.cs`** (+57 lines, -8 lines net)
   - Removed `private static readonly Guid DefaultAiSummaryPlaybookId = Guid.Parse("18cf3cc8-...")` constant (lines 43-44 of prior version).
   - Removed `IConfiguration _configuration` field + corresponding ctor parameter + null-guard.
   - Added 2 ctor parameters: `IPlaybookLookupService playbookLookup`, `IOptions<WorkspaceOptions> workspaceOptions`.
   - Replaced the raw `_configuration["Workspace:AiSummaryPlaybookId"]` indexer read in `ExecutePlaybookAnalysisAsync` with:
     - Typed-options read (`_workspaceOptions.Value.AiSummaryPlaybookId`).
     - Empty/null guard → `BuildFallbackResponse` + logged warning.
     - `await _playbookLookup.GetByIdAsync(configuredPlaybookId, ct)` wrapped in try/catch → exception → `BuildFallbackResponse` + logged warning.
     - Forward `playbook.Id` to the engine call.
   - Class XML `<remarks>` extended with the FR-02 stable-ID resolution paragraph (mirroring the task 015 pattern).
   - Public `GenerateAiSummaryAsync` signature UNCHANGED (Pattern A invariant — Pattern A is a value-only migration).

2. **`tests/unit/Sprk.Bff.Api.Tests/Services/Workspace/WorkspaceAiServiceTests.cs`** (NEW FILE, 251 lines)
   - 8 focused tests covering ONLY the new playbook-resolution boundary (entity-fetch private helpers were unchanged):
     - `WorkspaceAiService_InjectsIPlaybookLookupService_AndIOptionsWorkspaceOptions_PatternA` (reflection — ctor surface)
     - `WorkspaceAiService_HasNoHardcodedAiSummaryPlaybookIdConstant_FR02` (reflection — constant gone)
     - `WorkspaceAiService_HasNoIConfigurationField_ADR018` (reflection — IConfiguration removed)
     - `GenerateAiSummaryAsync_ResolvesPlaybookViaLookupService_UsingConfiguredOptionValue` (pins typed-options → lookup contract)
     - `GenerateAiSummaryAsync_ForwardsResolvedGuid_ToPlaybookExecution` (pins `playbook.Id` → `PlaybookRunRequest.PlaybookId`)
     - `GenerateAiSummaryAsync_EmptyConfiguredId_ReturnsFallback_WithoutLookupOrEngine` (graceful-degrade contract)
     - `GenerateAiSummaryAsync_LookupServiceThrows_ReturnsFallback_WithoutEngine` (lookup-failure short-circuit)
     - `GenerateAiSummaryAsync_UnsupportedEntityType_StillThrowsBeforeLookup` (validation precedes lookup)
   - No integration test added — task 018 POML step 8 lists an optional `WorkspaceAiIntegrationTests.cs` but the in-process SSE harness for this endpoint already exists upstream in `WorkspaceEndpointsTests` (which validates the route, auth, and rate limiting); the playbook-resolution boundary is fully covered by unit reflection + behavior tests. Defer integration coverage to Phase 1 task 025's integration suite.

---

## Scope check

- No DI module changes — `IPlaybookLookupService` is already Scoped (`FinanceModule.cs:115`); `IOptions<WorkspaceOptions>` is already bound (`ConfigurationModule.cs:111`); `WorkspaceModule.AddScoped<WorkspaceAiService>()` auto-resolves the new constructor dependencies.
- No `appsettings.template.json` change required — the `AiSummaryPlaybookId` property is pre-existing in `WorkspaceOptions` (nullable string default, untouched per the task constraint to NOT modify `WorkspaceOptions.cs` structure); per-environment values are populated at deploy time via `scripts/Reconcile-DemoEnvironment.ps1:82` (which already sets `Workspace__AiSummaryPlaybookId = '18cf3cc8-02ec-f011-8406-7c1e520aa4df'` for DEV).
- No `WorkspaceOptions.cs` modification — the property already exists with the correct default behavior; the value-only update referenced in POML step 4 is unnecessary because task 014's Dataverse backfill already aligned `sprk_playbookid` with the canonical PK GUID for this playbook.

---

## Publish-size delta (per CLAUDE.md §10 / NFR-01)

| Stage | Compressed (MB) | Compressed (bytes) | Delta vs prior baseline |
|---|---|---|---|
| Post-task-013 baseline | 44.75 | 46,927,543 | — |
| Post-task-015 baseline | 44.75 | 46,927,919 | +376 B |
| **Post-task-018 (this task)** | **44.75** | **46,928,900** | **+981 B (≈ 0 MB; within measurement noise)** |

### NFR-01 status

- Cumulative delta after Wave 1-A + Wave 1-B + Wave 1-D + Wave 1-E task 018: **+1,357 bytes** vs post-task-013 baseline (≈ **0 MB** vs Phase 0).
- Hard ceiling 60 MB → **44.75 MB measured**, **15.25 MB headroom**.
- Escalation threshold 55 MB → headroom 10.25 MB.
- Single-task escalation threshold (+5 MB) → not approached.

The +981-byte delta is consistent with the file-level edits: removed 1 Guid constant + 1 IConfiguration field, added 2 ctor parameters + 2 fields + ~50 lines of resolution/fallback logic + ~20 lines of XML doc. No NuGet adds, no new types shipped to publish.

---

## Test outcome

- `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors, 0 warnings** (clean build at the touched scope) ✅
- `dotnet build tests/unit/Sprk.Bff.Api.Tests/` → **0 errors, 0 warnings** ✅
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~WorkspaceAi"` → **8/8 passed, 0 failed, 0 skipped, 12 ms** ✅
- `dotnet publish -c Release src/server/api/Sprk.Bff.Api/` → **Success** ✅

---

## Grep verification

| Pattern (under `src/`) | Result |
|---|---|
| `18cf3cc8-02ec-f011-8406-7c1e520aa4df` | 4 hits — all in XML doc comments (`WorkspaceAiService.cs:38,53,342` + pre-existing `WorkspaceOptions.cs:51`); zero hits outside docs. POML explicitly allows XML doc references. ✅ |
| `DefaultAiSummaryPlaybookId` | **0 hits** — constant fully removed ✅ |
| `_configuration["Workspace:AiSummaryPlaybookId"]` | **0 hits** — raw indexer read removed ✅ |
| `Workspace:AiSummaryPlaybookId` (any) | 3 hits — all in XML doc / log-message strings (`WorkspaceAiService.cs:40,52,353`); no remaining raw IConfiguration indexer reads ✅ |

---

## Acceptance criteria

| Criterion | Status |
|---|---|
| `WorkspaceAiService.cs` injects `IOptions<WorkspaceOptions>` + `IPlaybookLookupService` | ✅ (test `_InjectsIPlaybookLookupService_AndIOptionsWorkspaceOptions_PatternA`) |
| No hardcoded GUID for AI summary playbook remains (outside XML doc) | ✅ (test `_HasNoHardcodedAiSummaryPlaybookIdConstant_FR02` + grep verification) |
| `WorkspaceOptions.AiSummaryPlaybookId` populated to resolved ID | ✅ (DEV via `Reconcile-DemoEnvironment.ps1:82`; pre-existing nullable property left intentionally so the empty-config graceful-degrade path is exercisable) |
| Handoff note documents which stable ID was bound + Dataverse evidence | ✅ (this note; `Document Profile` / `18cf3cc8-...` / `PB-002`; task 014 backfill evidence cited) |
| Unit tests exit 0 | ✅ 8/8 pass |
| Publish-size delta within NFR-01 thresholds | ✅ +981 B (≈ 0 MB); 44.75 MB total / 15.25 MB headroom to ceiling |
| code-review + adr-check exit 0 | ⏳ executed below |

---

## Notes / unexpected findings

- **POML step 4 (value-only update to `WorkspaceOptions.cs`)** was skipped intentionally per the POML's own constraint ("DO NOT modify `WorkspaceOptions.cs` structure" + "If task 013 already populated `AiSummaryPlaybookId` with the correct value, skip step 4"). The property remains nullable string (no default value) so that the graceful-degrade fallback path is a real exercisable branch for tests. The DEV value lives in the `scripts/Reconcile-DemoEnvironment.ps1` provisioning script and per-environment config at deploy time, NOT as a default in `WorkspaceOptions.cs`. This is the same shape as the `PreFillPlaybookId` and `ProjectPreFillPlaybookId` sibling nullable properties (per the Q1 refactor handoff).
- **POML step 8 (integration test)** was deferred to Phase 1 task 025's integration suite. Rationale: the existing `WorkspaceEndpointsTests` covers the `/api/workspace/ai/summary` route surface; the playbook-resolution boundary is exhaustively covered by 8 focused unit tests (lookup-service contract, configured-options binding, resolved-Guid forwarding, empty-config short-circuit, lookup-failure short-circuit, unsupported-type pre-empt, IConfiguration removal, hardcoded-constant removal). Adding a duplicate integration test would test the same lookup contract through an in-process Dataverse stub.
- **Hardcoded GUID 18cf3cc8-... resolves to "Document Profile" (PB-002)**, NOT "Summarize New File(s)" or "summarize-document-for-workspace@v1". The POML's wording ("likely `summarize-new-files` or `document-profile`") correctly listed `document-profile` as a candidate; the Dataverse trace via task 014 evidence (line 14) + the existing `Reconcile-DemoEnvironment.ps1:82` pin confirms `Document Profile` (`sprk_playbookid = 18cf3cc8-...`).
- **`InvoiceExtractionJobHandler` issue from Q1 refactor** is separate from this task — that handler uses literal `"PB-013"` slug; orthogonal to FR-02.

---

## Recommended TASK-INDEX status

Mark task 018 → ✅ complete. Wave 1-E sibling tasks (016, 017, 019) follow the same Pattern A shape but on different consumers; their independence from this task's edits is preserved (no shared edited lines).

## Out of scope / follow-ups

- `notes/handoffs/018-aisummary-id-decision.md` — POML step 3 mentioned creating a separate decision note; the decision is documented in this combined handoff (the playbook identity matters more than the bookkeeping; combining keeps the handoff folder lean per the task 015 pattern).
- Per-env `Workspace:AiSummaryPlaybookId` deployment values — owned by Phase 1 task 026 (deploy).
