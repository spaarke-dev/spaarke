# Task 141 — Pre-deletion BFF Publish Size Baseline

**Date**: 2026-06-25
**Commit**: `994adcdeb` (HEAD before 141 work)
**Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`

## Baseline measurement (pre-deletion)

| Metric | Value |
|---|---|
| **Uncompressed** | 141 MB |
| **Compressed (.tar.gz)** | 45 MB |
| **File count** | 264 |
| **Warning count** | (carry-over from prior baselines; ~18) |

Command:
```bash
cd src/server/api/Sprk.Bff.Api
dotnet publish -c Release -o /tmp/api-publish-pre-141/
```

## Inventory: files slated for deletion

### Production source (in scope of POML)

**`Services/Ai/Capabilities/` (17 files):**
1. `CapabilityClassificationPromptBuilder.cs`
2. `CapabilityManifest.cs`
3. `CapabilityManifestEntry.cs`
4. `CapabilityManifestInitializer.cs`
5. `CapabilityRouter.cs`
6. `CapabilityRouterOptions.cs`
7. `CapabilityRoutingResult.cs`
8. `CapabilityValidationContext.cs`
9. `CapabilityValidator.cs`
10. `DataverseCapabilityManifestLoader.cs`
11. `ICapabilityManifest.cs`
12. `ICapabilityManifestLoader.cs`
13. `ICapabilityRouter.cs`
14. `ICapabilityValidator.cs`
15. `IManifestRefreshTrigger.cs`
16. `ManifestRefreshOptions.cs`
17. `ManifestRefreshService.cs`

**Additional files identified during inventory:**
18. `Api/Ai/CapabilityEndpoints.cs` — webhook endpoint that triggers `IManifestRefreshTrigger`; useless after deletion of the manifest
19. `Infrastructure/DI/AiCapabilitiesModule.cs` — DI module registering all of the above; useless after deletion

### Tests slated for deletion

`tests/unit/Sprk.Bff.Api.Tests/`:
1. `Services/Ai/Capabilities/CapabilityRouterTests.cs`
2. `Services/Ai/Capabilities/CapabilityValidatorTests.cs`
3. `Services/Ai/Capabilities/DataverseCapabilityManifestLoaderTests.cs`
4. `Services/Ai/Capabilities/ManifestRefreshServiceTests.cs`
5. `Services/Ai/Capabilities/CapabilityRouterVoiceMemoryTests.cs`
6. `Services/Ai/Capabilities/CapabilityRouterBenchmarkTests.cs`
7. `Services/Ai/Capabilities/CapabilityRouterDedupTests.cs` — **migrated** to FR-24 dedup verification (task 143 will own; 141 leaves a deletion-only placeholder)
8. `Services/Ai/CapabilityManifestTests.cs`
9. `Api/Ai/CapabilityEndpointsTests.cs`

Other test files with `CapabilityRoutingResult`/`CapabilityRouter` references that need surgical strip (not deletion):
- `Services/Ai/Chat/SprkChatAgentFactoryTests.cs` — strip `routingResult:` references from existing tests
- `Services/Ai/Chat/SprkChatAgentFactoryToolResolutionTests.cs` — strip
- `Services/Ai/Chat/OrchestratorPromptBuilderTests.cs` — rewrite for new `IReadOnlyList<string> activeToolNames` contract
- `Services/Ai/Chat/OrchestratorPromptBuilderBudgetTests.cs` — rewrite for new contract
- `Services/Ai/Chat/DirectOpenAiAgentTests.cs` — strip `CapabilityRoutingResult.Fallback(...)`
- `Services/Ai/Telemetry/ContextEventEmissionTests.cs` — strip if applicable
- `Sprk.Bff.Api.Tests.csproj` — remove any `<Compile Include>` for deleted files (likely none — glob pattern)

### Production source — STRIP coupling (NOT delete)

| File | Coupling | Action |
|---|---|---|
| `Services/Ai/Chat/SprkChatAgentFactory.cs` | Deep: ctor param + AIPU2-061 routing block (lines ~426-544) + R6 FR-30 dedup directive (lines ~546-640) + `routingResult` in `ResolveTools` + `SelectedToolNames` resolver (lines ~1657-1740) | Strip router + rewire dedup directive to use `playbookId` argument directly |
| `Services/Ai/Chat/NullSprkChatAgentFactory.cs` | Doc-comment only | Strip comment reference |
| `Services/Ai/Chat/DirectOpenAiAgent.cs` | Uses `CapabilityRoutingResult.Fallback(...)` (line 132-135) + `_promptBuilder.BuildSystemPrompt(routing, promptContext)` (line 137) | Strip Fallback; call new signature |
| `Services/Ai/Chat/OrchestratorPromptBuilder.cs` | Constructor takes `ICapabilityManifest`; method takes `CapabilityRoutingResult` | Rewrite: take `IReadOnlyList<string> activeToolNames` instead. Persona/Standing/Enrichment unchanged. Capability index becomes Tool index (uses names from passed list). |
| `Services/Ai/Chat/IOrchestratorPromptBuilder.cs` | Method signature uses `CapabilityRoutingResult` | Rewrite to match new builder |
| `Services/Ai/Chat/OrchestratorPromptContext.cs` | Doc comment references CapabilityRoutingResult | Strip comment; record itself is fine |
| `Api/Ai/ChatEndpoints.cs` | 4 doc-comment references (line 492, 580, 701, 2661) — all marked as "Phase 5R task 116 / FR-20 removed" | Strip stale comments |
| `Services/Ai/PlaybookService.cs` | Doc comment line 712 references `DataverseCapabilityManifestLoader` | Strip stale comment |
| `Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` | Doc comment line 305 | Strip stale comment |
| `Services/Ai/Telemetry/IContextEventEmitter.cs` | Unclear; check if interface uses Capability* type | Strip if any |
| `Services/Ai/Memory/IPromptBudgetTracker.cs` | Unclear; check | Strip if any |
| `Services/Ai/Handlers/ManagePinnedContextHandler.cs` | Comment | Strip stale comment |
| `Telemetry/AiLatencyTracker.cs` + `AiLatencyTelemetry.cs` | Comment | Strip stale comment |
| `Infrastructure/DI/AnalysisServicesModule.cs` | Likely a registration call | Strip |
| `Services/Ai/Chat/Playbooks/summarize-document-for-workspace.playbook.json` | Config (probably uses "capability_name" key) | Verify; likely no change needed |
| `Models/Ai/NodeRoutingConfig.cs` | Comment | Strip stale comment |
| `Models/Ai/node-routing-config.schema.json` | Comment in schema | Strip if applicable |

### FE references (out of POML scope; in src/solutions/SpaarkeAi/)

These are CLIENT-side SoftSlash/HardSlash/CommandRouter that reference "Capability" in identifiers / strings, NOT the BFF `CapabilityRouter`. Phase 5R task 116 already removed the dict; the remaining references are stale doc comments + interface identifiers. **No action required for 141** — already addressed by Phase 5R.

### DI registration moves

Currently in `AiCapabilitiesModule.cs`:
- `CapabilityManifest`, `ICapabilityManifest` → DELETE
- `DataverseCapabilityManifestLoader` (HttpClient + ICapabilityManifestLoader) → DELETE
- `CapabilityManifestInitializer` (IHostedService) → DELETE
- `ManifestRefreshOptions` (IOptions) → DELETE
- `ManifestRefreshService` (singleton + IHostedService + IManifestRefreshTrigger) → DELETE
- `OrchestratorPromptBuilder` + `IOrchestratorPromptBuilder` → **MOVE** to `AiChatModule.cs` (already references it; verify)
- `CapabilityRouterOptions` (IOptions) → DELETE
- `CapabilityRouter` + `ICapabilityRouter` (singleton with keyed IChatClient "raw") → DELETE
- `CapabilityValidator` + `ICapabilityValidator` (scoped) → DELETE

`Program.cs` / wherever `AddAiCapabilitiesModule` is called → REMOVE the call.

`EndpointMappingExtensions.cs` / wherever `MapCapabilityEndpoints` is called → REMOVE the call.

## Replacement: FR-23 tool filtering — already present

Per spec FR-23: matched playbook tools = playbook's Action + Tool scopes; always-on tools = `recall_session_file`, `get_workspace_tab_state`, `document_search`, `update_workspace_tab` (when tab is agent-editable).

The current `SprkChatAgentFactory.CreateAgentAsync` at line 422-424 ALREADY implements this:

```csharp
var capabilities = playbookId.HasValue
    ? await GetPlaybookCapabilitiesAsync(scope.ServiceProvider, playbookId.Value, cancellationToken)
    : (IReadOnlySet<string>)new HashSet<string>(PlaybookCapabilities.CoreCapabilities);
