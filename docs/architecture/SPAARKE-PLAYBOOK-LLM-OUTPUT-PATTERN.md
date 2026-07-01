# Spaarke Playbook-driven LLM Output Pattern

> **Audience**: BFF + AI platform developers building new narrative-output consumers (Daily Briefing, Insight Engine, work assignment briefings, project status updates, document review summaries, and any future Workspace UX feature that needs an LLM to produce structured output from runtime data).
> **Last Updated**: 2026-06-29 (R7 Wave 11 task 111a — operator-required for Insights Engine and other narrative consumers)
> **Source ADRs**: [ADR-013](../adr/ADR-013-bff-ai-architecture.md) (BFF AI Architecture), [ADR-037](../adr/ADR-037-multinode-output-composition.md) (Multinode Output Composition), [ADR-010](../adr/ADR-010-di-minimalism.md) (DI Minimalism)

---

## 1. Purpose

A single, centralized pattern for **passing runtime data into LLM prompts via the playbook engine** without coupling the prompt-instruction text to the data shape. The pattern decomposes into:

1. **Layer 1 (universal)** — orchestrator-level Handlebars template resolution against `RunContext.NodeOutputs + Parameters + run` metadata. Every executor's configJson gets `{{X}}` resolution.
2. **Layer 2 (AI-specific)** — `PromptSchemaRenderer` renders a structured `## Input` section between Context and Document. AI executors pass resolved `configJson.inputBinding` as a JsonElement to the renderer.

A new "narrative output" consumer (Insight Engine matter summary, project briefing, etc.) = **1 Action JPS + 1 playbook + 1 destination node**. Zero new C#.

---

## 2. Why this exists

Pre-Wave-11 R7, the BFF orchestrator only did literal `{{paramName}}` substitution from the playbook run's `Parameters` dictionary. Node outputs (`{{tldrResult.summary}}`, `{{start.channels}}`, `{{json start}}`) were never resolved. AI executors (AiCompletion, AiAnalysis) read only `configJson.templateParameters` — they ignored the `inputBinding` shape playbook authors had been writing. So `/narrate` returned HTTP 200 with empty `summary` / `keyTakeaways[]` / `channelNarratives[]` because the LLM nodes received literal `{{json start}}` text instead of resolved data.

R1/R2/R3 Daily Briefing worked because its `HandleNarrate` was pure-C# composition calling `IOpenAiClient.GetCompletionAsync` directly — the playbook engine was never involved. R4 migrated to playbook dispatch but the migration was incomplete. R7 Wave 11 closes the gap.

The structured `## Input` section design (Layer 2) is the "external function" boundary — instructions and data are separate, the LLM sees them in distinct prompt sections, and the prompt body never embeds data shape.

---

## 3. Two-layer architecture

```
┌─────────────────────────────────────────────────────────────┐
│ Layer 1: Orchestrator-level template resolution (UNIVERSAL)  │
│                                                              │
│   PlaybookOrchestrationService.ApplyConfigJsonTemplates      │
│     ├─ PlaybookTemplateContextBuilder.Build(runContext)      │
│     │    Builds Dictionary<string, object?> from:            │
│     │      • NodeOutputs (each prior node's OutputVariable)  │
│     │      • Parameters (BFF wrapper-provided scalars)       │
│     │      • run metadata bag                                │
│     └─ ITemplateEngine.Render(node.ConfigJson, context)      │
│                                                              │
│   Result: node.ConfigJson has all {{X.Y.Z}} resolved at the  │
│   string level BEFORE the executor receives it.              │
└──────────────────────────┬───────────────────────────────────┘
                           ▼
              ┌───────────────────────────────┐
              │ Executor receives RESOLVED    │
              │ configJson — no templates left│
              └────────────┬──────────────────┘
       ┌───────────────────┼───────────────────────┐
       ▼                   ▼                       ▼
  AI executors        Load/Return            Other executors
  (Layer 2 below)     (resolved bindings;    (read resolved
                       per-executor          configJson fields;
                       BuildTemplateContext  no template code
                       calls shared helper)  needed)

┌─────────────────────────────────────────────────────────────┐
│ Layer 2: PromptSchemaRenderer ## Input section (AI ONLY)     │
│                                                              │
│   AiCompletionNodeExecutor (and future AiAnalysis per DEF-001) │
│     ├─ ExtractInputBindingAsJsonElement(node.ConfigJson)     │
│     │    Returns the resolved inputBinding object as a       │
│     │    JsonElement (or null if absent/malformed).          │
│     └─ _promptSchemaRenderer.Render(..., runtimeInput: ...)  │
│                                                              │
│   PromptSchemaRenderer.RenderJps                             │
│     Emits "## Input\n{indented json}\n" section between     │
│     Context (4) and Document (5) sections when runtimeInput  │
│     is non-null + non-Null/Undefined.                        │
└─────────────────────────────────────────────────────────────┘
```

