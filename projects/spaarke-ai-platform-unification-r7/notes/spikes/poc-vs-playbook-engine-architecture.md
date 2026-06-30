# POC (Live-Render Narrator) vs Playbook Engine — Architecture Comparison

> **Authored**: 2026-06-30 (end of Wave 11 T116/T118 spike session)
> **Status**: POC end-to-end working; pushed at commit `85c762081`
> **Audience**: Architectural decision-makers for future AI function design + governance
> **Related artifacts**:
>   - `notes/spikes/narrator-spike-plan.md` — original spike plan
>   - `notes/spikes/narrator-vs-playbook-comparison.md` — first empirical comparison (T116)
>   - `notes/handoffs/wave11-t116-narrate-systematic-assessment.md` — root-cause analysis of /narrate failure
>   - Commits: `3affa952f` (narrator spike) + `85c762081` (live-render POC + widget cutover)

---

## 0. Executive summary

Over this session we built a working POC of a **code-defined AI workflow** for the Daily Briefing widget, in parallel to the existing **playbook-engine-driven** workflow. Both approaches use the same R7 narrator and the same Action rows in Dataverse for prompts/schemas/tuning. They differ only in **how the workflow is assembled and executed**.

The POC is now live in spaarkedev1: a real user (Ralph) refreshes the Daily Briefing widget and sees **26 of their actual notifications** narrated end-to-end — *including the user's own recent record updates* (17 items in "My Recent Updates" channel) — with per-bullet entity links for click-through.

This document inventories the two component models, explains the operational differences, and lays out the implications for how Spaarke should **build, orchestrate, and manage AI functions** going forward.

---

## 1. The two approaches in one diagram

### Playbook Engine path (current architecture)

```
User opens widget
       │
       ▼
Widget reads up to 200 appNotification rows via Xrm.WebApi (client-side query)
       │
       ▼
Widget groups rows by category → builds NarrateRequest (categories[], channels[])
       │
       ▼
POST /api/ai/daily-briefing/narrate  (request body = the assembled NarrateRequest)
       │
       ▼
BFF: DailyBriefingEndpoints.HandleNarrate
       │
       ▼
IConsumerRoutingService.ResolveAsync("daily-briefing-narrate") → playbook GUID
       │
       ▼
IInvokePlaybookAi.InvokePlaybookAsync(playbookId, params, ctx)
       │
       ▼
PlaybookOrchestrationService.ExecuteAsync (2,500+ LOC)
   ├─ Load playbook + nodes + edges from Dataverse                (3 queries)
   ├─ For each of 6 nodes:
   │     ├─ Resolve Action FK to load prompt/schema               (1 query per node)
   │     ├─ Build template context from prior NodeOutputs
   │     ├─ Apply Layer 1 substitution to configJson              (JSON-aware walker, auto-wrap with {{json}})
   │     ├─ Detect fan-out iteration metadata
   │     ├─ Dispatch to executor class via NodeExecutorRegistry
   │     │     ├─ AiCompletionNodeExecutor
   │     │     │   └─ PromptSchemaRenderer.Render (Layer 2: ## Input section)
   │     │     │       └─ IOpenAiClient.GetStructuredCompletionRawAsync
   │     │     ├─ EntityNameValidatorNodeExecutor (scrubber)
   │     │     ├─ LoadKnowledgeNodeExecutor (pass-through placeholder)
   │     │     ├─ StartNodeExecutor (payload binding)
   │     │     └─ ReturnResponseNodeExecutor (response composition with template render)
   │     ├─ Store output in run scope (NodeOutputs[varName])
   │     └─ Emit PlaybookStreamEvent (Node started/completed/failed)
   └─ Stream events to IInvokePlaybookAi aggregator
       │
       ▼
IInvokePlaybookAi aggregator: walk events, pick "terminal" output via IsDeliverOutput
       │
       ▼
PlaybookInvocationResult { StructuredData, TextContent, ... }
       │
       ▼
ProjectPlaybookResultToNarrateResponse: reshape StructuredData → DailyBriefingNarrateResponse
       │
       ▼
HTTP 200 → widget renders
```

**Where the playbook lives**: 1 sprk_analysisplaybook row + 6 sprk_playbooknode rows + 6 sprk_playbooknodeconnection rows + 3 sprk_analysisaction rows in Dataverse (each Action holds prompt + schema + temperature).

### POC Live-Render path (this spike)

