# AI Architecture — Consumer Routing & Path A.5

> **Last reviewed**: 2026-06-26
> **Authored by**: canonical-truth loop step 3 (spaarke-daily-update-service-r4)
> **Status**: Canonical. Lifts the `sprk_playbookconsumer` / `IConsumerRoutingService` / `IInvokePlaybookAi` triangle from R4 `notes/decisions/030-dispatch-path.md` and chat-routing-redesign-r1 `architecture/stateful-chat-architecture.md` into a first-class architecture doc.
> **Scope**: How a consumer surface (widget, endpoint, agent) resolves a playbook by *consumer type* + environment + optional code, and dispatches it via the non-streaming facade. Includes the decision matrix for Path A / Path A.5 / Path B and the R4 `/narrate` case study.
> **NOT in scope**: Runtime mode-detection inside the orchestrator (see `ai-architecture-playbook-runtime.md` §3), JPS authoring (see `ai-guide-jps-authoring.md`), `sprk_playbookconsumer` schema in Dataverse data model (see `docs/data-model/`).

---

## 1. The triangle

| Surface | Owns |
|---|---|
| **`sprk_playbookconsumer` (Dataverse entity)** | A row per (consumer surface, environment, optional code) → playbook FK. Data-driven so a redirect changes Dataverse only, not BFF code. |
| **`IConsumerRoutingService` (`Services/Ai/PublicContracts/ConsumerRoutingService.cs:53`)** | Looks up the row, picks the best match for the calling context, caches 5 min. |
| **`IInvokePlaybookAi` (`Services/Ai/PublicContracts/InvokePlaybookAi.cs:42`)** | Non-streaming facade — calls `PlaybookOrchestrationService.ExecuteAsync` (Path B), aggregates SSE stream into `PlaybookInvocationResult`, returns to caller. |

These three together are **Path A.5** (see §4). They form the canonical bridge between an HTTP endpoint that does NOT want to stream + does NOT have a document + does NOT want to know which playbook to call by GUID, and the streaming, node-graph-aware orchestrator.

---

## 2. `sprk_playbookconsumer` contract

The entity carries the routing table.

| Column | Type | Meaning |
|---|---|---|
| `sprk_consumertype` | Choice (string, see `ConsumerTypes.cs`) | The compile-time identifier of the calling surface. R4 example: `"DailyBriefingNarrate"`. |
| `sprk_code` | String | Optional per-instance discriminator (e.g., language code, tenant code). Null = wildcard. |
| `sprk_enabled` | Bool | If false, row is ignored at resolve time. |
| `sprk_playbookid` | Lookup → `sprk_analysisplaybook` | The target playbook FK. |
| `sprk_environment` | Choice/String | Optional environment scope (`dev`, `staging`, `prod`). Null = wildcard. |
| `sprk_priority` | Int | Tiebreaker when multiple rows match. Lowest wins (consistent with the chat-routing-redesign-r1 precedent). |
| `sprk_matchconditionsjson` | Memo | Optional JSON conditions evaluated by `SelectBestMatch`. Used when context carries arbitrary keys (e.g., tenant flags). |

`ConsumerRoutingService.ResolveAsync(consumerType, code, ctx, env, ct)`:

1. Queries `sprk_playbookconsumer` filtered by `sprk_consumertype` + `sprk_enabled = true`.
2. In-memory `SelectBestMatch` filters by `(code, env, matchConditions)`.
3. Sorts remaining rows by `sprk_priority` ascending and returns the head.
4. Returns the resolved playbook Guid; caches the result for 5 minutes via `IMemoryCache`.

**Compile-time identifiers** live in `ConsumerTypes.cs`. R4 adds (or relies on) `DailyBriefingNarrate`. Adding a new consumer surface requires:

1. A new `const string` in `ConsumerTypes.cs`.
2. A Dataverse row in `sprk_playbookconsumer` referencing the new constant.
3. The calling endpoint to inject `IConsumerRoutingService` + `IInvokePlaybookAi` and resolve → invoke.

No new endpoint registration, no new orchestrator code, no new DI module. Cf. ADR-013 facade principle.

---

## 3. `IInvokePlaybookAi.InvokePlaybookAsync` — non-streaming semantics

