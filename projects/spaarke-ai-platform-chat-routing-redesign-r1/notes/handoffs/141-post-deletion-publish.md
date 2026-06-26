# Task 141 — Post-deletion BFF Publish Size Measurement

**Date**: 2026-06-25
**Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
**Pre-deletion baseline**: see `141-pre-deletion-publish.md`

## Size measurement

| Metric | Pre-deletion | Post-deletion | Delta |
|---|---|---|---|
| **Sprk.Bff.Api.dll** | 9,889,792 bytes | 9,823,744 bytes | **-66,048 bytes (-65 KB)** |
| **Uncompressed total** | 144,113 KB (≈141 MB) | 144,037 KB (≈141 MB) | **-76 KB** |
| **Compressed (.tar.gz)** | 47,160,251 bytes (≈45 MB) | 47,132,261 bytes (≈45 MB) | **-27,990 bytes (-27 KB)** |
| **File count** | 264 | 264 | 0 |
| **Build warning count** | ~17 (pre-existing) | 17 (pre-existing) | 0 |

**FR-22 / ADR-029 NET REDUCTION criterion satisfied** — all three size metrics decreased. The absolute reduction is modest (the deleted .cs source files were small text; the published bytes are dominated by NuGet dependencies which were not removed). The file count is unchanged because file count is dominated by transitive NuGet-package DLLs in the publish output, not by our 17 source-file deletions.

## Test result

Full suite: **7,795 passed / 0 failed / 134 skipped** (pre-existing skips, none introduced or affected by task 141).

Run command:
```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ -c Debug --no-build --nologo
```

## Build result

`dotnet build src/server/api/Sprk.Bff.Api/ -c Debug` — **Build succeeded** with 17 warnings (all pre-existing — primarily `CS1998` "async without await" and `CS0618` "DemoProvisioningOptions obsolete"; none related to capability removal).

## Acceptance-criterion status (POML)

| Criterion | Status | Evidence |
|---|---|---|
| `grep "CapabilityRouter" src/server/` returns 0 hits | PASS | Verified post-edit |
| `grep "ICapabilityRouter\|ICapabilityValidator\|ICapabilityManifest" src/server/` returns 0 hits | PASS | Verified post-edit |
| `dotnet build src/server/api/Sprk.Bff.Api/` exits 0 | PASS | 0 errors, 17 (pre-existing) warnings |
| `dotnet test` full suite passes | PASS | 7795/7795 passed, 0 failed |
| FR-23 tool list unit tests pass | PASS | New tests in `OrchestratorPromptBuilderTests.cs` + `SprkChatAgentFactoryTests.cs` (matched-playbook + always-on conversational set) |
| BFF publish-size NET REDUCTION vs baseline | PASS | -27 KB compressed, -76 KB uncompressed, -65 KB Sprk.Bff.Api.dll |
| Single atomic commit | DEFERRED | Main session will commit at end (per CRIT-6 + brief) |
| code-review + adr-check exit 0 | DEFERRED | Main session runs Step 9.5 gates |

## Key implementation notes

### FR-23 (per-playbook tool filtering) satisfied by existing path
The pre-existing `SprkChatAgentFactory.CreateAgentAsync` already enforces FR-23 at lines 412-414:
```csharp
var capabilities = playbookId.HasValue
    ? await GetPlaybookCapabilitiesAsync(scope.ServiceProvider, playbookId.Value, cancellationToken)
    : (IReadOnlySet<string>)new HashSet<string>(PlaybookCapabilities.CoreCapabilities);
```
- Matched-playbook tools = playbook's declared capabilities (Action + Tool scopes) → resolved from Dataverse `sprk_playbookcapabilities` via `GetPlaybookCapabilitiesAsync`.
- Always-on tools = `PlaybookCapabilities.CoreCapabilities` → used when no playbook is matched.

Task 141 simply STRIPPED the AIPU2-061 per-turn `CapabilityRouter` override that previously could shrink this set further on a per-turn basis. The replacement is intentionally less granular — tools are scoped by playbook, not by per-turn keyword classification. Per spec Q8, this is the single-phase cutover.

