# AI Architecture — Playbook Consumer Routing & Path A.5

> **Last reviewed**: 2026-06-28
> **Renamed from**: `ai-architecture-consumer-routing.md` (2026-06-28)
> **Authored by**: canonical-truth loop step 3 (spaarke-daily-update-service-r4); augmented by chat-routing-redesign-r1 (2026-06-28) with origin context, Triangle diagram, runtime code example, consumer inventory, and Action Engine relationship.
> **Status**: Canonical. Lifts the `sprk_playbookconsumer` / `IConsumerRoutingService` / `IInvokePlaybookAi` triangle from R4 `notes/decisions/030-dispatch-path.md` and chat-routing-redesign-r1 `architecture/stateful-chat-architecture.md` into a first-class architecture doc.
> **Scope**: How a consumer surface (widget, endpoint, service, agent) resolves a playbook by *consumer type* + environment + optional code, and dispatches it via the non-streaming facade. Includes the decision matrix for Path A / Path A.5 / Path B, the R4 `/narrate` case study, and the Action-Engine-vs-consumer-routing relationship.
> **NOT in scope**: Runtime mode-detection inside the orchestrator (see `ai-architecture-playbook-runtime.md` §3), JPS authoring (see `ai-guide-jps-authoring.md`), `sprk_playbookconsumer` schema details (see [`docs/data-model/sprk-playbookconsumer.md`](../data-model/sprk-playbookconsumer.md)).

---

## 1. The Triangle

`sprk_playbookconsumer` is the data side of a three-component architecture pattern. Each component owns one concern; together they form **Path A.5** — the canonical bridge between a CRUD-side caller that needs to dispatch a playbook by name and the streaming, node-graph-aware orchestrator beneath.

| Surface | Owns |
|---|---|
| **`sprk_playbookconsumer`** (Dataverse entity) | A row per (consumer surface, environment, optional code) → playbook FK. Data-driven so a redirect changes Dataverse only, not BFF code. |
| **`IConsumerRoutingService`** ([`Services/Ai/PublicContracts/ConsumerRoutingService.cs:53`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerRoutingService.cs)) | Looks up the row, picks the best match for the calling context, caches 5 min. |
| **`IInvokePlaybookAi`** ([`Services/Ai/PublicContracts/InvokePlaybookAi.cs:42`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/InvokePlaybookAi.cs)) | Non-streaming facade — calls `PlaybookOrchestrationService.ExecuteAsync` (Path B), aggregates SSE stream into `PlaybookInvocationResult`, returns to caller. |

### The Triangle — visual

```
┌──────────────────────────────────────────────────────────────────┐
│                          The Path A.5 Triangle                    │
├──────────────────────────────────────────────────────────────────┤
│                                                                   │
│   ┌─────────────────────────┐                                    │
│   │ sprk_playbookconsumer   │ ← Dataverse routing table           │
│   │   (the DATA)            │   "consumer X uses playbook Y       │
│   └────────────┬────────────┘    in env Z, priority N"            │
│                │                                                  │
│                ▼                                                  │
│   ┌─────────────────────────┐                                    │
│   │ IConsumerRoutingService │ ← BFF facade (ADR-013 boundary)     │
│   │   ResolveAsync(...)     │   "give me the playbook GUID for    │
│   │   (the LOOKUP)          │    'matter-pre-fill' in dev"        │
│   └────────────┬────────────┘                                    │
│                │                                                  │
│                ▼                                                  │
│   ┌─────────────────────────┐                                    │
│   │ IInvokePlaybookAi       │ ← Non-streaming dispatch            │
│   │   InvokePlaybookAsync() │   "execute that playbook with       │
│   │   (the EXECUTION)       │    these parameters; no SSE"        │
│   └─────────────────────────┘                                    │
│                                                                   │
└──────────────────────────────────────────────────────────────────┘

   Data → Lookup → Execution.
   Each layer is independent. Each is a facade (ADR-013).
```

---

## 2. Why this was created

Three converging drivers, recorded here so future readers understand the cost-of-no-routing-table answer:

### 2.1 The 2026-06-24 UAT-2 typo incident

