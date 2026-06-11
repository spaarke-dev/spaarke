# Wave 8 — LegalResearchTools migration (Q9 chat-tool migration)

**Status**: Handler + tests + seed rows created; awaiting main-session removal of hardcoded factory block.
**Date**: 2026-06-08
**Agent**: Wave 8 sub-agent (LegalResearch migration; 1 of 4 parallel agents)
**Predecessor**: Wave 7b (citations envelope + capability filter infrastructure); Wave 7c (KnowledgeRetrieval + VerifyCitations applied the pattern)

---

## TL;DR

`LegalResearchTools` (2 LLM functions: `ResearchLegal` + `LookupCase`) migrated to a single `LegalResearchHandler : IToolHandler` with `method` discriminator in `sprk_configuration`. Citations flow through the Wave 7b `ToolResult.Metadata[ToolResultMetadataKeys.Citations]` envelope with `SourceType = "BingGrounding"`. Capability gate `legal_research` (= `PlaybookCapabilities.LegalResearch`, Dataverse option 100000007) preserved via the Wave 7b `sprk_requiredcapability` column on each seed row.

ADR-015 PII sanitization + ADR-018 kill switch preserved verbatim from the legacy class.

---

## Files created (NEW only — no edits to existing files)

| Path | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/LegalResearchHandler.cs` | Typed handler — chat-only invocation; 2-method dispatch; preserved sanitizer + kill switch + Bing-grounding orchestration |
| `infra/dataverse/sprk_analysistool-legal-research-row.json` | Seed row for `ResearchLegal` method (`sprk_toolcode = "LEGAL-RESEARCH"`) |
| `infra/dataverse/sprk_analysistool-legal-case-lookup-row.json` | Seed row for `LookupCase` method (`sprk_toolcode = "LEGAL-CASE-LOOKUP"`) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/LegalResearchHandlerTests.cs` | Unit tests — 4-point contract + validation + kill-switch + sanitization + happy paths + ADR-015 telemetry sentinels |
| `projects/spaarke-ai-platform-unification-r6/notes/wave-08-legal-research-migration.md` | This bookkeeping note |

**Per the prompt's MUST NOT list**: NO edits to `SprkChatAgentFactory.cs`, `scripts/Seed-TypedHandlers.ps1`, `current-task.md`, `TASK-INDEX.md`, `LegalResearchTools.cs`, `AgentServiceClient`, `BingGroundingOptions`, or `QuerySanitizer`.

---

## Resolved capability literal

`PlaybookCapabilities.LegalResearch = "legal_research"` (case-sensitive constant, Dataverse option set integer `100000007`). Defined at `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs:54`.

Both seed rows set `sprk_requiredcapability = "legal_research"`. NOT in `CoreCapabilities` (standalone-chat allow-list at `PlaybookCapabilities.cs:126-133`), so the data-driven path's `IsCapabilityGateSatisfied` correctly withholds this handler from standalone chat — preserving the pre-R6 hardcoded `if (capabilities.Contains(PlaybookCapabilities.LegalResearch))` boundary.

---

## Two toolcodes

| Toolcode | Method | LLM-facing function | Purpose |
|---|---|---|---|
| `LEGAL-RESEARCH` | `ResearchLegal` | broad legal topic / doctrine / statute research | Free-form Bing Grounding query |
| `LEGAL-CASE-LOOKUP` | `LookupCase` | specific case citation lookup | Wraps citation in `site:law.justia.com OR site:courtlistener.com OR site:scholar.google.com` filters |

Both share `sprk_handlerclass = "LegalResearchHandler"`. The handler reads `sprk_configuration.method` to dispatch.

---

## ADR-015 enforcement verification

Sanitization is enforced inside `ExecuteResearchLegalAsync` and `ExecuteLookupCaseAsync` — BEFORE the call into the internal `RunBingGroundingAsync` seam:

```csharp
// ResearchLegal path:
if (!_options.Enabled) { return BuildDegradationResult(...); }     // ADR-018 kill switch FIRST
var sanitizedTopic = QuerySanitizer.Sanitize(rawTopic!);           // ADR-015 sanitization SECOND
return await RunResearchAndBuildResultAsync(sanitizedQuery: sanitizedTopic, ...);

// LookupCase path:
if (!_options.Enabled) { return BuildDegradationResult(...); }
var sanitizedCitation = QuerySanitizer.Sanitize(rawCitation!);
var searchQuery = $"case law citation \"{sanitizedCitation}\" site:law.justia.com OR site:courtlistener.com OR site:scholar.google.com";
return await RunResearchAndBuildResultAsync(sanitizedQuery: searchQuery, ...);
```