### FR-24 dedup rewired to `playbookId` parameter
The R6 FR-30 dedup directive (originally driven by `routingResult.SelectedPlaybookId`) has been rewired to use the explicit `playbookId` parameter passed to `CreateAgentAsync`. Semantics are unchanged:
- When `playbookId.HasValue` AND the playbook's terminal destination ≠ `Chat` → append non-chat dedup directive.
- When `playbookId.HasValue` AND the destination == `Chat` → append Hotfix B-G9b chat-ack directive.
- Otherwise → no directive (free-form / standalone conversational turns are unaffected).

The dispatcher-resolved playbook ID is already passed by `ChatEndpoints` per the existing chat-routing flow (`request.PlaybookId` → session → `CreateAgentAsync(playbookId: ...)`), so no upstream changes were needed.

Task 143 (next task in Phase 7) owns the regression test that verifies the FR-24 dedup invariant survives the rewire.

### `EmitCapabilityChangesIfDifferentAsync` — PRESERVED
Decision: **kept the method** but stripped the `AIPU2-061` provenance comments.

Rationale: the `capability_change` SSE event is still meaningful in the post-CapabilityRouter world — the tool set CAN differ between turns when the dispatcher resolves different playbooks on consecutive turns (e.g., first turn matches `summarize-document-for-chat@v1`, second turn matches no playbook → tools shift from the chat-summarize set to core-only). The existing same-vs-previous-tool-set comparison continues to work correctly; only the per-turn router-driven shrinkage is gone.

### `OrchestratorPromptBuilder` API change (FR-22)
The contract was rewritten from `BuildSystemPrompt(CapabilityRoutingResult, OrchestratorPromptContext)` to `BuildSystemPrompt(IReadOnlyList<string> activeToolNames, OrchestratorPromptContext)`. Removed:
- `ICapabilityManifest` constructor parameter
- `ResolveToolNames(CapabilityRoutingResult)` method (now `NormalizeToolNames(IReadOnlyList<string>)`)
- `AppendCapabilityIndex` method (the "Available Capabilities" section is gone — the per-turn "Active Tools" suffix is the single tool surface)
- `ManifestHash()` cache key (now `context.ActivePlaybookName ?? "_default_"`)

`DirectOpenAiAgent` passes `Array.Empty<string>()` for `activeToolNames` because it does not surface tools at this layer — the system prompt simply omits the "Active Tools" section.

### DI restructuring
- **Deleted**: `Infrastructure/DI/AiCapabilitiesModule.cs` + the `AddAiCapabilitiesModule` call in `Program.cs:147` + the `MapCapabilityEndpoints` call in `EndpointMappingExtensions.cs:144`.
- **Moved**: `OrchestratorPromptBuilder` + `IOrchestratorPromptBuilder` registration to `AiChatModule.AddAiChatModule` (the natural sibling for an orchestrator-side singleton).
- **Removed**: All `CapabilityManifest`, `ICapabilityManifestLoader`, `CapabilityManifestInitializer`, `ManifestRefreshOptions`, `ManifestRefreshService`, `IManifestRefreshTrigger`, `CapabilityRouterOptions`, `CapabilityRouter`, `ICapabilityRouter`, `CapabilityValidator`, `ICapabilityValidator` registrations.

`AiChatModule` doc-comment updated to reflect the 7th registration (was 6).

### File deletions

**Production (19 files)**:
- `Services/Ai/Capabilities/` directory removed wholesale (17 files):
  - `CapabilityClassificationPromptBuilder.cs`, `CapabilityManifest.cs`, `CapabilityManifestEntry.cs`, `CapabilityManifestInitializer.cs`, `CapabilityRouter.cs`, `CapabilityRouterOptions.cs`, `CapabilityRoutingResult.cs`, `CapabilityValidationContext.cs`, `CapabilityValidator.cs`, `DataverseCapabilityManifestLoader.cs`, `ICapabilityManifest.cs`, `ICapabilityManifestLoader.cs`, `ICapabilityRouter.cs`, `ICapabilityValidator.cs`, `IManifestRefreshTrigger.cs`, `ManifestRefreshOptions.cs`, `ManifestRefreshService.cs`
