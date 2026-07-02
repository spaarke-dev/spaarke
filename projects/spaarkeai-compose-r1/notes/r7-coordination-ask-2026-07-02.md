# Compose R1 → R7 Coordination Ask (2026-07-02)

> **From**: `spaarkeai-compose-r1` (Compose R1 drafting workspace, task 098 SSE wiring)
> **To**: `spaarke-ai-platform-unification-r7` team
> **Purpose**: Confirm the r7-owned AI surface points that Compose R1's `compose-summarize` action depends on, so we can either coexist with r7's redesign or migrate to r7's new pattern before further debugging our SSE stream failure.
> **Master state**: PR #534 (Compose R1 supplement) MERGED on master at commit `7111712f1` on 2026-07-01/02.
> **Ship blocker**: post-deploy smoke on Dev shows `compose-summarize` SSE fails with client-side "stream read failed — network error" after the toolbar identifier + tenant fixes shipped. Server-side compose-summarize POST reaches the endpoint (JWT logged); outcome not yet traced past the Load stage. Not yet known whether root cause is our code or an r7-side surface change already on master.

---

## TL;DR — the ask

Compose R1's `compose-summarize` consumer depends on **9 interface / contract points** that live in the r7-owned area of the BFF and Dataverse. For each, we need one of three answers:

1. ✅ **Stays as-is** — Compose R1 can ship + coexist with r7's redesign. Debug our failure on this branch.
2. ⚠️ **Changing shape** — tell us the new shape + timeline; we adapt at task 097/098 level.
3. ❌ **Removed / superseded** — tell us the replacement path; we migrate Compose to it.

**Below is one section per surface point. Each has**: (a) what we consume, (b) files, (c) our usage, (d) the yes/no/how question for r7.

---

## Surface 1 — `IInvokePlaybookAi` facade (widened by us via ADR-013 Path B amendment 2026-07-01)

**What we consume**:
```csharp
Task<PlaybookInvocationResult> InvokePlaybookAsync(
    Guid playbookId,
    IReadOnlyDictionary<string, string>? parameters,
    PlaybookInvocationContext context,
    CancellationToken cancellationToken = default,
    string? userContext = null,          // ← NEW in task 095/096
    DocumentContext? document = null);   // ← NEW in task 095/096
```

**Files**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInvokePlaybookAi.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/InvokePlaybookAi.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/NullInvokePlaybookAi.cs`

**Our usage** (single call site — `Api/ComposeEndpoints.cs` `DispatchAction`):
```csharp
var result = await invokePlaybook.InvokePlaybookAsync(
    playbookId.Value,        // 47686eb1-9916-f111-8343-7c1e520aa4df (Document Summary)
    parameters,              // built from compose-document JPS scope
    invocationContext,       // {TenantId, HttpContext, CorrelationId}
    ct,
    userContext: extractedText,       // plain text from DOCX (up to 100k chars)
    document: documentContext);       // {DocumentId, Name, FileName, ContentType, ExtractedText}