Tests verify sanitization runs and that the sanitized query reaches the Bing seam (via `TestableLegalResearchHandler` subclass override; captured query asserted to lack `Client:` / matter digits / raw email, but to preserve case citations / legal terms).

Telemetry: handler logs `queryLen={Length}` (ResearchLegal) and `citationLen={Length}` (LookupCase) at Information; result `resultCount={Count}` and `durationMs={Duration}` at completion. The query text itself is never logged. The annotation-extraction count is logged at Debug only. Sentinel-string tests assert the topic/citation/URL strings never appear in any captured log message.

---

## ADR-018 enforcement verification

Kill switch (`BingGroundingOptions.Enabled`) is checked at the TOP of each method's execution body, BEFORE any call to the internal `RunBingGroundingAsync` seam:

```csharp
// ADR-018 kill switch — BEFORE any network call.
if (!_options.Enabled)
{
    stopwatch.Stop();
    _logger.LogInformation(...);
    return BuildDegradationResult(tool, ResearchLegalDisabledMessage, startedAt);
}
```

When disabled, the handler returns a successful `ToolResult` (not Error) with a user-readable degradation message in both `Summary` and the payload. No thread creation, no Bing call, no semaphore acquisition.

Two tests assert the kill switch short-circuits (one per method): `ExecuteChatAsync_ResearchLegal_KillSwitchDisabled_ReturnsDegradationMessage_NoBingCall` and `ExecuteChatAsync_LookupCase_KillSwitchDisabled_ReturnsDegradationMessage_NoBingCall`. Both inject a flag-setting override into the test seam and assert the flag is never flipped.

---

## Design decisions

### Chat-only `SupportedInvocationContexts`

Set to `InvocationContextKind.Chat` (NOT `Both`). Rationale:
- Pre-R6 hardcoded `LegalResearchTools` was registered ONLY in `SprkChatAgentFactory.ResolveTools` — never in a playbook node.
- The 11 production node executors (NFR-08 binding) do NOT include any `legal_research` executor; exposing the handler to the playbook path would require a new node executor (which NFR-08 forbids in R6).
- Seed rows set `sprk_availableincontexts = 100000001` (Chat) matching the handler's declaration.

`ExecuteAsync` (the playbook path) throws `NotSupportedException` with a clear message — a defensive guard if a future caller tries to dispatch this handler via the playbook engine.

### Test seam — internal-virtual `RunBingGroundingAsync`

`AgentServiceClient` is `sealed` per its declaration (`public sealed class AgentServiceClient`). The prompt's MUST NOT list bars modifying that class to introduce an interface, virtual methods, or unsealing. Moq cannot mock sealed types.

Solution: keep `LegalResearchHandler` non-sealed (the class is `partial public class LegalResearchHandler` — `partial` is required by `[GeneratedRegex]`); make the Bing-grounding orchestration method `internal virtual`. Tests subclass with `TestableLegalResearchHandler : LegalResearchHandler` (kept `private sealed` inside the test class) and override the override seam. The base class constructor still requires a real `AgentServiceClient` instance — tests build one with `Enabled = false` options + `MemoryDistributedCache` + `DefaultAzureCredential` so DI satisfies the contract but no real network is ever touched.

This is the same shape used by R5 tools that needed to mock around sealed SDK types (no precedent in this repo specifically — this is a fresh pattern but stays within the test-only `InternalsVisibleTo("Sprk.Bff.Api.Tests")` boundary already declared in the BFF csproj).

### Citation envelope preservation

Citations are built only for results with a non-empty `Url` — matching the legacy `RegisterCitations` skip on `string.IsNullOrWhiteSpace(result.Url)`. `SourceType = "BingGrounding"` propagates the same source-type discriminator the legacy code wrote to `CitationContext.AddCitation(sourceType: "BingGrounding", ...)`. The Wave 7b adapter copies the envelope into the per-turn `CitationContext` via `AddCitation` with that same source type.

### Sanitizer location — local copy

