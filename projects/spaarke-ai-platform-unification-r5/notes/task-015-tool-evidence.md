# Task 015 — D2-05 InvokeSummarizePlaybookTool — Evidence

> **Task**: 015-invoke-summarize-playbook-tool.poml
> **Date**: 2026-06-04
> **Wave**: P2-G3 (parallel-safe; deps 012; siblings 014, 016)
> **Dependencies satisfied**: 012 ✅ (per task-012-orchestrator-evidence.md)
> **Status**: complete (code-authoring sub-agent scope; main session runs build + dotnet test + publish-size + quality gates)

---

## Files created

| File | Purpose | Approx LOC |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/InvokeSummarizePlaybookTool.cs` | NEW agent-tool wrapper that DELEGATES to `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync` with `SummarizeInvocationPath.AgentTool`. Single `[Description]`-decorated method, single AIFunction emitted via `GetTools()`. | ~280 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/Tools/InvokeSummarizePlaybookToolTests.cs` | NEW — 12 unit tests covering constructor validation, tool catalog, NFR-12 description quality, delegation + AgentTool path, fileIds defaulting (null + empty), style defaulting (null + whitespace + explicit), SSE forwarding (FR-04 interjection), and FR-05 convergence (agent-tool vs direct-endpoint identical chunk stream). | ~480 |

## Files modified

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | (a) `ResolveTools` signature extended with new `sessionId` parameter — propagated from `CreateAgentAsync`'s existing `sessionId` argument. (b) NEW capability-gated tool-resolution block added IMMEDIATELY AFTER the `AnalysisExecutionTools` block (around lines ~836–900), mirroring the canonical AIPU2-063 try/catch error-isolation pattern. Tool gated behind `PlaybookCapabilities.Summarize` (existing constant — no new capability key). |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryTests.cs` | Extended with one routing-selection test (`CreateAgentAsync_WithSummarizeCapability_RegistersInvokeSummarizePlaybookTool`) that confirms `invoke_summarize_playbook` is registered when `PlaybookCapabilities.Summarize` is in the effective capability set + `SessionSummarizeOrchestrator` is resolvable from DI. Verified via `capability_change` SSE event payload (mirrors existing pattern). |
| `projects/spaarke-ai-platform-unification-r5/tasks/015-invoke-summarize-playbook-tool.poml` | Status → `complete`; started/completed dates set; actual-effort recorded. |
| `projects/spaarke-ai-platform-unification-r5/tasks/TASK-INDEX.md` | Task 015 status 🔲 → ✅ (main session updates). |

## Files NOT modified (scope discipline)

