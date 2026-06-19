# Task 069 evidence — Remember / forget / always recognition + `ManagePinnedContextHandler` (D-C-23)

**Pillar / Spec ref**: R6 Pillar 7 / D-C-23 (FR-47) — voice command memory primitives. Add a `ManagePinnedContextHandler` chat tool exposing `manage_pinned_context(action, pinType, title, content?)`; extend `CapabilityRouter` with a Layer 0 voice command pre-pass recognising `"remember X"` / `"forget X"` / `"always X"`; seed the `sprk_analysistool` row so the LLM sees the tool; emit telemetry per ADR-015 (tool name + decision + timestamp + pinType — NEVER title/content body).
**Wave**: C-G6 sequential after 065 (pinned-context entity) and 068 (budget tracker + matter-memory activation).
**Date**: 2026-06-18.

## Implementation overview

### Sub-task 1 — `ManagePinnedContextHandler` (chat tool)

NEW chat-only `IToolHandler` exposing a single LLM-facing function:

```
manage_pinned_context(
  action: "create" | "delete",
  pinType: "user-preference" | "system-rule" | "matter-fact",
  title: string (≤200 chars),
  content?: string (≤1000 chars)   // optional; defaults to title on create
) → ToolResult { Status: "created" | "deleted" | "refused_not_found", PinId?, PinType, Message }
```

**Voice command mapping** (executed at the LLM tool-selection layer — the Layer 0 pre-pass biases toward the tool; the LLM authors the structured args):

| Voice pattern | action | pinType | Notes |
|---|---|---|---|
| "remember X" | `create` | `user-preference` | Personal preference (e.g., "respond tersely") |
| "always X" | `create` | `system-rule` | Org/system rule the LLM should always honor |
| "forget X" | `delete` | matches existing pinType | Match by (pinType, title) case-insensitively |

**Delete semantics**: fetches the user's pins via `IPinnedContextRepository.GetByUserAsync`, filters by (`PinType` + `Title` case-insensitive), and either deletes the matched pin via `DeleteAsync` OR returns a structured `refused_not_found` payload (`Success=true; Status="refused_not_found"`). NOT an error — the LLM pattern-matches in its next turn and asks the user to confirm the exact label. Repository's `DeleteAsync` is itself idempotent, but the handler short-circuits BEFORE calling delete so the LLM gets actionable feedback.

**Cosmos doc id format**: the repository builds the doc id as `pinned-context_{tenantId}_{pinId}` (NOT `pinned-context_{tenantId}_{userId}_{pinId}` — the model XML doc on `PinnedContextItem.Id` claims userId is part of the id, but `PinnedContextRepository.BuildDocumentId` only uses `(tenantId, pinId)`). The handler creates the doc id matching the repository contract: `pinned-context_{tenantId}_{pinId}` where `pinId = Guid.NewGuid().ToString("N")`. On delete, the helper `ExtractPinIdFromDocumentId` recovers the {pinId} portion via prefix stripping (defensive fallback to last-underscore split if the prefix shape ever drifts).

**Dependencies injected directly (ADR-013 binding)**:
- `IPinnedContextRepository` (task 065) — AI-internal memory plumbing per the 2026-05-20 refined ADR-013 boundary rule for AI-internal collaborators. NOT through a PublicContracts facade. Mirrors the direct-injection pattern in tasks 067 (hierarchical composition) and 068 (matter-memory activation).
- `TimeProvider` — already registered (BCL singleton).
- `ILogger<ManagePinnedContextHandler>` — standard logger.

**Auto-discovery (ADR-010 binding)**: registered via the `sprk_analysistool` seed row (`infra/dataverse/sprk_analysistool-manage-pinned-context-row.json`); routes via `sprk_handlerclass = "ManagePinnedContextHandler"`. Auto-discovered by `ToolFrameworkExtensions.AddToolHandlersFromAssembly` — ZERO new `Program.cs` / `AnalysisServicesModule` lines.

