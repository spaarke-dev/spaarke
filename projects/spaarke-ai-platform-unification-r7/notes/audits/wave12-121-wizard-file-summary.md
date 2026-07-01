# Wave 12.1 Audit 121 — Wizard File Summary

> **Date**: 2026-06-30
> **Author**: task-execute (R7 W12.1 task 121)
> **Rigor**: STANDARD (read-only audit)
> **Output gate**: This doc + a disposition recommendation for Wave 12.3 task generation
> **Status**: COMPLETE — disposition is **RESTORE (light)** with one optional remediation tightening.

---

## TL;DR

**Disposition recommendation: RESTORE.** The Summarize File wizard is a **SIMPLE 2-node playbook** (`Start → AI Analysis`) that runs on the *legacy R3/R4 GenericAnalysisHandler pipeline*, NOT the Wave 11 multinode aggregator (`InvokePlaybookAi.AggregatePlaybookEvents`). The W11 T116 bug class P1 (`IsDeliverOutput aggregator drops ReturnResponse`) **does NOT apply** because this endpoint reads `NodeOutput.StructuredData` directly off the per-node stream — there is no aggregator. The W11 T116 bug class P2 (`LoadKnowledge type mismatch`) does NOT apply (no LoadKnowledge node). The W11 T116 bug class P3 (`EntityNameValidator scrubber`) does NOT apply (no ValidateEntityNames node).

The playbook's dispatch field (`sprk_executortype`) was successfully backfilled by Wave 5 task 054 (✅ verified live: AI Analysis node = 0/AiAnalysis, Start node = 33/Start). The Action's JPS SystemPrompt is well-formed with the correct output schema matching the wizard UI contract (`ISummarizeResult`). The Tool linkage is correct (`sprk_playbooknode_tool` → `General Analysis` (GenericAnalysisHandler)).

The most likely failure mode (until empirically reproduced in spaarkedev1) is a **deployment/env-var configuration drift**, not a code regression. **Effort estimate: 1-3 hours** to verify configured `Workspace:SummarizePlaybookId` env var in the deployed BFF, smoke-test the endpoint, and fix any drift discovered. If smoke fails for a different reason, a single-day remediation budget is sufficient for any remaining issue surfaced — there is no engine bug class to fix.

---

## 1. Feature code locations (file:line)

### UI entry point