**Key property**: the Action JPS body (`instruction.role/task/constraints/context`) stays pure instructions. Data lives in `## Input`. The LLM clearly sees the two as distinct.

---

## 4. Component model

| Component | Path | Role |
|---|---|---|
| `PlaybookTemplateContextBuilder` (static) | [`src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookTemplateContextBuilder.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookTemplateContextBuilder.cs) | Layer 1 — single source of truth for "what context can templates see". Two overloads: `Build(PlaybookRunContext)` (orchestrator) + `Build(NodeExecutionContext)` (per-executor). Both delegate to private `BuildCore`. |
| `PlaybookOrchestrationService.ApplyConfigJsonTemplates` | [`PlaybookOrchestrationService.cs:1921`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs#L1921) | Layer 1 caller — invokes the builder + `ITemplateEngine.Render` for every node before handing configJson to its executor. |
| `ITemplateEngine` + `TemplateEngine` (Handlebars.NET) | [`TemplateEngine.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs) | Renders `{{X}}` / `{{X.Y.Z}}` against the merged context. Already DI-registered as Singleton ([`AnalysisServicesModule.cs:860`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs#L860)). Custom helpers: `default`, `safe`, `joinIds`. |
| `LoadKnowledgeNodeExecutor.BuildTemplateContext` + `ReturnResponseNodeExecutor.BuildTemplateContext` | [`LoadKnowledgeNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LoadKnowledgeNodeExecutor.cs), [`ReturnResponseNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/ReturnResponseNodeExecutor.cs) | Per-executor resolvers — now thin delegations to `PlaybookTemplateContextBuilder.Build(NodeExecutionContext)`. Their own `_templateEngine.Render` calls become no-ops on already-resolved configJson. |
| `PromptSchemaRenderer.Render` (`runtimeInput` parameter + "## Input" section) | [`PromptSchemaRenderer.cs:72`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs#L72) | Layer 2 — new `JsonElement? runtimeInput` parameter. When non-null, emits "## Input\n{indented json}" between Context and Document. |
| `AiCompletionNodeExecutor.ExtractInputBindingAsJsonElement` | [`AiCompletionNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiCompletionNodeExecutor.cs) (private static) | Layer 2 wiring — parses `configJson.inputBinding` (already template-resolved by Layer 1) into a `JsonElement` clone. Defensive null/malformed handling. Passed to renderer as `runtimeInput`. |
| `AiAnalysisNodeExecutor` (DEFERRED per [DEF-001](../../projects/spaarke-ai-platform-unification-r7/notes/defer-issues.md#def-001--wire-aianalysisnodeexecutor-to-wave-11-option-b-inputbinding-pattern)) | [`AiAnalysisNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs) | Same pattern; mechanical follow-up (~1-2 hours) when an AiAnalysis-based playbook consumer needs structured input. AiAnalysis goes through `ToolExecutionContext` to 4 tool handlers. |

---

## 5. Runtime data flow (6-step trace)

Worked example: `DAILY-BRIEFING-NARRATE.GenerateTldr` node.

**Setup** (before Wave 11):
- Playbook node configJson: `{"actionCode": "BRIEF-NARRATE-TLDR", "inputBinding": { "briefing": "{{json start}}" }}`
- Action JPS body (sprk_systemprompt): `instruction.task = "Read the structured input below and produce..."`; `input.parameters = { description: "..." }`

**Runtime**:

```
1. BFF /narrate wrapper builds PlaybookRunContext with:
     Parameters = { briefingPayload: "<serialized request>", ... }
     NodeOutputs (initially empty)

2. Orchestrator runs Start node → registers NodeOutputs["start"] = { categories, priorityItems, channels, totalNotificationCount, ... }

3. Orchestrator advances to GenerateTldr node:
     ApplyConfigJsonTemplates(node, runContext):
       context = PlaybookTemplateContextBuilder.Build(runContext)
         = { start: {...}, briefingPayload: "<...>", run: {...} }
       rendered = _templateEngine.Render(
         '{"actionCode":"BRIEF-NARRATE-TLDR","inputBinding":{"briefing":"{{json start}}"}}',
         context)
       → '{"actionCode":"BRIEF-NARRATE-TLDR","inputBinding":{"briefing":"{\"categories\":[...]...}"}}'

4. AiCompletionNodeExecutor.ExecuteAsync(context with resolved configJson):
     runtimeInput = ExtractInputBindingAsJsonElement(configJson)
       = JsonElement { briefing: { categories: [...], priorityItems: [...], ... } }
     rendered = _promptSchemaRenderer.Render(
         rawPrompt: actionSystemPrompt,
         ...,
         runtimeInput: runtimeInput)

5. PromptSchemaRenderer.RenderJps assembles final prompt:
     "notification summarizer

      Read the structured input below and produce ...

      ## Constraints
      1. Use only entity names present in the input.
      ...

      Test context

      ## Input
      {
        "briefing": {
          "categories": [...],
          "priorityItems": [...],
          ...
        }
      }
      "

6. LLM call (GetStructuredCompletionRawAsync) returns constrained JSON:
     { summary: "5 notifications across 2 categories...", keyTakeaways: [...], ... }
   → NodeOutput.StructuredData → runContext.StoreNodeOutput("tldrResult", ...)
   → downstream nodes (ValidateEntityNames, ReturnResponse) reference
     {{tldrResult.summary}}, {{tldrResult.keyTakeaways}}, etc. via Layer 1
```

---

## 6. What's centralized vs what's per-consumer

| Layer | What | Where centralized |
|---|---|---|
| Template resolution | Universal — every executor's configJson gets `{{X}}` resolution | `PlaybookTemplateContextBuilder` + `PlaybookOrchestrationService.ApplyConfigJsonTemplates` (one place) |
| Run metadata bag | Universal — `{{run.id}}`, `{{run.playbookId}}`, `{{run.tenantId}}`, `{{run.startedAt}}`, `{{run.completedAtUtc}}` | `PlaybookTemplateContextBuilder.BuildCore` |
| `## Input` section assembly | AI-only — applies to any executor that calls `PromptSchemaRenderer.Render` | `PromptSchemaRenderer.RenderJps` (one place; runtimeInput passed in per call) |
| `inputBinding` → `JsonElement` extraction | AI-only — per-executor (currently AiCompletion; AiAnalysis deferred per DEF-001) | Each AI executor has its own private static `ExtractInputBindingAsJsonElement` (5-20 LOC; mechanical) |

**A new "narrative output" consumer adds**:
- 1 Action JPS (sprk_analysisaction row) — declarative; no code
- 1 playbook (sprk_analysisplaybook + sprk_playbooknode rows) — declarative; no code
- 1 destination node config (UpdateRecord / ReturnResponse / SendEmail / CreateNotification) — declarative; no code

**Zero new C# per consumer.** This is the right primitive for the Workspace UX narrative-output rollout.

---

## 7. When to use this pattern

| Scenario | Use this pattern? |
|---|---|
| LLM produces structured output from playbook runtime data → display in widget / write to Dataverse field / send via email | ✅ Yes |
| LLM produces structured output from a single Dataverse document (RAG, classification) | Consider AiAnalysis instead — it has knowledge-source + tool-handler integration (lift DEF-001 if structured input also needed) |
| Pure C# computation (no LLM) | No LLM, no pattern. Write straight C#. |
| One-shot LLM call from a single endpoint, no playbook composition needed | Consider direct `IOpenAiClient.GetStructuredCompletionRawAsync` call (R3 Daily Briefing's pre-R4 approach) — but verify the AI Architecture team approves; the playbook engine is the canonical surface for narrative outputs |
| Chat session with multi-turn context, RAG retrieval, tools | Use the SprkChat system instead (out of scope here) |

---

## 8. Failure modes

| Symptom | Likely cause | Where to look |
|---|---|---|
| `/narrate` returns HTTP 200 with empty `summary` / `keyTakeaways` | Action JPS body references `{{X}}` that's never substituted; OR playbook configJson `inputBinding` is missing | (1) Verify Action JPS body uses `## Input` (no `{{X}}` in `instruction.task` for data); (2) Verify playbook node configJson has `inputBinding` with the right keys |
| `{{nodeOutput.field}}` renders as empty string | Prior node didn't run, OR its OutputVariable is misspelled, OR its output had no StructuredData | Check orchestrator logs for prior-node completion + StoreNodeOutput; verify `OutputVariable` spelling matches the `{{...}}` reference |
| `## Input` section not present in LLM prompt | AI executor isn't passing `runtimeInput`, OR `configJson.inputBinding` is malformed/missing | Check AI executor logs for `ExtractInputBindingAsJsonElement` warnings; verify configJson includes `inputBinding` object after Layer 1 resolution |
| `inputBinding.X` carries literal `{{json Y}}` instead of resolved value | Orchestrator template engine not running, OR ITemplateEngine not injected | Check that `PlaybookOrchestrationService` constructor receives a non-null ITemplateEngine; verify DI registration at `AnalysisServicesModule.cs:860` |
| LLM produces output with fields not in your schema | Action's `sprk_outputschemajson` column is missing or stale; structured-completion isn't constrained | Run `scripts/dataverse/Sync-BriefNarrateOutputSchemas.ps1` pattern for your Action — output schema must be PATCHed on the row |
| Template uses helper like `{{map X 'field'}}` that doesn't render | Custom Handlebars helper not registered (Wave 11 T112 adds the standard 6: json, map, flatten, distinct, concat, join) | Check `TemplateEngine.cs` for the helper's `RegisterHelper` call; add it if missing |
| Lambda / pipe syntax (`(lambda c ...)`, `(... \| flatten)`) in source playbook | These are NOT valid Handlebars; rewrite using nested subexpressions and the `flatMap` helper (T113) | See T113 in [`projects/spaarke-ai-platform-unification-r7/tasks/113-eliminate-lambda-via-flatmap-helper.poml`](../../projects/spaarke-ai-platform-unification-r7/tasks/113-eliminate-lambda-via-flatmap-helper.poml) for the rewrite pattern |
| Fan-out iteration (`iteration.iterateOver` + `itemAlias`) executor only called once | Wave 11 T114 implements iteration semantics; pre-T114 the orchestrator ignores `iteration` config | Check the orchestrator version + the configJson — single-call execution is the pre-T114 behavior |

---

## 9. Where to start reading code

| Goal | Start here |
|---|---|
| Understand the orchestrator's template-render path | [`PlaybookOrchestrationService.ApplyConfigJsonTemplates` line 1921](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs#L1921) |
| Understand the merged context shape | [`PlaybookTemplateContextBuilder.BuildCore`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookTemplateContextBuilder.cs) |
| Understand the `## Input` section render | [`PromptSchemaRenderer.RenderJps` line 211 area](../../src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs#L211) — search for "Runtime Input" comment |
| Understand AI executor inputBinding extraction | [`AiCompletionNodeExecutor.ExtractInputBindingAsJsonElement`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiCompletionNodeExecutor.cs) (private static helper) |
| Tests covering the pattern | [`PlaybookTemplateContextBuilderTests`](../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PlaybookTemplateContextBuilderTests.cs), [`PromptSchemaRenderer_InputSectionTests`](../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PromptSchemaRenderer_InputSectionTests.cs) |
| Reference: how to author a new consumer | [`docs/guides/BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md`](../guides/BUILD-A-NEW-NARRATIVE-OUTPUT-CONSUMER.md) |

---

## 10. ADR + constraint pointers

- **[ADR-013 BFF AI Architecture](../adr/ADR-013-bff-ai-architecture.md)** — IInvokePlaybookAi triangle stays canonical; orchestrator is internal AI service
- **[ADR-037 Multinode Output Composition](../adr/ADR-037-multinode-output-composition.md)** — composeStrategy semantics; fan-out iteration belongs in orchestrator (Wave 11 T114)
- **[ADR-010 DI Minimalism](../adr/ADR-010-di-minimalism.md)** — no new abstractions; ITemplateEngine constructor-injected only
- **[ADR-029 BFF Publish Hygiene](../adr/ADR-029-bff-publish-hygiene.md)** — per-task publish-size + CVE on BFF-touching tasks
- **[`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md)** — binding pre-merge checklist for BFF additions

---

## 11. Related docs

- **[`AI-ARCHITECTURE.md`](AI-ARCHITECTURE.md)** — overall BFF AI subsystem architecture
- **[`ai-architecture-playbook-runtime.md`](ai-architecture-playbook-runtime.md)** — playbook runtime contract (executor dispatch, scope resolution, node lifecycle)
- **[`ai-architecture-actions-nodes-scopes.md`](ai-architecture-actions-nodes-scopes.md)** — Actions vs Nodes vs Scopes vs Skills/Knowledge — config-home decision tree
- **[`ai-architecture-consumer-routing.md`](ai-architecture-consumer-routing.md)** — Path A.5 dispatch via `IConsumerRoutingService` → `IInvokePlaybookAi`
- **[`JPS-AUTHORING-GUIDE.md`](../guides/JPS-AUTHORING-GUIDE.md)** — Action JPS body authoring
- **[`PLAYBOOK-AUTHOR-GUIDE.md`](../guides/PLAYBOOK-AUTHOR-GUIDE.md)** — Playbook node + scope authoring
- **[`ai-guide-playbook-deploy-recipe.md`](../guides/ai-guide-playbook-deploy-recipe.md)** — Deploy-Playbook.ps1 contract
- **[`ai-guide-consumer-wiring.md`](../guides/ai-guide-consumer-wiring.md)** — `sprk_playbookconsumer` wiring (R7 FR-31)
- **[Wave 11 design spike](../../projects/spaarke-ai-platform-unification-r7/notes/spikes/wave11-orchestrator-resolution-design.md)** — R7 design rationale + audit findings

---

## 12. Document changelog

| Date | Change | Trigger |
|---|---|---|
| 2026-06-29 | Initial — Wave 11 task 111a per operator binding requirement ("we will need it for Insights Engine and many other areas"). Documents the Layer 1 + Layer 2 architecture shipped by T111. | R7 Wave 11 |