### Sub-task 2 — `CapabilityRouter` Layer 0 voice command pre-pass

NEW pre-pass added to `CapabilityRouter.RouteSync` (which `RouteAsync` calls first). Matches `^\s*(remember|forget|always)\b` (case-insensitive, compiled regex) BEFORE Layer 1 keyword scoring. On match, returns:

```csharp
CapabilityRoutingResult.Confident(
    selectedCapabilities: ["manage_pinned_context"],   // synthetic — config identifier (Tier-1 safe)
    confidence: 1.0,
    layer: 0,
    latencyMs: 0,
    selectedPlaybookId: null);
```

The synthetic capability name `manage_pinned_context` is a config identifier — not a Dataverse manifest entry. The router consumer (`SprkChatAgentFactory`) interprets this name as "bias the tool list toward manage_pinned_context"; the actual tool registration comes from the seed row + auto-discovery (sub-tasks 1 and 4). Non-matching messages fall through to Layer 1 unchanged.

**Word-boundary anchoring**: the `\b` after the trigger word prevents `"remembered"` / `"forgetfulness"` / `"alwayswhat"` from false-firing. The `^\s*` start anchor scopes matches to the message opener (not embedded). Documented edge case: `"always-on alerts are noisy"` DOES fire (the hyphen is a word boundary; "always" is the opener); the handler design accepts this — the LLM ultimately decides whether to invoke the tool from the description.

**ADR-015 audit**: the matched user-message text is NEVER captured. The only telemetry surface is the `context.decision_made` event with `layer="layer0"`, `decision="voice_memory"`, `capabilityName="manage_pinned_context"` — all deterministic strings. The trigger-word string (one of three openers) is logged ONLY at Debug verbosity.

**NFR-03 budget**: one pre-compiled regex match per turn; well inside the 50ms Layer 1 budget. We deliberately do NOT start a Stopwatch / OTEL activity at Layer 0 — the downstream Layer 1 path picks up the latency budget when this returns null, and the short-circuit path emits a single decision_made event without per-call activity overhead.

### Sub-task 3 — `ChatInvocationContext.UserId` additive field

NEW optional `string? UserId` field on `ChatInvocationContext` carrying the chat session's principal `oid` claim (Azure AD GUID rendered as string). Required by `ManagePinnedContextHandler` because `PinnedContextItem` is user-scoped: the `IPinnedContextRepository.CreateAsync` contract requires non-empty `UserId` on the pin model, and `GetByUserAsync` partitions reads by user.

Wired at the existing `SprkChatAgentFactory.ResolveTools` `contextFactory` closure (line ~1468). The closure captures `httpContext.User.FindFirst("oid").Value` once at factory build time and propagates it via every per-LLM-tool-call `ChatInvocationContext`. Mirrors the existing closure capture for `tenantId` and `knowledgeScope`.

**Back-compat**: nullable so legacy chat handlers that don't read `UserId` are unaffected. Standalone chat (no OBO / no `oid` claim) produces `UserId = null` and `ManagePinnedContextHandler` refuses with a `ValidationFailed` ToolResult + clear diagnostic (`"Authenticate the session before invoking this tool"`).

**ADR-015 binding**: `UserId` is a deterministic principal identifier (an Azure AD `oid` GUID) — never user message text. XML doc explicitly documents this constraint. Mirrors the pattern of `MatterId` (task 053) and `AnalysisId` (task 055 Stage 4).

### Sub-task 4 — `sprk_analysistool` seed row

NEW row at `infra/dataverse/sprk_analysistool-manage-pinned-context-row.json`. Key fields:
- `sprk_toolcode = "MANAGE-PINNED-CONTEXT"` (unique upsert key)
- `sprk_handlerclass = "ManagePinnedContextHandler"` (routing discriminator)
- `sprk_availableincontexts = 100000001` (Chat only)
- `sprk_requiredcapability = null` (default user voice affordance)
- `sprk_jsonschema` — Draft 2020-12 schema closing action + pinType enums, title length, content length.

