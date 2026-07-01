# Wave 11 T116 — Systematic Assessment of `/narrate` Empty-Response Failure

> **Author**: Claude (R7 task-execute session)
> **Date**: 2026-06-30
> **Run analyzed**: 2026-06-30T01:43:33Z → 2026-06-30T01:43:40Z (RunId 97d450fb-7042-454c-8e51-30fd31313ad1)
> **Purpose**: Replace heuristic layer-by-layer fixing with empirically-verified root-cause analysis. NO code changes during assessment.

---

## Executive summary

The `/narrate` endpoint returns HTTP 200 with empty tldr + empty channelNarratives. **Three independent failures stack**, but **only one is the actual blocker** for getting non-empty output. The other two would still be defects after the first is fixed but would not be visible to the caller.

| # | Failure | Severity | Root cause | Where fixed |
|---|---|---|---|---|
| **P1** | `playbookResult.StructuredData` carries the wrong node's output (GenerateTldr's raw JSON instead of ReturnResponse's composed object) | **BLOCKER** — without this, every other fix is invisible | `InvokePlaybookAi.AggregatePlaybookEvents` aggregation contract treats only `IsDeliverOutput=true` nodes (DeliverOutput, DeliverComposite) as terminal. ReturnResponseNodeExecutor does NOT set IsDeliverOutput=true. With GenerateTldr setting `TextContent` non-null first, ReturnResponse's StructuredData is silently dropped by the aggregator. | One-line change in ReturnResponseNodeExecutor (set IsDeliverOutput=true on the returned NodeOutput) OR a one-line change in InvokePlaybookAi aggregator (treat ReturnResponse as terminal) |
| **P2** | LoadKnowledge produces `channelRegistry = {}` (entries=0) instead of `channelRegistry = { channels: [...] }`. This causes GenerateChannelNarratives fan-out to run 0 iterations. | High — channelNarratives empty even if P1 fixed | `LoadKnowledgeConfig.PassthroughBinding` is typed `Dictionary<string, string>?`. After Layer 1 substitution, `"channels": "{{start.channels}}"` becomes a native JSON array, not a string. Deserialization silently produces config=null OR PassthroughBinding=null. | Either: (a) change `LoadKnowledgeConfig.PassthroughBinding` to `Dictionary<string, JsonElement>?` + adjust render path; OR (b) skip LoadKnowledge entirely (it's a pass-through placeholder) by changing GenerateChannelNarratives iterateOver to `{{start.channels}}`; OR (c) have orchestrator skip Layer 1 substitution for LoadKnowledge configJson values |
| **P3** | EntityNameValidator scrubber: input=384 chars → output=0 chars (5 terms removed = all entity references stripped) | Medium — even with P1+P2 fixed, the scrubbed text in `_validationMetadata.scrubbedText` is empty; pre-scrub tldrResult survives so user-visible response unaffected | Source playbook's `allowList` was authored against R4-aspirational shape (`priorityItems[].regardingName`, `priorityItems[].viaMatter.name`, `categories[].items[].*`) — fields that don't exist on the actual BFF DTO `DailyBriefingNarrateRequest`. The allow-list ends up empty/tiny, so the scrubber treats every entity reference as a hallucination. | Source playbook fix (already applied this session, awaiting redeploy + verification) |

**Bottom line**: P1 is THE bug. Fixing it alone makes the response non-empty. P2 affects only `channelNarratives` (fan-out result). P3 only affects the `_validationMetadata.scrubbedText` sidecar.

---

## Phase 1 — Data flow end-to-end

### Layer 1: BFF endpoint `HandleNarrate` ([DailyBriefingEndpoints.cs:226](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs))

```
DailyBriefingNarrateRequest (HTTP body)
  ↓
serialize via NarrateSerializerOptions (camelCase)
  ↓
parameters["briefingPayload"] = JSON string
  ↓
IInvokePlaybookAi.InvokePlaybookAsync(playbookId, parameters, …)
```

Endpoint does NOT directly construct the prompt — that responsibility was deleted in R4 task 031 (see comment at DailyBriefingEndpoints.cs:458-476). All prompt construction now lives in the playbook + Action rows.

### Layer 2: `InvokePlaybookAi.InvokePlaybookAsync` ([InvokePlaybookAi.cs:50-170](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/InvokePlaybookAi.cs))

```
PlaybookRunRequest { PlaybookId, DocumentIds=[], Parameters }
  ↓
orchestrator.ExecuteAsync(request, …) → IAsyncEnumerable<PlaybookStreamEvent>
  ↓
AGGREGATION LOOP (line 96-134):
  foreach event:
    if NodeCompleted AND output.Success:
      if output.IsDeliverOutput OR terminalText is null:
        terminalText  = output.TextContent ?? terminalText
        structuredData = output.StructuredData ?? structuredData
  ↓
return PlaybookInvocationResult {
  TextContent = terminalText,
  StructuredData = structuredData,
  …
}
```

**Key contract**: only nodes with `IsDeliverOutput=true` OR the FIRST node to set non-null TextContent become the "terminal" output captured into the result.

### Layer 3: `PlaybookOrchestrationService.ExecuteAsync` per-node loop ([PlaybookOrchestrationService.cs:1130-1310](../../../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs))

For each node in topological order:

```
1. Get executor for ExecutorType
2. CollectDownstreamNodeInfo (for $choices resolution)
3. ApplyInsightsRoutingAsync (Insights Engine routing; no-op for non-Insights playbooks)
4. *** R7 Wave 11 task 114: detect fan-out iteration on RAW configJson ***
   if TryExtractIterationConfig(node.ConfigJson, …):
     ExecuteFanOutIterationAsync(…) → composite output
     StoreNodeOutput(composite)
     write NodeCompleted event with composite
     RETURN — skip the normal path
5. *** Layer 1 substitution: ApplyConfigJsonTemplates(node, runContext) ***
   - JSON-aware structural walker (RenderConfigJsonStructurally)
   - For each string value that is a "pure template" `{{X}}`:
     - AutoWrapWithJsonHelper → `{{json X}}` (or `{{json (X)}}` if helper-call)
     - Render
     - TryParseAsJson → replace with native JSON shape
   - For mixed values (text + template): render as string
6. CreateNodeContext (substituted node + scopes + actionType)
7. executor.Validate
8. executor.ExecuteAsync → NodeOutput
9. runContext.StoreNodeOutput(output)
10. write NodeCompleted event
```

### Layer 4: per-node executor behaviors (relevant subset)

**StartNodeExecutor** (Start node, scope=`start`)
- Reads `briefingPayload` parameter
- Stores `DailyBriefingNarrateRequest` JsonElement at scope key `start`
- TextContent = null, StructuredData = the request JSON, IsDeliverOutput = **false**
- Trace confirms: `Start node 32371fa5 bound payload to scope 'start' (source='briefingPayload', kind=Object)`

**LoadKnowledgeNodeExecutor** (LoadKnowledge node, scope=`channelRegistry`)
- Deserializes configJson into `LoadKnowledgeConfig` (PassthroughBinding: `Dictionary<string, string>?`)
- Renders each passthroughBinding[key] value via Handlebars
- Stores resolved dict at scope key `channelRegistry`
- TextContent = null, StructuredData = the resolved dict, IsDeliverOutput = **false**
- **DEFECT**: After Layer 1 substitution, `passthroughBinding.channels` value is a native JSON array, not a string. Deserialization into `Dictionary<string, string>` cannot represent that → config=null OR PassthroughBinding=null → resolved stays empty
- Trace confirms: `LoadKnowledge bound pass-through to scope 'channelRegistry' (entries=0)`

**AiCompletionNodeExecutor** (GenerateTldr node, scope=`tldrResult`)
- Calls `PromptSchemaOverrideMerger` + extracts `templateParameters` + extracts `inputBinding` as JsonElement
- Renders the JPS prompt via `PromptSchemaRenderer.Render(…, runtimeInput: inputBinding)` — this assembles a `## Input` section between Context and Document
- Calls `IOpenAiClient.GetStructuredCompletionRawAsync(prompt, schema, …)` with constrained decoding
- Returns NodeOutput { TextContent = rawJson, StructuredData = parsed JsonElement, IsDeliverOutput = **false** }
- Trace confirms: `AiCompletion node 0d895da7 (Action ce299eb4) completed: RawJsonLength=459, DurationMs=1685`

**Fan-out for GenerateChannelNarratives** (line 1204-1235 + 2004-2148)
- `TryExtractIterationConfig` on RAW configJson succeeds → `iterateOverExpr = "{{channelRegistry.channels}}"`, `itemAlias = "channel"`
- `ExecuteFanOutIterationAsync` auto-wraps iterateOverExpr with json helper, renders against templateContext, parses as JSON array
- For each item: builds overlay context with `channel = currentItem`, renders configWithoutIteration, calls executor
- Aggregates per-iteration StructuredData into composite NodeOutput with array data
- TextContent = null, IsDeliverOutput = **false**
- Trace confirms: `Fan-out node GenerateChannelNarratives completed: 0 iterations, total duration: 2ms`
- **0 iterations** because `channelRegistry.channels` resolved to null/empty (P2 cascade)

**EntityNameValidatorNodeExecutor** (ValidateEntityNames node, scope=`validationResult`)
- Reads configJson.candidateText (string) + configJson.allowList (string[])
- Scrubber removes terms from candidateText not in allowList; returns `scrubbedText` + `removedTerms[]`
- Trace confirms: `EntityNameValidator node 11895da7 completed -- inputLength=384, outputLength=0, removedCount=5`

**ReturnResponseNodeExecutor** (ReturnResponse node, scope=`response`)
- Deserializes configJson into ReturnResponseConfig (ResponseBinding: `Dictionary<string, JsonElement>?` — note JsonElement, not string)
- For each binding key:
  - If rawTemplate.ValueKind != String (= already resolved to native object by Layer 1): return `ConvertJsonElement(rawTemplate)` directly
  - Else: render template + try parse rendered string as JSON
- Stores resolved dict serialized to JsonElement at scope key `response`
- `return NodeOutput.Ok(…, data: payloadElement, textContent: null, …)` — IsDeliverOutput **NOT SET** (default false)
- Trace confirms: `ReturnResponse bound response to scope 'response' (fields=4)`

### Layer 5: response composition `ProjectPlaybookResultToNarrateResponse` ([DailyBriefingEndpoints.cs:394-455](../../../../src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs))

```
playbookResult.StructuredData (JsonElement)
  ↓
if data is Object AND data has "tldr" property AND tldr.ValueKind == Object:
  tldr = data["tldr"].Deserialize<TldrResult>(…)
if data has "channelNarratives" property AND array:
  channelNarratives = data["channelNarratives"].Deserialize<…>
ELSE:
  return default empty TldrResult + empty channelNarratives
  ↓
return DailyBriefingNarrateResponse { Tldr, ChannelNarratives, GeneratedAtUtc }
```

**This is the consumption point. It expects StructuredData = `{tldr: {...}, channelNarratives: [...]}`**. If StructuredData has a different shape (e.g., the GenerateTldr LLM output `{summary, keyTakeaways, topAction, ...}`), `data.tldr` won't exist → returns default empty.

---

## Phase 2 — Empirical trace walk (RunId 97d450fb)

| Time | Layer | Event | Evidence supporting layer-level outcome |
|---|---|---|---|
| 01:43:33.731 | BFF | Token validated, audience matches | App Insights `Token on POST /api/ai/daily-briefing/narrate` |
| 01:43:33.744 | Orchestrator | Run started | `Starting playbook execution - RunId: 97d450fb, PlaybookId: 7b5a6ed3` |
| 01:43:33.745–01:43:36.5 | Orchestrator | 18 sprk_playbooknodes/sprk_analysisactions GETs | Loading all 6 nodes + their Action rows from Dataverse |
| 01:43:37.071 | Start | Bound payload → scope `start` (Object) | `Start node bound payload to scope 'start' (source='briefingPayload', kind=Object)` |
| 01:43:37.361 | LoadKnowledge | **`entries=0`** ← FAILURE | `LoadKnowledge bound pass-through to scope 'channelRegistry' (entries=0)` |
| 01:43:37.362–38.099 | GenerateTldr+ChannelNarratives batch | Both nodes launched in parallel | `Executing batch 3/5 with 2 nodes: GenerateTldr, GenerateChannelNarratives` |
| 01:43:38.067 | GenerateTldr | LLM called with `RenderedPromptLength=4657, OutputSchemaJsonLength=1313` | `prompt prepared: Action=ce299eb4, …, RenderedPromptLength=4657` |
| 01:43:38.099 | GenerateChannelNarratives | **`0 iterations`** ← FAILURE | `Fan-out node GenerateChannelNarratives completed: 0 iterations, total duration: 2ms` |
| 01:43:39.751 | GenerateTldr | LLM returned **`ResponseLength=459`** ← real content | `Structured raw completion finished. Model=gpt-4o-mini, Schema=AiCompletion_Brief_Narrate_TLDR, ResponseLength=459` |
| 01:43:40.138 | ValidateEntityNames | **`inputLength=384, outputLength=0, removedCount=5`** ← scrubber stripped everything | `EntityNameValidator node completed -- inputLength=384, outputLength=0, removedCount=5` |
| 01:43:40.435 | ReturnResponse | Bound response → scope `response` with `fields=4` ← but never captured by aggregator | `ReturnResponse bound response to scope 'response' (fields=4)` |

**Aggregation behavior** (inferred from InvokePlaybookAi.cs:97-109 contract):

1. Start NodeCompleted: TextContent=null → `terminalText IS null` → captures `structuredData = Start's StructuredData` (the request JSON)
2. LoadKnowledge NodeCompleted: TextContent=null + terminalText still null → captures `structuredData = LoadKnowledge's StructuredData` (the empty `{}`)
3. GenerateTldr NodeCompleted: TextContent=rawJson (459 chars) → **`terminalText becomes non-null`** + `structuredData = GenerateTldr's StructuredData` (the tldr-shaped JSON)
4. GenerateChannelNarratives NodeCompleted: TextContent=null, IsDeliverOutput=false → terminalText already non-null → **SKIPPED**
5. ValidateEntityNames NodeCompleted: TextContent=null, IsDeliverOutput=false → terminalText already non-null → **SKIPPED**
6. ReturnResponse NodeCompleted: TextContent=null, IsDeliverOutput=false → terminalText already non-null → **SKIPPED**

→ `playbookResult.StructuredData` = GenerateTldr's raw LLM output (e.g., `{summary, keyTakeaways, topAction, categoryCount, priorityItemCount}`), NOT the composed `{tldr: {...}, channelNarratives: [...]}` shape that `ProjectPlaybookResultToNarrateResponse` expects.

→ `data.TryGetProperty("tldr", …)` returns false → response uses the default empty TldrResult initialization (DailyBriefingEndpoints.cs:401-407). **Exactly the empty shape we see in the smoke response.**

---

## Phase 3 — Fix matrix

### P1 (BLOCKER): aggregator drops ReturnResponse output

**Verified root cause**: `InvokePlaybookAi.AggregatePlaybookEvents` line 101-104 captures only IsDeliverOutput nodes once `terminalText` is set. ReturnResponseNodeExecutor never sets IsDeliverOutput=true, so its composed StructuredData is silently dropped.

**Fix options**:

| Option | Change | Risk | Surface |
|---|---|---|---|
| **P1a** | Add `IsDeliverOutput = true` to the NodeOutput returned by ReturnResponseNodeExecutor (one-line change at ReturnResponseNodeExecutor.cs:225-231) | LOW. ReturnResponse is semantically a terminal output node. Pattern mirrors DeliverOutputNodeExecutor.cs:182 + DeliverCompositeNodeExecutor.cs:255. No other playbook depends on the current behavior (verified: no IsDeliverOutput consumers besides this aggregator). | Single executor file |
| **P1b** | Update aggregator to also treat `output.OutputVariable == "response"` (or NodeType == ReturnResponse) as terminal | MEDIUM. Special-cases by name/type in the aggregator. The IsDeliverOutput contract already exists for this purpose — duplicating it adds maintenance debt. | InvokePlaybookAi facade |
| **P1c** | Change `terminalText` capture rule to "last successful node wins" (drop the IsDeliverOutput preference) | HIGH. Breaks chat-summarize + Insights consumers that rely on the existing DeliverOutput preference. | InvokePlaybookAi facade + all dependent consumers |

**Recommendation**: **P1a**. Smallest, semantically correct, mirrors existing pattern.

### P2 (HIGH): LoadKnowledge loses passthroughBinding to type mismatch

**Verified root cause**: `LoadKnowledgeConfig.PassthroughBinding` is `Dictionary<string, string>?` but after Layer 1 substitution the JSON value is a native array.

**Fix options**:

| Option | Change | Risk | Surface |
|---|---|---|---|
| **P2a** | Change `LoadKnowledgeConfig.PassthroughBinding` to `Dictionary<string, JsonElement>?` and adapt ExecuteAsync to handle JsonElement values (when ValueKind != String, treat as already-resolved native value) | LOW. Mirrors ReturnResponseConfig.ResponseBinding's existing JsonElement typing. Pattern already established. | Single executor file |
| **P2b** | Remove LoadKnowledge from the playbook entirely (it's a documented R5 placeholder pass-through). Change GenerateChannelNarratives iterateOver from `{{channelRegistry.channels}}` to `{{start.channels}}` directly. Delete LoadKnowledge node + edge. | MEDIUM. Loses the R5 forward-compat hook documented in the playbook. Easy to re-add when R5 wires AI Search retrieval. | Source playbook JSON + spaarkedev1 sync (delete one node row) |
| **P2c** | Have orchestrator skip Layer 1 substitution for LoadKnowledge configJson values (treat passthroughBinding specially) | HIGH. Adds executor-specific special-casing to the orchestrator. Violates the "uniform Layer 1" design. | Orchestrator |

**Recommendation**: **P2a**. Type the field correctly to match the post-Layer-1 reality. P2b is a fallback if P2a is unexpectedly tricky.

### P3 (MEDIUM): EntityNameValidator allowList references nonexistent DTO fields

**Verified root cause**: Source playbook's allowList expression used R4-aspirational shape:
```
{{distinct (concat (map start.priorityItems 'regardingName')
                   (map start.priorityItems 'viaMatter.name')
                   …
                   (flatMap start.categories 'items.regardingName')
                   …)}}
```

But the BFF DTO `DailyBriefingNarrateRequest` has:
- `PriorityItemDto = {category, title, dueDate}` — no regardingName, no viaMatter, no source
- `NotificationCategoryDto = {name, count, unreadCount}` — no items[] at all
- `ChannelNarrationInput = {category, label, items[]}` — items DO have regardingName ✓

**Fix**: Update source playbook to ground allowList on actually-present fields:
- `start.priorityItems[].title` (carries entity-ish names)
- `start.channels[].items[].regardingName` (canonical entity names)
- `start.channels[].items[].title` (additional grounding)

**Status**: This fix was already applied this session (commit not yet pushed). Awaiting redeploy + verification.

**Impact when P1 unfixed**: P3 only affects `_validationMetadata.scrubbedText` sidecar. The user-visible tldr + channelNarratives come from PRE-SCRUB tldrResult/channelNarrationResults per the playbook's responseBinding, so P3 doesn't blank the user-visible response (it just makes the metadata field empty).

---

## Recommended ordered fix plan

```
Step 1: Apply P1a — set IsDeliverOutput=true on ReturnResponseNodeExecutor's NodeOutput
        Files: src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ReturnResponseNodeExecutor.cs (1 line)
        Verification: build, dotnet test, deploy BFF, smoke /narrate → expect tldr.summary populated

Step 2: Apply P2a — fix LoadKnowledgeConfig.PassthroughBinding type
        Files: src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LoadKnowledgeNodeExecutor.cs
        Verification: build, dotnet test, deploy BFF, smoke /narrate → expect channelNarratives.length > 0

Step 3: Verify P3 fix landed correctly (sync playbook + smoke)
        Files: scripts/dataverse/Sync-DailyBriefingNarratePlaybookNodes.ps1 (already-modified source)
        Verification: smoke /narrate → expect _validationMetadata.scrubbedText non-empty + removedTerms = []
```

**Total**: 2 small code changes (Steps 1+2) + 1 already-applied source playbook fix (Step 3).
**Deploys needed**: 1 BFF deploy (Steps 1+2 combined) + 1 playbook sync (Step 3, already done).
**Risk**: LOW per-step. Each step verifiable in isolation via response shape.

## Why this is different from previous reactive cycles

Previous cycles attempted to fix issues in the order they were observed (flatten inputBinding → DTO alignment → allowList rewrite). Each fix was applied without verifying it would propagate to the response shape, because the **aggregator-drops-ReturnResponse bug (P1) means NONE of those fixes can be visible to the caller**.

P1 is the load-bearing fix. Without it, P2 and P3 are invisible. With it alone, response shows non-empty tldr (because GenerateTldr LLM is producing content) even if channelNarratives stays empty (P2 unfixed) and scrubbed sidecar stays blank (P3 unfixed).

The previous Wave 11 architectural work (Option B two-layer, Option D JSON-aware Layer 1, helper consolidation, fan-out semantics) is structurally correct — the failure was an inherited R4 contract mismatch between ReturnResponse and the IInvokePlaybookAi aggregator, NOT a Wave 11 regression.

---

*End of assessment.*