Pre-Phase-1R, the matter pre-fill surface read its playbook GUID from an environment variable named `Workspace__MatterPreFillPlaybookId`. On 2026-06-24, a Power Apps form deploy set this value with `matter-pre-fil` (missing the final `l`). The env-var lookup returned `null`; matter pre-fill silently fell through to a degraded code path and shipped broken to UAT. Free-text identifiers without compile-time validation are a class of bug we need to engineer out.

### 2.2 chat-routing-redesign-r1 Phase 1R mandate

FR-1R-02 / FR-1R-03 / FR-1R-04 of `spaarke-ai-platform-chat-routing-redesign-r1` mandated retirement of the entire `Workspace__*PlaybookId` env-var pattern. Cost of the old pattern per consumer:

- An env var set in App Service configuration (and forgotten on env clone)
- A BFF code change to read it (typed-options class plus indexer plus null-check)
- A coordinated config-change + BFF redeploy for ANY redirect (re-routing a consumer to a different playbook required two-system change control)

Cost of the new pattern per consumer: **change a Dataverse row.** No BFF deploy. No env-var change. Maker-managed.

### 2.3 ADR-013 facade boundary

The routing decision is AI infrastructure, but CRUD-side callers (matter pre-fill, project pre-fill, daily briefing, etc.) shouldn't depend on AI-internal types. The `IConsumerRoutingService` interface lives in `Services/Ai/PublicContracts/` — the canonical AI facade — so CRUD-side callers inject one stable interface and get back a `Guid`. No leakage of `IPlaybookService` / `IOpenAiClient` / Dataverse-internal types into CRUD code. This is the same principle ADR-013 enforces for `IInvokePlaybookAi` itself.

---

## 3. `sprk_playbookconsumer` contract

The entity carries the routing table. Full schema reference: [`docs/data-model/sprk-playbookconsumer.md`](../data-model/sprk-playbookconsumer.md). Summary:

| Column | Type | Meaning |
|---|---|---|
| `sprk_consumertype` | String (see `ConsumerTypes.cs`) | The compile-time identifier of the calling surface. R4 example: `"daily-briefing-narrate"`. |
| `sprk_consumercode` | String | Optional per-instance discriminator (e.g., language code, tenant code, area-specific variant). Defaults to `"default"`. |
| `sprk_enabled` | Bool | If false, row is ignored at resolve time. Soft-disable without deletion. |
| `sprk_playbookid` | Lookup → `sprk_analysisplaybook` | The target playbook FK. |
| `sprk_environment` | Choice/String | Optional environment scope (`dev`, `staging`, `prod`, or `*` wildcard). |
| `sprk_priority` | Int | Tiebreaker when multiple rows match. **Lowest wins** (consistent with the chat-routing-redesign-r1 precedent). |
| `sprk_matchconditionsjson` | Memo | Optional JSON predicates evaluated by `SelectBestMatch`. Used when context carries arbitrary keys (e.g., MIME type, document classification). |

---

## 4. Resolution algorithm + compile-time safety

### 4.1 The resolution algorithm

`ConsumerRoutingService.ResolveAsync(consumerType, code, ctx, env, ct)`:

1. **Query Dataverse**: `sprk_playbookconsumer` filtered by `sprk_consumertype = X` AND `sprk_enabled = true`
2. **In-memory `SelectBestMatch`**: filter by `(consumerCode, environment, matchConditions)`
   - `consumerCode` — exact match preferred, falls back to `"default"`
   - `environment` — exact match wins over `*` wildcard wins over null
   - `matchConditions` — JSON predicates compared against `IRoutingContext`
3. **Sort by `sprk_priority` ascending** — lowest wins. Return head.
4. **Return** the resolved `sprk_analysisplaybook` system PK GUID, or `null` if no row matches
5. **Cache** the result for 5 minutes via `IMemoryCache` (ADR-014)

### 4.2 Compile-time safety on the BFF side

The `sprk_consumertype` column is **free-text on Dataverse** (NVARCHAR(250)) — a maker editing the form in Power Apps can type anything. The 2026-06-24 UAT-2 typo (`matter-pre-fil`) demonstrated this is dangerous.

Defense: [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs) declares every legitimate consumer type as a `public const string`. BFF code MUST reference the constants:

```csharp
// ✅ CORRECT — compiler enforces typo-free
var playbookId = await routing.ResolveAsync(
    ConsumerTypes.MatterPreFill,
    consumerCode: "default",
    context: null,
    environment: null,
    ct);

// ❌ WRONG — literal string, no compiler check
var playbookId = await routing.ResolveAsync(
    "matter-pre-fil",   // typo — silent fail at runtime
    "default", null, null, ct);
```