The facade at `Services/Ai/PublicContracts/InvokePlaybookAi.cs` aggregates a streaming `IAsyncEnumerable<PlaybookStreamEvent>` into a single `PlaybookInvocationResult`. Key behaviour:

1. **No document context** — at line 68-73 the facade constructs `PlaybookRunRequest { DocumentIds = Array.Empty<Guid>(), Parameters = parameters }`. Empty DocumentIds is the **intended** path for invoke-by-payload callers (per `Services/Ai/PublicContracts/InvokePlaybookAi.cs:64-67` comment).
2. **Always Path B** — line 86: `_orchestrator.ExecuteAsync(request, context.HttpContext, ct)` (the OBO node-based orchestrator). Path C (app-only) is not facade-accessible.
3. **Failure → 503** — on `PlaybookEventType.RunFailed` the facade sets `ErrorCode = "PLAYBOOK_INVOCATION_FAILED"`; the caller endpoint translates to a 503 ProblemDetails (canonical pattern at `DailyBriefingEndpoints.cs:309-321`).
4. **Empty-payload tolerance is conditional** — `IInvokePlaybookAi` handles empty payload correctly **only if the resolved playbook is NodeBased** (has `sprk_playbooknode` rows). If it falls through to Legacy mode (Path A inside the legacy orchestrator), the pre-2026-06-26 IOORE risk applied; the R4 hotfix at `AnalysisOrchestrationService.cs:720-728` closed it (now a clean error chunk). See `ai-architecture-playbook-runtime.md` §7.

The facade's contract is therefore: **"call me with parameters, no documents, expect either success-with-output or a single failure result. Never deal with SSE."**

---

## 4. Path A / A.5 / B decision matrix

After R4, three dispatch shapes coexist. Use this matrix when designing a new playbook-consuming surface.

| Path | Use when | Don't use when | Reference |
|---|---|---|---|
| **Path A** (legacy doc-bound) | Caller has 1+ document GUIDs and needs streaming SSE back to a client | Caller has no document (use A.5); caller doesn't need streaming (use A.5) | `AnalysisOrchestrationService.ExecutePlaybookAsync:702` |
| **Path A.5** (`IConsumerRoutingService` + `IInvokePlaybookAi`) | (a) Caller has NO document; (b) caller has a `parameters` blob; (c) caller wants non-streaming response; (d) the playbook GUID should be Dataverse-data-driven not hardcoded; (e) result fits in a single JSON response (no progressive UI) | Caller needs streaming SSE (use A or B); caller has a document AND needs document semantics in the legacy orchestrator (use A) | `DailyBriefingEndpoints.cs:201-374` (R4 canonical case study) |
| **Path B** (direct `PlaybookOrchestrationService.ExecuteAsync`) | (a) Caller has the playbook GUID already (no consumer-routing lookup needed); (b) caller needs streaming SSE; (c) caller may or may not have documents | Caller wants Dataverse-driven playbook selection (use A.5); caller doesn't need streaming (use A.5 for simpler return shape) | `PlaybookOrchestrationService.ExecuteAsync:81` |

Path C (`ExecuteAppOnlyAsync`) is the app-only sibling of Path B, used by `PlaybookSchedulerJob` for notification fan-out. It is not facade-accessible and not a runtime caller-choice — selection is structural (background services vs request pipeline).

---

## 5. The R4 case study — `/narrate` dispatch wiring

R4's task 031 (commit `88dd66a1c`) wires `POST /api/ai/daily-briefing/narrate` end-to-end as the canonical Path A.5 reference. Sequence:

1. **Endpoint receives** `BriefingNarrateRequest { briefingPayload, userId, ... }` (no DocumentIds in the contract).
2. **Resolve playbook** — `_consumerRouting.ResolveAsync(ConsumerTypes.DailyBriefingNarrate, code: null, ctx: …, env: currentEnvironment, ct)` at `DailyBriefingEndpoints.cs:250`. Cache-hit returns the playbook Guid in <1 ms.
3. **Build parameters** — convert `briefingPayload` (already-formed JSON object) + scalars (`userId`, `today`, `recencyHours`) into a `Dictionary<string, object>`.
4. **Invoke facade** — `_invokePlaybook.InvokePlaybookAsync(playbookId, parameters, context, ct)` at `:303`.
5. **Translate result** — on success, return `Results.Ok(...)`; on `RunFailed`, return `Results.Problem(503)` per `:309-321`.