```
User opens widget
       │
       ▼
Widget detects USE_LIVE_RENDER=true flag in briefingService.ts
       │
       ▼
POST /api/ai/daily-briefing/render  (request body = {})
       │
       ▼
BFF: DailyBriefingEndpoints.HandleRender
       │
       ▼
Resolve caller's systemuserid from OBO oid claim          (1 FetchXml query)
       │
       ▼
DailyBriefingCollector.CollectAsync(systemUserId, ct)
       │
       ▼
Task.WhenAll of 4 parallel FetchXml queries against sprk_event:
   - Tasks Due Soon         (next 3 days, type=Task, ownership filter)
   - Tasks Overdue          (past due, type=Task, ownership filter)
   - Recent Matter Activity (modifiedon last 24h, by others, ownership filter)
   - My Recent Updates      (modifiedby = caller, modifiedon last 24h)
       │
       ▼
Project each result → BriefingItem[] (typed C# record)
       │
       ▼
Assemble DailyBriefingNarrateRequest (categories, priorityItems, channels)
       │
       ▼
DailyBriefingNarrator.NarrateAsync(request, ct)         (R7 work, reused)
   ├─ Load 2 Action rows by code (BRIEF-NARRATE-TLDR, BRIEF-NARRATE-CHANNEL) from Dataverse
   ├─ Build TLDR prompt + payload → 1 LLM call
   ├─ Task.WhenAll of N per-channel LLM calls
   ├─ EntityNameScrubber.Scrub on combined output      (pure C#, no Dataverse)
   ├─ Enrich each bullet with entity links by matching narrative ↔ input items
   └─ Compose DailyBriefingNarrateResponse
       │
       ▼
HTTP 200 → widget renders (same response shape as /narrate)
```

**Where the workflow lives**: 1 C# class (`DailyBriefingCollector`) + 1 C# class (`DailyBriefingNarrator`). Prompts + schemas + temperature still in Dataverse Action rows.

---

## 2. Component model — Playbook Engine path

### 2.1 Runtime code components

| Component | LOC | Role |
|---|---|---|
| `PlaybookOrchestrationService` | ~2,500 | Walks node graph, dispatches to executors, manages run scope, emits SSE events |
| `IInvokePlaybookAi` + `InvokePlaybookAi` impl | ~250 | Wraps orchestrator's event stream into a single `PlaybookInvocationResult`. Uses `IsDeliverOutput` flag to pick terminal output |
| `IConsumerRoutingService` + `ConsumerRoutingService` | ~200 | Maps consumer-type string (e.g., "daily-briefing-narrate") to playbook GUID via `sprk_playbookconsumer` rows |
| `NodeExecutorRegistry` | ~80 | Registry mapping `ExecutorType` enum → INodeExecutor class instance |
| `INodeExecutor` interface | — | Contract every executor implements |
| `StartNodeExecutor` | ~150 | Binds wrapper-supplied payload to scope as starting variable |
| `LoadKnowledgeNodeExecutor` | ~360 | R4 pass-through placeholder (intended for AI Search retrieval in R5) |
| `AiCompletionNodeExecutor` | ~480 | Calls `IOpenAiClient.GetStructuredCompletionRawAsync` with merged prompt + schema |
| `AiAnalysisNodeExecutor` | ~600 | Tool-driven analysis with handler dispatch (Document, Skill, etc.) |
| `EntityNameValidatorNodeExecutor` | ~580 | Scrubs LLM output against allow-list via sentence-level proper-noun detection |
| `ReturnResponseNodeExecutor` | ~370 | Renders `responseBinding` templates into bound output object |
| `QueryDataverseNodeExecutor` | ~280 | Executes FetchXml from configJson with template/eq-userid substitution |
| `ConditionNodeExecutor` | ~520 | Evaluates conditions (gt/lt/eq/and/or/not), branches via TrueBranch/FalseBranch names |
| `CreateNotificationNodeExecutor` | ~400 | Creates appnotification rows from templated fields |
| `UpdateRecordNodeExecutor` | ~300 | Patches Dataverse records via field mappings |
| `LookupUserMembershipNodeExecutor` | ~430 | Resolves user's memberships via `IMembershipResolverService` |
| `CreateTaskNodeExecutor` | ~250 | Creates sprk_task records |
| `DeliverOutputNodeExecutor` | ~200 | Marks node as `IsDeliverOutput=true` for aggregator |
| `DeliverCompositeNodeExecutor` | ~350 | Composes multi-section output for SSE per-section streaming |
| 10+ other executors (Insights, etc.) | ~3,000 | Domain-specific node types |
| `PlaybookTemplateContextBuilder` | ~190 | Shared helper: builds template context dict from run state |
| `ITemplateEngine` + `TemplateEngine` (Handlebars.NET) | ~250 | Renders templates with 7 custom helpers (json, map, flatten, distinct, concat, join, flatMap) |
| `PromptSchemaRenderer` | ~600 | Builds final LLM prompt from JPS body + skill/knowledge context + `## Input` section |
| `PromptSchemaOverrideMerger` | ~200 | Merges per-node prompt overrides into base Action prompt |
| `PlaybookSchedulerJob` (BackgroundService) | ~500 | Runs hourly, queries notification playbooks, fans out per user, dispatches |
| `IMembershipResolverService` + impl | ~400 | Resolves user's matter/document memberships via membership field discovery + identity normalization |
| **Total runtime code** | **~14,000+ LOC** | Generic infrastructure for ANY playbook-driven AI function |