### 4.3 The Dataverse-side typo class

The compile-time check on the BFF side does NOT prevent a maker from creating a `sprk_playbookconsumer` row with a misspelled `sprk_consumertype`. Future enhancement (chat-routing-redesign-r1 task 028e, Phase 1R exit gate): a **startup health-log diff** that compares Dataverse-side `sprk_consumertype` values against `ConsumerTypes.All` and warns on mismatch. Until that lands, makers must follow the 3-step recipe in [`docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md`](../guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md).

### 4.4 Adding a new consumer type — the recipe

1. **Add a `public const string`** to `ConsumerTypes.cs` (and append to `ConsumerTypes.All`)
2. **Create a `sprk_playbookconsumer` row** in Dataverse with `sprk_consumertype = {new constant value}` (or extend `scripts/dataverse/Seed-PlaybookConsumers.ps1`)
3. **Update the calling surface** to inject `IConsumerRoutingService` + `IInvokePlaybookAi` and resolve → invoke

No new endpoint registration. No new orchestrator code. No new DI module. ADR-013 facade principle realised.

---

## 5. How a typical resolution looks at runtime

End-to-end code skeleton, showing the typical CRUD-side caller pattern:

```csharp
// Endpoint or service that needs to dispatch a playbook
public sealed class MyService
{
    private readonly IConsumerRoutingService _routing;
    private readonly IInvokePlaybookAi _invokePlaybook;
    private readonly ILogger<MyService> _logger;

    public MyService(
        IConsumerRoutingService routing,
        IInvokePlaybookAi invokePlaybook,
        ILogger<MyService> logger)
    {
        _routing = routing;
        _invokePlaybook = invokePlaybook;
        _logger = logger;
    }

    public async Task<MyResult> DispatchAsync(
        MyRequest request,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // STEP 1 — Resolve the playbook GUID via consumer routing.
        //          The compile-time constant ConsumerTypes.X is the safe form.
        //          environment=null reads from IHostEnvironment.EnvironmentName.
        var playbookId = await _routing.ResolveAsync(
            consumerType: ConsumerTypes.MyConsumer,
            consumerCode: "default",
            context: null,                  // or RoutingContext with MimeType/DocumentType
            environment: null,              // implicit from IHostEnvironment
            cancellationToken: ct);

        // STEP 2 — Handle no-match. Caller's choice of fallback semantics.
        if (playbookId is null)
        {
            _logger.LogWarning(
                "No sprk_playbookconsumer row matched for {ConsumerType}.",
                ConsumerTypes.MyConsumer);
            return MyResult.ServiceUnavailable("Dispatch is unconfigured.");
        }

        // STEP 3 — Build parameters as IReadOnlyDictionary<string, string>.
        //          The playbook's Start node binds {{json start}} / {{start.*}}.
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["payload"] = JsonSerializer.Serialize(request),
            ["userId"] = httpContext.User.GetUserId(),
        };

        // STEP 4 — Build invocation context.
        var tenantId =
            httpContext.User?.FindFirst("tid")?.Value
            ?? httpContext.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? string.Empty;

        var invocationContext = new PlaybookInvocationContext
        {
            TenantId = tenantId,
            HttpContext = httpContext,
            CorrelationId = httpContext.TraceIdentifier
        };

        // STEP 5 — Invoke via the non-streaming facade.
        //          Returns one PlaybookInvocationResult (success-with-output OR failure).
        //          Never deals with SSE.
        var playbookResult = await _invokePlaybook.InvokePlaybookAsync(
            playbookId.Value,
            parameters,
            invocationContext,
            ct);

        // STEP 6 — Translate to the caller's response shape.
        if (!playbookResult.Success)
        {
            return MyResult.AiUnavailable(playbookResult.ErrorMessage);
        }

        return ProjectPlaybookResultToMyResult(playbookResult, request);
    }
}
```

The complete reference implementation in production is [`DailyBriefingEndpoints.cs:201-374`](../../src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs) (R4 task-031). It's the canonical pattern — every new consumer should follow it.

---

## 6. Who's wired to consumer routing today