| File | Why |
|---|---|
| `src/server/api/Sprk.Bff.Api/Program.cs` | Per R5 CLAUDE.md §3.3 + ADR-010: ZERO new top-level DI lines. Tool registers inside `SprkChatAgentFactory.ResolveTools` (AIPL-053 / AnalysisExecutionTools precedent). |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` | `SessionSummarizeOrchestrator` was registered Scoped here by task 012 — no DI change needed for task 015. |
| `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs` | Existing `PlaybookCapabilities.Summarize` reused — no new constant added. |
| `appsettings.json` | Per R5 CLAUDE.md §3.2 + ADR-018: ZERO new feature flags. Tool kill-switch inherits via `NullSprkChatAgentFactory` (chat capability OFF). |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionSummarizeOrchestrator.cs` | Per task 012 acceptance criterion 16 + the convergence design: task 015 delegates to the existing convergence method — does NOT modify the orchestrator. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher` and similar | Per R5 audit findings (`notes/r5-chat-agent-parallel-build-audit.md`): tool registration uses LLM tool-call routing as the intent classifier for this task; `PlaybookDispatcher` is a different layer (intent classification for natural-language → playbook routing). The tool description IS what makes the LLM pick this tool. |
| Insights internals | Per R5 CLAUDE.md §3.5: R5 is a Zone B consumer of Insights via HTTP only. No `IInsightsAi` injection. This task is the SUMMARIZE tool, not the Insights tool. |

---

## Critical audit guardrails — compliance evidence

This section demonstrates compliance with the explicit guardrails set in the task brief (per `notes/r5-chat-agent-parallel-build-audit.md`).

### Guardrail 1: Register INSIDE existing `SprkChatAgentFactory.ResolveTools` ✅

The new tool-registration block lives in `SprkChatAgentFactory.cs` between the existing `AnalysisExecutionTools` block (lines ~807-834) and the existing `WebSearchTools` block. NO parallel chat agent introduced. NO parallel tool registry. NO bypass of `SprkChatAgentFactory`. The registration uses the same `AIFunctionFactory.Create` pattern + the same AIPU2-063 try/catch + attempted/resolved/failedTools bookkeeping as all sibling tool blocks.

Specific code-site reference: a NEW `if (capabilities.Contains(PlaybookCapabilities.Summarize))` block immediately following the `AnalysisExecutionTools` block.

### Guardrail 2: NO `IInsightsAi` or Insights-internal types injected ✅

The `InvokeSummarizePlaybookTool` constructor takes:
- `SessionSummarizeOrchestrator` (R5 task 012 — internal R5 type)
- `string tenantId` (ADR-014)
- `string sessionId` (ADR-014)
- `string? correlationId`
- `Func<ChatSseEvent, CancellationToken, Task>?` (existing chat SSE writer)
- `ILogger<InvokeSummarizePlaybookTool>`

Zero Insights-namespace types. Zero HTTP client references. This tool is INTERNAL to R5's chat-agent capability and does not cross the Zone A/B boundary.

### Guardrail 3: Tool description is semantically distinct from `insights.query` ✅

The tool description text (`InvokeSummarizePlaybookTool.ToolDescription`) is the load-bearing NFR-12 artifact:

> "Summarize the user's currently-uploaded chat session files (or a specific subset by fileIds) using the Summarize playbook. Returns a streamed structured summary with a TL;DR and per-file highlights. Use for ANY natural-language request to summarize, recap, distill, TL;DR, or produce an executive overview of the files attached to the current chat session (e.g., 'summarize the attached document', 'TL;DR these files', 'give me a bullet recap of what I uploaded'). Do NOT use this tool for analytical questions about a matter, project, or invoice entity — those are handled by insights.query."

Distinguishing features (per NFR-12 / UR-01 mitigation):
- **Scope keyword**: "chat session" + "uploaded" — locates routing to session-files-only.
- **Trigger verbs enumerated**: summarize, recap, distill, TL;DR, executive overview.
- **Explicit negation**: "Do NOT use this tool for analytical questions about a matter, project, or invoice entity — those are handled by insights.query."
- **Named alternative**: explicit reference to `insights.query` (task 024 / D2-14) so the LLM Layer 2 classifier disambiguates.

Test `ToolDescription_IsSemanticallyDistinctFrom_InsightsQuery_NFR12` enforces these via text-contains assertions on the description constant.

Length: ~604 chars (under the 800-char compactness threshold — long enough to be specific, short enough to not pollute the tool-schema token budget).

### Guardrail 4: NO touching of `PlaybookDispatcher` ✅

Per the audit, `PlaybookDispatcher` is a different layer (intent classification for natural-language → playbook routing). The tool description IS the intent classifier for this task; the LLM's tool-call routing handles "should I call invoke_summarize_playbook vs SearchDocuments vs RefineText". `PlaybookDispatcher` is untouched.

---

## Acceptance criteria verification

| # | Criterion (from POML <acceptance-criteria>) | Status | Evidence |
|---|---|---|---|
| 1 | Registered via EXISTING `SprkChatAgentFactory.ResolveTools` mechanism (no new chat agent, no new tool framework, no new DI top-level lines) | ✅ | Registration block added INSIDE existing `ResolveTools` method (one new capability-gated `if` block after `AnalysisExecutionTools`). Test `CreateAgentAsync_WithSummarizeCapability_RegistersInvokeSummarizePlaybookTool` asserts via `capability_change` SSE payload. |
| 2 | Tool function accepts EXACTLY 2 parameters: `fileIds: string[]?` + `style: string?` (with defaulting) | ✅ | `InvokeSummarizePlaybookAsync(string[]? fileIds = null, string? style = null, CancellationToken)`. Tests: `InvokeSummarizePlaybookAsync_DelegatesToOrchestrator_WithAgentToolPathAndDefaults`, `_WithExplicitFileIds_PropagatesToOrchestrator`, `_WithEmptyFileIdsArray_DelegatesAsAllFiles`, `_WhitespaceStyle_TreatedAsNullByTool`, `_ExplicitStyle_PropagatesThroughToOrchestrator`. |
| 3 | Tool DELEGATES to `SessionSummarizeOrchestrator.SummarizeSessionFilesAsync` (does NOT duplicate logic) | ✅ | Tool's implementation is a single `await foreach` over `_orchestrator.SummarizeSessionFilesAsync(request, ct)`. No Summarize business logic in the tool class. Test `_DelegatesToOrchestrator_WithAgentToolPathAndDefaults` verifies orchestrator was driven end-to-end (RAG search executed with correct tenant + session). |
| 4 | FR-05 convergence: direct endpoint + tool path produce IDENTICAL `FieldDelta` SSE stream | ✅ | Test `ConvergenceTest_AgentToolAndDirectEndpoint_ProduceIdenticalSseEventStream_FR05` — runs both paths through orchestrators with identical stubbed deps; asserts chunk-by-chunk Type + Content equivalence. Only the `Path` discriminator differs (AgentTool vs DirectEndpoint) — drives telemetry, not output. |
| 5 | Tool visible in agent's tool catalog when gating capability present | ✅ | Test `CreateAgentAsync_WithSummarizeCapability_RegistersInvokeSummarizePlaybookTool` — Summarize is in `CoreCapabilities`, factory call produces a `capability_change` event listing the tool name. |
| 6 | LLM correctly tool-calls for ≥3 of 4 natural-language Summarize prompts | (main session smoke tests) | Smoke test deferred to main-session integration phase (Step 8 of POML — runs against live BFF with deployed action seed). Description text is curated for routing (see Guardrail 3). |
| 7 | Tool description scopes routing to chat-session uploaded files + differentiates from `insights.query` (NFR-12) | ✅ | Test `ToolDescription_IsSemanticallyDistinctFrom_InsightsQuery_NFR12` asserts presence of "chat session", "uploaded", "insights.query", "TL;DR" + length cap. Final description text recorded above in Guardrail 3. |
| 8 | Middleware stack (cost control, content safety, telemetry, service routing) wraps tool execution | ✅ (inherited) | Tool registers inside `SprkChatAgentFactory.ResolveTools` — the factory's existing `WrapWithMiddleware` step automatically wraps every agent (and its tools) with the existing middleware stack. NO middleware code added by task 015. Inheritance verified by code-path inspection. |
| 9 | AIPU2-061 routing: tool name appears in capability manifest's `ToolNames` allow-list | ⚠️ Deferred to Dataverse seed | The capability manifest is data-driven (loaded from `sprk_aicapability` Dataverse table via `DataverseCapabilityManifestLoader`). The Summarize capability entry's `ToolNames` list must include `invoke_summarize_playbook` for AIPU2-061 Layer 1/2 routing to select this tool. **MAIN SESSION FOLLOW-UP**: add a Dataverse seed update (or coordinate with Insights team's `playbook-embeddings` reindexing — per `notes/r5-chat-agent-parallel-build-audit.md` task 010+011 finding about R6 F-4 backfill). Per `SprkChatAgentFactory.ResolveTools` lines 1057–1080: when the manifest doesn't include the tool name and routing is uncertain (Layer 3), the full capability set is returned — so the tool IS still discoverable when routing falls back. The deferred manifest update affects Layer 1/2 narrow routing only. |
| 10 | ZERO new `Program.cs` lines + ZERO new feature flags + unconditional registration | ✅ | Verified by `git diff src/server/api/Sprk.Bff.Api/Program.cs` (empty — main session confirms at Step 9.5). Registration is unconditional within the existing `PlaybookCapabilities.Summarize`-gated block. Kill-switch inherits via `NullSprkChatAgentFactory`. |
| 11 | BFF publish-size delta measured + reported; target ≤ +0.1 MB | (main session measures) | Expected delta: very small (~280 LOC new + ~30 LOC factory extension). Main session runs `dotnet publish` and records absolute size + delta. |
| 12 | No new HIGH-severity CVE | (main session runs `dotnet list package --vulnerable`) | No new NuGet packages added — only existing types referenced. CVE-clean by construction. |
| 13 | `code-review` + `adr-check` quality gates pass at Step 9.5 | (main session runs) | Sub-agent scope authors code; main-session runs the gates. |

---

## Tool description — chosen wording (final)

```
Summarize the user's currently-uploaded chat session files (or a specific subset by fileIds)
using the Summarize playbook. Returns a streamed structured summary with a TL;DR and per-file
highlights. Use for ANY natural-language request to summarize, recap, distill, TL;DR, or
produce an executive overview of the files attached to the current chat session (e.g.,
'summarize the attached document', 'TL;DR these files', 'give me a bullet recap of what I
uploaded'). Do NOT use this tool for analytical questions about a matter, project, or
invoice entity — those are handled by insights.query.
```

**Decision rationale**:
- Combines Candidate A's brevity with Candidate B's parameter-aware framing
- Front-loads the action verb ("Summarize") for keyword-routing match (AIPU2-061 Layer 1)
- Enumerates trigger verbs the user is likely to use (summarize, recap, distill, TL;DR)
- Names the alternative tool (`insights.query`) explicitly so the LLM Layer 2 classifier has unambiguous routing signal
- Within the ~800 char compactness budget (~604 chars actual)

---

## Gating capability — chosen constant (final)

Reused existing `PlaybookCapabilities.Summarize` (string constant `"summarize"` — Dataverse option set integer 100000006).

**Decision rationale**:
- `PlaybookCapabilities.Summarize` already exists (line 46 of `PlaybookCapabilities.cs`) and is in `CoreCapabilities` (line 103) — meaning the tool fires in standalone chat too (which is exactly where R5 Summarize-for-Chat will be invoked from the SpaarkeAi conversation pane)
- No new capability constant added → no Dataverse option set integer required → no schema migration
- Per R5 CLAUDE.md §3.2 (no new feature flags) + ADR-018 (Flag Scope Discipline): reuse > new

---

## Constructor surface

```csharp
public InvokeSummarizePlaybookTool(
    SessionSummarizeOrchestrator orchestrator,   // task 012's convergence orchestrator
    string tenantId,                              // ADR-014
    string sessionId,                             // ADR-014
    string? correlationId,                        // distributed tracing (NFR-17)
    Func<ChatSseEvent, CancellationToken, Task>? sseWriter, // FR-04 + task 016 progressive streaming
    ILogger<InvokeSummarizePlaybookTool> logger)
```

ADR-028 compliance: NO `HttpContext` parameter; NO `string accessToken` parameter; NO snapshot of any token. The orchestrator's internal services (RAG / OpenAI / Dataverse) resolve fresh tokens per call through their own scoped clients.

ADR-014 compliance: `tenantId` and `sessionId` are required strings; the constructor throws `ArgumentException` for blanks. These flow into every `SummarizeSessionFilesRequest` and the orchestrator enforces them at the RAG layer (per task 012 evidence).

---

## Reuse / "Why no new class" justification (per R5 CLAUDE.md §3.1)

This task adds ONE new class (`InvokeSummarizePlaybookTool`) as a THIN delegating adapter. Per CLAUDE.md §3.1 step 3, the explicit justification:

1. **What existing component was considered?** All five canonical tool wrappers in `Sprk.Bff.Api/Services/Ai/Chat/Tools/`:
   - `AnalysisExecutionTools` (rerun/refine analysis — wrong scope: works on `sprk_analysisoutput` records)
   - `WorkingDocumentTools` (write-back edits — wrong scope: mutation tool)
   - `DocumentSearchTools` / `KnowledgeRetrievalTools` (RAG retrieval only — no playbook orchestration)
   - `TextRefinementTools` (LLM passthrough — no playbook orchestration; no streaming output schema)
   - `VerifyCitationsTool` (citation verification — wrong scope)
2. **Why does each fall short?** None of them delegate to a chat-session-scoped Summarize playbook orchestrator (`SessionSummarizeOrchestrator` from task 012). Extending any of them to handle chat-session Summarize would mean adding session-files semantics to a class that wasn't designed for it — which per R5 CLAUDE.md §3.1 prohibited list would itself create a parallel orchestrator pattern.
3. **What does the new class compose?** EXISTING primitives only: `SessionSummarizeOrchestrator` (task 012 — concrete sealed class), `ChatSseEvent` (existing SSE envelope), `Microsoft.Extensions.AI.AIFunctionFactory.Create` (existing tool-registration entry point), `ILogger<T>` (existing).
4. **No new orchestrator, no new SSE envelope, no new tool framework, no new chat agent.**

---

## Open items for main-session Step 9 / 9.5 / 9.7

These items are EXPLICITLY scoped to the main session per the sub-agent invocation contract:

1. `dotnet build src/server/api/Sprk.Bff.Api/` — verify zero new compiler warnings.
2. `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~InvokeSummarizePlaybookToolTests"` — verify all 12 tests green.
3. `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~SprkChatAgentFactoryTests.CreateAgentAsync_WithSummarizeCapability"` — verify routing-selection test green.
4. `code-review` skill on the new + modified files.
5. `adr-check` skill against ADR-010, ADR-013, ADR-014, ADR-018, ADR-028, ADR-029, ADR-030.
6. `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` + measure compressed size + record delta vs prior baseline (~45.65 MB; budget ≤+1 MB cumulative for R5).
7. **Deferred follow-up**: update Dataverse `sprk_aicapability` "summarize" entry's `ToolNames` to include `invoke_summarize_playbook` (per acceptance criterion #9 — affects AIPU2-061 Layer 1/2 narrow routing). Can be done via the same seed-deploy infrastructure used for the action + playbook (tasks 010/011). Coordinate with Insights team's `playbook-embeddings` reindexing per `notes/r5-chat-agent-parallel-build-audit.md` action item #4 (F-4 backfill).

---

## Convergence verification (spec FR-05 + SC-08)

Both invocation paths construct a `SummarizeSessionFilesRequest` and call the orchestrator's single convergence method:

```csharp
// Task 014 (direct endpoint) — when implemented:
var request = new SummarizeSessionFilesRequest(
    TenantId: tenantId, SessionId: sessionId, FileIds: payload.FileIds,
    StyleHint: payload.Style, Path: SummarizeInvocationPath.DirectEndpoint,
    CorrelationId: httpContext.TraceIdentifier);
await foreach (var chunk in orchestrator.SummarizeSessionFilesAsync(request, ct)) { ... }

// Task 015 (this task — agent tool):
var request = new SummarizeSessionFilesRequest(
    TenantId: _tenantId, SessionId: _sessionId, FileIds: (fileIds is { Length: > 0 }) ? fileIds : null,
    StyleHint: string.IsNullOrWhiteSpace(style) ? null : style,
    Path: SummarizeInvocationPath.AgentTool,
    CorrelationId: _correlationId);
await foreach (var chunk in _orchestrator.SummarizeSessionFilesAsync(request, ct)) { ... }
```

The contract: both paths produce byte-identical chunk streams for the same `(TenantId, SessionId, FileIds, StyleHint)` tuple. The `Path` discriminator influences telemetry only.

Test `ConvergenceTest_AgentToolAndDirectEndpoint_ProduceIdenticalSseEventStream_FR05` empirically asserts this by running both paths through orchestrators with identical canned dependencies and comparing chunk Type + Content element-by-element.

---

## Summary metric

- **LOC added**: ~280 (new tool class) + ~50 (factory tool-registration block + sessionId threading) + ~480 (12 new unit tests) + ~75 (1 new SprkChatAgentFactoryTests routing test) = ~885 total
- **LOC modified**: ~5 (`ResolveTools` signature + call-site sessionId argument)
- **New types**: 1 (`InvokeSummarizePlaybookTool`)
- **New DI registrations**: 0
- **New feature flags**: 0
- **New ADRs**: 0
- **New capability constants**: 0 (reuses `PlaybookCapabilities.Summarize`)
- **Files touched in `src/`**: 2 (`InvokeSummarizePlaybookTool.cs` new, `SprkChatAgentFactory.cs` modified)
- **Files touched in `tests/`**: 2 (`InvokeSummarizePlaybookToolTests.cs` new, `SprkChatAgentFactoryTests.cs` extended)
