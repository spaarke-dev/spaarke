# Decision: /narrate Dispatch Path for DAILY-BRIEFING-NARRATE Playbook

> **Decision Owner**: spaarke-daily-update-service-r4 task 001 (research) → task 030 (final) → task 031 (impl)
> **Authored**: 2026-06-25
> **Spec References**: FR-12 (line 159–164), Owner Q&A on dispatch mechanism (spec.md line 340), AC-12c (line 164), Unresolved Question (line 369)
> **Status**: Recommendation — finalized by task 030 in PR 4

---

## Decision

**Path A.5 (recommended) — Hybrid**: Use `IConsumerRoutingService.ResolveAsync` (the `sprk_playbookconsumer`-backed routing facade from chat-routing-redesign-r1 Phase 1R) to look up the `DAILY-BRIEFING-NARRATE` playbook GUID at runtime, but invoke playbook execution through a NEW **non-document** entry point on `AnalysisOrchestrationService` (or a thin sibling service) that accepts the daily-briefing structured payload directly rather than `DocumentIds[]`.

This preserves the platform's canonical routing pattern (one source of truth for playbook ↔ consumer mapping; admins can re-point a consumer without redeploying BFF) while accommodating the daily-briefing payload shape which is fundamentally different from the document-summarization shape that `ExecutePlaybookAsync` was built for.

**Reject**: pure Path A using the existing `ExecutePlaybookAsync` (incompatible payload shape). **Reject as default**: Path B (direct degenerate-playbook invoke without ConsumerRoutingService) — it sacrifices the chat-routing-redesign-r1 routing precedent for no benefit. Path B remains the documented fallback if Path A.5 implementation runs into an unanticipated blocker during task 031.

---

## Evidence

### A. `sprk_playbookconsumer` infrastructure (chat-routing-redesign-r1 Phase 1R) — confirmed canonical

- **`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IConsumerRoutingService.cs:39`** — `IConsumerRoutingService` defined. Returns `Task<Guid?>` for `ResolveAsync(consumerType, consumerCode, context, environment, ct)`. Documented as "the single point of access for all BFF consumers" replacing per-consumer env-var pattern.
- **`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IConsumerRoutingService.cs:18`** — XML doc explicitly labels this as the ADR-013 facade boundary (lives in `Services/Ai/PublicContracts/`), which is exactly the §10 BFF Hygiene + ADR-013 + bff-extensions.md §A.3 pattern that R4 must follow.
- **`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs:40`** — 6 consumer-type constants already defined: `MatterPreFill`, `ProjectPreFill`, `AiSummary`, `SummarizeFile`, `ChatSummarize`, `EmailAnalysis`. R4 will add a 7th: `DailyBriefingNarrate = "daily-briefing-narrate"`.
- **`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs:83`** — `ConsumerTypes.All` read-only list is intended for startup health-log diffing against Dataverse (task 028e from chat-routing-redesign-r1 — the same defense that catches `matter-pre-fil` typos catches `daily-briefing-narrat`).

**Adoption pattern is well-established** — 6 existing consumers across `MatterPreFillService`, `ProjectPreFillService`, `WorkspaceAiService`, `WorkspaceFileEndpoints`, `SessionSummarizeOrchestrator`, `AppOnlyAnalysisService` (per the Phase 1 explore-agent survey). Adding a 7th consumer for daily-briefing is purely additive.

### B. Existing playbook execution entry point — document-centric, requires shaping

- **`src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs:702`** — `ExecutePlaybookAsync(PlaybookExecuteRequest request, HttpContext httpContext, CancellationToken ct)` returns `IAsyncEnumerable<AnalysisStreamChunk>` (streaming).
- **`AnalysisOrchestrationService.cs:707`** — `var documentId = request.DocumentIds[0];` — this method **requires at least one document ID** and immediately loads it. This is the structural incompatibility with daily-briefing.
- **`AnalysisOrchestrationService.cs:714`** — Loads playbook by `request.PlaybookId` via `_playbookService.GetPlaybookAsync` — this part is reusable.
- **`AnalysisOrchestrationService.cs:723`** — `_documentLoader.GetDocumentAsync(documentId, ...)` — requires Dataverse document load. Daily-briefing has no document — payload is structured notifications.
- **Streaming response shape** — `IAsyncEnumerable<AnalysisStreamChunk>` returns chunks as they complete. The existing `/narrate` endpoint returns a single `DailyBriefingNarrateResponse` JSON (`DailyBriefingEndpoints.cs:267`), not a stream. Wrapping streaming → non-streaming is doable but requires aggregation.