Source: [`ConsumerTypes.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/ConsumerTypes.cs). Seven consumer types are defined as of 2026-06-28; each is exercised by exactly one BFF surface:

| Consumer Type Constant | Dataverse `sprk_consumertype` | Surface | Purpose | Origin |
|---|---|---|---|---|
| `ConsumerTypes.MatterPreFill` | `matter-pre-fill` | `MatterPreFillService` | Pre-fills new Matter form from uploaded docs (NFR-07 preserved) | chat-routing-redesign-r1 Phase 1R |
| `ConsumerTypes.ProjectPreFill` | `project-pre-fill` | `ProjectPreFillService` | Pre-fills new Project form from uploaded docs | chat-routing-redesign-r1 Phase 1R |
| `ConsumerTypes.AiSummary` | `ai-summary` | `WorkspaceAiService` | Generates workspace tile AI summary (Document Profile playbook) | chat-routing-redesign-r1 Phase 1R |
| `ConsumerTypes.SummarizeFile` | `summarize-file` | `WorkspaceFileEndpoints` | File summarization behind the Workspace summarize button | chat-routing-redesign-r1 Phase 1R |
| `ConsumerTypes.ChatSummarize` | `chat-summarize` | `SessionSummarizeOrchestrator` | **The canonical chat `/summarize` flow** — what end users see when they type `/summarize` or "summarize this" | chat-routing-redesign-r1 Phase 1R |
| `ConsumerTypes.EmailAnalysis` | `email-analysis` | `AppOnlyAnalysisService` | Email analysis pipeline (Path C app-only execution context — see §8) | chat-routing-redesign-r1 Phase 1R |
| `ConsumerTypes.DailyBriefingNarrate` | `daily-briefing-narrate` | `DailyBriefingEndpoints.HandleNarrate` | Daily-briefing narration dispatch | spaarke-daily-update-service-r4 FR-12 (task-031) |

Adding a consumer is configuration + a constant. Removing one is the same in reverse.

---

## 7. `IInvokePlaybookAi.InvokePlaybookAsync` — non-streaming semantics

The facade at [`Services/Ai/PublicContracts/InvokePlaybookAi.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/InvokePlaybookAi.cs) aggregates a streaming `IAsyncEnumerable<PlaybookStreamEvent>` into a single `PlaybookInvocationResult`. Key behaviour:

1. **No document context** — at line 68-73 the facade constructs `PlaybookRunRequest { DocumentIds = Array.Empty<Guid>(), Parameters = parameters }`. Empty DocumentIds is the **intended** path for invoke-by-payload callers (per `InvokePlaybookAi.cs:64-67` comment).
2. **Always Path B** — line 86: `_orchestrator.ExecuteAsync(request, context.HttpContext, ct)` (the OBO node-based orchestrator). Path C (app-only) is not facade-accessible.
3. **Failure → 503** — on `PlaybookEventType.RunFailed` the facade sets `ErrorCode = "PLAYBOOK_INVOCATION_FAILED"`; the caller endpoint translates to a 503 ProblemDetails (canonical pattern at `DailyBriefingEndpoints.cs:309-321`).
4. **Empty-payload tolerance is conditional** — `IInvokePlaybookAi` handles empty payload correctly **only if the resolved playbook is NodeBased** (has `sprk_playbooknode` rows). If it falls through to Legacy mode (Path A inside the legacy orchestrator), the pre-2026-06-26 IOORE risk applied; the R4 hotfix at `AnalysisOrchestrationService.cs:720-728` closed it (now a clean error chunk). See `ai-architecture-playbook-runtime.md` §7.

The facade's contract is therefore: **"call me with parameters, no documents, expect either success-with-output or a single failure result. Never deal with SSE."**

---

## 8. Path A / A.5 / B decision matrix

After R4, three dispatch shapes coexist. Use this matrix when designing a new playbook-consuming surface.