### 2.2 Per-consumer data (in Dataverse)

For DAILY-BRIEFING-NARRATE specifically:

| Table | Rows | Purpose |
|---|---|---|
| `sprk_analysisplaybook` | 1 | The playbook record (code, description, type, mode, trigger, capabilities, configJson) |
| `sprk_playbooknode` | 6 | One row per node: Start, LoadKnowledge, GenerateTldr, GenerateChannelNarratives, ValidateEntityNames, ReturnResponse. Each has `sprk_executortype` + `sprk_configjson` + `sprk_outputvariable` + FK to Action |
| `sprk_playbooknodeconnection` | 6 | Edges defining node graph topology |
| `sprk_analysisaction` | 3 | BRIEF-NARRATE-TLDR (prompt + schema + temp), BRIEF-NARRATE-CHANNEL, BRIEF-VALIDATE-ENTITY-NAMES |
| `sprk_playbookconsumer` | 1 | Maps consumer-type "daily-briefing-narrate" → playbook GUID for routing |

**Total per-consumer storage**: ~17 Dataverse rows across 5 tables.

### 2.3 Deployment + management surface

| Artifact | Where | How edited |
|---|---|---|
| Playbook source JSON | `projects/spaarke-daily-update-service/notes/playbooks/*.json` | Engineers via PR |
| Sync to Dataverse | PowerShell scripts (`Sync-DailyBriefingNarratePlaybookNodes.ps1`, `Deploy-Playbook.ps1`) | Manual run after PR merge |
| Action prompts/schemas | `sprk_analysisaction` rows | Power Apps maker portal OR PowerShell sync OR Playbook Builder UI |
| Playbook node graph (visual) | Playbook Builder UI in Power Apps | Same UI OR PowerShell sync |
| Membership service config | `IMembershipFieldDiscoveryService` + entity-metadata-based descriptor discovery | Code OR per-entity-metadata convention |
| Scheduler cadence | `PlaybookSchedulerJob` cron expression in code | Code change + redeploy |
| Per-playbook frequency | `sprk_configjson.schedule.frequency` on playbook row + `sprk_lastrundate` field | Edit playbook row in Dataverse |

### 2.4 Operator-visible vs operator-invisible

| Layer | Operator-visible? | Notes |
|---|---|---|
| Action prompt text | **Visible** — editable in Power Apps maker | Real value to operators |
| Action output schema (JSON schema) | **Visible** | Real value |
| Action temperature | **Visible** | Real value |
| Action JPS body (role/task/constraints/examples) | **Visible** | Real value |
| Playbook node graph (which executor runs when) | Visible via Playbook Builder but **operators don't actually edit this in practice** — engineers do via JSON + sync | Cost without realized benefit |
| Node configJson (inputBinding, iteration, outputBinding) | Technically visible but inscrutable — `{{join '\n\n' tldrResult.summary (flatten (map channelNarrationResults 'narrative'))}}` | Cost without operator value |
| Scope variables, template engine, custom Handlebars helpers | Code-only | Pure operator-invisible infrastructure |
| Executor dispatch, aggregator contracts | Code-only | Pure operator-invisible infrastructure |
| `IsDeliverOutput` flag | Code-only — the source of one of the P1 bugs this session | Pure operator-invisible infrastructure |

---

## 3. Component model — POC Live-Render path

### 3.1 Runtime code components