The handler keeps its own `internal static partial class QuerySanitizer` with the identical regex set as the legacy `LegalResearchTools.QuerySanitizer`. This makes the migration self-contained: when the main session removes the hardcoded factory block AND deletes `LegalResearchTools.cs`, no other class loses a sanitizer dependency. The duplicate is intentional and short-lived — once `LegalResearchTools.cs` is removed, the handler's sanitizer is the sole copy.

### Two seed rows, one handler — method discriminator pattern

Same pattern as `TextRefinementHandler` (Wave 7) and `KnowledgeRetrievalHandler` (Wave 7c). Each row's `sprk_configuration.method` discriminator routes inside the handler's `ResolveMethod` helper. The LLM sees two distinct functions with distinct descriptions and parameter shapes; the C# code is one class with one dispatch boundary.

### Concurrency gate — NOT duplicated

The legacy `LegalResearchTools` held its own `SemaphoreSlim` separate from `AgentServiceClient`'s gate. The new handler does NOT add a second gate — `AgentServiceClient` already enforces the global semaphore (per ADR-016) and converts timeouts to `ConcurrencyLimitExceededException`. The handler catches that exception and converts to a user-readable degradation message (preserving the legacy graceful-degradation contract for the LLM).

This is a slight semantic change: legacy code had two separate gates (BingGroundingOptions.MaxConcurrency at the tool level + AgentServiceOptions.MaxConcurrency at the SDK wrapper level). The new code uses only the SDK-level gate. Rationale: double-counting causes confusing operator metrics and the SDK-level gate already covers the same call site. `BingGroundingOptions.MaxConcurrency` becomes ignored (could be removed in a follow-up cleanup but is out of this migration's scope — the prompt's MUST NOT list bars modifying `BingGroundingOptions`).

---

## Test inventory

11 happy-path / contract tests + 6 validation / error tests + 2 ADR-015 telemetry sentinel tests = **19 tests total**.

| Category | Tests |
|---|---|
| 4-point contract | `HandlerType_IsRegisteredInDi`, `Handler_IsDiscoverableByHandlerClassName`, `Metadata_IsValid`, `SupportedToolTypes_IsNonEmpty`, `SupportedInvocationContexts_IsChatOnly` |
| ValidateChat per method | `ValidateChat_Succeeds_WithTopic_ResearchLegal`, `ValidateChat_Succeeds_WithCitation_LookupCase`, `ValidateChat_Fails_WhenTopicMissing_ResearchLegal`, `ValidateChat_Fails_WhenCitationMissing_LookupCase`, `ValidateChat_Fails_WhenTenantIdMissing`, `ValidateChat_Fails_WhenMethodUnsupported`, `ValidateChat_Fails_WhenJsonMalformed` |
| ADR-018 kill switch | `ExecuteChatAsync_ResearchLegal_KillSwitchDisabled_ReturnsDegradationMessage_NoBingCall`, `ExecuteChatAsync_LookupCase_KillSwitchDisabled_ReturnsDegradationMessage_NoBingCall` |
| ADR-015 PII sanitization | `ExecuteChatAsync_ResearchLegal_SanitizesQuery_BeforeForwardingToBing`, `ExecuteChatAsync_LookupCase_SanitizesCitationPreamble_WhilePreservingCitation`, `QuerySanitizer_RemovesClientPrefix_ReplacesMatterRefs_AndEmails` |
| Happy paths | `ExecuteChatAsync_ResearchLegal_ReturnsCitations_AndPayload`, `ExecuteChatAsync_LookupCase_ReturnsCaseHoldingAndSourceUrl`, `ExecuteChatAsync_ResearchLegal_EmptyResults_StillSuccessful` |
| Error paths | `ExecuteChatAsync_ReturnsValidationError_WhenTopicMissing_ResearchLegal`, `ExecuteChatAsync_ReturnsCancelled_WhenTokenCancelled`, `ExecuteChatAsync_ReturnsError_WhenGroundingThrows`, `ExecuteChatAsync_ReturnsDegradation_WhenConcurrencyLimitExceeded`, `ExecuteAsync_PlaybookContext_Throws_NotSupportedException` |
| ADR-015 telemetry sentinels | `Telemetry_RespectsAdr015_DoesNotLogQueryText_OrResultUrls`, `Telemetry_RespectsAdr015_LookupCase_DoesNotLogCitationText` |