| Path | Use when | Don't use when | Reference |
|---|---|---|---|
| **Path A** (legacy doc-bound) | Caller has 1+ document GUIDs and needs streaming SSE back to a client | Caller has no document (use A.5); caller doesn't need streaming (use A.5) | `AnalysisOrchestrationService.ExecutePlaybookAsync:702` |
| **Path A.5** (`IConsumerRoutingService` + `IInvokePlaybookAi`) | (a) Caller has NO document; (b) caller has a `parameters` blob; (c) caller wants non-streaming response; (d) the playbook GUID should be Dataverse-data-driven not hardcoded; (e) result fits in a single JSON response (no progressive UI) | Caller needs streaming SSE (use A or B); caller has a document AND needs document semantics in the legacy orchestrator (use A) | `DailyBriefingEndpoints.cs:201-374` (R4 canonical case study) |
| **Path B** (direct `PlaybookOrchestrationService.ExecuteAsync`) | (a) Caller has the playbook GUID already (no consumer-routing lookup needed); (b) caller needs streaming SSE; (c) caller may or may not have documents | Caller wants Dataverse-driven playbook selection (use A.5); caller doesn't need streaming (use A.5 for simpler return shape) | `PlaybookOrchestrationService.ExecuteAsync:81` |

Path C (`ExecuteAppOnlyAsync`) is the app-only sibling of Path B, used by `PlaybookSchedulerJob` for notification fan-out. It is not facade-accessible and not a runtime caller-choice — selection is structural (background services vs request pipeline).

---

## 9. The R4 case study — `/narrate` dispatch wiring

R4's task 031 (commit `88dd66a1c`) wires `POST /api/ai/daily-briefing/narrate` end-to-end as the canonical Path A.5 reference. Sequence:

1. **Endpoint receives** `BriefingNarrateRequest { briefingPayload, userId, ... }` (no DocumentIds in the contract).
2. **Resolve playbook** — `_consumerRouting.ResolveAsync(ConsumerTypes.DailyBriefingNarrate, code: null, ctx: …, env: currentEnvironment, ct)` at `DailyBriefingEndpoints.cs:250`. Cache-hit returns the playbook Guid in <1 ms.
3. **Build parameters** — convert `briefingPayload` (already-formed JSON object) + scalars (`userId`, `today`, `recencyHours`) into a `Dictionary<string, object>`.
4. **Invoke facade** — `_invokePlaybook.InvokePlaybookAsync(playbookId, parameters, context, ct)` at `:303`.
5. **Translate result** — on success, return `Results.Ok(...)`; on `RunFailed`, return `Results.Problem(503)` per `:309-321`.

This is the entire wiring. NO new orchestrator code, NO new DI module beyond the ConsumerRouting registration, NO new endpoint filter complexity — the existing `IConsumerRoutingService` + `IInvokePlaybookAi` facades carry the load. ADR-013's "thin facade over orchestrator" principle is realised.

The playbook itself (`DAILY-BRIEFING-NARRATE`) is the R4 W0/W1 deployment target. As long as it has `sprk_playbooknode` rows, empty `DocumentIds` is tolerated (see `ai-architecture-playbook-runtime.md` §7). If it does NOT have nodes, Legacy mode fires; pre-R4-hotfix that was an IOORE, post-R4-hotfix it's a clean 503 with `PLAYBOOK_INVOCATION_FAILED`.

---

## 10. The Action Engine, Agents, and the consumer model

This is the strategic question worth answering explicitly because it determines how Spaarke's overall AI surface composes.

### 10.1 Where consumer routing lives in the layer stack

```
┌──────────────────────────────────────────────────────────────────┐
│  Layer 4 — Spaarke AI Assistant (conversational surface)          │
│    User says "summarize this matter" → Assistant matches intent → │
│    dispatches an Action                                           │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│  Layer 3 — Action Engine (action authoring + management)           │
│    Actions are user-defined artifacts: triggers + parameters +    │
│    playbook reference. An Agent is an Action with a non-manual    │
│    trigger (schedule, event, signal, webhook).                    │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│  Layer 2 — Consumer Routing (this doc)                            │
│    sprk_playbookconsumer + IConsumerRoutingService +              │
│    IInvokePlaybookAi. Decoupling layer: turns a stable consumer   │
│    identity into a current playbook GUID.                         │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────┐
│  Layer 1 — Playbook Runtime                                       │
│    PlaybookOrchestrationService + nodes + actions + scopes +      │
│    tools. The orchestration substrate that actually executes.     │
└──────────────────────────────────────────────────────────────────┘
```

Consumer routing is **Layer 2** — sits between the user-facing surfaces (Layers 3–4) and the orchestration runtime (Layer 1). Each layer is a facade.