### C. Current `/narrate` payload shape (must be preserved for backward compat — AC-12b)

- **`src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs:189–271`** — `HandleNarrate` signature: `(DailyBriefingNarrateRequest request, ILoggerFactory, HttpContext, CancellationToken, IBriefingAi?)`.
- **`DailyBriefingEndpoints.cs:214`** — Empty-payload tolerance: returns 200 + empty response when `Categories`, `PriorityItems`, `Channels` are all empty. The new playbook dispatch MUST preserve this branch.
- **`DailyBriefingEndpoints.cs:267`** — Response: `DailyBriefingNarrateResponse { Tldr, ChannelNarratives, GeneratedAtUtc }`. The existing widget parser at `useBriefingNarration.ts` consumes this exact shape — change is forbidden.
- **`DailyBriefingEndpoints.cs:199`** — Service-availability fail-fast (503 if `briefingAi is null`). New dispatch must preserve this guard or substitute an analogous check on playbook resolution.

### D. Payload contract for `DAILY-BRIEFING-NARRATE` playbook

The playbook (PR 2 task 010 authors) expects this structured input from the dispatching code:

```typescript
{
  categories:   Array<{ category: string, count: number, items: NotificationItem[] }>,
  priorityItems: NotificationItem[],
  channels:     Array<{ channel: string, items: NotificationItem[] }>,
  totalNotificationCount: number,
  // R4 enrichment fields (W1 PR 3): viaMatter, regardingName, source per FR-6
}
```

This is the same shape as `DailyBriefingNarrateRequest`. The playbook's node graph (per task 010): `Start → LoadKnowledge(channelRegistry) → [GenerateTldr ‖ GenerateChannelNarratives] → ValidateEntityNames → ReturnResponse` — Start accepts the payload as JSON scope variable; nodes read named fields.

---

## Proposed Implementation Sketch for Task 031 (Wrapper Rewrite)

The new `HandleNarrate` body becomes (pseudocode):

```csharp
private static async Task<IResult> HandleNarrate(
    DailyBriefingNarrateRequest request,
    ILoggerFactory loggerFactory,
    IConsumerRoutingService routing,                     // ← NEW: routing facade
    IAnalysisOrchestrationService orchestration,          // ← NEW: non-document overload
    HttpContext httpContext,
    CancellationToken cancellationToken)
{
    var logger = loggerFactory.CreateLogger("DailyBriefingEndpoints");

    // Empty-payload tolerance preserved (existing branch)
    if (request.Categories.Length == 0 && request.PriorityItems.Length == 0 && request.Channels.Length == 0)
        return TypedResults.Ok(EmptyNarrateResponse);

    // 1. Resolve playbook via ConsumerRoutingService (Path A.5 binding)
    var playbookId = await routing.ResolveAsync(
        ConsumerTypes.DailyBriefingNarrate,
        consumerCode: "default",
        context: null,
        environment: null,
        cancellationToken);

    if (playbookId is null)
    {
        logger.LogWarning("No sprk_playbookconsumer row matched for {ConsumerType} — service unavailable", ConsumerTypes.DailyBriefingNarrate);
        return Results.Problem(statusCode: 503, title: "Service Unavailable", detail: "Daily briefing dispatch unconfigured.");
    }

    // 2. Invoke playbook via NEW non-document entry point
    //    NEW method on IAnalysisOrchestrationService (or sibling):
    //    Task<TResult> ExecutePayloadPlaybookAsync<TResult>(Guid playbookId, object payload, HttpContext, CT)
    var response = await orchestration.ExecutePayloadPlaybookAsync<DailyBriefingNarrateResponse>(
        playbookId.Value,
        payload: request,
        httpContext,
        cancellationToken);

    return TypedResults.Ok(response);
}
```

### What task 031 must build

