# Task 010 — ToolHandlerToAIFunctionAdapter Completion Notes

**Status**: ✅ Completed (2026-06-07; Wave 4 of Phase A)
**Rigor**: FULL

## What was built

`src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ToolHandlerToAIFunctionAdapter.cs` (~399 LOC) — wraps `IToolHandler` as `Microsoft.Extensions.AI.AIFunction`. Constructor takes data + delegates (4 ctor params: tool, handler, contextFactory, optional logger). **No DI registration** — instantiated per-tool by `SprkChatAgentFactory.ResolveTools()` in task 011.

## Guards & validation

- **FR-09**: Constructor rejects handlers without `InvocationContextKind.Chat`. Misconfigured rows cannot expose "not supported" tools to LLM.
- **FR-08**: JsonSchema well-formedness + object-root + `properties` shape validation at construction. Three guard rails surface at chat-session start, not LLM invocation.
- **Semantic JSON Schema validation deferred** — would add ~1MB NuGet (JsonSchema.Net), outside R6 NFR-02 ≤+5MB budget. Runtime validation handed off to handler's `ValidateChat` per FR-08 separation-of-concerns.

## ADR-015 compliance

- `LogToolOutcome` passes `toolName`, `handlerId`, `decisionId`, `outcome`, `durationMs`, `errorCode` ONLY. Never `AIFunctionArguments` / `ChatInvocationContext.ToolArgumentsJson` / `ToolResult.Data`.
- Dedicated test `InvokeAsync_Telemetry_DoesNotLogArgumentPayload` enforces with sentinel-string scan across every `Mock<ILogger>` invocation argument — strongest form of regression test for the ADR-015 binding.

## Tests

27 unit tests pass: happy path, null/whitespace/malformed JSON, non-object root, properties shape, playbook-only handler rejection, AIFunction surface (Name/Description/JsonSchema), dispatch to `ExecuteChatAsync` (not legacy), validation short-circuit returns structured error envelope, error propagation, cancellation, decision-id freshness per invocation, null-logger safety, ADR-015 telemetry hygiene.

## Build + size

- `dotnet build`: 0 errors, 16 pre-existing warnings
- BFF size delta: **+0.067 MB** (DLL adapter + no new NuGet packages)

## Unblocks

Task 011 — `SprkChatAgentFactory.ResolveTools()` instantiates this adapter per `sprk_analysistool` row with `AvailableInContexts ∋ Chat`.