### 10.2 The Playbook-as-Orchestration-Boundary principle (alignment note)

> **Vocabulary collision**: the word "Action" is overloaded across two Spaarke contexts. Reading this section without disambiguating leads to category errors.
>
> - **Action (Action Engine sense)** — the user-facing artifact: trigger + parameters + playbook reference. Defined in [`projects/ai-spaarke-action-engine-r1/design.md`](../../projects/ai-spaarke-action-engine-r1/design.md) §4. An Agent is an Action with a non-manual trigger.
> - **Action (JPS / `sprk_analysisaction` sense)** — the executable unit invoked inside a playbook node. Analogous to what the Action Engine vocabulary calls a "Tool."
>
> These are different concepts. The principle below applies to BOTH but is most often misunderstood at the `sprk_analysisaction` boundary.

**The principle**: Playbook is THE orchestration boundary. Consumer routing resolves to a `sprk_analysisplaybook` GUID — **never** to `sprk_analysisaction`, `sprk_playbooknode`, `sprk_analysistool`, or scope.

**Why standalone Actions are not directly callable**: a `sprk_analysisaction` row defines an instruction, an output schema, and a model binding — but it does NOT carry orchestration, execution context, idempotency, retry policy, output composition rules, or observability. Those concerns live on the playbook + the orchestrator. An Action without a playbook wrapper has no execution semantics — it can get lost: no audit trail, no failure handling, no retry, no compose-with-other-actions guarantee. As a matter of Spaarke solution architecture, this is not supported.

**Implications**:

| What | How it actually works |
|---|---|
| An Action Engine Action / Agent dispatches AI | It binds to a **playbook** (direct `sprk_playbookid` OR via consumer routing). Never to a `sprk_analysisaction`. |
| A playbook runs an Action (`sprk_analysisaction`) | Internal to playbook execution — orchestrated by a `sprk_playbooknode` of type `Action` per the playbook's node graph. The Action is invoked BY the playbook, not externally. |
| Reusing the same `sprk_analysisaction` across multiple flows | Multiple playbooks reference it via their respective nodes. Composition happens at the playbook layer, not by external callers reaching into a shared action. |
| An external caller wants to invoke "just the summarize action" | Not supported. Wrap it in a single-node playbook and dispatch that. The playbook can be trivial (one node), but the boundary must exist. |

**Schema-level enforcement**: the `sprk_playbookid` lookup column on `sprk_playbookconsumer` is constrained to `sprk_analysisplaybook`. This is not arbitrary — it's the schema-level expression of the principle. Same applies to the `playbook reference` field on an Action Engine `sprk_action` row (per Action Engine design §4 — references a Playbook, not an Action).

### 10.3 Are Agents just another type of playbook consumer?

**Yes — structurally.** An Agent (per the Action Engine design at [`projects/ai-spaarke-action-engine-r1/design.md`](../../projects/ai-spaarke-action-engine-r1/design.md) §1) is an Action with a non-manual trigger. Every Action — agent or otherwise — references exactly one playbook to execute. That reference can be:

- **Direct GUID binding** — the Action authoring surface stores `sprk_playbookid` directly on the Action. Faster lookup, no routing-table indirection. Best for "this Agent always runs this exact playbook, full stop."
- **Consumer-routing indirection** — the Action stores `sprk_consumertype` instead, and resolves at run-time via `IConsumerRoutingService`. Slower (cache-hit ~<1ms, cache-miss + Dataverse query ~50ms), but the playbook target can be redirected by a maker without touching the Action. Best for "this Agent runs the canonical chat-summarize playbook, and if we ship a new version we want all Agents using it to switch over automatically."

The current production Actions (the 7 in §6) all use the consumer-routing indirection. Future Actions authored via the Action Engine could pick either pattern; the platform should make both available.

### 10.4 Could one consumer use multiple playbooks?

**Yes, via three different mechanisms** — and only one of them is N:N at the routing-table level. Pick deliberately.

#### Mechanism A — Multiple rows, same `consumertype`, different `code` (variant routing)

Today's pattern. One `sprk_consumertype` value (e.g., `chat-summarize`) with multiple rows differentiated by `sprk_consumercode` or `sprk_matchconditionsjson`:

