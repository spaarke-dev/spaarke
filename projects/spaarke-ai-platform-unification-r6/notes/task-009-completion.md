# Task 009 — Split Execution Context (D-A-09) — Completion Notes

**Completed**: 2026-06-07
**Rigor**: FULL
**Phase**: A — Data-driven Foundation, Wave A-G3
**Branch**: `work/spaarke-ai-platform-unification-r6`
**Status**: ✅ Complete; downstream task 010 unblocked

---

## Files Touched

### New files
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolInvocationContextBase.cs` — shared base record (abstract)
- `src/server/api/Sprk.Bff.Api/Services/Ai/ChatInvocationContext.cs` — chat-driven invocation context
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/InvocationContextTests.cs` — 9 unit tests

### Modified files
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolExecutionContext.cs` — now derives from base; `AnalysisId` forwards to base `InvocationId`
- `src/server/api/Sprk.Bff.Api/Services/Ai/IToolHandler.cs` — adds three default interface members + `InvocationContextKind` flags enum

### Bookkeeping
- `projects/spaarke-ai-platform-unification-r6/tasks/009-split-execution-context.poml` — status `completed` + `<completion-notes>`
- `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` — row 009 🔲 → ✅
- `projects/spaarke-ai-platform-unification-r6/notes/task-009-completion.md` — this file

---

## Key Design Decisions

### D1 — Base class name: `ToolInvocationContextBase`

**Chosen**: `ToolInvocationContextBase` (abstract record).

**Rationale**: Matches the names in the task POML (`ToolInvocationContextBase`) and the project CLAUDE.md per-pillar binding rules ("Both contexts inherit a shared base"). The "Base" suffix signals intent ("never directly instantiated; always derived"), and the prefix mirrors the existing `ToolExecutionContext` naming family in `Services/Ai/`.

### D2 — Shared vs. derived field split

**Shared (on base)**:
- `InvocationId` (Guid, not required) — generic correlation; derived types expose strongly-named required accessors (`AnalysisId` / `ChatSessionId`) that forward to this storage
- `TenantId` (required string) — multi-tenant isolation
- `UserContext` (string?) — session-level instructions (NOT raw user message body per ADR-015)
- `MaxTokens` (int = 4096) — LLM parameter
- `Temperature` (double = 0.3) — LLM parameter
- `ModelDeploymentId` (Guid?) — LLM parameter
- `CorrelationId` (string?) — distributed-tracing
- `CreatedAt` (DateTimeOffset) — instantiation timestamp

**Playbook-only (on `ToolExecutionContext`)**:
- `AnalysisId` — delegating accessor over `InvocationId`
- `Document` (DocumentContext, required)
- `PreviousResults`, `ActionSystemPrompt`, `SkillContext`, `KnowledgeContext`
- `DownstreamNodes`, `AdditionalKnowledge`, `AdditionalSkills`, `TemplateParameters`, `PreResolvedLookupChoices`

**Chat-only (on `ChatInvocationContext`)**:
- `ChatSessionId` — delegating accessor over `InvocationId` (symmetric to `AnalysisId`)
- `DecisionId` — per-tool-call id (auto-generated)
- `ConversationHistoryRef` — handle only, never content (ADR-015)
- `RequestedToolName`, `ToolArgumentsJson` — LLM-driven dispatch
- `MatterId` — deterministic id for tenant-scoped routing (no user content)

### D3 — `IToolHandler` signature update strategy

**Chosen**: **C# 8 default interface methods** on the existing interface, NOT a separate `IChatToolHandler` companion interface, and NOT a generic `<TContext>` parameter.

**Why**:
1. Preserves source compatibility for existing 4 handlers (`GenericAnalysisHandler`, `DocumentClassifierHandler`, `SummaryHandler`, `SemanticSearchToolHandler`) — they inherit safe defaults and require ZERO source changes. The 80 production-side references to `IAnalysisToolHandler` (via the `GlobalUsings.cs` alias from task 006) remain valid.
2. Lets handlers opt in to chat by overriding `SupportedInvocationContexts`, `ValidateChat`, and `ExecuteChatAsync`.
3. Avoids a generic `<TContext>` that would have forced a per-handler reconfiguration of the registry + DI surface (would cascade into ADR-010 territory).
4. The Task-010 adapter pattern is: inspect `handler.SupportedInvocationContexts` flag; only invoke `ExecuteChatAsync` on handlers that have opted in; defensive throw on the default if mis-dispatched.

**New `IToolHandler` surface (additive only)**:
```csharp
InvocationContextKind SupportedInvocationContexts => InvocationContextKind.Playbook;
ToolValidationResult ValidateChat(ChatInvocationContext ctx, AnalysisTool tool) =>
    ToolValidationResult.Failure(...);
Task<ToolResult> ExecuteChatAsync(ChatInvocationContext ctx, AnalysisTool tool, CancellationToken ct) =>
    throw new NotSupportedException(...);