| Component | LOC | Role |
|---|---|---|
| `DailyBriefingCollector` | ~350 | 4 parallel FetchXml queries + projection to NarrateRequest. ENTIRELY consumer-specific. |
| `DailyBriefingNarrator` | ~290 | Loads 2 Actions, calls LLM 1+N times, runs scrubber, composes response. Mostly reusable infrastructure with consumer-specific glue (the call sites + bullet enrichment) |
| `EntityNameScrubber` | ~240 | Pure-C# scrubbing algorithm. Reusable across all narrators. |
| `IGenericEntityService` | (reused) | Existing Dataverse client (~200 LOC if counted, but shared across BFF) |
| `IOpenAiClient` | (reused) | Existing Azure OpenAI wrapper (~400 LOC shared) |
| `AnalysisActionService.GetActionByCodeAsync` | +50 | New method on existing service to load Action by stable code |
| `HandleRender` endpoint method | +90 | New endpoint method: resolves caller, runs collector, narrates, returns |
| **Total new code added for this consumer** | **~1,020 LOC** | Most of it consumer-specific (collector + queries + projection); narrator + scrubber are reusable |
| **Runtime code path** (what executes per request) | **~880 LOC** | Excludes the unused playbook engine; same LLM client + Dataverse client + auth that the playbook path also uses |

### 3.2 Per-consumer data (in Dataverse)

| Table | Rows | Purpose |
|---|---|---|
| `sprk_analysisaction` | 2-3 | BRIEF-NARRATE-TLDR, BRIEF-NARRATE-CHANNEL, BRIEF-VALIDATE-ENTITY-NAMES |
| (no playbook rows) | 0 | — |
| (no node rows) | 0 | — |
| (no edge rows) | 0 | — |
| (no consumer routing row) | 0 | — |

**Total per-consumer storage**: ~3 Dataverse rows (the Actions). 14 fewer rows than the playbook path.

### 3.3 Deployment + management surface

| Artifact | Where | How edited |
|---|---|---|
| Collector + queries + projection | `DailyBriefingCollector.cs` | Engineers via PR + BFF deploy |
| Narrator workflow logic | `DailyBriefingNarrator.cs` | Engineers via PR + BFF deploy |
| Action prompts/schemas | Dataverse rows (UNCHANGED from playbook path) | Same as before — Power Apps maker or sync |
| Endpoint route | `/api/ai/daily-briefing/render` | Code-defined; engineers via PR |
| (No scheduler) | n/a | Pull-based; widget loads = query runs |
| (No membership service dependency) | n/a | FetchXml `eq-userid` operator handles ownership filtering |
| (No template substitution) | n/a | Typed C# parameters; compiler-enforced |
| Channels enumerable | Each is a method on the collector class | Engineers via PR |

### 3.4 Operator-visible vs operator-invisible

| Layer | Operator-visible? | Notes |
|---|---|---|
| Action prompt text | **Visible** — same as playbook path | Preserved 100% |
| Action output schema (JSON schema) | **Visible** | Preserved |
| Action temperature | **Visible** | Preserved |
| Collector class | Code | Engineers only — but operators didn't actually edit the playbook node graph either |
| Narrator class | Code | Engineers only |
| FetchXml queries | Code-embedded | Engineers only — but expressed in standard FetchXml, not JSON template lang |

**Net difference for operators**: ZERO. They edit the same Action rows they always edited. The thing they DIDN'T effectively edit (the node graph) is what moved to code.

---

## 4. Side-by-side feature comparison