| `consumertype` | `consumercode` | `playbookid` |
|---|---|---|
| `chat-summarize` | `default` | generic-summarize@v1 |
| `chat-summarize` | `vendor-contract` | summarize-vendor-contract@v1 |
| `chat-summarize` | `employment` | summarize-employment-agreement@v1 |
| `chat-summarize` | `real-estate-lease` | summarize-real-estate-lease@v1 |

The consumer surface (`SessionSummarizeOrchestrator`) calls `ResolveAsync(ChatSummarize, code: detectedArea, ...)`. The dispatcher's vector match passes `detectedArea` (or `IRoutingContext.DocumentType` after classification). **One consumer call → one playbook, but the consumer surface has access to N playbook variants.** This is the P1-summarize-document-v1 design.

#### Mechanism B — Composition playbook (one consumer → one playbook that internally calls others)

If you genuinely want multiple playbooks to run as part of one consumer dispatch, the canonical pattern is a **composition playbook** — a multi-node playbook whose nodes invoke other playbooks via `InvokePlaybookNodeExecutor` (or its successor). One row in `sprk_playbookconsumer`, one resolved playbook GUID, but the playbook itself fans out internally. This keeps `sprk_playbookconsumer` 1:1 and lets the orchestration runtime own composition semantics.

#### Mechanism C — True N:N (would require a schema change)

A genuine N:N relationship — one logical consumer simultaneously bound to multiple playbooks at the routing-table level — would require a new join entity:

| Hypothetical `sprk_playbookconsumer_playbook` |
|---|
| `sprk_consumerid` (FK → consumer) |
| `sprk_playbookid` (FK → playbook) |
| `sprk_dispatchmode` ("parallel" / "sequential-then-merge" / "primary-with-audit-side-channel") |

This is **NOT in scope** today. Mechanism B (composition playbook) covers every use case we have. If a future Action Engine requirement surfaces — e.g., "an Agent must run a primary playbook AND a compliance-audit playbook in parallel and merge results" — the right path is to evaluate Mechanism B first (one composition playbook with both as nodes) before changing the routing schema.

### 10.5 Practical guidance for Action Engine authors

When designing an Action that dispatches AI:

| Question | Recommendation |
|---|---|
| Does the Action need stable identity that survives a playbook version bump? | Use consumer routing (Mechanism A). The Action references `consumertype`; the routing table maps to the current playbook. |
| Does the Action have multiple legitimate variants (vendor / employment / generic)? | Use `sprk_consumercode` for variant selection (Mechanism A). |
| Does the Action need to invoke multiple distinct playbook flows AT ONCE? | Build a composition playbook (Mechanism B). One `sprk_playbookconsumer` row, one resolved playbook, internal fan-out. |
| Does the Action need to bind to one specific playbook forever (no indirection)? | Direct GUID binding on the Action row (skip consumer routing). |

The default for new Actions should be **consumer routing with `code = "default"`** unless one of the above patterns specifically applies. Indirection is cheap; rip-and-replace is expensive.

---

## 11. Boundary: what a consumer-routing row is NOT for

The `sprk_playbookconsumer` row is a **routing pointer**, not a config bag.

Anti-patterns to avoid:

- ❌ **Stuffing executor parameters into `sprk_matchconditionsjson`** — this column is for *match selection* (which row wins), not *node config*. Per-node config goes in `sprk_playbooknode.sprk_configjson` (see `ai-architecture-actions-nodes-scopes.md`).
- ❌ **Stuffing playbook scope arrays into `sprk_playbookconsumer`** — scopes belong on the playbook row via N:N relationships, not on the routing row.
- ❌ **Adding a new `sprk_consumertype` value without a corresponding `ConsumerTypes.cs` constant** — the constant is the compile-time anchor; Dataverse rows reference it by string but the constant is the source of truth.
- ❌ **Using the priority field for permissioning** — priority is a tiebreaker among already-matched rows. Permissioning happens upstream at the endpoint (auth filter), not at consumer routing.
- ❌ **Trying to route consumer dispatch to anything other than a `sprk_analysisplaybook`** — the `sprk_playbookid` FK constrains this at the schema level, but the principle is broader (see §10.2): never bind dispatch to a `sprk_analysisaction`, `sprk_playbooknode`, `sprk_analysistool`, or scope directly. Playbook is the only legitimate dispatch target.