1. **Add `DailyBriefingNarrate = "daily-briefing-narrate"` to `ConsumerTypes`** (+ update `ConsumerTypes.All`).
2. **Author + deploy a `sprk_playbookconsumer` row** in spaarkedev1 mapping `sprk_consumertype = "daily-briefing-narrate"` → `sprk_analysisplaybookid = DAILY-BRIEFING-NARRATE playbook GUID` (deployed in PR 2 task 011). This is a NEW deployment task — add to PR 4 plan.
3. **Author NEW non-document playbook execution method** on `IAnalysisOrchestrationService` (or a thin sibling — `IPlaybookExecutorService` or similar). Method signature: `Task<TResult> ExecutePayloadPlaybookAsync<TResult>(Guid playbookId, object payload, HttpContext, CT)`. Internally: load playbook by ID, run node graph synchronously (or stream + aggregate), bind payload as the Start node's scope variable, return typed response. Justification element required: existing `ExecutePlaybookAsync` is document-centric and doesn't fit; this method is a sibling not a replacement. Extension test: can `ExecutePlaybookAsync` be made document-optional? NO — its first line reads `request.DocumentIds[0]` and its entire flow assumes document context. A new method is the right surface.
4. **Wire `HandleNarrate`** to use `IConsumerRoutingService` + the new payload-execution method.
5. **Preserve empty-payload tolerance + 503 fail-fast branches.**
6. **Update `DailyBriefingEndpoints.HandleNarrate` Justification element** in task 031 POML: existing = hardcoded body, extension = Yes (replace implementation), cost-of-doing-nothing = hallucination persists; prompts can't iterate without recompile.

### What task 031 does NOT need to build

- No change to `IConsumerRoutingService` interface — existing surface works.
- No change to `ConsumerRoutingService` implementation — existing caching, environment matching, conditions matching all work for daily-briefing.
- No change to the playbook deployment flow — task 011 deploys `DAILY-BRIEFING-NARRATE` row in PR 2 already.

---

## Impact on Task 030 (PR 4 dispatch decision)