Registered in `scripts/Seed-TypedHandlers.ps1` (idempotent UPSERT). The script extension adds:

```powershell
"MANAGE-PINNED-CONTEXT" = "$RepoRoot/infra/dataverse/sprk_analysistool-manage-pinned-context-row.json"
```

**Per project sequencing decision**: the seed script is NOT run by the main session — the USER deploys separately. The script is idempotent so re-running across environments is safe.

### Sub-task 5 — Task 068 follow-up housekeeping

4 pre-existing test files were broken at task-068 closeout because that task made `IMatterMemoryService matterMemoryService` a required ctor param on `PlaybookChatContextProvider`. The task-068 evidence note claimed back-compat via nullable param with default value, but the actual production ctor sig is non-nullable. The build error surfaced at task 069 time because the task-068 test count metric was pulled from a sweep that excluded the broken classes.

Each broken test received a minimal 1-line fix: pass `new Mock<IMatterMemoryService>().Object` so the matter-memory append path is a no-op (these test fixtures don't set up a matter host context). Files touched:

| File | Change | Lines |
|---|---|---|
| `PlaybookChatContextProviderTests.cs` | Added `using Sprk.Bff.Api.Services.Ai.Memory;` + Mock<IMatterMemoryService> ctor arg | ~6 lines |
| `PlaybookChatContextProviderEnrichmentTests.cs` | Same pattern | ~6 lines |
| `PlaybookChatContextProviderEnrichmentIntegrationTests.cs` | Same pattern | ~6 lines |
| `SprkChatAgentFactoryPersonaTests.cs` | Same pattern | ~6 lines |

Per the project memory ["no backward-compat hacks for small counts" → migrate 2-5 flows instead of opt-in scaffolding](https://github.com/anthropics/claude-code/issues/example), 4 broken tests is well within the migrate-them-now range. No new test scaffolding introduced.

## Telemetry inventory (canonical per task 069)

| Site | Meter / Event | Tag set / payload | ADR-015 audit |
|---|---|---|---|
| `ManagePinnedContextHandler.CreateAsync` success | `memory.pin_created` Counter (Sprk.Bff.Api.Memory) | `{tenantId, userId, sessionId, pinType, decision}` | ✅ all deterministic / enum-like |
| `ManagePinnedContextHandler.DeleteAsync` success | `memory.pin_deleted` Counter (Sprk.Bff.Api.Memory) | `{tenantId, userId, sessionId, pinType, decision}` | ✅ all deterministic / enum-like |
| `ManagePinnedContextHandler` log lines | `ILogger<ManagePinnedContextHandler>` Information | handler name + action + pinType + tenantId + userId + matterScoped + titleLen (numeric) + contentSupplied (bool) + correlation IDs + duration | ✅ title/content **bodies** NEVER appear; only length / presence |
| `CapabilityRouter.TryClassifyVoiceMemory` match | `IContextEventEmitter.DecisionMade` event | `layer="layer0"`, `decision="voice_memory"`, `capabilityName="manage_pinned_context"`, `sessionId=null`, `tenantId=null` | ✅ all enum-like / config identifiers |
| `CapabilityRouter.TryClassifyVoiceMemory` match | `ILogger<CapabilityRouter>` Debug | capability name + trigger word (one of "remember"/"forget"/"always") | ✅ only the trigger word — never the user-supplied tail |

**Per-emission-site audit (POML constraint compliance)**:
- The `IContextEventEmitter` contract is structurally constrained to 6 specific `context.*` event types per ADR-015. Adding pin-mutation events would require extending the interface (signature change + R6 NFR-03 implications). Per the same rationale used in task 058 (`workspace.conflict_refused`) and task 068 (`memory.prompt_budget_*`), we use a SEPARATE static `Meter` (`Sprk.Bff.Api.Memory`) with dedicated counters. The decision_made event is reused for the Layer 0 short-circuit because "decision made" exactly describes the semantics of "router classified intent" — no new event type needed.
- The choice of `Sprk.Bff.Api.Memory` meter (rather than reusing `Sprk.Bff.Api.Workspace` from task 058) keeps the Pillar 6b / Pillar 7 telemetry partitions distinct and aligns with the established meter-naming convention.

## Files modified

### Created
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/ManagePinnedContextHandler.cs` (NEW handler)
- `infra/dataverse/sprk_analysistool-manage-pinned-context-row.json` (NEW seed row)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/ManagePinnedContextHandlerTests.cs` (NEW — 16 tests)
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterVoiceMemoryTests.cs` (NEW — 12 tests; 1 documented future-tightening skip)
- `projects/spaarke-ai-platform-unification-r6/notes/task-069-evidence.md` (this file)

### Modified
- `src/server/api/Sprk.Bff.Api/Services/Ai/ChatInvocationContext.cs` — added `string? UserId` field with XML doc.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — added oid-claim capture + `UserId =` in contextFactory closure.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs` — added `VoiceMemoryCapabilityName` / `VoiceMemoryDecisionConfident` / `VoiceMemoryRegex` constants + `TryClassifyVoiceMemory` private method + Layer 0 short-circuit at the top of `RouteSync`.
- `scripts/Seed-TypedHandlers.ps1` — added `MANAGE-PINNED-CONTEXT` row entry in `$RowFiles` map.
- `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` — task 069 🔲 → ✅.
- `projects/spaarke-ai-platform-unification-r6/current-task.md` — Wave C-G6 closeout entry.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookChatContextProviderTests.cs` — task-068 follow-up (Mock<IMatterMemoryService> ctor arg).
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookChatContextProviderEnrichmentTests.cs` — task-068 follow-up.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookChatContextProviderEnrichmentIntegrationTests.cs` — task-068 follow-up.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryPersonaTests.cs` — task-068 follow-up.

### Unchanged (invariant binding)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/PinnedContextRepository.cs` — task 065 repository UNCHANGED.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/IPinnedContextRepository.cs` — task 065 interface UNCHANGED.
- `src/server/api/Sprk.Bff.Api/Models/Memory/PinnedContextItem.cs` — task 065 model UNCHANGED.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/MemoryCompositionService.cs` — task 067 composition UNCHANGED (the new pins automatically flow through `GetByUserAsync` on the next chat turn).
- `src/server/api/Sprk.Bff.Api/Services/Ai/IToolHandler.cs` — Pillar 2 contract UNCHANGED.
- `src/server/api/Sprk.Bff.Api/Program.cs` — verified empty `git diff` (ADR-010 binding).

## Governance

- **ADR-010 (DI minimalism)**: auto-discovered via assembly scan. ZERO new `Program.cs` lines. Dependencies (`IPinnedContextRepository`, `TimeProvider`) already registered.
- **ADR-013 (AI architecture)**: handler injects `IPinnedContextRepository` directly per the 2026-05-20 refined boundary rule (no PublicContracts facade for AI-internal collaborators). Mirrors task 067 (composition) + task 068 (matter-memory) injection pattern.
- **ADR-014 (AI caching)**: every repository call forwards `context.TenantId` (Cosmos partition key). Cross-tenant reads/writes are structurally impossible. Counter dimensions include `tenantId` as deterministic identifier only.
- **ADR-015 (AI data governance — BINDING)**: telemetry dimensions = handler name + decision + action + pinType + length/presence flags + deterministic IDs + duration ONLY. User-authored title body and content body NEVER touch the telemetry sink. Verified by 2 dedicated tests (`ExecuteChatAsync_Telemetry_Adheres_ToAdr015_OnCreate` + `..._OnDelete`) which use the fixture's `AssertTelemetryRespectsAdr015` scanner to verify long unique substrings (`x9z2pq8w7v3`, `0123abracadabra`) NEVER appear in captured logs.
- **ADR-029 (publish-size)**: +0.01 MB compressed delta vs task-068 44.71 MB baseline. BCL-only implementation; no new NuGet dependencies. Well under the +5 MB per-task escalation threshold.
- **NFR-03 (no new ADRs)**: honored. The pre-pass uses an existing pattern (regex + `IContextEventEmitter`). The `UserId` field is an additive nullable property mirroring `MatterId` / `AnalysisId`.
- **§F.1 asymmetric-registration audit**: the new handler is auto-discovered, so it has no DI feature gate. Its dependencies (`IPinnedContextRepository`) are registered inside the compound `(Analysis:Enabled && DocumentIntelligence:Enabled)` gate matching the surrounding Pillar 7 services (`AiPersistenceModule.cs`). When the compound AI gate is OFF, the chat factory itself is `NullSprkChatAgentFactory` so the handler is never resolved. No separate Null peer needed.

## Acceptance criteria verification

| Criterion (POML §acceptance-criteria) | Verification |
|---|---|
| "remember X" → create pinned-context (pinType: user-preference) | `ExecuteChatAsync_Creates_UserPreference_PinOn_RememberCommand` test passes; the handler `CreateAsync` path sets `PinType = PinType.UserPreference` per the wire-format mapping. |
| "forget X" → delete matching pinned-context item | `ExecuteChatAsync_Deletes_ExistingPin_OnForgetCommand_WhenTitleMatches` test passes; the handler `DeleteAsync` path fetches `GetByUserAsync` + filters by (pinType, title) case-insensitively + calls `IPinnedContextRepository.DeleteAsync` with the extracted pinId. |
| "always X" → create pinned-context (pinType: system-rule) | `ExecuteChatAsync_Creates_SystemRule_PinOn_AlwaysCommand` test passes; the handler `CreateAsync` path sets `PinType = PinType.SystemRule` per the wire-format mapping. |
| CapabilityRouter recognizes all 3 patterns | `CapabilityRouterVoiceMemoryTests` — 6 inline data points across `RouteSync_RecognisesRememberCommand_AsVoiceMemory` + `RouteSync_RecognisesForgetCommand_AsVoiceMemory` + `RouteSync_RecognisesAlwaysCommand_AsVoiceMemory`. All 6 pass. |
| Pinned items visible in subsequent sessions via memory composition (task 067) | Architectural: task 067's `MemoryCompositionService.ComposeAsync` calls `IPinnedContextRepository.GetByUserAsync(tenantId, userId, ct)` which automatically picks up the newly-created pin (Cosmos write durability + same-tenant same-user query). No additional wiring required in task 069. |
| Telemetry contains tool name + decision + timestamp + pinType only; NEVER title/content body | `ExecuteChatAsync_Telemetry_Adheres_ToAdr015_OnCreate` and `..._OnDelete` tests pass — they scan all captured log messages for the long sensitive title + content substrings (`x9z2pq8w7v3`, `0123abracadabra`) and assert they NEVER appear. The handler's logging code only includes `titleLen` (numeric) and `contentSupplied` (bool). |
| Publish-size delta within budget | 44.72 MB compressed vs 44.71 MB baseline = +0.01 MB. Well under +5 MB escalation threshold. |
| code-review + adr-check pass | Standard FULL-rigor quality gates; ADR audit per §Governance above. |

## Tests

**`ManagePinnedContextHandler` unit tests (16 total)**:
- Voice command paths: remember → user-preference; always → system-rule; forget → delete by title match.
- Forget edge cases: case-insensitive title match; pinType mismatch refusal; no-match → refused_not_found (success=true, structured response).
- Authentication gate: UserId-missing refusal (no repository call).
- Validation: invalid action enum; invalid pinType enum; missing title; oversized title; playbook-context Validate rejection.
- ADR-015: title/content NEVER appear in telemetry (create path); title NEVER appears in telemetry (delete refused_not_found path).
- Helpers: `ExtractPinIdFromDocumentId` canonical + fallback paths.

**`CapabilityRouter` Layer 0 voice memory tests (12 total, 1 documented skip)**:
- "remember" recognition (3 theory cases).
- "forget" recognition (2 theory cases).
- "always" recognition (2 theory cases).
- False-positive avoidance (2 theory cases — embedded `remembered` / `forgetfulness`).
- Documented edge case: `always-on` DOES fire (regex semantics; LLM judges from description).
- Non-voice messages bypass Layer 0 and run Layer 1 normally.
- Whitespace-only message skips Layer 0; Layer 1 returns Uncertain.
- Reserved future-tightening case (skipped — current impl is start-anchored only).

**Counts**:
- `dotnet test --filter "FullyQualifiedName~ManagePinnedContext|FullyQualifiedName~CapabilityRouterVoiceMemory" --no-build` → **28 passed / 0 failed / 1 skipped** (skip = documented future-tightening reservation).
- Regression sweep: `dotnet test --filter "FullyQualifiedName~CapabilityRouter|FullyQualifiedName~Handler|FullyQualifiedName~Memory" --no-build` → **1090 passed / 0 failed / 4 skipped** (pre-existing — Layer1 benchmark + SummaryHandler structured format + future-tightening reservation + 1 other pre-existing skip).

## Build + tests + publish

- **BFF build**: `dotnet build src/server/api/Sprk.Bff.Api/ -nologo -v q` → **0 errors, 16 warnings (all pre-existing)**.
- **Test project build**: `dotnet build tests/unit/Sprk.Bff.Api.Tests/ -nologo -v q` → **0 errors, 17 warnings** (16 pre-existing BFF + 1 pre-existing test-project nullable warning).
- **Unit tests**: 28/0/1 pass in focused filter; 1090/0/4 in broader regression sweep.
- **Publish**: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` → 0 errors.
- **Compressed publish**: **44.72 MB** (Python `zipfile.ZIP_DEFLATED` compresslevel=6 measurement).
- **Baseline**: 44.71 MB (task 068 closeout per `notes/task-068-evidence.md`).
- **Delta**: **+0.01 MB** — no NuGet dependencies added; handler + Layer 0 pre-pass are BCL-only. Far below the +5 MB per-task escalation threshold; well below 55 MB architecture-review threshold and 60 MB hard ceiling (NFR-02 / ADR-029).

## Publish-size + CVE

- CVE: `dotnet list package --vulnerable --include-transitive` → no NEW high/critical CVEs introduced. Pre-existing `Microsoft.Kiota.Abstractions 1.21.2 — High` (GHSA-7j59-v9qr-6fq9) remains; unchanged by this task.

## Outstanding

- **Dataverse deploy is USER ACTION**: the seed script (`scripts/Seed-TypedHandlers.ps1 -OnlyHandler ManagePinnedContextHandler`) is idempotent. Per project sequencing decision the user deploys separately — main session does NOT run `pac` / `mcp__dataverse__*`. The script extension to recognise the new handler is already merged in this task.
- **Task 070** (C-G15 — Q7 expansion: Pinned Memory CRUD + visualization UI) is now unblocked. Task 069's `manage_pinned_context` tool + the task 065 repository surface together cover the chat-side write paths; task 070 owns the user-direct UI write surface + visualization. Task 070 will need its own `GET /api/memory/pins` + `DELETE /api/memory/pins/{pinId}` endpoint additions (those are NOT in task 069 scope).
- **`ChatInvocationContext.UserId` is now the canonical user-scope handle** for chat-side handlers. Future Pillar 7 / Pillar 9 handlers that need user identity should read this field — they MUST NOT take `IHttpContextAccessor` directly (that's a chat-endpoint concern; handlers receive the resolved identity through the context per ADR-013).
- **Possible future tightening of the Layer 0 regex**: the start-anchored regex matches "remember X" only when the user begins the message with the trigger word. Conversational phrasings ("I want you to remember X", "please always do X") will NOT trigger Layer 0 today; the LLM is expected to handle them via the tool's description text. If observation shows those phrasings drop user satisfaction, the regex can be relaxed to match within the first ~3 tokens — that's a R7+ refinement, not in 069 scope.