```

**ADR trail**:
- Refined ADR-013 (2026-05-20) — CRUD code MUST NOT inject AI-internal types; MUST use `Services/Ai/PublicContracts/` facades
- Amendment 2026-07-01 (Path B per CLAUDE.md §6.5) — `docs/adr/ADR-013-ai-architecture.md` §"Amendment 2026-07-01 — Document-context invocation on `IInvokePlaybookAi` facade"

**Compile-time boundary guard**:
- `tests/unit/Sprk.Bff.Api.Tests/Integration/PhaseAVerticalSliceTests.cs` — `ADR013_InvokePlaybookAiFacade_DoesNotExposeAiInternalTypesInSurface` — updated with named allow-list permitting `Sprk.Bff.Api.Services.Ai.DocumentContext` on the facade surface.

**Ask r7**:
- (a) Does `IInvokePlaybookAi` continue to exist under r7's redesign?
- (b) If yes, is the widened signature (with `userContext` + `document` at the end, defaulted to `null`) preserved?
- (c) If yes, does `InvokePlaybookAi.cs` still forward these to `PlaybookRunRequest.UserContext` + `.Document`?
- (d) If the facade is being renamed / replaced, what's the successor, and can Compose R1 keep using this facade for R1 shipping + migrate in R2?

---

## Surface 2 — `IConsumerRoutingService.ResolvePlaybookIdAsync(consumerType)`

**What we consume**:
```csharp
Guid? playbookId = await consumerRouter.ResolvePlaybookIdAsync("compose-summarize", ct);
if (playbookId is null) → SSE error chunk "unknown consumer type"
```

**Our usage** (in `DispatchAction`, immediately before invoking the facade).

**Data dependency**:
- Dataverse table `sprk_playbookconsumer`
- Seeded row for R1 (via `notes/dataverse-seed/compose-summarize-playbookconsumer.md`):
  - `sprk_consumertype = "compose-summarize"`
  - `sprk_consumercode = "default"` (or environment code)
  - `sprk_environment = "production"`
  - `sprk_playbookid = 47686eb1-9916-f111-8343-7c1e520aa4df` (Document Summary playbook)
  - `sprk_issystem = true`

**Ask r7**:
- (a) Is `IConsumerRoutingService.ResolvePlaybookIdAsync` still the resolver r7 wants CRUD-side dispatch code to call?
- (b) Is `sprk_playbookconsumer` still the source-of-truth table? (I noticed r7's `bff-extensions.md §G` rewrite discusses executor-type/typed-config as the new mechanism — is `sprk_playbookconsumer` staying or being consolidated?)
- (c) Should our compose-summarize seed row shape change (e.g., new required columns) in r7's world?
- (d) In r7's chat consumer, the equivalent lookup returned `44285d15-1360-f111-ab0b-70a8a59455f4` (per BFF logs 2026-07-02 14:23:39). If r7 has changed lookup semantics, we need to know whether our lookup path shares the same code path.

---

## Surface 3 — `IPlaybookOrchestrationService.ExecuteAsync` (downstream from the facade)

**We do NOT call this directly**. We only call `IInvokePlaybookAi` which delegates to `IPlaybookOrchestrationService` inside `InvokePlaybookAi.cs` (r7-owned).

**Our concern**: if r7 renames / restructures `IPlaybookOrchestrationService` or its `ExecuteAsync` signature, the delegation inside `InvokePlaybookAi.cs` (owned by r7 or shared) needs to still forward:
- `PlaybookRunRequest.UserContext` (from our `userContext` param)
- `PlaybookRunRequest.Document` (from our `document` param)

**Ask r7**:
- (a) Does `PlaybookRunRequest` still have `UserContext` (string) and `Document` (DocumentContext) fields?
- (b) If r7 has renamed these fields, will `InvokePlaybookAi.cs` be updated to map from our facade args to the new field names?
- (c) Are there any new REQUIRED fields on `PlaybookRunRequest` post-r7 that we're not populating (which would cause the executor to reject)?

---

## Surface 4 — `AnalysisStreamChunk` SSE envelope + factory methods

**What we consume**:
```csharp
public record AnalysisStreamChunk(string Type, string? Content, bool Done, ...);

// factory methods used by our task 097 SSE endpoint:
AnalysisStreamChunk.Progress(step, message)
AnalysisStreamChunk.Result(jsonContent)
AnalysisStreamChunk.Completed(runId, tokenUsage)
AnalysisStreamChunk.FromError(errorMsg)
```

**File**:
- `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` (record definition + factories)

**Wire format we emit** (task 097 `Api/ComposeEndpoints.cs` `DispatchAction`):
```
Content-Type: text/event-stream