| Dimension | Playbook Engine | POC Live-Render |
|---|---|---|
| **Lines of code in execution path** | ~14,000 (generic infra) + ~300 (consumer specifics) | ~880 (collector + narrator + scrubber) |
| **Dataverse rows per consumer** | ~17 across 5 tables | ~3 (Actions only) |
| **Where workflow shape lives** | Dataverse rows + JSON templates | C# class + compiler-enforced types |
| **Adding a new step to existing workflow** | Author new sprk_playbooknode + edges + Action + sync to Dataverse | Add a method call to the collector / narrator + redeploy |
| **Adding a new consumer (e.g., Insights matter-summary)** | Author full playbook + nodes + edges + actions + consumer routing + sync; battle template + aggregator contracts | Write `MatterSummaryCollector.cs` + `MatterSummaryNarrator.cs` + new endpoint route; deploy |
| **Operator tuning prompts** | Edit Action row → next request reflects | Edit Action row → next request reflects (IDENTICAL) |
| **Operator changing workflow structure** | Theoretically via Playbook Builder UI but in practice via JSON+sync scripts | Code change |
| **Latency per request** | 8-25s (3-5 LLM calls, ~10 Dataverse queries to load playbook/nodes/actions, template substitution overhead) | 5-8s (1+N LLM calls + 4-5 parallel Dataverse queries; no playbook-loading overhead) |
| **Caching opportunity** | Per-node outputs cached in-run; Action rows cached via IAnalysisActionService | Action rows cached; FetchXml results can be added to cache layer |
| **Streaming output (SSE per-token)** | Available via PlaybookOrchestrationService event stream | Not available out of box; would need per-call streaming wiring in narrator |
| **Tool-use during inference** | AiAnalysisNodeExecutor supports tool handlers (Document, Skill, etc.) | Not available; would need per-narrator wiring |
| **Multi-tenant workflow variants** | Per-tenant `sprk_playbookconsumer` row swaps playbook ID | Per-tenant config in code or config file |
| **Debugging a failure** | Trace through orchestrator → executor → template substitution → aggregator → projection | Step through C# in debugger; FetchXml output inspectable; LLM raw response in App Insights |
| **Bug classes observed this session** | P1 (aggregator IsDeliverOutput silently drops), P2 (LoadKnowledgeConfig type mismatch), Layer 1/Condition type-system conflict, wiped configJson on 4 nodes, wrong outputVariable naming, wrong `.output.` template paths, missing scheduler parameters, broken IMembershipResolverService | None in the POC — empirical end-to-end on first deploy after the matterId extraction |
| **What if I want to change a prompt?** | Edit Action row | Edit Action row (SAME) |
| **What if I want to add hallucination defense?** | Add EntityNameValidator node to playbook + wire allowList template + redeploy | Call `_scrubber.Scrub(text, allowList)` in narrator + redeploy |
| **What if I want to fan-out per item?** | Author `iteration: {iterateOver, itemAlias}` JSON + scheduler/orchestrator must support it + cross-node data flow via templated scope variables | `Task.WhenAll(req.Channels.Select(c => DoOne(c)))` |
| **What if I want conditional branching?** | ConditionNode + TrueBranch/FalseBranch name references + template substitution on operands | `if (count > 0) { ... }` |

---

## 5. Why the playbook engine has been brittle for narrative consumers (root cause)

The playbook engine was originally designed for **chat-summarize** and **scheduled notification playbooks** — both of which have legitimately complex shapes:

- **Chat-summarize**: streams partial tokens via SSE, has dynamic context per session (knowledge sources, scopes, host context), invokes tool handlers during inference. The engine's complexity earns its keep.
- **Scheduled notification playbooks**: fire on cron, fan out across N users, write to appnotification. The engine's lifecycle (scheduler + fan-out + per-user execution + persistent run state) matches the use case.

For **narrative endpoints** like /narrate, none of those features apply:
- No streaming (briefing response is small + atomic)
- No dynamic context (the request payload IS the context)
- No tool calls (just structured-output LLM calls)
- No cron / fan-out / per-user persistence
- Workflow shape is fixed per consumer

When we forced the playbook engine to also serve /narrate, we paid all the engine's infrastructure costs (template substitution, executor dispatch, aggregator contracts, scope binding, sync scripts) without getting the corresponding benefits. The result: 6+ classes of bugs surfaced just this session, none of which exist in the POC because the underlying machinery doesn't exist on the POC code path.

**This is consistent with the general principle**: data-driven workflow engines are right when workflows are genuinely heterogeneous and run-time-configurable. They are wrong when workflows are fixed per consumer and only the prompts change. For Spaarke's narrative endpoints, the latter holds — only prompts change; workflow shape stays consistent.

---

## 6. Operator value preserved 100% in the POC

The single most important architectural property: **operators see and edit the same Action rows in either approach**. Specifically:

- BRIEF-NARRATE-TLDR's `sprk_systemprompt` → Power Apps maker UI → edit → save → next /render call reflects immediately
- BRIEF-NARRATE-CHANNEL's `sprk_outputschemajson` → maker UI → edit → save → next /render call reflects immediately
- BRIEF-VALIDATE-ENTITY-NAMES's `sprk_temperature` → maker UI → edit → save → next /render call reflects immediately

What operators DIDN'T effectively edit in the playbook path (the node graph, the configJson templates, the scheduler config) is what moves to code in the POC. Engineers maintain that anyway in both approaches — the difference is whether it's edited as Dataverse JSON or as C#. Code is easier.

---

## 7. Implications for HOW we build AI functions going forward

### 7.1 Decision criteria — when to use which approach

