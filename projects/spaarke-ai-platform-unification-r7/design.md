# Spaarke AI Platform Unification R7 — Design

> **Status**: DRAFT v0.6 (2026-06-28 — owner review round 5: maker-facing "how to wire a new consumer" guide LOCKED as R7 scope)
> **Authors**: ralph.schroeder@hotmail.com + Claude (working session)
> **Predecessor**: R6 + R5 + R4 + R3 + R2 + R1 (all "unification" iterations have addressed partial slices; R7 is the foundational reframe)
> **Sibling**: [spaarke-daily-update-service-r4](../spaarke-daily-update-service-r4/) — the R4 UAT that surfaced the architectural gap that this project closes
> **Memory**: [`spaarke-ai-canonical-truth-principle`](~/.claude/projects/c--code-files-spaarke-wt-spaarke-daily-update-service-r4/memory/spaarke-ai-canonical-truth-principle.md)

> **Framing note (owner emphasis 2026-06-27)**: This project is named "AI Platform Unification" but the dispatch reform serves BOTH AI workflows AND deterministic workflows (rule engine, notification, data query, integration, human-in-the-loop). The Playbook model is an orchestration engine; AI is one node-class among many. The R7 reform makes BOTH cleaner. Project name retained per owner directive (preserve project history); reframing is conceptual, not branding.

> **Consumer-driven model (owner emphasis 2026-06-28)**: The maker model is consumer-driven, not playbook-first. A playbook with no consumer (no row in `sprk_playbookconsumer` pointing at it) is dead code. The authoring UX surfaces templates + guided building IN THE CONTEXT of the consumer ("what surface is this for?"). R7 promotes `sprk_playbookconsumer` to a first-class concept and ensures it's load-bearing for discovery + invocation. See §3 + §11.

> **Out of scope — agent-building (Action Engine R1 owns)**: The "Agent Builder", "Spaarke Claw", customer-facing agent UX, Action Templates, Tool Registry classification, gate approvals, phase deny-tools, meta-tools (`FindResources` / `GetResourceDetail` / `InvokeResource`), etc. all live in [`ai-spaarke-action-engine-r1`](../ai-spaarke-action-engine-r1/). R7 is the dispatch + schema foundation that Action Engine sits on top of. R7 does NOT include substantive agent-builder work. See §11 (pointer only, not scope).

<hot-path-declaration>
  <bff>YES</bff>
  <spaarke-ai>NO</spaarke-ai>
  <ci-workflows>NO</ci-workflows>
  <skill-directives>YES</skill-directives>
  <root-claude-md>NO</root-claude-md>
</hot-path-declaration>

> **Hot-path rationale**: BFF=YES (AiCompletionNodeExecutor + PlaybookOrchestrationService refactor + enum rename + endpoint additions, per spec §"Affected Areas"). Skill-directives=YES (FR-32/FR-33 rewrite jps-action-create, jps-playbook-design, jps-playbook-audit, jps-validate; minor update to jps-scope-refresh). SpaarkeAi=NO (PlaybookBuilder lives at `src/client/code-pages/PlaybookBuilder/`, not `src/solutions/SpaarkeAi/**`). CI-workflows=NO. Root-CLAUDE.md=NO.

---

## 1. Why this project exists (problem statement)

Spaarke AI does not have a functional, coherent dispatch model. Symptoms surfaced in R4 UAT:

1. `/narrate` 503: Start node failed with "Condition expression is required" — `Deploy-Playbook.ps1` doesn't inject `__actionType=33`; structural fallback routed Control → ConditionNodeExecutor (closed by R4 commit `d9c648e30`).
2. Same failure recurred for LoadKnowledge (commit `06be7c0e6`) and ReturnResponse (same commit).
3. Stale-zip deploy regression: an older artifact was redeployed because `deploy/api-publish.zip` mounted a stale `api-publish/` folder; runtime ran pre-fix code (R4 UAT session 2026-06-26).
4. `GenerateTldr` failed with "AI analysis node requires a tool to be configured; AI analysis node requires document context" — `AiAnalysisNodeExecutor` requires Tool + Document, but `/narrate` is a prompt-only payload-driven call. No `AiCompletionNodeExecutor` exists for the enum value `AiCompletion = 1` that was clearly intended for this case.
5. Schema introspection revealed three storage layers for "what executor handles this Action," none enforced to agree:
   - C# enum `ActionType` (33 values, code-bound, source of truth)
   - `sprk_analysisaction.sprk_executoractiontype` (plain INT, free-form, ignored by runtime per `AnalysisActionService.cs:57-67`)
   - `sprk_analysisactiontype.sprk_executoractiontype` on the lookup table (load-bearing for runtime dispatch)

Each defect is a different version of the same root cause: **the Spaarke AI dispatch model is split across too many surfaces with no canonical contract, so makers, deploy scripts, and runtime drift apart**. Every release introduces another version of the same class of bug.

**The R4 R3 R2 R1 sequence has been chasing symptoms.** R7 closes the foundation.

### Owner statement (verbatim, 2026-06-26 during R4 UAT)
> "we don't have a functioning system at all if we don't get this figured out — currently nothing really works. so it's not tonight but it moves to the very front because nothing else works if playbooks don't work."

### Canonical-truth principle (applied — see memory)
> "we need to understand this more holistically because otherwise we are chasing version of the same issue every time we are deploying a playbook based function (which is most everything in spaarke ai). If we do not institutionalize this knowledge we will never get the spaarke ai or any other ai functions working as intended (NOR will we be able to instruct customers how to use playbooks)"

---

## 2. The unification — single architectural reframe

The R7 reframe collapses the dispatch model around four invariants:

### Invariant 1 — Playbook is the only AI invocation model

Every AI / orchestration call goes through `PlaybookOrchestrationService.ExecuteAsync` with a `PlaybookRunRequest`. Even a single LLM call is a 3-node playbook: `Start → AiCompletion → ReturnResponse`. This eliminates the legacy dual-dispatch path (`AnalysisOrchestrationService.ExecuteAnalysisAsync` direct invocation goes away).

### Invariant 2 — Executor dispatch lives on the node, not the Action