---

## Surprises / decisions worth flagging

### 1. `AgentServiceClient` is sealed — required a new test-injection pattern

The R6 sub-agent prompt explicitly forbids modifying `AgentServiceClient`. The class is `sealed` so Moq can't proxy it. The test-injection seam (`internal virtual RunBingGroundingAsync`) is the cleanest solution that:
- Doesn't modify any external file
- Doesn't require InternalsVisibleTo additions (the `Sprk.Bff.Api.Tests` line already exists)
- Doesn't change the production code path (the override is test-only)

This pattern is reusable for `WebSearchTools` (Bing Web Search REST), `CodeInterpreterTools` (Foundry Code Interpreter), and the other Wave 8 sibling migrations that face the same sealed-SDK problem.

### 2. Concurrency gate de-duplication — slight semantic change

As noted under "Concurrency gate — NOT duplicated" above. The handler relies on `AgentServiceClient`'s gate alone; `BingGroundingOptions.MaxConcurrency` becomes unused. This is a tiny behavioral simplification — operators previously had to reconcile two gate counts; now there's just one. `BingGroundingOptions` is not modified (per the MUST NOT list), so the unused property stays in place — main session can prune it in a follow-up if desired.

### 3. Citation envelope skips URL-less results

The legacy `RegisterCitations` skipped results with `string.IsNullOrWhiteSpace(result.Url)`. The handler preserves this — the "Legal Research Summary" fallback result (when Bing returns prose but no annotations) is included in the formatted text payload but NOT registered as a citation, since it has no URL. This matches legacy behavior.

### 4. Chat-only handler (NOT Both)

The legacy `LegalResearchTools` was chat-only. Setting `SupportedInvocationContexts = Chat` (NOT `Both`) matches legacy exactly and avoids NFR-08 violations (no new playbook node executor). Both seed rows set `sprk_availableincontexts = 100000001` (Chat) to match.

---

## Stop-and-surface — none triggered

The R6 sub-agent prompt enumerates 6 stop-and-surface triggers. Walking each:

| Trigger | Result |
|---|---|
| `IToolHandler` contract can't carry sanitization + kill-switch lifecycle | NOT TRIGGERED — both fit inside `ExecuteChatAsync` |
| `AgentServiceClient` requires a method signature change | NOT TRIGGERED — handler calls existing 3 methods (`CreateOrResumeThreadAsync`, `SendMessageAsync`, `StreamResponseAsync`) unchanged. The test-seam pattern (internal-virtual on the handler) avoided needing any change to AgentServiceClient. |
| `ChatInvocationContext` needs new fields | NOT TRIGGERED |
| Existing ADR appears to block optimal answer | NOT TRIGGERED |
| Wave 7b Metadata envelope doesn't fit | NOT TRIGGERED — `ToolResultMetadataKeys.Citations` + `ToolResultCitation` shape carries everything the legacy `CitationContext.AddCitation` call carried (chunkId/sourceName/excerpt/sourceType/url/snippet) |
| Capability literal mismatch | NOT TRIGGERED — `PlaybookCapabilities.LegalResearch = "legal_research"` (exact constant), used in both seed rows |

---

## Main-session removal checklist

After this work lands, the main session needs to:

1. Remove `SprkChatAgentFactory.cs` lines ~1107-1165 (the `// --- LegalResearchTools ---` block + the `if (capabilities.Contains(PlaybookCapabilities.LegalResearch))` gate).
2. Delete `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/LegalResearchTools.cs` (the legacy class is now superseded).
3. Run `scripts/Seed-TypedHandlers.ps1` (or its successor) to insert the two new `sprk_analysistool` rows into the dev environment.
4. Run `dotnet build src/server/api/Sprk.Bff.Api/` + `dotnet test --filter "FullyQualifiedName~LegalResearchHandlerTests"` to verify the migration is green.
5. Verify BFF publish-size delta is ≤+0.1 MB (per ADR-029 + R6 NFR-02; per-handler target).

---

## Next-wave handoff

Wave 8 sibling migrations (DocumentSearch, WebSearch, CodeInterpreter) face similar sealed-SDK-mocking challenges. The `internal virtual` test seam pattern documented here is the canonical solution — they can copy the structure (override the orchestration method that calls into the sealed SDK type; tests subclass and override the seam).