```

Plus a new `[Flags] enum InvocationContextKind { None=0, Playbook=1, Chat=2, Both=3 }`.

### D4 — Forwarding accessors for required correlation fields

Both derived types expose a required correlation field (`AnalysisId` on playbook; `ChatSessionId` on chat) whose getter+init delegate to the base's non-required `InvocationId` storage. This:

- Preserves source compatibility (existing code reads `context.AnalysisId` unchanged).
- Maintains the pre-R6 invariant that callers MUST set the correlation id at object-initializer time (the required modifier enforces it on the derived type).
- Lets shared code (e.g., future cross-context tracing) read `context.InvocationId` polymorphically through the base.

---

## Build + Test Results

- **BFF API**: `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors, 0 warnings**
- **Test project**: `dotnet build tests/unit/Sprk.Bff.Api.Tests/` → **0 errors, 0 warnings**
- **Tests**: `dotnet test ... --filter "InvocationContext|SummaryHandler|DocumentClassifier"` → **85 passed, 0 failed, 1 skipped**
- **Existing handlers verified to compile unchanged**: GenericAnalysisHandler, DocumentClassifierHandler, SummaryHandler, SemanticSearchToolHandler all compile + their existing tests pass

---

## Publish-Size Delta

| Snapshot | Bytes | MB |
|---|---|---|
| Baseline (pre-task) | 144,485,738 | 137.792 MB |
| Post-task | 144,514,154 | 137.819 MB |
| **Delta** | **+28,416** | **+0.027 MB** |

Well under +5 MB R6 budget (NFR-02). Three new files + interface members add minimal IL bytes; no new transitive dependencies introduced.

---

## ADR + NFR Compliance

| Concern | Status | Evidence |
|---|---|---|
| ADR-010 (DI minimalism) | ✅ | Zero new top-level DI registrations. All new types are POCO records / interface members / enum — not services. |
| ADR-013 (AI facade boundary) | ✅ | All new types live in `Services/Ai/` (not in `PublicContracts/`). Refactor is purely internal to the AI namespace. |
| ADR-015 (data governance) | ✅ | `ChatInvocationContext` exposes IDs + opaque history handle only; no user-message-body fields. Reflection-based unit test (`ChatInvocationContext_AdrFifteenBinding_DoesNotExposeRawUserMessageContent`) asserts no `UserMessage` / `ConversationHistory` / `MessageBody` property. |
| ADR-029 (publish hygiene) | ✅ | +0.027 MB delta ≪ +5 MB R6 budget. |
| NFR-04 (Zero Agent Framework) | ✅ | No `Microsoft.Agents.*` references introduced. |
| NFR-08 (11 node executors preserved) | ✅ | Not touched. `ToolExecutionContext` retains every playbook field with identical name + semantics. |
| FR-09 (split contexts) | ✅ | Base + two derived types exist; existing handlers compile + run unchanged. |

---

## Stop-and-Report Triggers — None Fired

- Build succeeded; no resolution failures.
- No modifications to `PublicContracts/`.
- No new ADR or feature flag introduced.
- Pre-fill flow not modified (it uses different code paths; FR/NFR-07 unaffected).
- 11 production node executors not modified (NFR-08 preserved).
- Existing 4 handlers compile + work unchanged (FR-06 honored via task-006 alias + this task's default-interface-method strategy).
- Quality gates passed.
- BFF publish-size delta +0.027 MB (well below +1 MB report threshold).

---

## Downstream Unblocks

- **Task 010** (`ToolHandlerToAIFunctionAdapter`) — can now build the adapter; will inspect `IToolHandler.SupportedInvocationContexts` flag and construct `ChatInvocationContext` for chat-driven LLM tool calls. Expected adapter pattern:
  1. Adapter holds reference to `(AnalysisTool tool, IToolHandler handler)`.
  2. Adapter exposes `Microsoft.Extensions.AI.AIFunction` whose parameter schema comes from `tool.JsonSchema` (FR-08 / task 008).
  3. When LLM invokes the AIFunction, adapter:
     - Validates `handler.SupportedInvocationContexts.HasFlag(InvocationContextKind.Chat)` — refuses dispatch if not opted in.
     - Builds `ChatInvocationContext` from the active chat session + LLM args JSON.
     - Calls `handler.ValidateChat(ctx, tool)` → if Failure, return error result without invoking handler.
     - Calls `handler.ExecuteChatAsync(ctx, tool, ct)` → returns `ToolResult`.
     - Emits telemetry (tool name + decision id + outcome + timestamp ONLY per ADR-015).

- **Handler workstream (tasks 100, 101–108)** — wave 1 deterministic handlers (DateExtractor, FinancialCalculator, ClauseComparison, FinancialCalculation) can choose to opt in to chat by setting `SupportedInvocationContexts = InvocationContextKind.Both` and overriding `ExecuteChatAsync`. Wave 2 LLM-assisted handlers likewise.

- **Q9 batch migration (task 012)** — the 10 pre-R5 chat tools will need to be reauthored as `IToolHandler` implementations that opt in to chat; this task's framework makes that mechanically straightforward.

---

*Filed by task-execute (FULL rigor) per project CLAUDE.md parallel-wave bookkeeping rule.*