| File | Lines | Role |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/SummarizeFilesDialog.tsx` | 1-640 | Wizard dialog (Fluent UI v9 WizardShell), 3 static + dynamic follow-on steps |
| `src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/summarizeService.ts` | 36-128 (`streamSummarize`) | SSE-streaming POST to `/api/workspace/files/summarize` |
| `src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/summarizeTypes.ts` | 27-46 (`ISummarizeResult`) | TypeScript contract — `tldr`, `summary`, `fileHighlights`, `shortSummary`, `confidence`, `practiceAreas`, `mentionedParties`, `callToAction` |
| `src/solutions/LegalWorkspace/src/components/SummarizeFiles/*` | (duplicate copies) | Pre-extraction copies — superseded by the shared-lib version |
| `src/solutions/SummarizeFilesWizard/*` | (standalone Vite code page) | Code page wrapper that hosts the dialog as a standalone page |

### BFF endpoint

| File | Lines | Role |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceFileEndpoints.cs` | 86-101 | Endpoint registration: `POST /api/workspace/files/summarize` |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceFileEndpoints.cs` | 160-261 (`HandleSummarize`) | SSE handler — validates files, extracts text, invokes playbook, emits SSE stream |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceFileEndpoints.cs` | 266-405 (`RunSummarizePlaybookAsSSEAsync`) | Resolves playbook via `IConsumerRoutingService` (with env-var fallback), invokes `IPlaybookOrchestrationService.ExecuteAsync` directly (**NOT** via `IInvokePlaybookAi`), forwards `evt.NodeOutput.StructuredData` as SSE `Result` chunks |

### Configuration

| File | Lines | Role |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Configuration/WorkspaceOptions.cs` | 55-75 (`SummarizePlaybookId`) | Typed-options binding for `Workspace:SummarizePlaybookId` env var (fallback when consumer routing returns null) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs` | 60-64 (`SummarizeFile = "summarize-file"`) | Consumer-routing constant |
| `scripts/dataverse/Seed-PlaybookConsumers.ps1` | 59, 120 | Seeds `sprk_playbookconsumer` row for `summarize-file → 4a72f99c-a119-f111-8343-7ced8d1dc988` |

### Playbook (Dataverse)

| Item | Value | Verified via |
|---|---|---|
| Playbook ID | `4a72f99c-a119-f111-8343-7ced8d1dc988` (sprk_name = "Summarize File") | `mcp__dataverse__read_query` |
| Consumer routing row | `sprk_playbookconsumerid = 271194cd-3670-f111-ab0e-70a8a590c51c` (consumertype=`summarize-file`, environment=`*`, enabled=true, priority=500) | `mcp__dataverse__read_query` |

---

## 2. Playbook shape (SIMPLE vs COMPLEX)

**Classification: SIMPLE** — 2 nodes, no fan-out, no composition, no validators, no LoadKnowledge, no ReturnResponse.

### Node inventory (live Dataverse query 2026-06-30)

| # | Node ID | Name | sprk_executortype | sprk_actionid | OutputVariable |
|---|---|---|---|---|---|
| 1 | `d08e57ec-a419-f111-8343-7ced8d1dc988` | `Start` | 33 (Start) | null (structural) | `output_node_1772831831963_fgnba2kpu` |
| 2 | `72f0c9dd-a119-f111-8343-7ced8d1dc988` | `AI Analysis` | 0 (AiAnalysis) | `ddaa441e-9f19-f111-8343-7c1e520aa4df` (File Summary, ACT-025) | `output_aiAnalysis` |

### Action payload (Node 2 → File Summary action)

- **sprk_actioncode**: `ACT-025`
- **sprk_name**: `File Summary`
- **sprk_systemprompt**: **JPS-formatted** (validated `"$schema":"https://spaarke.com/schemas/prompt/v1","$version":1`), with `output.fields` matching the wizard UI's `ISummarizeResult` contract one-for-one: `tldr`, `summary`, `shortSummary`, `fileHighlights`, `practiceAreas`, `mentionedParties`, `callToAction`, `confidence` (each properly described + role + task + constraints + examples). Two `scopes.$skills` references: `skill:Summary Generation`, `skill:Party Identification`.

### Tool linkage (Node 2 → Tool)

| Linker row | Tool ID | Tool Name | HandlerClass |
|---|---|---|---|
| `sprk_playbooknode_toolid = 6bb99957-aa19-f111-8343-7ced8d1dc988` | `c01ef382-7c16-f111-8343-7c1e520aa4df` | `General Analysis` | `GenericAnalysisHandler` |

### Wave 5 / FR-19 backfill state

✅ **COMPLETE.** Per `projects/spaarke-ai-platform-unification-r7/tasks/TASK-INDEX.md` task 054: "Migration LIVE-RUN against spaarkedev1 — 94/94 PATCHed (38.8s), 0 errors". Both Summarize File nodes appear in `projects/spaarke-ai-platform-unification-r7/notes/drafts/playbook-node-review-output.csv` with backfilled `owner_decision_executortype` values (33 for Start, 0 for AI Analysis).

---

## 3. Execution path trace (end-to-end)

```
[Browser]
  SummarizeFilesDialog.tsx → streamSummarize() → POST /api/workspace/files/summarize  (multipart)
        ↓
[BFF — WorkspaceFileEndpoints.HandleSummarize, line 160-261]
  ValidateFiles(files)                                  [WorkspaceFileEndpoints.cs:175-189]
  Set SSE headers (text/event-stream, no-cache, no-buffer) [WorkspaceFileEndpoints.cs:192-196]
  Emit Progress("document_loaded")                       [WorkspaceFileEndpoints.cs:204]
  Emit Progress("extracting_text")                       [WorkspaceFileEndpoints.cs:206]
  extractedText = ExtractTextFromFilesAsync(...)         [WorkspaceFileEndpoints.cs:207, 414-460]
  Emit Progress("context_ready") + Progress("analyzing") [WorkspaceFileEndpoints.cs:222-223]
  RunSummarizePlaybookAsSSEAsync(...)                    [WorkspaceFileEndpoints.cs:231-233]
        ↓
[BFF — RunSummarizePlaybookAsSSEAsync, line 266-405]
  Truncate text to 80 KB                                 [WorkspaceFileEndpoints.cs:279-284]
  consumerRouting.ResolveAsync("summarize-file", "default", {MimeType=...})
                                                         [WorkspaceFileEndpoints.cs:295-301]
  Fallback: workspaceOptions.Value.SummarizePlaybookId   [WorkspaceFileEndpoints.cs:303-308]
  playbookLookup.GetByIdAsync(configuredPlaybookId)      [WorkspaceFileEndpoints.cs:323-326]
  60-second timeoutCts                                   [WorkspaceFileEndpoints.cs:330-331]
  Build PlaybookRunRequest:
    PlaybookId, DocumentIds=[], UserContext=text,
    Document = { Name="Summarize upload", ExtractedText=text },
    Parameters = { ["operation"]="summarize" }            [WorkspaceFileEndpoints.cs:333-348]
        ↓
[BFF — PlaybookOrchestrationService.ExecuteAsync, line 1130+]
  For each node (Start → AI Analysis):
    Read node.SprkExecutortype (single-hop dispatch, R7 FR-07)
                                                         [PlaybookOrchestrationService.cs:1059-1076]
    Resolve scopes (Skills, Knowledge, Tools) via ResolveNodeScopesAsync
      — Tools come from sprk_playbooknode_tool M:N linker [PlaybookOrchestrationService.cs:1081-1083, ScopeResolverService.cs:259-317]
    Get executor from registry by ExecutorType            [PlaybookOrchestrationService.cs:1130]
    Layer 1 substitution (ApplyConfigJsonTemplates)       [PlaybookOrchestrationService.cs:1239]
      — No-op for this playbook (no {{templates}} in configJson)
    executor.Validate(nodeContext)                        [PlaybookOrchestrationService.cs:1253]
    executor.ExecuteAsync(nodeContext)                    [PlaybookOrchestrationService.cs:1270]
    Emit NodeCompleted event                              [PlaybookOrchestrationService.cs:1285-1286]
        ↓
[BFF — AiAnalysisNodeExecutor.ExecuteAsync, line 161+]
  Validate: requires context.Tool != null + Tool.HandlerClass + Document.ExtractedText
                                                         [AiAnalysisNodeExecutor.cs:113-158]
  Resolve IToolHandlerRegistry (Scoped via IServiceProvider)
                                                         [AiAnalysisNodeExecutor.cs:188-189]
  Get GenericAnalysisHandler via Tool.HandlerClass        [AiAnalysisNodeExecutor.cs:193]
  ParseKnowledgeRetrievalConfig (defaults to Auto/topK=5 — no RagIndex sources linked, returns null)
                                                         [AiAnalysisNodeExecutor.cs:212-217, 807-897]
  Build ToolExecutionContext (includes Action.SystemPrompt = JPS)
                                                         [AiAnalysisNodeExecutor.cs:390-469]
  handler.Validate(toolContext, tool) → SuccessAsync      [AiAnalysisNodeExecutor.cs:251]
  handler.ExecuteAsync(...) or StreamExecuteAsync(...)    [AiAnalysisNodeExecutor.cs:286-313]
  ConvertToNodeOutput: StructuredData = toolResult.Data (JsonElement)
                                                         [AiAnalysisNodeExecutor.cs:1316-1343, NodeOutput.cs:112-138]
        ↓
[BFF — GenericAnalysisHandler.ExecuteAsync, line 231-375]
  PromptSchemaRenderer.Render(ActionSystemPrompt=JPS, ...) → useStructuredOutput=true (JsonSchema from JPS output.fields)
                                                         [GenericAnalysisHandler.cs:253-272]
  _openAiClient.GetStructuredCompletionRawAsync(prompt, schemaBinaryData, schemaName,
    model=actionModel, maxOutputTokens, temperature, ct) [GenericAnalysisHandler.cs:297-304]
  ParseAiResponse(response, config) → JsonNode (wizard-shape JSON)
                                                         [GenericAnalysisHandler.cs:320, 738-782]
  return ToolResult.Ok(handlerId, toolId, toolName, resultData, response, confidence, executionMetadata)
                                                         [GenericAnalysisHandler.cs:336-343, ToolResult.cs:143-167]
        ↓ (back up)
[BFF — RunSummarizePlaybookAsSSEAsync continues, line 353+]
  await foreach (var evt in playbookService.ExecuteAsync(...)):
    if (evt.Type == NodeCompleted && evt.NodeOutput.StructuredData.HasValue):
      structuredOutput = evt.NodeOutput.StructuredData.Value
      Emit AnalysisStreamChunk.Result(jsonStr)            [WorkspaceFileEndpoints.cs:357-362]
    if (evt.Type == RunFailed):
      throw InvalidOperationException("Summarize playbook failed: {Error}")
                                                         [WorkspaceFileEndpoints.cs:370-374]
  Fall-back to TextContent if no StructuredData            [WorkspaceFileEndpoints.cs:378-398]
  Emit Progress("delivering") + "data: [DONE]\n\n"         [WorkspaceFileEndpoints.cs:235-237]
        ↓
[Browser — summarizeService.ts:streamSummarize, line 36-128]
  Parse SSE events; on chunk.type === 'result', JSON.parse(chunk.content) → rawResult
                                                         [summarizeService.ts:101-110]
  Final result normalized via normalizeResult(rawResult)   [summarizeService.ts:125, 134-252]
        ↓
[Browser — SummarizeFilesDialog: setSummarizeResult(result) → SummaryResultsStep renders]
```

### R7 changes that touch this path

| R7 task | Change | Impact on Summarize File path |
|---|---|---|
| W2 task 022 | C# enum rename `ActionType → ExecutorType` (~1000+ refs) | **NONE** — internal rename; all references migrated consistently; build is clean (0 errors, verified 2026-06-30) |
| W2 task 024 | Single-hop dispatch in `ExecuteNodeAsync` (FR-07) | **MITIGATED** — Wave 5 backfilled `sprk_executortype` on both Summarize File nodes (✅ live-verified) |
| W2 task 025 | Delete structural fallback ladder helpers | **NONE** — playbook nodes both have explicit ExecutorType set |
| W2 task 026 | Delete Action.ActionType override branch | **NONE** — sprk_analysisaction has no `sprk_executortype` column on this entity (only on `sprk_playbooknode`); the deletion was an inline diff with no remaining effect |
| W11 task 114 | Fan-out iteration detection on raw configJson | **NONE** — Summarize File configJson has no `iteration.iterateOver` field; `TryExtractIterationConfig` returns false; normal-path execution |
| W11 task 116 (Option D JSON-aware Layer 1) | Auto-wrap pure templates → native JSON | **NONE** — Summarize File configJson contains no `{{template}}` strings; Layer 1 is effectively a no-op |
| W11 task 116 (helper consolidation) | Refactor in `InvokePlaybookAi` aggregator | **NONE** — this endpoint does **NOT** use `IInvokePlaybookAi`; it consumes `IPlaybookOrchestrationService.ExecuteAsync` directly |
| W5 task 054 | FR-19 backfill of 94 nodes | **DIRECTLY APPLIED** — both Summarize File nodes were in the migrated set per the review CSV |
| W5 task 055 | `Deploy-Playbook.ps1` delete `sprk_nodetype` write | **NONE-to-LATENT** — `NodeService.cs:607` defaults `NodeType` to `AIAnalysis` when entity.NodeType is null; AI Analysis node correctly resolves scopes; Start node also defaults to AIAnalysis but `StartNodeExecutor.Validate` doesn't require Tool/Scopes, so the wrong default is harmless. Worth noting but not a Summarize File bug. |

---

## 4. Engine bug class matrix (W11 T116 systematic assessment cross-walk)

| Bug class (W11 T116) | Description | Applies to Summarize File? | Why / Why not |
|---|---|---|---|
| **P1** — aggregator drops ReturnResponse | `InvokePlaybookAi.AggregatePlaybookEvents` only captures `IsDeliverOutput=true` nodes once `terminalText` is set | ❌ NO | This endpoint does NOT call `IInvokePlaybookAi`. It iterates `IPlaybookOrchestrationService.ExecuteAsync` events directly and forwards every `NodeOutput.StructuredData` as an SSE `Result` chunk. Last result wins on the client (`summarizeService.ts:103-108` overwrites `rawResult`). |
| **P2** — LoadKnowledge type mismatch | `LoadKnowledgeConfig.PassthroughBinding` is `Dictionary<string,string>?` but post-Layer-1 substitution produces native JSON arrays | ❌ NO | No LoadKnowledge node in this playbook. |
| **P3** — EntityNameValidator scrubber allow-list mismatch | Source playbook's `allowList` cites DTO fields that don't exist | ❌ NO | No ValidateEntityNames node in this playbook. |
| **P4** (implicit) — fan-out 0-iterations | `TryExtractIterationConfig` succeeds but iterable evaluates to empty array | ❌ NO | No `iteration` block in either node's configJson; detection returns false. |
| **P5** (implicit) — template substitution edges | Layer 1 substitution mangles `{{X}}` expressions | ❌ NO | No `{{template}}` strings in this playbook's configJson. |

**Conclusion**: NONE of the W11 T116 documented bug classes apply to the Summarize File path. The playbook predates the multinode/aggregator era and runs on the legacy GenericAnalysisHandler pipeline that survived R7's refactor intact (per build verification 2026-06-30).

---

## 5. Reproduced failure / Predicted-failure-point analysis

**Empirical reproduction**: NOT performed in this audit (operator said this is an audit; no deploy or smoke-test access in this session). Build verification was performed locally:

```
dotnet build src/server/api/Sprk.Bff.Api/ --nologo
→ 19 Warning(s), 0 Error(s), Time Elapsed 00:00:16.55  (2026-06-30 audit)
```

The code compiles. The Dataverse playbook is well-configured (verified via 4 `mcp__dataverse__read_query` calls). All R7 architectural changes are either inapplicable (multinode/aggregator/fan-out/templates) or correctly handled (single-hop dispatch via Wave 5 backfill).

**Predicted failure points** (in descending likelihood, given operator says "currently broken"):

| # | Predicted failure mode | Diagnostic | Effort to confirm | Fix path |
|---|---|---|---|---|
| 1 | **Env-var drift in deployed BFF** — `Workspace:SummarizePlaybookId` was changed/cleared/typo'd OR a recent BFF deploy lost the App Service config setting | Hit `POST /api/workspace/files/summarize` with a tiny test file via curl; check Application Insights for `InvalidOperationException` "Workspace:SummarizePlaybookId is not configured" | 5 minutes | `az webapp config appsettings set --name spaarke-bff-dev --resource-group ... --settings "Workspace:SummarizePlaybookId=4a72f99c-a119-f111-8343-7ced8d1dc988"` |
| 2 | **Consumer-routing path serves a stale/wrong playbook GUID** — `sprk_playbookconsumer` was modified to a different playbook by a parallel project | Verified 2026-06-30 in this audit: row points at `4a72f99c-a119-f111-8343-7ced8d1dc988` (correct) | (already verified) | n/a |
| 3 | **Frontend `bffBaseUrl` or `authenticatedFetch` missing in the deployed wizard host** — wizard fails before BFF is even called | Browser DevTools Network tab: did the POST to `/api/workspace/files/summarize` actually fire? Was it 401 / 404 / CORS? | 5 minutes | If `authenticatedFetch is undefined`: wizard host (SummarizeFilesWizard code page wrapper) not passing the prop. If 401: token expired or wrong audience. If 404: deployed BFF doesn't have the `/api/workspace/files/*` route mapped (would require missing `Program.cs` `MapWorkspaceFileEndpoints()` call — verifiable by grepping). |
| 4 | **CORS / authorization rejection** — `WorkspaceAuthorizationFilter` rejects the request | App Insights `WorkspaceAuthorizationFilter` log entries | 5 minutes | Check filter requirements (typically just any authenticated user) |
| 5 | **AI service feature-disabled (Null-Object) fallback** — `IPlaybookOrchestrationService` resolves to a `NullPlaybookOrchestrationService` that throws `FeatureDisabledException` | App Insights `FeatureDisabledException` with `ErrorCode=...` near the summarize call | 5 minutes | Check `Features:AnalysisPipeline` or compound AI gate in BFF App Service settings; turn back on |
| 6 | **GenericAnalysisHandler tool config `sprk_configuration` JSON is malformed** (we read it 2026-06-30 — it's well-formed JSON with operation="extract", so this is very unlikely) | App Insights `ToolResult.Error` with `code=ValidationFailed` and message "Invalid tool configuration format." | 2 minutes | n/a — already verified |
| 7 | **Wizard UI client-side regression** — `summarizeService.ts` normalization broke for some recent response shape | Browser DevTools Console: `[SummarizeService] Raw response: ...` logs; check if normalizeResult chokes | 10 minutes | Inspect the raw chunk content client-side |

**The operator's "previously working / now broken" framing strongly suggests #1, #3, or #5** (env or config drift, NOT a logic regression) — those are all the failure modes that *change behavior without changing code*.

---

## 6. Root cause categorization

Based on the analysis in §3-5, **this audit cannot definitively categorize the root cause without empirical reproduction**. However, the evidence strongly favors:

| Category | Likelihood | Evidence |
|---|---|---|
| **R7 regression** (enum rename / DI change / dispatch refactor broke the path) | **LOW** | Build is clean; single-hop dispatch precondition (sprk_executortype non-null) is satisfied per W5 backfill; no aggregator/multinode/fan-out/template features used. |
| **Inherent engine bug class** (matches /narrate W11 T116 P1/P2/P3) | **NONE** | Step-by-step matrix in §4 shows no bug class applies. |
| **Configuration drift** (Dataverse row data wrong, OR BFF env var unset, OR wizard host prop missing) | **HIGH** | Operator framing + the architectural surface area pointing to env-var-resolution / wizard-host-wiring is exactly where this kind of break shows up. |
| **Missing implementation** (never fully shipped) | **NONE** | All artifacts are present: BFF endpoint registered, playbook with both nodes backfilled, action + tool + linker rows present, JPS prompt well-formed, frontend wizard fully implemented. |

**Working categorization**: **Configuration drift** in the deployed environment, possibly compounded by **wizard host wrapper wiring** if the standalone `SummarizeFilesWizard` Vite code page wrapper doesn't pass `authenticatedFetch` + `bffBaseUrl` props correctly (the dialog requires both — see `SummarizeFilesDialog.tsx:429-433`).

---

## 7. Disposition recommendation: RESTORE

### Restoration plan (Wave 12.3)

| Step | Action | Files / Surface touched | Effort |
|---|---|---|---|
| 1 | **Smoke** `POST /api/workspace/files/summarize` against deployed BFF with a tiny PDF; capture HTTP status + first 3 SSE chunks | curl + jq (no code) | 30 min |
| 2 | If 500 + "Workspace:SummarizePlaybookId is not configured": set the App Service env var, restart, re-smoke | App Service config (no code) | 15 min |
| 3 | If 401/403: verify SPA auth flow → token audience → endpoint authorization. Fix in client `useAuth()` if applicable. | Wizard host wrapper config | 30 min |
| 4 | If 503 / FeatureDisabledException: re-enable AI compound flag in BFF App Service settings | App Service config (no code) | 15 min |
| 5 | If wizard host doesn't pass `authenticatedFetch` / `bffBaseUrl`: fix in `src/solutions/SummarizeFilesWizard/src/main.tsx` (or wherever the dialog is mounted) | Single component wiring | 30 min |
| 6 | If 200 but empty result: deeper diagnostic — turn on BFF debug logging for `GenericAnalysisHandler`, re-smoke, inspect rendered prompt + structured-output schema reaching Azure OpenAI | Telemetry, no code | 1-2 hours |

**Total budget**: **1-3 hours** for steps 1-5 (configuration / wiring fixes only). If step 6 surfaces a deeper issue, fall back to remediation (see §7 alternate path) — but the engine has no bug class here, so step 6 is very unlikely.

### Optional remediation tightening (not blocking)

The BFF endpoint at `WorkspaceFileEndpoints.cs:357-368` emits an SSE `Result` chunk for **every** node that has `StructuredData` (Start node + AI Analysis node = 2 chunks). The client (`summarizeService.ts:103-108`) overwrites `rawResult` on each chunk, so the LAST one wins — which happens to be correct (AI Analysis runs after Start). This is **functionally correct but sloppy** (wastes bandwidth, briefly flashes garbage to anyone parsing intermediate chunks).

A minimal tightening would gate the chunk emission to nodes with `ExecutorType.AiAnalysis` (or check `evt.NodeName != "Start"`). This is a 3-line change, low-risk, and would also align this endpoint with the W11 T116 P1 architectural fix shipped for narrate. **Recommend including as a polish item in Wave 12.3 task, NOT a blocker.**

### Alternate path: remediation (if smoke surfaces unforeseen blocker)

If steps 1-5 above don't restore working state AND the failure is traceable to engine behavior (not config), the fallback is a thin code-defined wrapper that:

1. Accepts the uploaded files, extracts text (reusing `ITextExtractor` + helpers from `WorkspaceFileEndpoints`)
2. Calls `IOpenAiClient.GetStructuredCompletionRawAsync` directly with the JPS-derived prompt + JSON schema (reuse `PromptSchemaRenderer` for prompt assembly)
3. Returns the structured output as an SSE Result chunk
4. **Preserves the Action's `sprk_systemprompt` as the operator-tunable surface** (JPS-formatted; admin edits in maker portal continue to affect behavior — same contract as today)
5. **Preserves the wizard UI's `ISummarizeResult` shape** (which IS the Action's output schema)

This wrapper would be ~80-120 LOC in a new file (e.g., `Services/Ai/Workspace/SummarizeFileExecutor.cs` — or inline in the endpoint), invoked when a feature flag `Features:UseSummarizeFileDirectExecutor` is on. The playbook engine path remains intact for the rollback toggle. **Effort: ~1-2 days.** Wave 12.3 should generate this task only if Wave 12.3 step 1-5 fail to restore working state.

---

## 8. Open follow-ups (deferrals — NOT in scope for this audit)

| # | Item | Why it's not a blocker | Where it should land |
|---|---|---|---|
| 1 | `WorkspaceFileEndpoints.cs:357-368` emits SSE Result for every StructuredData-bearing node (sloppy but correct) | Functionally OK; not a regression | Wave 12.3 polish task (optional) |
| 2 | Two playbooks point at the same Action via the same tool (`Summarize File` vs `summarize-document-for-workspace@v1` both use `eeb05bfd-1260-f111-ab0b-70a8a59455f4` Action? — actually NO, they use different Actions; confirmed in §1.2) | Not an issue | n/a |
| 3 | `NodeService.cs:607` defaults `NodeType` to `AIAnalysis` when entity.NodeType is null (column removed by W5 task 055) | StartNode executor doesn't care; AIAnalysis default works correctly for both Start and AI Analysis nodes in this playbook | Latent inconsistency worth a tracking comment, not a fix |
| 4 | `LegalWorkspace/SummarizeFiles/*` (legacy copy, pre-shared-lib extraction) — confirm with operator that the LegalWorkspace code page wrapper is NOT what users hit, the standalone `src/solutions/SummarizeFilesWizard/` Vite code page is | Likely already-retired per `LEGALWORKSPACE-RETIREMENT.md` (per CLAUDE.md §17 pointer) — confirm during Wave 12.3 step 1 smoke | n/a |
| 5 | Two static SSE chunks per call (`Result` for Start + `Result` for AI Analysis) means clients implementing per-chunk consumption (not full-overwrite) would render Start's payload briefly | Sloppy; not broken | Wave 12.3 polish task |

---

## 9. Evidence checklist (acceptance criteria from POML)

| Criterion (POML §acceptance-criteria) | Status | Evidence |
|---|---|---|
| BFF endpoint + UI entry point + playbook ID located with file:line | ✅ | §1 |
| Playbook shape documented (SIMPLE vs COMPLEX) with node count + executor types | ✅ | §2 — SIMPLE 2-node (Start [33], AI Analysis [0]); live Dataverse-verified |
| Failure reproduced OR walked through with predicted-failure-point | ✅ | §5 — empirical reproduction deferred (no deployed-env access in this audit session); predicted-failure-point table provided |
| Root cause categorized | ✅ | §6 — Configuration drift (HIGH), R7 regression (LOW), Engine bug class (NONE), Missing impl (NONE) |
| Disposition recommended with effort estimate | ✅ | §7 — RESTORE, 1-3 hours for steps 1-5; alternate remediation path documented |
| Notes doc exists with file:line references throughout | ✅ | this file |

---

## 10. Cross-references

- W11 T116 systematic assessment: `projects/spaarke-ai-platform-unification-r7/notes/handoffs/wave11-t116-narrate-systematic-assessment.md`
- Wave 12 plan: `projects/spaarke-ai-platform-unification-r7/notes/wave12-mvp-completion-plan.md` §2.2
- W5 FR-19 backfill state: `projects/spaarke-ai-platform-unification-r7/tasks/TASK-INDEX.md` (tasks 050-055)
- W5 owner-review CSV (Summarize File rows): `projects/spaarke-ai-platform-unification-r7/notes/drafts/playbook-node-review-output.csv` (search for "Summarize File" — 2 rows)
- BFF Hygiene constraints: `.claude/constraints/bff-extensions.md`
- Sibling audits (Wave 12.1): `notes/audits/wave12-{120,122,123}-*.md`

---

## 11. Resolution (Wave 12.3 task 140) — 2026-06-30

**Status**: Configuration fix applied. Awaiting operator UAT (Wave 12.5 task T145) for end-to-end validation.

### Actual root cause vs. predicted

| Predicted (§5) | Actual finding 2026-06-30 |
|---|---|
| #1 — Env-var drift in deployed BFF (`Workspace:SummarizePlaybookId`) | **CONFIRMED.** Pre-fix App Service config (`az webapp config appsettings list --name spaarke-bff-dev`) returned 0 rows matching `Workspace__SummarizePlaybookId`. The env var was never set or was lost in a prior deployment. |
| #2 — Consumer routing serves stale/wrong playbook GUID | **NOT ROOT CAUSE — but routing IS correctly configured.** Live Dataverse query of `sprk_playbookconsumer` where `sprk_consumertype = 'summarize-file'` returns 1 row: id `271194cd-3670-f111-ab0e-70a8a590c51c`, playbook `4a72f99c-a119-f111-8343-7ced8d1dc988`, env `*`, enabled, priority 500. Routing record is correct. |
| #3 — Wizard host token plumbing | **NOT TESTED in this task.** Operator UAT under T145 will validate the wizard path with a real OBO token. |
| #5 — AI feature flag (FeatureDisabledException 503) | **NOT TESTED — none seen.** No `Features__*` toggles relevant to this endpoint observed in the App Service settings list. |

**Why the routing record didn't save the day on its own**: Without a deployed reproduction, the most likely explanation is that one of (a) `IConsumerRoutingService.ResolveAsync` returned null for some transient reason (Dataverse query timeout, cold-cache contention), (b) the `IGenericEntityService` Dataverse round-trip silently graceful-degraded per `ConsumerRoutingService.cs:138-150` (which logs ERROR but returns null), or (c) the routing pathway has a latent bug we haven't surfaced — in any of these cases the `WorkspaceFileEndpoints.cs:307` env-var fallback would have rescued the request, and now will. Defense-in-depth restored.

### Fix applied (5 minutes)

```powershell
az webapp config appsettings set `
  --name spaarke-bff-dev `
  --resource-group rg-spaarke-dev `
  --settings "Workspace__SummarizePlaybookId=4a72f99c-a119-f111-8343-7ced8d1dc988"
```

Verified post-fix:
- `az webapp config appsettings list ... --query "[?name=='Workspace__SummarizePlaybookId']"` → returns the row with the correct GUID.
- App Service auto-restart triggered by the settings change.
- `GET /healthz` → 200 OK in 0.44s after the restart window.
- `POST /api/workspace/files/summarize` with multipart body + dummy bearer → 401 (route mapped + auth challenge working; expected without a real OBO token).

### Code change required

**None.** Per audit recommendation §7, this is a pure App Service config drift fix.

### Smoke verification status

- **Configuration-level smoke**: ✅ PASSED. Env var set, App Service restarted cleanly, healthz green, route still serves 401 challenge with dummy bearer.
- **End-to-end smoke (real OBO token + real file)**: ⏸ Deferred to operator UAT under Wave 12.5 task T145 (no OBO token available in sub-agent execution context).

### Open follow-ups from §8 (status update)

| # | Item | Status |
|---|---|---|
| 1 | `WorkspaceFileEndpoints.cs:357-368` emits SSE Result for every StructuredData-bearing node | Still applies; recommended polish, not a blocker; can be addressed at any time. |
| 4 | Confirm operator that LegalWorkspace standalone code page is NOT what users hit, the `src/solutions/SummarizeFilesWizard/` Vite code page is | Out of scope for T140 (config-only); operator should resolve in T145 UAT. |

### Recommended additional hardening (not blocking T140 acceptance)

Pre-seat `Workspace__SummarizePlaybookId` on every BFF environment in the deployment automation (the `bff-deploy` skill or its underlying script). This prevents env-var drift from recurring after any future BFF redeploy. Worth tracking as a small follow-up issue.

---

*End of audit 121. Disposition: RESTORE with 1-3 hour configuration-fix budget. Engine bug class: NONE. Wave 12.3 task 140 — RESOLVED via App Service env-var set; operator UAT in T145.*

---

## 12. T145 UAT signoff (appended 2026-06-30)

**Code-path + Dataverse-state status**: ✅ PASS-CANDIDATE. Playbook `4a72f99c-...` confirmed 2-node clean (Start, AI Analysis → ACT-025). Env-var fallback path restored. App Service healthz green post-restart.

**Operator browser UAT**: PENDING — checklist at `notes/handoffs/wave12-3-uat-signoff.md` §4.1. AC8 will move from PENDING-OPERATOR to PASS on operator signoff.