```

`PlaybookCapabilities.CoreCapabilities` is the "always-on" set; per-playbook capabilities come from Dataverse playbook config. `ResolveTools` then filters tools by these capabilities. **FR-23 is structurally satisfied** by the existing playbook-capabilities path. The deletion does NOT need to add new tool-filtering logic — it just needs to remove the per-turn override of this gate.

## Replacement: FR-24 dedup preservation

Per spec FR-24: R6 FR-30 dedup directive must survive the deletion.

The current `SprkChatAgentFactory` lines 546-640 keys off `routingResult.SelectedPlaybookId.HasValue`. After deletion, this becomes a no-op unless rewired.

**Rewire**: the directive should apply when `playbookId` (the explicit `CreateAgentAsync` parameter) is non-null AND the playbook's terminal destination is not chat. This is exactly the same semantic — the dispatcher-resolved playbook ID is passed by `ChatEndpoints` per the existing chat-routing flow (`request.PlaybookId` → session → `CreateAgentAsync(playbookId: ...)`).

Task 143 owns the regression test that proves this still works.

## Acceptance criteria coverage plan

| Criterion | Plan |
|---|---|
| `grep "CapabilityRouter" src/server/` returns 0 hits | Achieved via 17-file deletion + comment strip |
| `grep "ICapabilityRouter\|ICapabilityValidator\|ICapabilityManifest" src/server/` returns 0 hits | Same |
| `dotnet build` exits 0 | Verified post-edit |
| `dotnet test` full suite passes | Verified post-edit |
| FR-23 tool list unit tests pass | Existing `SprkChatAgentFactoryToolResolutionTests.cs` covers playbook-capabilities gating; rename / re-purpose tests to assert FR-23 behavior explicitly |
| BFF publish-size NET REDUCTION vs 45 MB baseline | Expect ~1-2 MB reduction (17 .cs files + their attributes) |
| Single atomic commit | Stage all changes then ONE commit at end |
| code-review + adr-check exit 0 | Step 12 |