This is the entire wiring. NO new orchestrator code, NO new DI module beyond the ConsumerRouting registration, NO new endpoint filter complexity — the existing `IConsumerRoutingService` + `IInvokePlaybookAi` facades carry the load. ADR-013's "thin facade over orchestrator" principle is realised.

The playbook itself (`DAILY-BRIEFING-NARRATE`) is the R4 W0/W1 deployment target. As long as it has `sprk_playbooknode` rows, empty `DocumentIds` is tolerated (see `ai-architecture-playbook-runtime.md` §7). If it does NOT have nodes, Legacy mode fires; pre-R4-hotfix that was an IOORE, post-R4-hotfix it's a clean 503 with `PLAYBOOK_INVOCATION_FAILED`.

---

## 6. Boundary: what a consumer-routing row is NOT for

The `sprk_playbookconsumer` row is a **routing pointer**, not a config bag.

Anti-patterns to avoid:

- ❌ **Stuffing executor parameters into `sprk_matchconditionsjson`** — this column is for *match selection* (which row wins), not *node config*. Per-node config goes in `sprk_playbooknode.sprk_configjson` (see `ai-architecture-actions-nodes-scopes.md`).
- ❌ **Stuffing playbook scope arrays into `sprk_playbookconsumer`** — scopes belong on the playbook row via N:N relationships, not on the routing row.
- ❌ **Adding a new `sprk_consumertype` value without a corresponding `ConsumerTypes.cs` constant** — the constant is the compile-time anchor; Dataverse rows reference it by string but the constant is the source of truth.
- ❌ **Using the priority field for permissioning** — priority is a tiebreaker among already-matched rows. Permissioning happens upstream at the endpoint (auth filter), not at consumer routing.

When in doubt: the routing row answers "**which playbook should this surface dispatch to in this environment for this code?**" Anything else belongs elsewhere.

---

## 7. Cache semantics + invalidation

`ConsumerRoutingService` uses `IMemoryCache` with a 5-minute TTL keyed on `(consumerType, code, env)`. Implications:

- **A new `sprk_playbookconsumer` row takes up to 5 minutes to propagate** after deploy. For UAT smoke tests, restart the BFF or wait. There is no explicit invalidation hook.
- **Disabling a row by flipping `sprk_enabled = false`** also has the 5-minute lag. Plan deployments accordingly.
- **The cache is per-instance**. In multi-instance deployments (BFF behind a load balancer) the propagation window is per-instance.

If R5 needs cache invalidation, the pattern is to publish a Service Bus message on `sprk_playbookconsumer` write and clear the cache key — out of scope for R4.

---

## 8. Empty-payload behaviour in Path A.5

Path A.5 inherits the runtime's NodeBased empty-payload tolerance. As long as the resolved playbook has `sprk_playbooknode` rows, calling `InvokePlaybookAsync` with `parameters` containing empty or null fields is safe. Each node executor reads its own slice of `parameters` and either returns `null` / empty output or applies its default behaviour.

R4 design 010 (`DAILY-BRIEFING-NARRATE` node graph) documents the empty-payload contract concretely: when the briefing payload has zero items in every section, the graph still completes — the LLM is given an empty input, the EntityNameValidator returns the empty validated list, the CreateNotification node emits a "nothing to narrate" message. The contract is "200 + empty narration," not 204 No Content.

This is a playbook design choice, not a runtime requirement. If a future playbook author wants 204 for empty input, the `Output` node can encode that. The facade just relays whatever the orchestrator returns.

---

## 9. Relationship to other canonical docs

| Question | Read |
|---|---|
| How the orchestrator detects NodeBased vs Legacy mode | `ai-architecture-playbook-runtime.md` §3 |
| Where should this new config field live (action vs node vs playbook)? | `ai-architecture-actions-nodes-scopes.md` |
| `sprk_playbookconsumer` Dataverse schema (column types, key) | `docs/data-model/` |
| 6-tier memory & chat-routing precedent (Insights reuse boundary) | `projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/stateful-chat-architecture.md` |
| Deploying a playbook + consumer row | `ai-guide-playbook-deploy-recipe.md` |
| BFF facade boundary policy (when CRUD code needs AI) | ADR-013, `.claude/constraints/bff-extensions.md` |