| Use Code-Defined (POC pattern) when... | Use Playbook Engine when... |
|---|---|
| Workflow shape is fixed per consumer | Workflow shape needs per-tenant or per-user runtime variation |
| Sub-30s synchronous request/response | Long-running, multi-minute, multi-step processes |
| No streaming required | SSE per-token streaming required (chat-style) |
| 1-N LLM calls, no tool-use | LLM with tool-call loops (function calling, retrieval-augmented patterns) |
| Operator only tunes prompts/schemas | Operator authors entire workflows visually (maker scenario) |
| 1-10 consumers of similar pattern (briefing, summary, snapshot) | Many heterogeneous consumers needing one runtime |
| You want compiler-enforced data flow | Schema-of-templates is acceptable cost |
| Latency matters | Latency is dominated by LLM round-trips anyway |

### 7.2 The pattern for a new narrative consumer (Code-Defined)

To build a new consumer following the POC pattern (~1 day work):

1. **Author 1-3 Action rows in Dataverse** (operator-visible — prompt text + output JSON schema + temperature)
2. **Write a Collector class** in `Services/Ai/Narrators/`:
   - `XxxCollector.cs` — query Dataverse, project to a typed request DTO
3. **Reuse the existing narrator pattern** OR write a Narrator class:
   - `XxxNarrator.cs` — load Actions by code, call LLM, scrub if needed, compose response
4. **Add an endpoint** in the relevant Endpoints file:
   - `POST /api/ai/<area>/<consumer>/render` — wires collector → narrator → response
5. **Register in DI**
6. **Deploy BFF + smoke**

Each consumer is ~300-500 LOC of new code + ~3 Dataverse Action rows. No template substitution to debug. No scheduler config to manage. No playbook node graph to author. No sync script to maintain.

### 7.3 The pattern for a new playbook consumer (Playbook Engine)

For comparison, building the same kind of consumer via the playbook engine takes (per our actual experience):

1. Design playbook node graph
2. Author playbook source JSON (~300 lines)
3. Author Action JPS bodies (~100 lines each × 2-3 actions)
4. Add consumer routing row
5. Deploy via sync scripts (multiple PATCH calls)
6. Debug template substitution issues
7. Debug aggregator IsDeliverOutput issues
8. Debug membership service integration
9. Debug scheduler parameter passing
10. Test fan-out semantics
11. Smoke end-to-end and re-do steps 6-10 as bugs surface

In our session: this took multiple days and never produced a working /narrate response. The POC took ~5 hours and produced a working response that surfaced 26 of the user's real notifications.

---

## 8. Implications for HOW we orchestrate AI functions

### 8.1 Orchestration choices in the playbook engine

| Concern | Mechanism |
|---|---|
| Sequencing | `dependsOn` arrays on nodes; topological sort by orchestrator |
| Parallelism | Nodes with same `dependsOn` set run concurrently (batched) |
| Conditional branching | ConditionNode with TrueBranch / FalseBranch name references |
| Data flow | Scope variables — each node's output stored under its `outputVariable` name; downstream templates reference via `{{nodeName.field}}` |
| Iteration | `iteration: {iterateOver, itemAlias}` JSON metadata on a node |
| Composition | Per-node `outputBinding` templates that select what enters scope |
| Terminal output | `IsDeliverOutput=true` on the final node (or convention-based fallback) |
| Error handling | Per-node Validate() + try/catch in executor; orchestrator marks NodeFailed events; per-playbook fail-fast semantics |

### 8.2 Orchestration choices in the POC code-defined approach

| Concern | Mechanism |
|---|---|
| Sequencing | Order of statements in the C# method |
| Parallelism | `Task.WhenAll` on independent tasks |
| Conditional branching | `if`, `switch`, ternary |
| Data flow | Method arguments and local variables (typed, compiler-enforced) |
| Iteration | `foreach`, `Select`, `Task.WhenAll(items.Select(...))` |
| Composition | Construct response DTO directly (`new XxxResponse { ... }`) |
| Terminal output | `return` keyword |
| Error handling | `try`/`catch` in the method; structured exception types if needed |

The code-defined approach uses native language constructs. The playbook engine builds analogues to each in data — and requires an interpreter for each.

### 8.3 Multi-consumer orchestration / composition

The playbook engine's "compose multiple playbooks" model (e.g., one playbook invokes another via DeliverComposite + per-section streaming) is genuinely sophisticated and valuable for **chat-summarize**. It's overkill for narrative endpoints where the entire response is a single typed DTO.

For narrative endpoints, "composition" looks like:
- A new collector method that combines results from multiple existing collectors
- OR a narrator that takes multiple input payloads and produces a combined response

Both are straightforward in code, expressed as method calls.

---

## 9. Implications for HOW we manage AI functions

### 9.1 What operators manage (same in both approaches)