data: {"type":"progress","content":"Loading document...","step":"document_loaded",...}\n\n
data: {"type":"progress","content":"Extracting document text...","step":"extracting_text",...}\n\n
data: {"type":"progress","content":"Invoking playbook...","step":"invoking_playbook",...}\n\n
data: {"type":"result","content":"{\"runId\":\"...\",\"textContent\":\"...\",...}",...}\n\n
data: {"type":"done","done":true,"analysisId":"...","tokenUsage":{...}}\n\n
data: [DONE]\n\n
```

**Client-side parser** (`@spaarke/compose-components/executeComposeSummarize.ts`):
- Demuxes on `chunk.type = progress | result | done | error`
- Result chunk's `content` field is a JSON string containing `{runId, textContent, structuredData, confidence, durationMs, citationCount, correlationId}`
- Terminal sentinel: literal `data: [DONE]\n\n`

**Ask r7**:
- (a) Is `AnalysisStreamChunk` the canonical envelope r7 wants for all AI SSE endpoints, or is r7 introducing a new streaming type (e.g., aligned with the recent SSE side-channel work per ADR-033)?
- (b) If new envelope: what's the schema (JSON shape) and does the terminal sentinel change?
- (c) If r7 adds new chunk types (`section_started` / `section_data` / `section_completed` per ADR-037), does our result-only consumer still work correctly (i.e., we can ignore unknown types)?

---

## Surface 5 — `DocumentContext` DTO

**What we consume**:
```csharp
public class DocumentContext {
    Guid DocumentId { get; init; }
    string Name { get; init; }
    string? FileName { get; init; }
    string? ContentType { get; init; }
    string? ExtractedText { get; init; }
}
```

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/DocumentContext.cs` (this is r7-adjacent; we don't own it)

**Our usage** (task 097 `DispatchAction`):
```csharp
var documentContext = new DocumentContext {
    DocumentId = body.DocumentRecordId ?? Guid.Empty,
    Name = body.DocumentName ?? loadResult.Value.FileName ?? body.DocumentSpeId,
    FileName = loadResult.Value.FileName ?? body.DocumentName,
    ContentType = body.DocumentMimeType,
    ExtractedText = extractedText,   // from our IDocxTextExtractor (task 094)
};
```

**Ask r7**:
- (a) Is `DocumentContext` staying in `Sprk.Bff.Api.Services.Ai` namespace with these fields?
- (b) Are any new REQUIRED fields being added (that we'd fail to populate)?
- (c) If moved to a different namespace, will the ADR-013 reflection guard's allow-list need to be updated? (We already permit this type via a named allow-list per task 095.)

---

## Surface 6 — `PlaybookInvocationContext` + `PlaybookInvocationResult`

**What we consume** (passed to / returned by facade):
```csharp
// input:
public class PlaybookInvocationContext {
    string TenantId { get; init; }
    HttpContext HttpContext { get; init; }
    string CorrelationId { get; init; }
    // ...
}

// output:
public class PlaybookInvocationResult {
    bool Success;
    string RunId;
    string TextContent;
    object StructuredData;
    double Confidence;
    TimeSpan Duration;
    List<Citation> Citations;
    string ErrorCode;
    string ErrorMessage;
    // ...
}
```

**Our usage**: We serialize the result fields into the SSE result chunk's JSON payload — `{runId, textContent, structuredData, confidence, durationMs, citationCount, correlationId}` — the client's `ComposeSummarizeResult` type expects these field names.

**Ask r7**:
- (a) Are `PlaybookInvocationContext` + `PlaybookInvocationResult` staying in `Services/Ai/PublicContracts/` or moving?
- (b) Are any r7 changes to `TextContent` (Compose displays it verbatim as the assistant chat message)?
- (c) Is `Success` still a boolean, or is r7 moving to a discriminated-union / Result-monad shape?

---

## Surface 7 — Document Summary playbook (Dataverse row)

**Playbook ID**: `47686eb1-9916-f111-8343-7c1e520aa4df`
**Display name**: "Summarize Document for Chat" (per BFF logs 2026-07-02: `Loaded action from Dataverse: Summarize Document for Chat`)
**Executor observed**: `AiCompletion` node with schema `AiCompletion_Summarize_Document_for_Chat`
**Structured output schema** (per BFF logs 2026-07-02 14:23:40):
- `tldr[]` (1-3 bullets, ≤140 chars each — TL;DR streaming)
- `summary` (≤2000 chars narrative)
- `keywords` (comma-separated string)
- `entities.organizations[]` + `entities.persons[]`

**Compose R1 usage**: We call the SAME playbook the chat-summarize path uses. Consumer routing maps our `compose-summarize` consumer type → this same playbook ID.

**Ask r7**:
- (a) Is playbook `47686eb1-9916-f111-8343-7c1e520aa4df` staying in Dev/Prod?
- (b) Is r7 planning to change the response schema (e.g., add fields to `AiCompletion_Summarize_Document_for_Chat` that would alter what Compose R1 receives in `PlaybookInvocationResult.TextContent`)?
- (c) Compose R1 uses only `result.TextContent` for the assistant-facing chat message rendering (not the structured `tldr/summary/keywords/entities` breakdown). Is `TextContent` guaranteed to be populated with a human-readable summary regardless of the schema evolution?

---

## Surface 8 — DOCX Load path (`IComposeDocumentService.LoadDocxAsync`)

**What we consume**:
```csharp
Task<ComposeLoadResult> LoadDocxAsync(
    string documentSpeId,
    string driveId,
    string tenantId,
    HttpContext httpContext,
    CancellationToken ct);
```

**Behavior**: OBO Graph download of DOCX bytes from SPE drive-item.

**File**: `src/server/api/Sprk.Bff.Api/Services/Compose/IComposeDocumentService.cs` + `ComposeDocumentService.cs`

**We own this**. But it delegates to r7-adjacent Graph plumbing (`Sprk.Bff.Api.Infrastructure.Graph.DriveItemOperations.DownloadFileAsync`).

**Ask r7**:
- (a) Any changes to `Infrastructure/Graph/DriveItemOperations.cs` semantics (e.g., different OBO scope requirement)?
- (b) Are Graph 403 "Access denied" errors currently expected on OBO for `.docx` files in the SPE containers (post-r7 changes)?
- (c) BFF logs 2026-07-02 14:24:45 show a Graph 403 "Access denied" for `Sprk.Bff.Api.Services.Ai.FileIndexingService` — is this related to r7's auth changes, or a separate app-only permission gap?

---

## Surface 9 — `IDocxTextExtractor` (Compose R1 owns this, but relevant to r7)

**What we own** (task 094):
- `IDocxTextExtractor.ExtractPlainTextAsync(stream, maxCharacters=100_000, ct)` returns plain text
- Uses `DocumentFormat.OpenXml` SDK
- Walks `Body.Descendants<Paragraph>`

**Files**:
- `src/server/api/Sprk.Bff.Api/Services/Compose/IDocxTextExtractor.cs`
- `src/server/api/Sprk.Bff.Api/Services/Compose/DocxTextExtractor.cs`
- DI: `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ComposeModule.cs`

**Relevant to r7**: this is where DOCX bytes become the `userContext` string that we pass to the widened facade. If r7 wants a canonical text-extraction service (e.g., existing `TextExtractorService.cs`) instead of ours, we can retarget.

**Ask r7**:
- (a) Is there a canonical text-extractor r7 wants Compose to use instead of our new `IDocxTextExtractor`? (Our search showed `Services/Ai/TextExtractorService.cs` exists.)
- (b) If yes, does it accept a `Stream` and produce `string` synchronously (for the SSE pipeline)?
- (c) If we should migrate: preferred timing (R1 or R2)?

---

## What Compose R1 does NOT touch — for r7's peace of mind

- ❌ `IPlaybookExecutionEngine`, `IPlaybookService`, `IOpenAiClient`, `ScopeResolverService` — never injected into CRUD-side compose code (ADR-013 refined 2026-05-20 hard rule; enforced by compile-time reflection test)
- ❌ `PlaybookOrchestrationService` internals (we delegate through the facade only)
- ❌ Node types, executor types, `sprk_analysisaction`, `sprk_playbookexecution`, `sprk_playbookmetric`
- ❌ Chat session summarize path (`/api/ai/chat/sessions/{id}/summarize`) — that's r7's territory; we did not touch it
- ❌ `sprk_analysisactiontype` table (r7's decorative lookup per §G rewrite)
- ❌ `PlaybookDispatcher` (r7's Stage 1/Stage 2 chat routing) — Compose R1 hits `IConsumerRoutingService` directly for consumer-type → playbookId lookup

---

## Compose R1 files that r7 should be aware of

**Frontend (all new / heavily-modified in this branch)**:
- `src/client/shared/Spaarke.Compose.Components/src/orchestrators/executeComposeSummarize.ts` (SSE consumer — NEW)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeToolbar.tsx` (dispatches `compose_summarize_request` event)
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx` (owns document state; toolbar bindings)
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (subscribes to event → invokes orchestrator → progressive render)

**BFF (new / modified in this branch)**:
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` — task 097 SSE conversion (previously JSON `Task<IResult>`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInvokePlaybookAi.cs` — widened signature (task 095)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/InvokePlaybookAi.cs` — forwards `userContext`+`document`
- `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/NullInvokePlaybookAi.cs` — kill-switch signature updated
- `src/server/api/Sprk.Bff.Api/Services/Compose/{IDocxTextExtractor,DocxTextExtractor}.cs` — NEW (task 094)
- `src/server/api/Sprk.Bff.Api/Services/Compose/{IComposeDocumentService,ComposeDocumentService}.cs` — pre-existing R1
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs` + `ComposeSessionService.cs` — pre-existing R1
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ComposeModule.cs` — DI registrations

**Tests (may signal a contract-change need for r7)**:
- `tests/unit/Sprk.Bff.Api.Tests/Integration/PhaseAVerticalSliceTests.cs` — updated allow-list for `DocumentContext` on facade surface
- `tests/unit/Sprk.Bff.Api.Tests/Api/ComposeEndpointsTests.cs` — endpoint shape tests including new params (`IComposeDocumentService`, `IDocxTextExtractor`)
- `tests/integration/contract/Api/Compose/ComposeEndpointsContractTests.cs` — 3 tests Skip'd pending SSE-parser (task 097 FU-97a)
- `tests/integration/regression/Compose/ComposeSummarizeRoundtripSmokeTests.cs` — 7 tests Skip'd pending SSE-parser (task 097 FU-97a)

**ADR trail**:
- `docs/adr/ADR-013-ai-architecture.md` §"Amendment 2026-07-01 — Document-context invocation on `IInvokePlaybookAi` facade" (full ADR)
- `.claude/adr/ADR-013-ai-architecture.md` (concise version + MUST rules)
- `.claude/adr/INDEX.md` (row updated: status "Accepted (amended 2026-07-01)")
- `.claude/CHANGELOG.md` (`[Unreleased]` → 2026-07-01 spaarkeai-compose-r1 task 102 entry)

**Project artifact**:
- `projects/spaarkeai-compose-r1/spec-supplement-2026-07-01-three-pane-pivot.md` — supplement scope
- `projects/spaarkeai-compose-r1/tasks/{094,095,096,097,098,099,100,102}-*.poml` — task POMLs with full context blocks
- `projects/spaarkeai-compose-r1/notes/defer-issues.md` §AMD-102 (Path B amendment record)

---

## Current smoke failure snapshot (for reference during coordination)

**Pass**:
- ✅ Ribbon launch opens SpaarkeAi in `composeMode=editor` (task 092)
- ✅ Three-pane shell renders; no workspace tab strip (task 100 `hideTabBar` prop working)
- ✅ Document loads into TipTap editor via `GET /api/compose/documents/{speId}` (auth OBO to SPE Graph works for read)
- ✅ Manual edits save via `POST /api/compose/documents/{speId}/save` (OBO to SPE Graph works for write; DOCX conversion + storage confirmed)
- ✅ Open-in-Word Web + Open-in-Word Desktop toolbar buttons work (`/api/documents/{sprkDocumentId}/open-links`)
- ✅ Assistant pane subscribes to `compose_summarize_request` (task 098 wiring)
- ✅ POST to `/api/compose/action/compose-summarize` reaches the endpoint with valid JWT (per BFF logs, e.g. 2026-07-02T14:22:18)

**Fail**:
- ❌ Client sees "stream read failed — network error" mid-SSE-stream
- ❌ No client-observable SSE frames received (or the read errored before parsing them)
- ❌ Server-side outcome past the "Token on POST" log line not yet visible in the log snapshot we have

**Not yet ruled out**:
- Server-side `LoadDocxAsync` may be throwing (OBO Graph download in the dispatch context — potentially different scope/perms than the initial Load endpoint)
- Server-side `IInvokePlaybookAi.InvokePlaybookAsync` may be throwing (possibly due to r7-side changes to `IPlaybookOrchestrationService` internals that break the facade delegation)
- Network middleware may be terminating the SSE stream before the server finishes writing chunks (proxy timeout, HTTP/2 goaway, connection idle)
- Client-side WhatWG ReadableStream / TextDecoder edge case (less likely — same pattern as the working R5 `executeSummarizeIntent`)

---

## Recommended coordination outcomes

Based on r7's answers, we'll pick one of:

1. **All 9 surfaces still stable** → we debug the SSE stream failure on our end (add server-side logging, decode SSE frames on client with better error messages, investigate Graph OBO context in DispatchAction).

2. **1-3 surfaces changed in scope-compatible ways** → we adapt task 097/098 to the new shapes, redeploy, re-smoke.

3. **Facade/orchestrator restructured** → we hold task 110 (wrap-up) until r7 lands their changes on master, then Compose R2 (or an in-branch re-integration) adopts the new API.

4. **Playbook execution moving to a fundamentally different pattern** → surface as new ADR conflict (CLAUDE.md §6.5); coordinated design session with both project leads.

---

## Turnaround ask

Ideally we'd have answers to Surfaces 1, 2, 3, 4, 6 within 24 hours to unblock Compose R1 shipping. Surfaces 5, 7, 8, 9 are lower priority and can slip to end-of-week if needed.

Please respond in whatever format works — inline in this file, ADR amendment reference, or a message linking to the r7 spec / design section that answers the question.

---

*This document lives at `projects/spaarkeai-compose-r1/notes/r7-coordination-ask-2026-07-02.md` and travels with the Compose R1 project. Feel free to inline r7 responses below each surface's ask.*

---

## 📩 R7 team response — 2026-07-02

**Received via Slack / DM / verbal — captured verbatim for audit trail below.**

> Thanks for the structured ask. R7 Wave 12 tonight (2026-07-02) shipped the Linear AI Consumer library and migrated Document Upload / Profile off the Playbook Engine (`c2d26986d`). Below is the per-surface answer.

| # | Surface | R7 W12 answer |
|---|---|---|
| **1** | `IInvokePlaybookAi` facade + ADR-013 Path B amendment | **Stays as-is** for compose-summarize. R7 W12 didn't touch the facade; Linear path bypasses it entirely for the 6 in-scope linear consumers (Doc Profile / File Summarize / Prefills). Compose is not on that list — you keep going through the facade → Playbook Engine → Document Summary playbook. Your ADR-013 Path B widening stands. |
| **2** | `IConsumerRoutingService.ResolvePlaybookIdAsync` + `sprk_playbookconsumer` table | **Stays as-is**. Untouched by W12. Linear consumers use a NEW parallel mechanism (`LinearConsumersOptions` config-driven ActionId map, `LinearConsumers__ActionIds__*` App Service keys) — no overlap with the routing table. Routing service continues to serve Playbook Engine consumers. |
| **3** | `IPlaybookOrchestrationService.ExecuteAsync` | **Stays as-is**. Playbook Engine preserved for Chat, Insight Engine, Compose, Daily Briefing. Not on the retirement list. |
| **4** | `AnalysisStreamChunk` SSE envelope + factory methods | **Stays as-is**. R7 W12 REUSES the same envelope in DocumentProfileService — I explicitly designed the linear consumers to emit the identical SSE shape (Metadata / Progress / TextChunk / Completed / FromError / Result). Client contract preserved. |
| **5** | `DocumentContext` DTO in `Sprk.Bff.Api.Services.Ai` | **Stays as-is**. Not modified. R7 W12 added a NEW leaner record `DocumentText` in the new `Services/Ai/LinearConsumers/` namespace, scoped to the linear path only — it doesn't replace `DocumentContext`. |
| **6** | `PlaybookInvocationContext` + `PlaybookInvocationResult` shapes | **Stays as-is**. Untouched. |
| **7** | Playbook `47686eb1-9916-f111-8343-7c1e520aa4df` (Document Summary, shared with chat-summarize) | **Stays as-is**. NOT on the wave12 retirement list. The only playbooks retiring (Phase E) are the six linear ones: `18cf3cc8` (Doc Profile), `4a72f99c` (Summarize File), `2d660cad` (Matter PreFill), `fc343e9c` (Project PreFill), WA (if separate), Doc Create Profile. Yours is untouched. |
| **8** | `Infrastructure/Graph/DriveItemOperations.DownloadFileAsync` (OBO Graph download) | **Stays as-is**. Not touched by W12. R7's linear text extraction goes through `AnalysisDocumentLoader.ExtractDocumentTextAsync` → `ISpeFileOperations.DownloadFileAsUserAsync` (SpeFileStore facade per ADR-007), not `DriveItemOperations`. If they're separate seams, W12 didn't move yours. |
| **9** | Canonical text-extractor (your `IDocxTextExtractor`) | **Optional migration**. R7's canonical is `ITextExtractor` — handles DOCX + PDF + many more via a single facade with Redis ETag cache (ADR-009). If you'd like to consolidate, migrate at your convenience — not urgent, R7 doesn't require it. |

**On the SSE mid-stream network error**: R7 W12 intentionally left compose-summarize's code path unchanged. Two theories worth ruling out on your end:

> **Theory 1**: Post-`d75de048b` (endpoint pre-loads DocumentContext before invoking the orchestrator) — the `POST /api/ai/analysis/execute` endpoint now runs text extraction as its FIRST step. On a large doc that pre-load can push time-to-first-byte past ~30s. If compose-summarize hits `AnalysisEndpoints.ExecuteAnalysis`, watch for a slow client (nginx / App Service) idle timeout upstream of your streaming. If instead you call `IInvokePlaybookAi` directly from your own endpoint, this doesn't apply.
>
> **Theory 2**: The Wave 12 endpoint added a Linear-dispatch shim BEFORE the pre-load. It checks `LinearConsumersOptions.PlaybookIds` for a match and falls through to engine when no match. Fall-through is safe by design, but if you want confirmation from a live log, share your correlation id and I'll grep App Insights.

**Compose-summarize as a future Linear candidate**: compose-summarize is technically a linear workflow (Start → summarize → return response). If you hit the same interpreter tax we did on Doc Upload, R7's Linear library is available as a follow-on migration. Not planned in R7 scope; would be its own coordination.

*Reply here or ping me for App Insights correlation-id log dive.*
*— R7 team*

---

## 📤 Compose R1 acceptance reply — 2026-07-02

> Thanks r7 — this is exactly what we needed. All 9 surfaces green means we can debug our SSE failure on our end without holding up Compose R1 ship.

**Per-surface confirmations back**:

- **Surface 1** — We keep the widened facade + ADR-013 Path B amendment 2026-07-01. Understood that Linear consumers bypass the facade entirely and Compose is intentionally not on that list. No changes to our reflection guard allow-list needed.
- **Surfaces 2, 3, 5, 6** — Preserved. We keep our code as-is.
- **Surface 4** — Preserved AND reused by W12 DocumentProfileService — great for consistency. Our client-side parser (`executeComposeSummarize.ts`) is agnostic to chunk types other than `progress/result/done/error`, so if you add new discriminants (`section_started` etc. per ADR-037) our consumer will safely ignore them.
- **Surface 7** — Preserved. Canonical shared path for Compose + chat-summarize; comfortable with that shared dependency.
- **Surface 8** — Preserved. Good to know W12's linear text extraction uses a separate seam (AnalysisDocumentLoader → SpeFileStore facade per ADR-007).
- **Surface 9** — **Filed as follow-up for Compose R2.** Compose R1 keeps our own extractor for the R1 ship since it's in a small green surface boundary. We'll evaluate migration to `ITextExtractor` for R2 — the Redis ETag cache is compelling for repeated summarize invocations on the same doc. If R7 wants to nudge earlier consolidation, we're open.

**On the SSE mid-stream failure**:
- **Theory 1 confirmed OUT** — Our endpoint (`POST /api/compose/action/{consumerType}`) is a bespoke SSE endpoint in `ComposeEndpoints.cs`, NOT `AnalysisEndpoints.ExecuteAnalysis`. We do our own SSE framing with `WriteSSEAsync` (which flushes `response.Body` after every chunk) and call `IInvokePlaybookAi.InvokePlaybookAsync` directly. Confirmed by grep on our `ComposeEndpoints.cs`.
- **Theory 2 sanity-check pending** — if `InvokePlaybookAi.cs` (the concrete facade impl) delegates through `IPlaybookOrchestrationService` or `PlaybookRunRequest`, does that delegation path itself pass through any Wave 12 shim? Or is the Wave 12 shim ONLY on the AnalysisEndpoints controller layer, keeping the shared engine code untouched?
- **Next action on our side**: adding server-side structured logging + client-side raw-frame dump to get a debuggable trace on next smoke attempt. Will share correlation-id after next smoke for App Insights dive.
- **Failing request from earlier smoke**: approximately 2026-07-02 14:22:18 UTC (BFF log line `Token on POST /api/compose/action/compose-summarize: aud=api://1e40baad-... appid=170c98e1-... scp=user_impersonation`).

**Linear candidate observation acknowledged** — compose-summarize is architecturally a linear workflow. We'll consider it for Compose R2 planning as a future coordination. The Linear library sounds like the right architectural fit for R2's follow-on actions (Rewrite / Find Similar / Lookup References — all narrow deterministic AI actions on top of Compose).

**Compose R1 status**: unblocked from r7 coordination side; debug continues on our end with new instrumentation.

*— spaarkeai-compose-r1 team, 2026-07-02*

---

## Resolution status

| Item | Status | Follow-up |
|---|---|---|
| All 9 surface points | ✅ Confirmed stable for Compose R1 shipping | None — audit trail complete |
| ADR-013 Path B amendment (2026-07-01) | ✅ Preserved by R7 | None |
| SSE mid-stream network error debug | 🔄 Compose R1 owns; instrumentation in-flight | New Compose R1 commit — server + client trace instrumentation |
| Compose-summarize → Linear library migration | 📋 Deferred to Compose R2 | Track in Compose R2 spec (`spaarkeai-compose-r2` when drafted) |
| `IDocxTextExtractor` → `ITextExtractor` migration | 📋 Deferred to Compose R2 | Same — R2 evaluation with Redis ETag cache benefit |