- `Api/Ai/CapabilityEndpoints.cs`
- `Infrastructure/DI/AiCapabilitiesModule.cs`

**Tests (9 files)**:
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/` directory removed wholesale (7 files):
  - `CapabilityRouterTests.cs`, `CapabilityValidatorTests.cs`, `DataverseCapabilityManifestLoaderTests.cs`, `ManifestRefreshServiceTests.cs`, `CapabilityRouterVoiceMemoryTests.cs`, `CapabilityRouterBenchmarkTests.cs`, `CapabilityRouterDedupTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/CapabilityManifestTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/CapabilityEndpointsTests.cs`

### Test file rewrites
- `OrchestratorPromptBuilderTests.cs` — fully rewritten for the new `BuildSystemPrompt(IReadOnlyList<string>, OrchestratorPromptContext)` contract (15 tests covering prefix stability, cache hit/miss, persona, tenant isolation, tool list normalization, budget).
- `OrchestratorPromptBuilderBudgetTests.cs` — fully rewritten for the new contract (8 budget scenarios).
- `SprkChatAgentFactoryTests.cs` — stripped four AIPU2-061 tests that exercised the deleted CapabilityRouter; replaced with three FR-23 tests (matched-playbook gating, always-on core capabilities, capability_change SSE preservation).
- `SprkChatAgentFactoryToolResolutionTests.cs` — doc-comment fixed only (no test logic changed).
- `DirectOpenAiAgentTests.cs` — updated 4 mock setups to use `IReadOnlyList<string>` instead of `CapabilityRoutingResult`; renamed `ProcessAsync_CallsPromptBuilder_WithFallbackRoutingResult` → `ProcessAsync_CallsPromptBuilder_WithEmptyToolNames` (asserts `Array.Empty<string>()` is passed).

### Comment-strip files (non-deletion)
The following files had stale `CapabilityRouter` / `ICapabilityRouter` comment references stripped (no behavior change):
- `Services/Ai/Chat/SprkChatAgentFactory.cs` (AIPU2-061 + R6 FR-30 references; ctor + ResolveTools + BuildAllowedToolNames removed; FR-24 dedup block migration provenance comment also stripped per brief)
- `Services/Ai/Chat/DirectOpenAiAgent.cs`
- `Services/Ai/Chat/NullSprkChatAgentFactory.cs`
- `Api/Ai/ChatEndpoints.cs`
- `Services/Ai/PlaybookService.cs`
- `Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs`
- `Services/Ai/Telemetry/IContextEventEmitter.cs`
- `Telemetry/AiLatencyTracker.cs`
- `Telemetry/AiLatencyTelemetry.cs`
- `Services/Ai/Memory/IPromptBudgetTracker.cs`
- `Services/Ai/Handlers/ManagePinnedContextHandler.cs`
- `Infrastructure/DI/AnalysisServicesModule.cs`
- `Infrastructure/DI/RoutingModule.cs`
- `Infrastructure/DI/AiChatModule.cs` (added new `OrchestratorPromptBuilder` registration entry to header table)
- `Infrastructure/DI/EndpointMappingExtensions.cs`
- `Program.cs`
- `Models/Ai/NodeRoutingConfig.cs`
- `Models/Ai/node-routing-config.schema.json`
- `Services/Ai/Chat/Playbooks/summarize-document-for-workspace.playbook.json`
- `tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Telemetry/ContextEventEmissionTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryToolResolutionTests.cs`

## Notes for main session before commit

1. The change is structurally atomic — all deletions, the OrchestratorPromptBuilder contract change, the SprkChatAgentFactory rewire, and the test updates are interdependent. Per CRIT-6, commit as ONE unit.
2. Build succeeds (0 errors) and full test suite passes (7795/7795). No new warnings introduced.
3. Publish-size NET REDUCTION confirmed (modest at -27 KB compressed, but in the right direction per ADR-029).
4. No `.claude/` writes were attempted (brief constraint respected).
5. The `bin/` artifacts under `src/server/api/Sprk.Bff.Api/bin/` still contain stale references to deleted types in the build-output schema JSON copies. These are build artifacts (regenerated on next build/publish) and are not committed; they will refresh themselves and don't affect the commit.