The node owns "what executor to dispatch to" via a Choice field **`sprk_executortype`** (NEW; column on `sprk_playbooknode`). The Choice draws values from the global Choice set **`sprk_playbookexecutortype`** (33 values matching the C# `ActionType` enum; created by owner 2026-06-27).

The Action becomes a reusable prompt template referenced via optional `sprk_actionid` FK. The lookup-chain dispatch (`node.sprk_actionid → Action.sprk_actiontypeid → lookup row.sprk_executoractiontype`) collapses to one hop: **`node.sprk_executortype`**.

**Naming distinction** (binding throughout R7 design + spec + code):
- **`sprk_executortype`** = the COLUMN on `sprk_playbooknode` (what the node uses for dispatch)
- **`sprk_playbookexecutortype`** = the GLOBAL CHOICE SET (the constrained set of valid integer values; 33 today, will grow as new executors are added)

### Invariant 3 — Action is a reusable prompt template

For prompt-driven executors (AiAnalysis, AiCompletion, AiEmbedding), the Action carries:
- `sprk_systemprompt` (JPS-format prompt)
- `sprk_outputschemajson` (optional structured-output schema; if absent, derive from JPS `output.fields`)
- `sprk_temperature` (default override)

For pure executors (Condition, Start, ReturnResponse, EntityNameValidator, CreateNotification, QueryDataverse, LookupUserMembership, etc.), `sprk_actionid` is NULL — the executor's C# code is the behavior; node `sprk_configjson` is per-instance configuration.

### Invariant 4 — Two-tier dispatch (Tool/Handler) stays general — available to any executor that needs delegation

The `IAnalysisToolHandler` / `IToolHandlerRegistry` tier is part of the general playbook capability surface. **Any executor** that needs to delegate to multiple swappable implementations selected at maker time can use it. This is not narrowed to AiAnalysis — it's a node capability available to all.

The R4-era `AiAnalysisNodeExecutor` uses it for tool-mediated document-analysis flows. Future executors (agent-style multi-step flows, multi-LLM reasoning chains, retrieval-augmented compositions) can use it similarly.

`AiCompletionNodeExecutor` (the simple prompt-only case) chooses NOT to use the Tool/Handler tier — it calls `IOpenAiClient.GetStructuredCompletionRawAsync` directly. Action's prompt + node's overrides + LLM call. That's a per-executor design choice, not a structural restriction.

The only thing scoped by executor type is **whether the node carries an Action FK** (prompt-driven executors require Action; pure executors prohibit it). Tool/Handler tier availability is general.

### Invariant 5 — Each executor declares a typed config schema (new)

Every executor exports a config-schema declaration (TypeScript-like shape with field types, defaults, descriptions, required/optional). PlaybookBuilder reads the schema and renders a TYPED form per executor type (no free-form JSON editing). This enables:
- Maker-friendly validation at authoring time
- Type-safe defaults
- Intellisense / autocomplete
- Customer-facing authoring without code knowledge

Today this is informal — each executor parses `sprk_configjson` ad hoc and the canvas guesses. R7 formalizes the contract.

### Invariant 6 — Playbook is multi-purpose orchestration, not exclusively AI

The Playbook node-graph dispatches AI nodes, deterministic nodes (Condition / rule engine), data nodes (QueryDataverse / CreateNotification / UpdateRecord), integration nodes (CallWebhook / SendTeamsMessage), and human-in-the-loop nodes (Wait). R7 reform improves ALL of these. AI is one node-category among many.

---

## 3. Component model (after R7)

### Entities + their narrowed roles

| Entity | Role after R7 | Fields that matter | Fields that go away |
|---|---|---|---|
| `sprk_analysisplaybook` | Playbook header — name, scopes (skills/knowledge/tools/persona), compose strategy | `sprk_name`, `sprk_code`, `sprk_scopesjson`, `sprk_composejson` | `sprk_configjson` carrying node-graph data (R4 deploy bug — runtime reads node rows) |
| `sprk_playbooknode` | Per-instance execution slot — dispatch, config, scopes, bindings | + NEW: **`sprk_executortype`** (Choice column drawing from global Choice set `sprk_playbookexecutortype`, 33 values; created by owner 2026-06-27). DELETE: `sprk_nodetype` (6-value field) — collapsed into `sprk_executortype`. The 6 values are CONFLATED today (mix of structural categories like "Output"/"Control" + specific executors like "AI Analysis"/"EntityNameValidator"); one of the two dispatch axes must go, and `sprk_executortype` is the correct one. | `sprk_nodetype` Dataverse column (the 6-value Choice) is REMOVED. The 5-value C# `NodeType` enum may survive internally as a graph-traversal hint if useful, but is NOT what makers select and is NOT load-bearing for dispatch. |
| `sprk_analysisaction` | **Reusable prompt template** — JPS prompt + output schema + default temperature | `sprk_systemprompt`, `sprk_outputschemajson`, `sprk_temperature`, `sprk_actioncode` | `sprk_actiontypeid` (lookup), `sprk_executoractiontype` INT column |
| `sprk_analysisactiontype` (lookup table) | DECORATIVE — maker-friendly Action category browser (e.g., "Summarize", "Analyze") | `sprk_name`, optional categorization | `sprk_executoractiontype` (becomes meaningless once dispatch moves to node) |
| `sprk_analysistool` + Handlers | Multi-step capability delegation registry — available to ANY executor that needs implementation swap-out at authoring time. Per-executor opt-in. | (unchanged structurally) | Pretense of being THE universal AI dispatch surface — it's a capability, not a dispatch axis |
| `sprk_playbookconsumer` | **PROMOTED — first-class concept**. Consumer-key → playbook routing (Path A.5) PLUS load-bearing for maker UX (consumer-driven authoring). Every playbook author works "in context of consumer X." Playbooks with no consumer are dead. See §3.1 below for the WHY (env-var antipattern history). | (unchanged schema from `chat-routing-redesign-r1`; promoted in conceptual role) | The framing of consumer routing as "just an implementation detail." It's the maker mental model. |
| **Playbook Library** (Code Page modal — EXISTS but not wired into consumers) | Consumer-facing discovery + invocation surface — "what playbooks are available, what each does, what parameters they need." Becomes load-bearing under Q1 resolution (uniformity, no auto-synthesis). | Library indexes every playbook + scopes + input/output contract. Surfaced inside spaarke-ai consumer, briefing widget, ad-hoc launchers. Wiring into consumers is R7 deliverable. | The unlinked state — Library exists but doesn't route discovery from consumers today. |
| **Agent** (future concept; NOT in R7) | Consumer-of-playbooks abstraction. An Agent uses one or MORE playbooks. An Agent IS-A consumer (or HAS consumers in `sprk_playbookconsumer`). Owned by [`ai-spaarke-action-engine-r1`](../ai-spaarke-action-engine-r1/). | Action Engine project specifies the Agent entity model + UX + lifecycle. R7 provides only the dispatch foundation. | — (Agent is forward-looking; R7 makes it possible without owning it.) |

### Code components

| Component | Role after R7 | Notes |
|---|---|---|
| C# enum `ActionType` | Renamed to `ExecutorType` (matches the Choice column `sprk_executortype` on the node); source of truth for 33+ dispatch values | Number is binding contract; name is dev ergonomics. Sync mechanism (codegen or CI check) keeps C# enum + global Choice set `sprk_playbookexecutortype` in lockstep. |
| `NodeExecutorRegistry` | Dispatch by `ExecutorType` integer (read from `node.sprk_executortype`) | Unchanged structure; conceptual cleanup |
| `PlaybookOrchestrationService` | Reads dispatch from `node.sprk_executortype` directly | Simplifies — removes the structural fallback ladder + the "Insights Engine r2 Wave B" Action-FK override at line 1252 |
| `AnalysisActionService.GetActionAsync` | Returns Action with prompt + schema only; ActionType is not part of the contract | Read path simplifies; cache stays useful |
| `INodeExecutor` implementations (33) | Unchanged interface; some receive `Action` (prompt-driven), some don't (pure) | Validation contracts tighten: AI executors require Action; pure executors prohibit it |
| `AiCompletionNodeExecutor` (NEW) | Renders Action JPS prompt + node overrides via `PromptSchemaOverrideMerger`; calls `IOpenAiClient.GetStructuredCompletionRawAsync` | The "default" AI executor for simple cases |
| `AiAnalysisNodeExecutor` | Specializes in multi-step AI flows that need Tool/Handler tier | Document-loading + retrieval + tool-handler dispatch retained for this path only |
| `AnalysisOrchestrationService.ExecuteAnalysisAsync` (legacy direct path) | DEPRECATED → DELETED in R7 cutover | All callers migrate to playbook dispatch (degenerate 3-node playbook for trivial cases OR runtime-synthesized minimal playbook from an Action) |
| Tool/Handler tier (`IAnalysisToolHandler`, `IToolHandlerRegistry`, `Services/Ai/Handlers/`, `Services/Ai/Tools/`) | KEPT GENERAL — available to any executor that needs delegation. Tool selection is a per-executor opt-in (not a global dispatch axis). | Document the scope clearly: capability swap-out at authoring time. Distinct from executor dispatch. |
| **Executor config schema declaration** (NEW interface, e.g. `INodeExecutor.GetConfigSchema()`) | Every executor exports a typed schema for its config. PlaybookBuilder reads + renders typed form. No more free-form JSON. | Generated from C# attribute or static method per executor. Schema served to canvas via a new endpoint. |

---

## 3.1 Why `sprk_playbookconsumer` exists — the env-var antipattern (history + rationale)

This subsection captures the WHY behind `sprk_playbookconsumer` so R7's promotion of it to a first-class concept is grounded in real production pain, not abstract preference.

### Triggering production failure (2026-06-24 UAT-2)

The Phase 1 (`chat-routing-redesign-r1`) stable-ID migration shipped consumers that resolved playbooks BY GUID — but **the binding "which playbook GUID maps to which consumer code" still lived in App Service environment variables** like `Workspace__MatterPreFillPlaybookId`, set via `az webapp config appsettings set`.

**The UAT-2 failure**: Matter pre-fill broke on `bff-dev` because `Workspace__MatterPreFillPlaybookId` was set under a legacy env-var key. The wrong key + wrong value combined silently broke the consumer with no validation, no audit trail, no error until production runtime.

The owner's diagnosis (verbatim in [`projects/spaarke-ai-platform-chat-routing-redesign-r1/spec.md`](../spaarke-ai-platform-chat-routing-redesign-r1/spec.md) Phase 1R, 2026-06-24):

> "Phase 1R replaces env-var-based consumer→playbook routing with a Dataverse-backed `sprk_playbookconsumer` table."

This wasn't a one-off bug — it was the **inevitable failure mode** of binding live routing decisions in env vars. The pattern guaranteed recurrence.

### What was wrong with the env-var approach

| Problem | Why it bit |
|---|---|
| **Brittle across environments** | Stale GUID, wrong key, missing key — all fail silently. UAT-2 hit two of three. |
| **Cross-cutting changes require code/ops** | Redirecting a consumer to a different playbook needed App Service config redeploy. Not a data update. |
| **No audit trail** | Who set the env var to what when? No record. Compliance + debugging both suffer. |
| **No conditional routing** | Single value per env var. No way to dispatch by MIME type, classification, language, tenant flags. |
| **No prioritization or tiebreak** | Single binding. No way to say "use Y if condition, else fall back to Z." |
| **No soft-disable** | Env vars don't preserve history when disabled; you delete and lose context. |

These compound. The R4 attempt to add `/narrate` would have introduced ANOTHER `Workspace__NarratePlaybookId` env var, doubling down on the antipattern.

### The R4 connection — why R7 needs this entity

R4's `/narrate` faced the same dispatch question: how does the BFF know which playbook to invoke for "narrate this briefing payload"? The R4 decision doc ([`projects/spaarke-daily-update-service-r4/notes/decisions/030-dispatch-path.md`](../spaarke-daily-update-service-r4/notes/decisions/030-dispatch-path.md)) used `sprk_playbookconsumer` as the canonical answer:

- New `ConsumerTypes.DailyBriefingNarrate` constant in `ConsumerTypes.cs`
- New row in `sprk_playbookconsumer` referencing the constant
- Endpoint injects `IConsumerRoutingService` + `IInvokePlaybookAi`, resolves, invokes

**Zero new env vars. Zero new orchestrator code. Zero new DI registrations.** The pattern that started as a fix for a UAT-2 failure became the canonical dispatch contract for every new consumer surface.

### Why R7 promotes consumer to the maker mental model

Once you accept that `sprk_playbookconsumer` is the routing identity, two things follow:

1. **Every playbook has a consumer** (or it's dead code). A playbook with no `sprk_playbookconsumer` row pointing at it is never invoked. The maker authoring flow MUST begin with "which consumer am I authoring this for?"
2. **The consumer drives template selection + scope hints**. A consumer like "MatterPreFill" needs a playbook with specific input/output contract (matter payload in, prefill JSON out). A consumer like "DailyBriefingNarrate" has a different contract. Templates and authoring guidance focus by consumer type.

This is the operational shape R7 reflects — not a new invention, just promotion from "implementation detail" to "first-class maker mental model."

### Canonical documentation today

The triangle (`sprk_playbookconsumer` + `IConsumerRoutingService` + `IInvokePlaybookAi`) is documented in [`docs/architecture/ai-architecture-consumer-routing.md`](../../docs/architecture/ai-architecture-consumer-routing.md) (143 lines, 2026-06-26, canonical-truth loop step 3). The doc covers:

- §1 The triangle architecture
- §2 The 8-field contract on `sprk_playbookconsumer`
- §3 `IInvokePlaybookAi` facade semantics (non-streaming, no document, 503 on failure)
- §4 Path A / A.5 / B decision matrix
- §5 R4 `/narrate` canonical case study
- §6 Anti-patterns ("what consumer-routing is NOT for")
- §7 Cache semantics + invalidation

**What that doc does NOT cover** (R7 Wave 6 fills these):

- The WHY history above (env-var antipattern → UAT-2 failure → `sprk_playbookconsumer`)
- Maker-facing "how to wire a new consumer" guide
- Schema in `docs/data-model/` (the canonical doc explicitly says "NOT in scope; see `docs/data-model/`" but that entry doesn't exist yet)
- Future awareness — consumer is the central maker mental model under R7

§6 of this design tracks these gaps as Wave 6 deliverables.

---

## 4. Schema migration plan (Dataverse)

### Phase 1 — Add the new contract surfaces

1. Add Choice field `sprk_playbookexecutortype` to `sprk_playbooknode`. Choice values = the 33 from the C# enum (DONE today by owner in maker portal — the global Choice set "Playbook Executor Type" / `sprk_playbookexecutortype` exists with all 33 values).
2. Apply the same Choice as a column on `sprk_playbooknode`. Backfill via migration script (reads existing nodes; sets the Choice value from the linked Action's actiontype lookup).
3. (Optional) Apply the same Choice as a column on `sprk_analysisaction` to preserve the maker-friendly "this Action's intended dispatch" hint. **But runtime ignores it** — it's authoring metadata only.

### Phase 2 — Migrate the orchestrator

1. `PlaybookOrchestrationService.ExecuteNodeAsync` reads `node.PlaybookExecutorType` directly.
2. Remove the structural fallback ladder (`IsDeployedStartNode` / `IsDeployedLoadKnowledgeNode` / `IsDeployedReturnResponseNode`) — node has explicit dispatch now.
3. Remove the "Action ActionType is canonical regardless of NodeType" branch (line 1252) — node is canonical.
4. The `if (node.ActionId != Guid.Empty)` branch becomes "load Action for prompt context if dispatch needs it"; FK loading is decoupled from dispatch decision.

### Phase 3 — Deprecate the legacy direct path

1. Audit all callers of `AnalysisOrchestrationService.ExecuteAnalysisAsync`.
2. Migrate each to `PlaybookOrchestrationService.ExecuteAsync` with a degenerate 3-node playbook OR a runtime-synthesized minimal playbook from the Action.
3. Delete `ExecuteAnalysisAsync` method + supporting code.
4. Verify chat-summarize, all Service Bus job handlers, all PlaybookSchedulerJob fan-outs.

### Phase 4 — Schema cleanup (after runtime cutover)

1. Drop `sprk_actiontypeid` (lookup field) from `sprk_analysisaction`.
2. Drop `sprk_executoractiontype` INT column from `sprk_analysisaction`.
3. Decide: drop `sprk_analysisactiontype` table OR repurpose as Action templates (starter JPS prompts that authors can clone).
4. Update Deploy-Playbook.ps1 to write `sprk_playbookexecutortype` on each node row.

### Phase 5 — Migrate existing playbooks

Backfill EVERY existing `sprk_playbooknode` row with the correct `sprk_playbookexecutortype` value. PowerShell script: read each node, follow its Action lookup, set the Choice. Run idempotently.

Affected playbooks (known):
- Insights pipeline (universal-ingest, predict-matter-cost, matter-health-single, matter-health-synthesis, ...)
- Chat (summarize-document-for-chat, ...)
- Daily Briefing (5 R4 playbooks including DAILY-BRIEFING-NARRATE)
- All Office add-in playbooks
- All Workflow playbooks

### Phase 6 — Build AiCompletionNodeExecutor

In the new architecture (post-Phase 2). Reuses `PromptSchemaOverrideMerger` so the node-level Role/Task/Constraints/Output Fields canvas UI works. Calls `IOpenAiClient.GetStructuredCompletionRawAsync` directly. Closes `/narrate`.

---

## 5. Maker / authoring impact

### What gets simpler

- **One canvas pattern for ALL AI**: drop an "AI Node", pick the Executor Type from a Choice dropdown (33 values). Same canvas UX whether you want a prompt-only LLM call or a multi-step tool-mediated flow.
- **Actions become small + reusable**: just a JPS prompt + output schema + temperature. Easy to clone, version, A/B test. A maker can have one "Summarize" Action used in 20 playbooks.
- **Authoring a "simple AI playbook" is template-driven**: PlaybookBuilder gets a "Quick Playbook" button → drops Start + AiAnalysis + ReturnResponse with edges wired. Maker just picks an Action FK for the AI node, OR fills in node-level Role/Task/Output Fields directly.

### What changes for makers (transition cost)

- Existing Actions that were "self-contained mini-playbooks" (with tool refs + executor type baked in) lose their executor dispatch. Migration script repoints to the right node-level dispatch.
- Maker docs need rewriting: every "how to author an Action" guide becomes "how to author a prompt template + how to use it in a playbook node."

---

## 6. Documentation impact (CRITICAL — owner emphasis)

> "we just today put updated architecture and playbook documentation — we need to review our documentation to ensure we are not missing anything (AND IMPORTANTLY that we are removing what becomes tech debt) ... it makes documentation difficult or impossible to use or rely on..."

**Binding rule for R7 (owner directive 2026-06-28)**: outdated docs must be DELETED, not deprecated or marked superseded. Markers and redirects make documentation harder to use, not easier. Options for each doc:

- **DELETE** — file removed entirely OR sections removed in place. No stub. No redirect. Reader looks at the docs tree and sees ONLY current information.
- **UPDATE** — same file kept; content rewritten in place to match R7. Old content REMOVED, not appended.

**No third option.** No "superseded" banner. No "see new location" redirect. No archive folder. If the content is outdated, delete it.

**Mechanism**: every doc with a disposition in §6 below gets a tracking task in Wave 6 (documentation cleanup). PR review will block on documentation drift. Reviewer asks: "is there any outdated content remaining?" If yes, the PR is rejected until it is DELETED.

The canonical-truth docs published 2026-06-26 in R4 commits `f91981965` + `404012169` accurately describe the CURRENT runtime. Once R7 lands, the outdated sections in those docs become wrong — they get DELETED in Wave 6, not marked deprecated.

### `sprk_playbookconsumer` documentation gap (owner point 1a, 2026-06-28)

Investigation 2026-06-28: the entity has architectural coverage but documentation is incomplete. **R7 does NOT own updating the existing canonical architecture doc** — that responsibility lies with the chat-routing-redesign-r1 follow-on project (per owner directive 2026-06-28). R7 surfaces the gap and consumes whatever the canonical doc says.

| Surface | Status | Owner | R7 action |
|---|---|---|---|
| [`docs/architecture/ai-architecture-consumer-routing.md`](docs/architecture/ai-architecture-consumer-routing.md) | EXISTS — covers the triangle (consumer + IConsumerRoutingService + IInvokePlaybookAi), Path A/A.5/B decision matrix, runtime contract | chat-routing-redesign-r1 follow-on (NOT R7) | REFERENCE in R7 design.md §3.1. Out of R7 scope to update. |
| [`projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/playbookconsumer-matchconditions.schema.json`](projects/spaarke-ai-platform-chat-routing-redesign-r1/architecture/playbookconsumer-matchconditions.schema.json) | EXISTS — schema for `sprk_matchconditionsjson` | chat-routing-redesign-r1 follow-on | If/when it moves to `docs/data-model/`, R7 references the new location. NOT R7's job to move. |
| `docs/data-model/` entry for `sprk_playbookconsumer` schema | **MISSING (confirmed)** | chat-routing-redesign-r1 follow-on OR a dedicated data-model documentation project | R7 surfaces the gap; doesn't fill it. Once the entry exists, R7's consumer-driven sections cite it. |
| Maker-facing guide for "how to wire a new consumer" | **MISSING** | **R7 (LOCKED 2026-06-28)** | CREATE in Wave 6 — `docs/guides/ai-guide-consumer-wiring.md`. Critical for the consumer-driven authoring model. Owner emphasis: this is the central maker mental model and R7 owns the reframe, therefore R7 ships the maker-facing tutorial. |

This was a v0.3 oversight — added now per owner directive. v0.5: scope narrowed — R7 references the canonical architecture doc but does NOT update it.

### Documents written today (R4 canonical-truth loop) — disposition under R7

| Doc | Status under R7 | What needs to change |
|---|---|---|
| [`docs/architecture/ai-architecture-playbook-runtime.md`](docs/architecture/ai-architecture-playbook-runtime.md) | **MAJOR REWRITE** | §5 Action lookup precedence ladder ALL GOES AWAY (node has direct dispatch). §6 Three config columns boundary stays but reframes (Action = prompt; node = dispatch + config). Mode-is-emergent section stays. Structural-fallback section deleted. |
| [`docs/architecture/ai-architecture-consumer-routing.md`](docs/architecture/ai-architecture-consumer-routing.md) | **MINOR UPDATE** | Path A.5 / IConsumerRoutingService / IInvokePlaybookAi unchanged. Consumer routing is at the playbook layer, not the executor layer. |
| [`docs/architecture/ai-architecture-actions-nodes-scopes.md`](docs/architecture/ai-architecture-actions-nodes-scopes.md) | **MAJOR REWRITE** | 4-Home decision tree gets a new column for "node-level executor type." Action-as-self-contained-with-executor pattern explicitly deprecated. Anti-pattern list expands. |
| [`docs/guides/ai-guide-playbook-deploy-recipe.md`](docs/guides/ai-guide-playbook-deploy-recipe.md) | **UPDATE** | Step requiring deploy script to set `node.sprk_playbookexecutortype` (and stop writing Action's actiontype lookup). Control-flow node deploys become explicit dispatch sets, not name-detection hacks. |
| [`.claude/constraints/bff-extensions.md`](.claude/constraints/bff-extensions.md) §G | **UPDATE** | Config-bag boundary stays. Add: "node carries dispatch decision; Action carries prompt; lookup table is decorative." |
| [`docs/guides/JPS-AUTHORING-GUIDE.md`](docs/guides/JPS-AUTHORING-GUIDE.md) | **MAJOR REWRITE** | Authoring a JPS prompt is decoupled from authoring an Action's executor decision. Multiple JPS prompts can use the same executor type; same JPS prompt can be reused across executor types. |
| [`docs/guides/PLAYBOOK-AUTHOR-GUIDE.md`](docs/guides/PLAYBOOK-AUTHOR-GUIDE.md) | **MAJOR REWRITE** | Node-first authoring model. "Pick executor type from dropdown" replaces "follow the Action's actiontype lookup chain." |
| [`docs/architecture/AI-ARCHITECTURE.md`](docs/architecture/AI-ARCHITECTURE.md) | **UPDATE** | Top-level "AI Capability = Playbook" framing. Direct-execution legacy path called out as deprecated. |
| [`docs/architecture/playbook-architecture.md`](docs/architecture/playbook-architecture.md) | **REDIRECT** (already redirects to `ai-architecture-playbook-runtime.md`) | Once the runtime doc is rewritten, the redirect target lands correctly. |

### Skills written today (R4 canonical-truth Step 4) — disposition

| Skill | Status under R7 | What needs to change |
|---|---|---|
| `.claude/skills/jps-action-create` | **REWRITE** | Action-create becomes "prompt template create." Removes Step 1.5 config-home guard for non-prompt fields (they don't belong on Action anyway). |
| `.claude/skills/jps-playbook-design` | **REWRITE** | Node-first design model. Step 1.5 runtime-contract reminder updates to "set node executor type explicitly." Step 10 MCP verification: check node.executortype, not Action's lookup. |
| `.claude/skills/jps-playbook-audit` | **REWRITE** | Repo-vs-deployed reconciliation moves to "every node has explicit executor type." Orphan-node check stays. |
| `.claude/skills/jps-validate` | **REWRITE** | Step 7.5 validation: every node row must have `sprk_playbookexecutortype` in the C# enum range. Validate Action FK presence/absence matches the executor's requirement (prompt-driven needs Action; pure prohibits it). |
| `.claude/skills/jps-scope-refresh` | **MINOR UPDATE** | Scope refresh remains advisory; just verify the boundary between Choice (dispatch) vs scopes (capability resources). |

### Code-archaeology open questions resolved today (R4 commit `404012169`) — disposition

Most stay valid as **historical observations of the OLD architecture**. The R7 design supersedes them — they describe HOW things worked, not how things WILL work.

### Tech debt to call out + remove

Once R7 ships:

- The "Action ActionType is canonical regardless of NodeType" comment + branch in `PlaybookOrchestrationService.cs:1241-1278` — DELETE
- The structural fallback ladder + `IsDeployedStartNode` / `IsDeployedLoadKnowledgeNode` / `IsDeployedReturnResponseNode` helpers in `PlaybookOrchestrationService.cs:863-1053` — DELETE
- The lookup-chain dispatch in `AnalysisActionService.GetActionAsync` lines 57-67 — SIMPLIFY (no longer reads ActionType from lookup; Action is prompt-only)
- The `ExtractActionTypeFromConfig` helper for `__actionType` field in node ConfigJson — DELETE (node has explicit field)
- The `sprk_analysisactiontype` lookup table or its load-bearing role
- The legacy direct `AnalysisOrchestrationService.ExecuteAnalysisAsync` path — DELETE
- Multiple "passthrough" Action rows currently created just so deploy scripts can wire FKs by code (`INS-EVID`, `INS-GRND`, etc. that have NO prompts) — DELETE; nodes specify executor directly

---

## 7. Acceptance criteria (R7 done means)

1. Every `sprk_playbooknode` row in every environment has `sprk_playbookexecutortype` populated and accurate.
2. `PlaybookOrchestrationService.ExecuteNodeAsync` reads dispatch from node — only. No fallback ladder. No "ActionType from Action lookup."
3. Every consumer of AI dispatches through `PlaybookOrchestrationService` (no remaining callers of `ExecuteAnalysisAsync`).
4. `AiCompletionNodeExecutor` exists, is registered, and handles prompt-only LLM calls. `/narrate` works end-to-end through `DAILY-BRIEFING-NARRATE`.
5. PlaybookBuilder canvas: "Executor Type" dropdown on every AI node. Authoring a 3-node minimal playbook is ≤ 30 seconds.
6. Action authoring UI: simplified surface — prompt + schema + temperature only.
7. Canonical-truth docs reflect the new model. R4 docs reviewed; updated or marked superseded. Single source of truth, no overlap.
8. Skill bodies (jps-*) align with the new model.
9. Deploy-Playbook.ps1 sets executor type per node; no more name-detection hacks; no more `__actionType` injection workarounds.
10. The 33 (or whatever count R7 has) C# enum values are kept in sync with the Choice via a CI check or codegen step.
11. Documentation tech debt called out in §6 is removed or superseded — no dual content.

---

## 8. Open design question RESOLUTIONS (owner 2026-06-27 review)

All 9 questions from v0.1 have been answered. Open authoring-UX details flagged for spec.md.

### Q1 — Action-FK auto-synthesis
**RESOLVED: NO synthesis.** Uniformity wins. Every consumer dispatches by a real playbook. The Playbook Library becomes load-bearing — it's the consumer-facing index of "what playbooks (= agents) exist, what each does, what params they take." Cost: every trivial AI call requires a real 3-node playbook; benefit: zero AI buried in code, single dispatch model, full observability + library discoverability. The Playbook Library (existing Code Page modal, not yet wired) is promoted to a first-class consumer surface in R7.

### Q2 — PlaybookBuilder node-level prompt overrides + UI relocation
**RESOLVED 2026-06-28: KEEP per-node prompt overrides.**

UI relocation (binding for R7):
- The "Action" selection (currently buried in Overview tab) moves to its own first-class **Action tab** in node properties.
- The Executor type Choice selector also lives in the Action tab.
- The existing **Prompt tab** is KEPT — Role/Task/Constraints/Output Fields stay as per-node overrides via `PromptSchemaOverrideMerger`. This is the per-node personalization surface.

Rationale: per-node overrides serve real authoring needs (one Action used in 3 playbooks with slight contextual variations) without forcing maker to clone the Action. Removed earlier hybrid options — Spec phase doesn't need to revisit unless surfaces emerge.

### Q3 — Tool/Handler scope
**RESOLVED: KEEP GENERAL** — available to any executor that needs delegation. Tool/Handler is a node capability available across the playbook model, not a narrowed AI-only abstraction. The only thing scoped by executor type is the **Action FK** (some executors require Action; others prohibit it). Invariant 4 updated accordingly.

### Q4 — Lookup table fate
**RESOLVED: REPURPOSE** for now. `sprk_analysisactiontype` stays as a maker-friendly Action categorization / browsing surface. Loses its dispatch role (no longer load-bearing for runtime). Future evolution may use it as a template-cloning surface ("clone Summarize Action to start"). Don't drop yet; may be useful in other contexts.

### Q5 — Migration cutover strategy
**RESOLVED: BIG BANG.** No transition mode. No compatibility shim. R7 ships one cohesive migration: schema + code + data + docs. Owner directive: "nothing is really working now anyway" — clean cutover is acceptable risk. Wave 1 + Wave 2 + Wave 5 of §4 execute as one merge.

### Q6 — Backward compatibility
**RESOLVED: NOT a concern.** No legacy compatibility burden. Simplifies orchestrator, simplifies tests, simplifies docs. The R6/R5/R4 code paths that depended on the old dispatch model get deleted alongside the migration.

### Q7 — Naming
**RESOLVED:**
- Column on `sprk_playbooknode`: **`sprk_executortype`** (Choice; owner-created 2026-06-27)
- Global Choice set: **`sprk_playbookexecutortype`** (33 values; owner-created 2026-06-27)
- C# enum: `ActionType` → rename to `ExecutorType` (matches the column name and the semantic — "what executor handles this node")
- Variable usage: `node.ExecutorType` (was `node.ActionType` in some hot paths) — refactor as part of Wave 2

### Q8 — Typed field (`sprk_outputschemajson`) vs JPS-embedded (`output.fields`)
**RESOLVED: TYPED FIELD WINS.**

Honest analysis (per owner directive: don't make technical decisions just because of legacy JPS):

Typed `sprk_outputschemajson` column advantages:
- Queryable independently (e.g., "show me all Actions whose output has field 'summary'")
- Validatable at save time (separate constraint from prompt text)
- Versionable independently of prompt iterations
- Simpler tooling — schema editor in PlaybookBuilder reads/writes one field, not parses JPS
- Better PlaybookBuilder UX: dedicated "Output Schema" tab with typed editor

JPS-embedded `output.fields` advantages:
- One source of truth in one place (no schema/prompt drift)
- Self-contained Action (everything in one prompt blob)

**Decision**: typed field wins because PlaybookBuilder needs structured access. JPS `output.fields` becomes a denormalized hint for prompt rendering (read-only inside the prompt context); the canonical schema lives in `sprk_outputschemajson`. Spec scopes: schema → field; prompt → field (renderable). Loads structured-output schema from the column at runtime; prompt rendering may project the schema INTO the prompt for the LLM's benefit.

### Q9 — Customer-facing + AI-assisted authoring
**RESOLVED: YES, R7 scope.** AI-assisted authoring (Spaarke Claw copilot) IS R7 scope — the assistant is itself a playbook (eating our own dogfood). Customer onboarding tutorials are R7 deliverables, not afterthoughts. See §11 Spaarke Claw scope. The assistant + tutorials prove the architecture is teachable; without them, R7 is just plumbing.

### v0.4 resolutions (owner directives 2026-06-28)

- **Q10 — Canvas UX minimum scope**: **RESOLVED — categorized dropdown with descriptions** is R7 scope (NOT deferred to Action Engine). Each executor entry shows: tier prefix + name + 1-line description ("1 — AI Completion · Raw LLM call with prompt + structured output"). Descriptions sourced from C# enum XML doc comments or parallel metadata. The 33 values group into 6 tiers (0-9 AI / 10-19 Compute / 20-29 Mutations / 30-39 Control / 40-49 Delivery / 50+ Capability). Mega menu / fuzzy search / AI-assisted authoring remains Action Engine concern.
- **R4 graduation**: **RESOLVED — HOLD R4 in UAT until R7 ships AiCompletion.** No revert. R4 sits unclosed while R7 lands the proper dispatch model. Owner directive: "we'll hold r4 until it can be wired into the new work done here in r7; no need to revert to an approach that will be replaced by r7."
- **Q14 — R7 ↔ Action Engine timing**: **RESOLVED — Action Engine R1 HOLDS until R7 completes.** No parallel work. No merge. Action Engine resumes against the clean R7 foundation. Owner directive 2026-06-28.

### Still open (will resolve in spec.md)

- **Q11 — Per-user Action variants**: with Q2 resolved (per-node overrides KEPT), per-user personalization can layer on `PromptSchemaOverrideMerger` at the consumer/agent surface. Action Engine may want a user-scoped Action variant model in addition. Defer to Action Engine R1 unless surfaces emerge during R7 build.
- **Q12 — Migration of existing `chat-summarize` call**: currently uses the legacy direct path. R7 cutover migrates it through playbook dispatch. Spec scope (migration plan).
- **Q13 — Multi-tenant Playbook Library scoping**: which playbooks are system / per-tenant / per-user? Discovery rules? Defer to Action Engine for the multi-tenant agent model; R7 ships single-tenant Library wiring.
- **Q15 — `sprk_playbookconsumer` documentation gap**: RESOLVED 2026-06-28. The entity has architectural coverage (`docs/architecture/ai-architecture-consumer-routing.md`, owned by chat-routing-redesign-r1 follow-on). R7 owns the **maker-facing tutorial** (`docs/guides/ai-guide-consumer-wiring.md`) because the consumer-driven authoring model is R7's reframe. The `docs/data-model/` entry is OUT of R7 scope (different ownership).

---

## 9. Risks

| Risk | Mitigation |
|---|---|
| Migration affects many existing playbooks (Insights, chat, daily-briefing, etc.) | Build a comprehensive migration script + per-environment dry-run report + idempotent rerun |
| `chat-summarize` direct path is heavily exercised; migration could regress chat | Smoke-test chat first; consider feature-flagging the migration |
| Maker-facing documentation rewrite is large | Track in §6 deliverables; assign to a dedicated documentation task wave |
| Existing publicized canonical-truth docs (`ai-architecture-playbook-runtime.md` etc., commits `f91981965` + `404012169`) get superseded; risk of stale content shipping in `master` | Mark superseded sections with banner pointing at R7; remove or update on R7 ship |
| The R4 control-flow executors (Start, LoadKnowledge, ReturnResponse) we just built solve a problem that R7 makes simpler (explicit node dispatch). They still earn their keep but their "name detection" code paths get removed in R7. | Keep the executors; delete the matchers in `PlaybookOrchestrationService` |
| If R7 takes 6+ weeks, R4's `/narrate` UAT remains unclosable (no `AiCompletion` executor in old architecture) | Decision: revert `/narrate` to R3 inline LLM call for R4 graduation; build AiCompletion correctly in R7. Document this in R4 wrap-up. |
| Multi-tenant customer deploys could break if migration runs unevenly | Idempotent migration + dry-run audit per tenant + canary tenant before full rollout |

---

## 10. R7 scope boundaries (what's NOT in this project)

- Tool/Handler refactor for multi-step AI flows — stays as today, just scoped explicitly
- AI Search / RAG architecture — unrelated; addressed in `spaarke-ai-search-r1` (separate project)
- Consumer routing (Path A.5) — unchanged; the consumer/playbook layer is decoupled from the dispatch reform
- PCF / widget UX — unchanged
- New AI features beyond `/narrate` and existing playbooks — out of scope; this project unblocks them, doesn't ship them

---

## 11. Playbook Builder UI updates (R7 scope) + relationship to Action Engine R1

### What R7 owns — Playbook Builder UI updates (existing Code Page canvas)

**These are R7 scope** because the dispatch reform breaks the existing Playbook Builder canvas + model-driven form until they're updated.

| Update | What it delivers | Why R7-scoped |
|---|---|---|
| **Replace Node Type field with Executor Type selector** in the model-driven Playbook Node form | Maker picks from 33 executor types (Choice `sprk_executortype` populated). | Direct consequence of §3 schema change. Without this, the form is broken. |
| **Replace canvas "Node Types" panel** (currently lists ~11 named types) with full 33-executor selector | Maker can add ANY executor as a canvas node, not just the curated ~11. Currently the canvas omits LookupUserMembership, QueryDataverse, CreateNotification, and others. | Without this, the canvas can't author the playbooks the runtime supports. |
| **Add description + tier to each executor entry** in the selector | Maker sees "AI Completion — Raw LLM call with prompt + structured output" not just "AI Completion". Avoid the "user doesn't know what this does" failure mode (owner point 1b, 2026-06-28). | Usability requirement. The descriptions come from C# enum XML doc comments or a parallel metadata source. |
| **Render typed config form per executor** (Invariant 5) | Canvas drops free-form JSON editing in favor of typed forms driven by each executor's declared schema. | Direct consequence of Invariant 5. |
| **Promote Action selection to its own "Action" tab** in node properties | Currently buried in Overview tab. Per Q2 resolution, Action + Executor type live together in the Action tab. | UI usability + maker mental model alignment. |
| **Keep per-node Prompt tab + override capability** | Role/Task/Constraints/Output Fields persist on the node via `PromptSchemaOverrideMerger` (per Q2 resolution — KEPT). Acts as the personalization layer. | Owner directive 2026-06-28: per-node overrides stay. |
| **Replace `sprk_nodetype` references in canvas** with `sprk_executortype` | The C# `NodeType` enum (if retained internally) drives graph categorization for visual grouping; it is not exposed to makers. | Direct consequence of §3 — `sprk_nodetype` column removed. |

**Out of scope for R7 (Action Engine territory)**:
- Mega menu with fuzzy search across executors + Tools + scopes
- AI-assisted authoring copilot inside the canvas
- Templates browser ("clone Summarize playbook")
- Quick-start wizards
- Customer-facing tutorials
- Agent-level UX (agent definition, agent run history, agent monitoring)

### What Action Engine R1 owns (R7 does NOT)

See [`projects/ai-spaarke-action-engine-r1/`](../ai-spaarke-action-engine-r1/) for the full scope:

- The "Action" concept that sits ABOVE Playbook (Action → Playbook FK)
- `ActionTemplate`, `ActionInstance`, `ActionRun`, `Monitor` entities
- Tool Registry classification (Deterministic / AI / Hybrid; CostClass; LatencyClass; PhaseRestrictions)
- Three meta-tools: `FindResources`, `GetResourceDetail`, `InvokeResource`
- `IGateResolver` (EthicsCritical / MeaningCritical / FinalDelivery / EngagementAcceptance / TeamSelection / Custom)
- Phase deny-tools enforcement at `IToolHandlerRegistry` dispatch
- Three starter Action Templates: Summarize Matter / Weekly Task Digest / Find Similar Matters
- Conversational + explicit + scheduled invocation paths
- "Spaarke Claw" branding / customer-facing agent UX
- Agent concept (uses one or more playbooks via consumers)

### Coordination point

R7 ships the dispatch + schema + Playbook Builder updates. Action Engine wraps that foundation with agent-level abstractions, Tool Registry classification, and the customer-facing agent UX. Owner directive 2026-06-28: Action Engine holds until R7 completes (Q14).

### Future awareness — paused January/February 2026 effort

A separate effort to build a NEW Spaarke user-facing playbook builder UX was paused in early 2026. That effort is orthogonal to the EXISTING Playbook Builder Code Page (which R7 updates). When revived, it would build on top of R7's foundation (typed schemas + clean dispatch + consumer-driven model). R7 does NOT scope that future revival — it just makes the runway clear.

---

## 12. Predecessor relationship

This project SUPERSEDES the architectural intent of R6, R5, R4, R3, R2, R1 (all named "ai-platform-unification"). Each prior iteration addressed a slice:

- R1: initial dispatch surface
- R2: scope resolution
- R3: chat refactor
- R4: subscriptions & briefing
- R5: cache + redis
- R6: knowledge sources
- (and R4 daily-update-service-r4 which forced the reckoning during UAT)

R7 is the **foundational reframe** that prior R's worked around. The prior projects are not undone — their per-slice deliverables stand. But the dispatch model R7 establishes is the new floor.

---

## 13. What happens next (post-design)

1. **Owner review** of this design.md
2. Convert to spec.md per `design-to-spec` skill — FRs, NFRs, acceptance criteria, MUST/MUST-NOT rules
3. Run `project-pipeline` for plan + task decomposition + project artifacts
4. Wave 0 — documentation review + tech-debt inventory (concrete file list, line numbers)
5. Wave 1 — schema migration (Phases 1-2 from §4)
6. Wave 2 — orchestrator migration (Phase 2)
7. Wave 3 — legacy-path deprecation (Phase 3)
8. Wave 4 — `AiCompletionNodeExecutor` build + `/narrate` closure
9. Wave 5 — playbook backfill (Phase 5)
10. Wave 6 — documentation alignment + tech debt removal
11. Wave 7 — customer-facing playbook authoring guide
12. Wrap — merge to master + portfolio archive

---

## 14. References

### Code (current state)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` — ActionType enum (33 values)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs:1252-1342` — dispatch ladder
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisActionService.cs:57-67` — Action's ActionType resolution via lookup chain
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs:534` — `ApplyPromptSchemaOverride` (node overrides — keep)
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — legacy direct path (deprecate)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/NodeExecutorRegistry.cs` — DI registry

### Canonical-truth docs (R4 — partially superseded by R7)
- [`docs/architecture/ai-architecture-playbook-runtime.md`](../../docs/architecture/ai-architecture-playbook-runtime.md)
- [`docs/architecture/ai-architecture-consumer-routing.md`](../../docs/architecture/ai-architecture-consumer-routing.md)
- [`docs/architecture/ai-architecture-actions-nodes-scopes.md`](../../docs/architecture/ai-architecture-actions-nodes-scopes.md)
- [`docs/guides/ai-guide-playbook-deploy-recipe.md`](../../docs/guides/ai-guide-playbook-deploy-recipe.md)

### Sibling project (UAT that forced this)
- [`projects/spaarke-daily-update-service-r4/`](../spaarke-daily-update-service-r4/)
- [`projects/spaarke-daily-update-service-r4/notes/canonical-truth/01-code-archaeology.md`](../spaarke-daily-update-service-r4/notes/canonical-truth/01-code-archaeology.md)
- [`projects/spaarke-daily-update-service-r4/notes/canonical-truth/02-docs-survey.md`](../spaarke-daily-update-service-r4/notes/canonical-truth/02-docs-survey.md)

### Persistent memory
- `~/.claude/projects/c--code-files-spaarke-wt-spaarke-daily-update-service-r4/memory/spaarke-ai-canonical-truth-principle.md`

### Working session (2026-06-26) where this reframe emerged
- Owner / Claude discussion thread (R4 worktree session, post-`09f5b24c1` checkpoint)

---

*Draft v0. Iterate before /design-to-spec.*