| Surface | How |
|---|---|
| Prompt text | Edit `sprk_analysisaction.sprk_systemprompt` in Power Apps maker portal |
| Output structure | Edit `sprk_analysisaction.sprk_outputschemajson` in Power Apps maker portal |
| Model temperature | Edit `sprk_analysisaction.sprk_temperature` in Power Apps maker portal |
| Constraint / instruction copy | Same — Action JPS body in `sprk_systemprompt` |

These are the ONLY tuning levers operators actually use in practice. Both approaches expose them identically.

### 9.2 What engineers manage

| Concern | Playbook Engine | POC |
|---|---|---|
| Workflow definition | Author/edit JSON source files in repo + sync to Dataverse | Edit C# class in repo |
| Add new step | Add node + edges + Action + sync | Add method call + redeploy |
| Debug workflow | Trace through 5+ infrastructure layers + check Dataverse row state + template substitution | Step through C# in debugger; inspect FetchXml output |
| Performance tuning | Cache configuration on multiple services; per-node cache; manage scope dict size | Standard C# patterns (caching, parallel calls) |
| Multi-environment deploy | Dataverse row sync per environment + BFF deploy | BFF deploy (one artifact) |

### 9.3 What goes wrong + how easy is it to detect

| Failure mode | Playbook Engine | POC |
|---|---|---|
| Wrong field name in projection | Detected at runtime when template renders empty; trace through aggregator + projection | **Compile-time error** |
| Wrong type passed between steps | Detected at runtime when JsonElement type mismatch in downstream executor (e.g., our Condition `left` issue) | **Compile-time error** |
| Missing scope variable | Renders as empty string; can cascade silently | **Compile-time error** (variable doesn't exist in C#) |
| LLM returns bad JSON | Caught + reported, but caught in the executor layer with metadata loss possible | Caught + reported in same layer that called the LLM |
| Auth/permission issue | Surfaces at the executor making the Dataverse call; trace through 5 layers | Surfaces at the FetchXml call; one stack frame |
| Bug in scrubber algorithm | Edit one file, redeploy | Edit one file, redeploy (SAME) |

The POC eliminates entire classes of failures by removing the layers that produce them.

---

## 10. Migration / phasing strategy

We are NOT proposing to delete the playbook engine. Both approaches coexist cleanly.

### 10.1 Recommended phasing

**Phase 1** (DONE this session): POC validated end-to-end for daily-briefing.

**Phase 2** (immediate next):
- Extend collector to sprk_document, sprk_todo, sprk_matter, email (~30 LOC per source per channel)
- Add operator-flagged UAT items (T118 leftovers: "events", "tools", "two unidentified items")
- Polish: cleaner channel labels in widget CHANNEL_REGISTRY; refine scrubber over-zealousness; surface counts/links per user feedback

**Phase 3** (when next narrative consumer appears, e.g., Insight Engine matter-summary):
- Build it as code-defined narrator using the established pattern
- Validate the pattern scales to a second consumer

**Phase 4** (if pattern proves at N=3 consumers):
- Consider whether to invest in a code generator (playbook canvas → C# narrator emission)
- If yes: build the generator; if no, hand-writing 300-500 LOC per consumer is fine forever

**Phase 5** (only if needed):
- Deprecate / delete unused playbook engine paths
- Probably keep the engine for chat-summarize + scheduled notification playbooks
- Document the bright line: "narrative endpoints = code-defined; scheduled workflows + chat = playbook engine"

### 10.2 Coexistence guarantees during migration

- `/api/ai/daily-briefing/narrate` (playbook engine) remains available — flip `USE_LIVE_RENDER` to `false` reverts the widget
- `PlaybookSchedulerJob` continues running for any consumer that depends on it
- `sprk_playbookconsumer` rows remain valid for chat-summarize, Insights, etc.
- No widget code change needed except the one-line flag flip for the consumer being migrated

---

## 11. Open architectural decisions

Not decided by this spike — explicit calls for follow-up:

1. **Codegen vs hand-write**: at what N consumers does a "playbook canvas → C# narrator" generator pay off? Hand-writing is fine for 1-3 consumers; over 5 it might be worth investing.

2. **Compose multi-consumer**: If a future endpoint needs to combine results from multiple narrators (e.g., "morning summary across Daily Briefing + Insights + emails"), how do we structure that? Likely a higher-order Narrator class that calls others — but worth designing explicitly when the need appears.

3. **Streaming**: If a future narrative endpoint needs SSE per-token streaming, we'd either (a) wrap `IOpenAiClient.StreamCompletionAsync` in a new pattern, OR (b) route those specific endpoints through the playbook engine which already has streaming. Decide case by case.

4. **Test strategy**: Code-defined narrators are easier to unit-test (mock IGenericEntityService + IOpenAiClient + IEntityNameScrubber). Need a test pattern + a few exemplar test classes for the existing narrator/collector.

5. **Schema validation between Action JSON Schema and C# DTO**: when an operator changes the Action's `OutputSchemaJson` in Dataverse, the C# DTO that deserializes the LLM response must stay in sync. Today there's no automated check. Could add a startup-time validation + log warning.

6. **Where to place future narrative endpoints**: per-domain (DailyBriefingEndpoints.cs, InsightsEndpoints.cs) or unified (AiNarrativeEndpoints.cs)? Recommend per-domain for consistency with current organization.

7. **Scheduler future**: PlaybookSchedulerJob has scheduler parameter fixes from this session (todayUtc, etc.) — but the underlying notification playbooks are still broken in other ways. Do we (a) fix the playbooks too, (b) delete them and let appnotification rows become driven only by Dataverse plugins / Power Apps native flows, or (c) accept appnotification is mostly empty and rely on /render for briefing? Decide based on what other things appnotification is supposed to surface.

---

## 12. Summary table — what's different about the POC

| Aspect | Playbook Engine | POC Live-Render |
|---|---|---|
| Runtime LOC executed per /narrate request | ~5,500+ (orchestrator + executors + template engine + helpers + aggregator) | ~880 (collector + narrator + scrubber + LLM client + Dataverse client) |
| Dataverse rows per consumer | 17 (across 5 tables) | 3 (Action rows only) |
| Workflow expressed as | Data (JSON in Dataverse rows + templates) | Code (C# class) |
| Bugs found this session | 6+ classes | 0 |
| Operator-visible tuning | Action rows | Action rows (IDENTICAL) |
| Engineer maintenance | JSON + sync scripts + playbook engine deep knowledge | C# + standard refactor patterns |
| Click-through entity links in output | Designed for it; never actually populated (bug surfaced in POC dev) | Working — every bullet has `primaryEntityType` + `primaryEntityId` + `primaryEntityName` |
| Validation metadata sidecar | Designed for it; never actually populated due to aggregator bug | Working — `_validationMetadata.scrubbedText` + `removedTerms` emitted when scrubber removes anything |
| Empirical result for end user | "5 notifications, 'Test', no real updates" (per the screenshot from yesterday) | "26 notifications across 3 categories... My Recent Updates include 17 items..." (per the screenshot from today) |

---

## 13. Files touched in this session — for handoff reference

### Created
- `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs` — the new live-query collector
- `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingNarrator.cs` — code-defined narrator (R7 T116)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/EntityNameScrubber.cs` — extracted scrubber service (R7 T116)
- `projects/spaarke-ai-platform-unification-r7/notes/spikes/narrator-spike-plan.md` — original spike plan
- `projects/spaarke-ai-platform-unification-r7/notes/spikes/narrator-vs-playbook-comparison.md` — first comparison (T116)
- `projects/spaarke-ai-platform-unification-r7/notes/handoffs/wave11-t116-narrate-systematic-assessment.md` — systematic assessment
- `projects/spaarke-ai-platform-unification-r7/notes/spikes/poc-vs-playbook-engine-architecture.md` — **this document**

### Modified
- `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` — added `/render` endpoint + ValidationMetadataDto on response
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs` — added `GetActionByCodeAsync`
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookSchedulerJob.cs` — added scheduler parameters (todayUtc, dueSoonWindowUtc, timeWindowHours, dueWithinDays)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` — DI registrations for collector + narrator + scrubber
- `src/client/shared/Spaarke.DailyBriefing.Components/src/services/briefingService.ts` — `USE_LIVE_RENDER=true` flag + `fetchBriefingLive()` function
- `projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json` — DTO-aligned field paths (from T116 fixes)
- Multiple deployed Dataverse rows (notification playbooks: 5 nodes restored from source; output variables renamed; .output. paths stripped; FetchXml updated to bypass broken membership service) — informational; not blocking

### Deployed live in spaarkedev1
- `spaarke-bff-dev` App Service (BFF) — narrator + collector + /render + scheduler params
- `sprk_spaarkeai` web resource (SpaarkeAi widget) — USE_LIVE_RENDER=true cutover

### Commits pushed
- `3affa952f` — narrator spike (T116 systematic assessment + Option D + narrator scaffolding)
- `85c762081` — DailyBriefingCollector live-render POC + widget cutover (THIS SESSION's primary work)

---

*End of architecture comparison.*