When in doubt: the routing row answers "**which playbook should this surface dispatch to in this environment for this code?**" Anything else belongs elsewhere.

---

## 12. Cache semantics + invalidation

`ConsumerRoutingService` uses `IMemoryCache` with a 5-minute TTL keyed on `(consumerType, code, env)`. Implications:

- **A new `sprk_playbookconsumer` row takes up to 5 minutes to propagate** after deploy. For UAT smoke tests, restart the BFF or wait. There is no explicit invalidation hook.
- **Disabling a row by flipping `sprk_enabled = false`** also has the 5-minute lag. Plan deployments accordingly.
- **The cache is per-instance**. In multi-instance deployments (BFF behind a load balancer) the propagation window is per-instance.

If R5 needs cache invalidation, the pattern is to publish a Service Bus message on `sprk_playbookconsumer` write and clear the cache key — out of scope for R4.

---

## 13. Empty-payload behaviour in Path A.5

Path A.5 inherits the runtime's NodeBased empty-payload tolerance. As long as the resolved playbook has `sprk_playbooknode` rows, calling `InvokePlaybookAsync` with `parameters` containing empty or null fields is safe. Each node executor reads its own slice of `parameters` and either returns `null` / empty output or applies its default behaviour.

R4 design 010 (`DAILY-BRIEFING-NARRATE` node graph) documents the empty-payload contract concretely: when the briefing payload has zero items in every section, the graph still completes — the LLM is given an empty input, the EntityNameValidator returns the empty validated list, the CreateNotification node emits a "nothing to narrate" message. The contract is "200 + empty narration," not 204 No Content.

This is a playbook design choice, not a runtime requirement. If a future playbook author wants 204 for empty input, the `Output` node can encode that. The facade just relays whatever the orchestrator returns.

---

## 14. Relationship to other canonical docs

| Question | Read |
|---|---|
| How the orchestrator detects NodeBased vs Legacy mode | `ai-architecture-playbook-runtime.md` §3 |
| Where should this new config field live (action vs node vs playbook)? | `ai-architecture-actions-nodes-scopes.md` |
| `sprk_playbookconsumer` Dataverse schema (column types, key) | [`docs/data-model/sprk-playbookconsumer.md`](../data-model/sprk-playbookconsumer.md) |
| How to add a new consumer routing type (3-step procedure) | [`docs/guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md`](../guides/HOW-TO-ADD-A-CONSUMER-ROUTING-TYPE.md) |
| 6-tier memory & chat-routing precedent (Insights reuse boundary) | `projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/stateful-chat-architecture.md` |
| Deploying a playbook + consumer row | `ai-guide-playbook-deploy-recipe.md` |
| Action Engine project design (Agents, triggers, surfaces, conversational invocation) | [`projects/ai-spaarke-action-engine-r1/design.md`](../../projects/ai-spaarke-action-engine-r1/design.md) |
| BFF facade boundary policy (when CRUD code needs AI) | ADR-013, `.claude/constraints/bff-extensions.md` |

---

## Document changelog

| Date | Change | Author |
|---|---|---|
| 2026-06-28 | Added §10.2 (Playbook-as-Orchestration-Boundary principle) recording the architectural decision that `sprk_analysisaction` rows are not directly callable; Actions are always wrapped in a playbook. Vocabulary collision (Action Engine "Action" vs JPS `sprk_analysisaction`) disambiguated explicitly. Added matching anti-pattern to §11. Renumbered §10.2→§10.3, §10.3→§10.4, §10.4→§10.5. | chat-routing-redesign-r1 |
| 2026-06-28 | Renamed `ai-architecture-consumer-routing.md` → `ai-architecture-playbook-consumer-routing.md`. Added §2 (Why this was created), §1 Triangle ASCII diagram, §4 expanded compile-time safety, §5 (Runtime code example), §6 (Who's wired today), §10 (Action Engine, Agents, and the consumer model). Renumbered later sections. References updated in `ai-architecture-actions-nodes-scopes.md`, `ai-architecture-playbook-runtime.md`, `AI-ARCHITECTURE.md`, `playbook-architecture.md`, `ai-guide-playbook-deploy-recipe.md`, `.claude/skills/jps-playbook-design/SKILL.md`. | chat-routing-redesign-r1 |
| 2026-06-26 | Initial canonical document. | spaarke-daily-update-service-r4 |