Task 030 reads this document, confirms findings (optionally surveys 1–2 of the 6 existing consumers as additional precedent for the routing-facade pattern — `WorkspaceFileEndpoints` is closest analog because it's also an endpoint, not a service), and records the path decision. The same approach (Path A.5) becomes binding for task 031 unless task 030 surfaces a new blocker.

**One open item for task 030 to resolve**: confirm whether `IPlaybookService` already has a payload-execution method that bypasses document loading. The Phase 1 survey did not enumerate `IPlaybookService` methods. If such a method exists, task 031's new method-build (item 3 above) is unnecessary — the existing method is wired into the new `HandleNarrate` wrapper directly. `IPlaybookService.cs:9` (interface declaration) should be the starting point for that survey.

---

## Impact on Task 026 (PR 3 standardization)

The customData fields enriched by W1 (`viaMatter`, `regardingName`, `source`) reach the playbook payload via `DailyBriefingNarrateRequest` (which the widget builds from `parseNotificationData`). The playbook's `BRIEF-VALIDATE-ENTITY-NAMES` Tool node consumes the allow-list from these fields. No new work in PR 3 for this — but PR 3 customData enrichment MUST be live before PR 4 narrate dispatch is end-to-end testable.

---

## Risks Surfaced

| ID | Risk | Mitigation |
|---|---|---|
| D-1 | `IPlaybookService` may not expose a non-document execution path; new method on `IAnalysisOrchestrationService` increases its surface | Task 030 verifies `IPlaybookService` first. If new method needed, scope is small (existing patterns to mirror). Publish-size delta expected ≤ +0.1 MB. |
| D-2 | `sprk_playbookconsumer` matching may be opinionated toward document MIME-type matching (per `IRoutingContext`) | `RoutingContext` is optional; daily-briefing passes `null` (line 67 of IConsumerRoutingService.cs explicitly supports this). No `sprk_matchconditions` JSON needed on the new routing row. |
| D-3 | Streaming-to-non-streaming aggregation could lose error fidelity if playbook fails mid-node | New method should surface a typed `PlaybookExecutionException` if any node fails; `HandleNarrate` converts to 503 ProblemDetails or to the `ActivityNotesSection` empty-narrative fallback (FR-16). |

---

## MCP Verification

Per task 001 step 2 + AC, an MCP `read_query` smoke against spaarkedev1 was requested. This worktree's session has the Dataverse MCP available (`mcp__dataverse__*` tools). **The smoke is left to task 030** to run with explicit MCP invocation rather than this scoping research step; this document focuses on the code survey + payload contract, which is what task 031 needs to start implementation. Task 030 verifies + records the MCP smoke result.

---

## Decision Confidence

**High** for Path A.5 over Path B. Pure Path A is structurally precluded by the document-centric `ExecutePlaybookAsync`. Path B sacrifices a clean routing precedent for no benefit. Path A.5 preserves the §10 / chat-routing-redesign-r1 pattern with one small new entry point — the right architectural fit.

**Open** for the exact new-method placement (on `IAnalysisOrchestrationService` vs sibling `IPlaybookExecutorService`). Task 030 decides based on `IPlaybookService` survey.

---

## Confirmation — IPlaybookService survey result (task 030, 2026-06-26)

### Methods enumerated on `IPlaybookService` and document-centric status

Survey of `src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookService.cs` (interface declaration) yields 12 methods. NONE execute a playbook — all are CRUD / lookup / index-tracking:

| # | Method | Surface | Executes a playbook? |
|---|---|---|---|
| 1 | `CreatePlaybookAsync` | CRUD — create row | No (CRUD) |
| 2 | `UpdatePlaybookAsync` | CRUD — update row | No (CRUD) |
| 3 | `GetPlaybookAsync` | CRUD — read by ID | No (lookup) |
| 4 | `UserHasAccessAsync` | Authorization | No (auth check) |
| 5 | `ValidateAsync` | Validation | No (validation) |
| 6 | `ListUserPlaybooksAsync` | CRUD — list owned | No (query) |
| 7 | `ListPublicPlaybooksAsync` | CRUD — list public | No (query) |
| 8 | `GetByNameAsync` | Lookup by name (cached) | No (lookup) |
| 9 | `GetCanvasLayoutAsync` | Canvas-layout read | No (CRUD) |
| 10 | `SaveCanvasLayoutAsync` | Canvas-layout write | No (CRUD) |
| 11 | `ListTemplatesAsync` | CRUD — list templates | No (query, currently disabled) |
| 12 | `ClonePlaybookAsync` | CRUD — clone template | No (CRUD) |
| 13 | `ListAllActivePlaybooksAsync` | Drift-detection enumeration | No (query for FR-13) |
| 14 | `UpdateIndexStatusAsync` | Indexing tracking | No (CRUD on tracking fields) |

**Conclusion on `IPlaybookService`**: this is exclusively a CRUD + lookup + indexing service. Playbook **execution** is on a different interface family — `IAnalysisOrchestrationService` (document-centric, streaming) and `IPlaybookOrchestrationService` (legacy + node-based, streaming).

### CRITICAL discovery — `IInvokePlaybookAi` already provides the exact non-document execution path

While completing the survey, a Grep across `Services/Ai` for execute/invoke entry points surfaced an existing facade that PERFECTLY fits Path A.5:

- **`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IInvokePlaybookAi.cs:51`** — `IInvokePlaybookAi` (R6 Pillar 3 / Q11) public facade in `PublicContracts/` (the ADR-013 boundary).
- **Method signature** (lines 82–86):
  ```csharp
  Task<PlaybookInvocationResult> InvokePlaybookAsync(
      Guid playbookId,
      IReadOnlyDictionary<string, string>? parameters,
      PlaybookInvocationContext context,
      CancellationToken cancellationToken = default);
  ```
- **`InvokePlaybookAi.cs:67–72`** — Implementation comment **explicitly documents the non-document semantic**:
  > "Construct the orchestration request. Note: the facade does NOT accept documentIds today — invoke_playbook callers pass parameters only. The orchestration service interprets an empty documentIds array as 'no document context' (consistent with the existing M365 Copilot adapter path)."
- **Return type**: `PlaybookInvocationResult` — non-streaming, single typed result. Carries `TextContent`, `StructuredData` (JsonElement), `Citations`, `Confidence`, `Success`, `ErrorMessage`, `ErrorCode` — every shape `/narrate` needs.
- **Wave 7b precedent**: this facade is already used by `InvokePlaybookHandler` (chat-tool dispatch path) and is destined for the M365 Copilot adapter — both consume the same non-document, parameters-only contract.

### Final Path A.5 decision: USE EXISTING facade — no new method required

**Task 031 does NOT need to author a new playbook-execution method.** The existing `IInvokePlaybookAi.InvokePlaybookAsync` is the right binding. Task 031's work simplifies to:

1. **Add `DailyBriefingNarrate = "daily-briefing-narrate"` to `ConsumerTypes`** (+ update `ConsumerTypes.All`).
2. **Deploy `sprk_playbookconsumer` row** in spaarkedev1: `sprk_consumertype = "daily-briefing-narrate"` → `sprk_analysisplaybookid = DAILY-BRIEFING-NARRATE GUID`.
3. **Wire `HandleNarrate`** to inject `IConsumerRoutingService` + `IInvokePlaybookAi`:
   - Call `routing.ResolveAsync(ConsumerTypes.DailyBriefingNarrate, "default", null, null, ct)` to get the playbook GUID.
   - Build `IReadOnlyDictionary<string, string>` of parameters from `DailyBriefingNarrateRequest` (serialize the structured payload — Categories / PriorityItems / Channels / totals — as one or more JSON-string parameter entries the playbook node graph can read via template substitution).
   - Call `invokePlaybookAi.InvokePlaybookAsync(playbookId, parameters, new PlaybookInvocationContext { TenantId = …, HttpContext = httpContext }, ct)`.
   - Map `PlaybookInvocationResult.TextContent` / `StructuredData` to `DailyBriefingNarrateResponse { Tldr, ChannelNarratives, GeneratedAtUtc }` — preserving the existing widget-parser contract.
4. **Preserve empty-payload tolerance + 503 fail-fast branches** (unchanged from the prior sketch).

### One caveat → task 031 must validate playbook parameter shape

The `IInvokePlaybookAi` parameter dictionary is `IReadOnlyDictionary<string, string>` — designed for **template substitution in node prompts**, NOT for arbitrary structured payloads. The daily-briefing payload (categories array, channels array, priorityItems array) must be serialized into string-valued parameter entries that the DAILY-BRIEFING-NARRATE playbook's node graph can read via Handlebars template references like `{{categoriesJson}}` or `{{channelsJson}}`.

**Action for task 031**: cross-check with task 010's playbook node graph design (`projects/spaarke-daily-update-service-r4/notes/design/010-narrate-playbook-graph.md` or the deployed `sprk_configjson`) to confirm the playbook expects parameter inputs as JSON-string values readable via template substitution. If the playbook was designed assuming structured object inputs (not JSON strings), task 031 must serialize on the BFF side as part of the parameter-dictionary build.

### Concrete signature recommendation for task 031 — DI on `HandleNarrate`

```csharp
private static async Task<IResult> HandleNarrate(
    DailyBriefingNarrateRequest request,
    ILoggerFactory loggerFactory,
    IConsumerRoutingService routing,             // existing — no changes
    IInvokePlaybookAi invokePlaybookAi,           // existing — no changes
    HttpContext httpContext,
    CancellationToken cancellationToken)
{ ... }
```

**No new interface authored. No new service registered. No surface delta to publish-size.** Task 031 simplifies to: ConsumerTypes constant addition + Dataverse row deployment + `HandleNarrate` rewrite + payload serialization + response mapping.

### MCP smoke — closes task 001 AC-3 deferral

Per `notes/debug/001-mcp-smoke-deferred.md`, task 030 ran the deferred Dataverse-MCP smoke against spaarkedev1:

```sql
SELECT TOP 1 sprk_analysisactionid, sprk_name, sprk_executoractiontype
FROM sprk_analysisaction
WHERE sprk_executoractiontype = 52
```

Result (full evidence in `notes/smoke/030-mcp-spaarkedev1-smoke.md`):

```json
[{
  "sprk_analysisactionid": "ca44b7aa-fc70-f111-ab0e-7ced8ddc4cc6",
  "sprk_name": "Lookup User Membership",
  "sprk_executoractiontype": 52
}]
```

MCP connectivity to spaarkedev1 is live. Cross-purpose smoke confirms SYS-LOOKUP-MEMBERSHIP (task 005) is deployed. Task 001 AC-3 deferral is closed.

### Net impact on Risks Surfaced section above

| Original risk | Updated status (post-IInvokePlaybookAi discovery) |
|---|---|
| **D-1** — `IPlaybookService` may not expose non-document path; new method increases surface | **RESOLVED** — `IInvokePlaybookAi` provides the exact contract. ZERO publish-size delta from new code. ZERO new interface surface. |
| **D-2** — `sprk_playbookconsumer` matching may be opinionated toward document MIME-type | Unchanged — null `RoutingContext` is supported. |
| **D-3** — Streaming-to-non-streaming aggregation could lose error fidelity | **PARTIALLY RESOLVED** — `InvokePlaybookAi.cs:84–135` shows the existing aggregation already handles `NodeFailed` / `RunFailed` / `RunCancelled` and surfaces typed `errorMessage` + `errorCode` in `PlaybookInvocationResult`. Task 031 just maps these to ProblemDetails. |

### Sign-off

Task 030 complete. Path A.5 is binding. Task 031 implementation simplified — no new method authoring required. AC-12c satisfied with concrete IPlaybookService + IInvokePlaybookAi survey evidence (file:line citations).

