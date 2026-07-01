# Playbook Author Guide

> **Status**: Updated for R7 (2026-06-28) — node-first dispatch model. R3 content (LookupUserMembership, Handlebars helpers, Builder UI safety affordances) preserved verbatim and remains current.
> **Audience**: Spaarke makers + operators + AI engineers authoring multi-node playbooks in PlaybookBuilder. Plain-language guide — assumes you know your way around Power Apps but NOT around .NET or React internals.
>
> **Scope of this guide**: maker-facing recipe for authoring playbooks via the visual canvas (PlaybookBuilder) AND the JSON-input file consumed by `Deploy-Playbook.ps1`. For the script's input file format + 12-step procedure, see [`ai-guide-playbook-deploy-recipe.md`](ai-guide-playbook-deploy-recipe.md). For runtime semantics (single-hop dispatch, executor selection), see [`../architecture/ai-architecture-playbook-runtime.md`](../architecture/ai-architecture-playbook-runtime.md). For "where does this config field belong" decisions, see [`../architecture/ai-architecture-actions-nodes-scopes.md`](../architecture/ai-architecture-actions-nodes-scopes.md). For JPS (JSON Prompt Schema) authoring at the prompt-template level, see [`JPS-AUTHORING-GUIDE.md`](JPS-AUTHORING-GUIDE.md).

---

## Why this guide changed in R7 (read this first)

Pre-R7, authoring a playbook meant **picking an Action first**, dragging it onto the canvas, and trusting a multi-rung lookup chain (`node.actionid → Action.actiontypeid → sprk_analysisactiontype.sprk_executoractiontype`) plus a structural-fallback ladder (`IsDeployedStartNode` / `IsDeployedLoadKnowledgeNode` / `IsDeployedReturnResponseNode`) to figure out which C# executor would run at runtime. Two side effects: (1) authors had to internalize a three-layer dispatch model that lived only in code, and (2) "what executor does this node use?" was answerable only by reading orchestrator source. R7 collapses all of that into a **single column on the node row** — `sprk_playbooknode.sprk_executortype` (an explicit Choice of 33 values) — and inverts the authoring flow to **node-first**: you pick the executor per node FIRST, then attach an Action only when the executor is prompt-driven (AiAnalysis / AiCompletion / AiEmbedding) so the Action can carry the SystemPrompt + OutputSchema + Temperature. The lookup table (`sprk_analysisactiontype`) survives as **decorative maker categorization** (Q4) but is no longer consulted at runtime. The dispatch hop count went from three to one, and the playbook now declares its own runtime shape on the row rather than the orchestrator inferring it. See [FR-07](../../projects/spaarke-ai-platform-unification-r7/spec.md) (single-hop dispatch), [FR-12](../../projects/spaarke-ai-platform-unification-r7/spec.md) (AiCompletionNodeExecutor), [FR-15](../../projects/spaarke-ai-platform-unification-r7/spec.md) (R4 `/narrate` graduation), [FR-30](../../projects/spaarke-ai-platform-unification-r7/spec.md) (this guide's update), root [`CLAUDE.md` §10 BFF Hygiene](../../CLAUDE.md#10-bff-hygiene--binding-governance-read-before-adding-to-sprkbffapi), and [`.claude/constraints/bff-extensions.md` §G](../../.claude/constraints/bff-extensions.md#g-actions--nodes--scopes--configjson-boundary-binding-per-r4-canonical-truth-loop-2026-06-26).

---

## Table of Contents

- [What This Guide Covers](#what-this-guide-covers)
- [What a Playbook Is (Plain Language)](#what-a-playbook-is-plain-language)
- [Authoring a Playbook in 5 Steps (Node-First Dispatch)](#authoring-a-playbook-in-5-steps-node-first-dispatch)
- [Decision Points — Which Executor Per Node?](#decision-points--which-executor-per-node)
- [Per-Node Prompt Overrides (Q2 KEEP)](#per-node-prompt-overrides-q2-keep)
- [Worked Example 1 — 3-Node Prompt-Driven Playbook (Closes R4 `/narrate`)](#worked-example-1--3-node-prompt-driven-playbook-closes-r4-narrate)
- [Worked Example 2 — Multi-Node Insights Playbook (Mix of Pure + Prompt-Driven)](#worked-example-2--multi-node-insights-playbook-mix-of-pure--prompt-driven)
- [Worked Example 3 — Condition / Branching Playbook](#worked-example-3--condition--branching-playbook)
- [What's New in R3 (At a Glance)](#whats-new-in-r3-at-a-glance)
- [Quick-Start Recipe: Notify Me About New Documents on My Matters](#quick-start-recipe-notify-me-about-new-documents-on-my-matters)
- [Node Catalog (R3 Update)](#node-catalog-r3-update)
- [Handlebars Template Helpers (R3 Update)](#handlebars-template-helpers-r3-update)
- [Builder UI Safety Affordances (R3)](#builder-ui-safety-affordances-r3)
- [Anti-Patterns to Avoid](#anti-patterns-to-avoid)
- [Migration: Replacing Broken FetchXML with LookupUserMembership](#migration-replacing-broken-fetchxml-with-lookupusermembership)
- [Smoke-Test a Playbook Before Deploying](#smoke-test-a-playbook-before-deploying)
- [Related Documentation](#related-documentation)

---

## What This Guide Covers

This guide walks you through authoring a multi-node playbook end-to-end, both in PlaybookBuilder (visual canvas) and via a `Deploy-Playbook.ps1` JSON input file. It is the maker-facing companion to [`JPS-AUTHORING-GUIDE.md`](JPS-AUTHORING-GUIDE.md) (which covers JPS at the prompt-template / Action level — what the LLM sees) and to the [actions-nodes-scopes boundary doc](../architecture/ai-architecture-actions-nodes-scopes.md) (which covers "where does this config field belong"). After R7, two flows live in this guide:

1. **Node-first dispatch model (R7, §"Authoring a Playbook in 5 Steps")**: pick the executor per node FIRST, then choose an Action when the executor is prompt-driven, then configure typed config, then wire edges + scope, then deploy. This is the canonical authoring flow for every NEW playbook.
2. **R3 visual-canvas recipe (preserved verbatim, §"Quick-Start Recipe")**: a fully worked notification playbook using `LookupUserMembership` + `joinIds` + `Condition` + `CreateNotification`. The R3 recipe is still current — it just operates on top of the R7 dispatch model (every node carries `sprk_executortype` whether you set it explicitly or PlaybookBuilder defaults it from the palette item you dragged).

You don't write code. You drag, connect, fill in forms — OR you author JSON and run `Deploy-Playbook.ps1`. PlaybookBuilder validates your work as you go and warns you when something is likely to break.

---

## What a Playbook Is (Plain Language)

A playbook is a **visual workflow**. You drag nodes from a palette onto a canvas, connect them with lines (edges), and configure each node by clicking it and filling in a form. PlaybookBuilder takes care of two things behind the scenes:

1. **Saving** — every change is written to Dataverse (auto-save every 30 seconds, or Ctrl+S to save immediately). The canvas itself lives in a JSON blob on the playbook record; each node also gets its own `sprk_playbooknode` row so the server can execute them. In R7, every saved node row carries an explicit `sprk_executortype` value declaring its dispatch identity.
2. **Executing** — when the playbook runs (either on a schedule, when a user clicks something, or when an upstream consumer kicks it off via `IConsumerRoutingService` → `IInvokePlaybookAi`), the server walks the nodes in dependency order. Each node produces a piece of output that downstream nodes can reference with the `{{nodeName.output.field}}` template syntax. Dispatch is **single-hop**: the orchestrator reads `node.sprk_executortype` once and routes to the matching executor in C#. No fallback chain, no name-detection, no override branches.

You don't write code. You drag, connect, and fill in forms. PlaybookBuilder validates your work as you go and warns you when something is likely to break.

---

## Authoring a Playbook in 5 Steps (Node-First Dispatch)

The canonical R7 authoring flow. Same five steps whether you use PlaybookBuilder (canvas) or hand-author a JSON input file for `Deploy-Playbook.ps1`.

### Step 1 — Identify nodes and pick `sprk_executortype` for each

Write down the nodes your playbook needs (Start → ... → end). For **each node**, decide which C# executor handles it — that's the `ExecutorType` enum value you write to `sprk_playbooknode.sprk_executortype`. There are 33 executor types in five clusters. The [decision points](#decision-points--which-executor-per-node) section below walks you through picking the right one; the full per-executor reference lives in [`ai-architecture-actions-nodes-scopes.md` §8](../architecture/ai-architecture-actions-nodes-scopes.md).

| Cluster | When to use | Examples |
|---|---|---|
| **Prompt-driven AI** | Node sends prompt to LLM, consumes structured response | `AiAnalysis` (1500), `AiCompletion` (1), `AiEmbedding` |
| **Structural / lifecycle** | Anchors the graph or controls flow | `Start`, `ReturnResponse`, `LoadKnowledge` |
| **Data I/O** | Reads / writes Dataverse, SPE, external data | `QueryDataverse`, `CreateRecord`, `UpdateRecord` |
| **Composition / delivery** | Assembles outputs for the consumer | `DeliverComposite`, `DeliverOutput`, `CreateNotification` |
| **Control flow** | Decision / branching | `Condition` (30), `LookupUserMembership` (52), `EntityNameValidator` |

> **Rule of thumb**: every executor is either **prompt-driven** (needs Action FK for prompt template) or **pure** (config-only, no Action FK). Step 2 only matters for prompt-driven nodes.

### Step 2 — For prompt-driven nodes, choose an Action

If your executor is `AiAnalysis` / `AiCompletion` / `AiEmbedding`, attach an **Action** — a row in `sprk_analysisaction` that carries: **SystemPrompt** (`sprk_systemprompt`, flat text or JPS JSON — see [`JPS-AUTHORING-GUIDE.md`](JPS-AUTHORING-GUIDE.md)), **OutputSchemaJson** (`sprk_outputschemajson`), and **Temperature** (`sprk_temperature`). Attach by setting the node's `sprk_actionid` lookup to the Action row's GUID. Action FK is **required** for prompt-driven executors (their `Validate()` enforces it with a clear `requires an Action FK` error) and **prohibited** for pure executors (Condition / Start / ReturnResponse / QueryDataverse — `Validate()` rejects it).

For **pure executors**, skip Step 2 — leave `sprk_actionid` null. The lookup table (`sprk_analysisactiontype`) remains visible in the maker UI as decorative categorization (per Q4 / [§8a](../architecture/ai-architecture-actions-nodes-scopes.md)) but is never consulted at runtime.

> **Action reusability**: one Action row → many nodes across many playbooks. Per-instance variation goes in `configJson.promptSchemaOverride` (see [Per-Node Prompt Overrides](#per-node-prompt-overrides-q2-keep)). Don't author one Action per node — that's an anti-pattern.

### Step 3 — Configure each node's typed config

Each executor declares a **typed config schema** (per [FR-16](../../projects/spaarke-ai-platform-unification-r7/spec.md)) for the fields it consumes from `sprk_playbooknode.sprk_configjson`. PlaybookBuilder renders the schema as a form; the BFF endpoint `GET /api/ai/playbook-builder/executor-config-schemas` serves the schema set. Universal fields per node:

| Field | Required | Purpose |
|---|---|---|
| `sprk_executortype` | yes | Dispatch identity |
| `sprk_actionid` | conditional | Required for prompt-driven, prohibited for pure |
| `sprk_outputvariable` | yes | Canvas name for `{{nodeName.output.*}}` references downstream |
| `sprk_configjson` | per-executor | Typed config fields (varies by `ExecutorType`) |

Examples of executor-specific `configJson`: `QueryDataverse` needs `entityType` + `fetchXml`; `Condition` needs `condition` (boolean expr) + optional `trueBranch` / `falseBranch` labels; `LookupUserMembership` needs `entityType` + `roles`. See [§3 canonical contents](../architecture/ai-architecture-actions-nodes-scopes.md) for the full list.

### Step 4 — Wire edges between nodes + scope variables

Connect nodes with edges. Each edge declares a data dependency; the orchestrator runs independent nodes in parallel (default 3-wide) and serializes connected pairs. The R3 [edge perf hint](#c-edge-perf-hint-advisory) flags edges that serialize without moving data.

Output Variables: each node sets an `sprk_outputvariable` name; downstream nodes reference upstream outputs as `{{upstreamName.output.field}}`. The R3 [rename guard](#a-outputvariable-rename-guard) auto-updates references on rename. The R3 [`joinIds`](#joinids-new-in-r3) and [`default`](#default-new-in-r3) Handlebars helpers stay current under R7.

Playbook-level scope (Home D): attach Actions / Skills / Knowledge / Tools via N:N relationships on the playbook row (`sprk_analysisplaybook_action`, `sprk_playbook_skill`, `sprk_playbook_knowledge`, `sprk_playbook_tool`); per-node scope variants attach via `sprk_playbooknode_{skill,knowledge,tool}`. Scope arrays are **advisory**, not enforcing (see [playbook-runtime §6](../architecture/ai-architecture-playbook-runtime.md)).

### Step 5 — Deploy (writes `sprk_executortype` explicitly per FR-20)

For JSON-authored playbooks, `Deploy-Playbook.ps1` writes each node row to Dataverse. Per [FR-20](../../projects/spaarke-ai-platform-unification-r7/spec.md), the script writes `sprk_executortype` **explicitly on every node row** — no name-detection, no `__actionType` injection. A node missing `sprk_executortype` fails the script's step-1 lint with a clear error.

For canvas playbooks, PlaybookBuilder writes `sprk_executortype` on save (from the palette item OR from your explicit dropdown selection). For pre-R7 playbooks, owner-reviewed backfill ([FR-19](../../projects/spaarke-ai-platform-unification-r7/spec.md)) ensures every node carries an explicit value.

See [`ai-guide-playbook-deploy-recipe.md`](ai-guide-playbook-deploy-recipe.md) for the full 12-step procedure + input file format.

---

## Decision Points — Which Executor Per Node?

Pre-R7, the recurring authoring question was "which ActionType per node?" — answered by lookup-chain hopping. R7 inverts it: the question is **"which executor per node?"** and the answer is one column on the node row.

The decision flow:

```
For each node in your playbook:
  1. What does this node DO at runtime?
     ├─ Anchor / lifecycle (Start, ReturnResponse)?      → structural executor cluster
     ├─ Send prompt to LLM, consume response?             → AiAnalysis / AiCompletion / AiEmbedding
     ├─ Read / write Dataverse or SPE?                    → data-I/O cluster
     ├─ Make a decision / branch?                         → control-flow cluster
     ├─ Assemble outputs for the consumer?                → composition / delivery cluster
     └─ None of the above?                                → re-read; the catalog covers all 33 cases

  2. Is the chosen executor prompt-driven?
     ├─ Yes → ALSO attach an Action (Step 2 above)
     └─ No  → skip Action; leave sprk_actionid null
```

| Cluster | ExecutorType values (selected) | Action FK | Common config fields |
|---|---|---|---|
| **Prompt-driven AI** | `AiAnalysis` (1500), `AiCompletion` (1), `AiEmbedding` | required | `templateParameters`, `promptSchemaOverride`, `knowledgeRetrieval`, `includeDocumentContext` |
| **Structural / lifecycle** | `Start`, `ReturnResponse`, `LoadKnowledge` | none | (most config-empty; LoadKnowledge takes scope refs) |
| **Data I/O** | `QueryDataverse`, `CreateRecord`, `UpdateRecord` | none | `entityType`, `fetchXml`, `parameters`, `recordPayload` |
| **Composition / delivery** | `DeliverComposite`, `DeliverOutput`, `CreateNotification` | none | `sections[]`; `outputTarget` + `format`; `title` + `body` + ~17 fields |
| **Control flow** | `Condition` (30), `LookupUserMembership` (52), `EntityNameValidator` | none | `condition`; `entityType` + `roles`; `candidateText` + `allowList` |

The full 33-value enumeration with cluster ranges + per-executor 1-line descriptions lives in the rewritten decision tree at [`ai-architecture-actions-nodes-scopes.md` §8](../architecture/ai-architecture-actions-nodes-scopes.md).

> **What about the legacy "ActionType" choice on the Action row?** Per [FR-03](../../projects/spaarke-ai-platform-unification-r7/spec.md) + [FR-04](../../projects/spaarke-ai-platform-unification-r7/spec.md), the `sprk_actiontypeid` lookup + `sprk_executoractiontype` INT columns on Action are DROPPED. The `sprk_analysisactiontype` lookup TABLE survives ([FR-05](../../projects/spaarke-ai-platform-unification-r7/spec.md)) as **decorative maker categorization** — never consulted at runtime. Translate any pre-R7 "ActionType-per-node" phrasing in old docs to "ExecutorType-per-node."

---

## Per-Node Prompt Overrides (Q2 KEEP)

R7 explicitly **preserves** per-node prompt overrides — this is the [Q2 KEEP decision in the R7 spec](../../projects/spaarke-ai-platform-unification-r7/spec.md). When a prompt-driven node runs, `PromptSchemaOverrideMerger` combines the Action's base prompt template with per-node override fields set in `sprk_configjson.promptSchemaOverride`. Overridable per-node: **Role**, **Task**, **Constraints**, **Output Fields**. NOT overridable: `sprk_outputschemajson` shape, `sprk_temperature`, executor type (the node owns its dispatch identity).

The override mechanism implements the **template-reuse pattern**: one Action carries a generic prompt; many nodes reuse it with per-instance tweaks. Don't author a new Action for every minor variation. PlaybookBuilder surfaces overrides under the **Prompt** tab (KEPT per spec); JSON authors place them under `configJson.promptSchemaOverride`:

```jsonc
{
  "sprk_executortype": 1,                        // AiCompletion
  "sprk_actionid": "{BRIEF-NARRATE-TLDR}",
  "outputVariable": "narrative",
  "configJson": {
    "templateParameters": { "userName": "{{run.userName}}" },
    "promptSchemaOverride": {
      "role": "You are a calm, concise assistant.",
      "constraints": ["Limit response to 100 words.", "Avoid imperative voice."]
    }
  }
}
```

Implementation: `Services/Ai/PromptSchemaOverrideMerger.cs` — same merger reused by `AiAnalysisNodeExecutor` (pre-R7) and `AiCompletionNodeExecutor` (R7 Wave 1).

---

## Worked Example 1 — 3-Node Prompt-Driven Playbook (Closes R4 `/narrate`)

The simplest non-trivial playbook: a Daily Briefing narration that takes a list of activities, calls the LLM to summarize them, and returns the narration. This closes the [R4 graduation gate (FR-15)](../../projects/spaarke-ai-platform-unification-r7/spec.md) — once it's wired and AiCompletion lands, `/narrate` works end-to-end.

**Shape**: `Start → AiCompletion → ReturnResponse` (three nodes, two edges, one Action FK).

```jsonc
[
  // Node 1 — Start (structural anchor; pure)
  {
    "name": "Start",
    "sprk_executortype": 0,                       // Start
    "sprk_actionid": null,
    "outputVariable": "start",
    "configJson": {}
  },

  // Node 2 — AiCompletion (prompt-driven; Action FK REQUIRED)
  {
    "name": "Narrate brief",
    "sprk_executortype": 1,                       // AiCompletion
    "sprk_actionid": "{BRIEF-NARRATE-TLDR}",      // carries SystemPrompt + OutputSchemaJson + Temperature
    "outputVariable": "narrative",
    "configJson": {
      "templateParameters": {
        "userName": "{{run.userName}}",
        "activities": "{{start.output.payload.activities}}"
      }
    }
  },

  // Node 3 — ReturnResponse (structural delivery; pure)
  {
    "name": "Return narration",
    "sprk_executortype": 11,                      // ReturnResponse
    "sprk_actionid": null,
    "outputVariable": "response",
    "configJson": { "outputBinding": "{{narrative.output.structuredData}}" }
  }
]
```

The `BRIEF-NARRATE-TLDR` Action row carries `sprk_systemprompt` (JPS doc with Role / Task / Output), `sprk_outputschemajson` (JSON Schema for `summary` / `keyItems[]` / `tone`), `sprk_temperature = 0.3`.

At runtime: orchestrator reads `node.sprk_executortype = 1` → routes to `AiCompletionNodeExecutor` (single hop) → executor's `Validate()` confirms Action FK + SystemPrompt + OutputSchemaJson present → executor calls `IOpenAiClient.GetStructuredCompletionRawAsync()` → binds raw JSON to `narrative.output.structuredData`.

### Edges

```jsonc
[
  { "source": "Start", "target": "Narrate brief" },
  { "source": "Narrate brief", "target": "Return narration" }
]
```

Three nodes, two edges, one Action FK on the prompt-driven node. Single-hop dispatch on each node — no fallback chain, no name-detection, no override branches. Cross-refs: [FR-12](../../projects/spaarke-ai-platform-unification-r7/spec.md), [FR-15](../../projects/spaarke-ai-platform-unification-r7/spec.md), [JPS-AUTHORING-GUIDE.md](JPS-AUTHORING-GUIDE.md).

---

## Worked Example 2 — Multi-Node Insights Playbook (Mix of Pure + Prompt-Driven)

A realistic Insights playbook: "summarize a matter — load knowledge sources, fetch recent activity, ask the LLM to summarize, return composite delivery." Mix of structural + data-I/O + prompt-driven + delivery executors.

**Shape**: `Start → LoadKnowledge → QueryDataverse → AiAnalysis → DeliverComposite → ReturnResponse` (six nodes).

```jsonc
[
  // Node 1 — Start
  { "name": "Start", "sprk_executortype": 0, "sprk_actionid": null, "outputVariable": "start", "configJson": {} },

  // Node 2 — LoadKnowledge (pure; knowledge scope attached via N:N)
  {
    "name": "Load matter knowledge",
    "sprk_executortype": 4,                     // LoadKnowledge
    "sprk_actionid": null,
    "outputVariable": "matterKnowledge",
    "configJson": { "knowledgeRefs": ["{matter-summary-kb-id}", "{matter-correspondence-kb-id}"] }
  },

  // Node 3 — QueryDataverse (pure data-I/O)
  {
    "name": "Recent activity",
    "sprk_executortype": 10,                    // QueryDataverse
    "sprk_actionid": null,
    "outputVariable": "activity",
    "configJson": {
      "entityType": "sprk_matteractivity",
      "fetchXml": "<fetch top='50'><entity name='sprk_matteractivity'><filter><condition attribute='sprk_matter' operator='eq' value='{{start.output.payload.matterId}}' /><condition attribute='createdon' operator='last-x-days' value='30' /></filter><order attribute='createdon' descending='true' /></entity></fetch>"
    }
  },

  // Node 4 — AiAnalysis (PROMPT-DRIVEN; Action FK REQUIRED)
  {
    "name": "Summarize matter",
    "sprk_executortype": 1500,                  // AiAnalysis
    "sprk_actionid": "{MATTER-SUMMARIZE-V2}",
    "outputVariable": "summary",
    "configJson": {
      "templateParameters": {
        "matterName": "{{start.output.payload.matterName}}",
        "activityCount": "{{activity.output.count}}"
      },
      "knowledgeRetrieval": { "scopeRefs": ["{{matterKnowledge.output.scopeIds}}"] },
      "includeDocumentContext": false,
      "promptSchemaOverride": {
        "constraints": ["Limit response to 400 words.", "Always include a 'risks' field."]
      }
    }
  },

  // Node 5 — DeliverComposite (pure delivery)
  {
    "name": "Compose response",
    "sprk_executortype": 21,                    // DeliverComposite
    "sprk_actionid": null,
    "outputVariable": "composite",
    "configJson": {
      "sections": [
        { "key": "summary",  "from": "{{summary.output.structuredData.summary}}" },
        { "key": "keyItems", "from": "{{summary.output.structuredData.keyItems}}" },
        { "key": "risks",    "from": "{{summary.output.structuredData.risks}}" }
      ]
    }
  },

  // Node 6 — ReturnResponse
  { "name": "Return summary", "sprk_executortype": 11, "sprk_actionid": null, "outputVariable": "response", "configJson": { "outputBinding": "{{composite.output.assembled}}" } }
]
```

**Edges** (only edges that move data):

```jsonc
[
  { "source": "Start", "target": "Load matter knowledge" },
  { "source": "Start", "target": "Recent activity" },
  { "source": "Load matter knowledge", "target": "Summarize matter" },
  { "source": "Recent activity", "target": "Summarize matter" },
  { "source": "Summarize matter", "target": "Compose response" },
  { "source": "Compose response", "target": "Return summary" }
]
```

Nodes 2 and 3 are independent — both only depend on `Start` — so the orchestrator runs them in parallel. The **only** node with an Action FK is the prompt-driven AiAnalysis node; everything else is pure config-only. `MATTER-SUMMARIZE-V2` Action is reusable across other Insights playbooks via per-instance `configJson.promptSchemaOverride` tweaks.

---

## Worked Example 3 — Condition / Branching Playbook

A control-flow playbook that branches on a deterministic condition. Use case: "scan an inbound email — if it's a high-priority client, notify the assigned attorney; otherwise log silently."

**Shape**: `Start → QueryDataverse → Condition → CreateNotification (true branch) OR ReturnResponse no-op (false branch) → ReturnResponse`.

```jsonc
[
  { "name": "Start", "sprk_executortype": 0, "sprk_actionid": null, "outputVariable": "start", "configJson": {} },

  // Fetch matter priority
  {
    "name": "Fetch matter",
    "sprk_executortype": 10,                    // QueryDataverse
    "sprk_actionid": null,
    "outputVariable": "matter",
    "configJson": {
      "entityType": "sprk_matter",
      "fetchXml": "<fetch top='1'><entity name='sprk_matter'><attribute name='sprk_priority' /><attribute name='sprk_assignedattorney' /><filter><condition attribute='sprk_matterid' operator='eq' value='{{start.output.payload.matterId}}' /></filter></entity></fetch>"
    }
  },

  // Condition (pure control-flow; Validate() PROHIBITS Action FK)
  {
    "name": "Is high priority?",
    "sprk_executortype": 30,                    // Condition
    "sprk_actionid": null,
    "outputVariable": "isPriority",
    "configJson": {
      "condition": "{{matter.output.items[0].sprk_priority}} >= 200000000",
      "trueBranch": "High",
      "falseBranch": "Normal"
    }
  },

  // True branch — CreateNotification
  {
    "name": "Notify attorney",
    "sprk_executortype": 25,                    // CreateNotification
    "sprk_actionid": null,
    "outputVariable": "notification",
    "configJson": {
      "title": "High-priority matter email received",
      "body": "An email was received on a high-priority matter. Review immediately.",
      "recipientType": "user",
      "recipientId": "{{matter.output.items[0].sprk_assignedattorney}}",
      "category": "matter-priority",
      "priority": 200000000
    }
  },

  // False branch — no-op exit
  { "name": "Log silently", "sprk_executortype": 11, "sprk_actionid": null, "outputVariable": "noop", "configJson": { "outputBinding": "logged" } },

  // Terminal
  { "name": "Return", "sprk_executortype": 11, "sprk_actionid": null, "outputVariable": "response", "configJson": { "outputBinding": "{{notification.output.notificationId}}" } }
]
```

**Edges** (branching — the R3 [Branch Picker dialog](#b-branch-wiring-picker) handles this in PlaybookBuilder):

```jsonc
[
  { "source": "Start", "target": "Fetch matter" },
  { "source": "Fetch matter", "target": "Is high priority?" },
  { "source": "Is high priority?", "target": "Notify attorney", "branch": "true" },
  { "source": "Is high priority?", "target": "Log silently",   "branch": "false" },
  { "source": "Notify attorney", "target": "Return" }
]
```

**Single-hop dispatch on every node** — including Condition. The orchestrator reads `node.sprk_executortype = 30` → routes to `ConditionNodeExecutor` (no Action FK, just typed config). The executor evaluates the boolean expression, sets `output.result = true/false` + `output.branch = "true"/"false"`, and the orchestrator follows the matching branch edge.

---

## What's New in R3 (At a Glance)

| What | Where you'll see it | Why it matters |
|---|---|---|
| **`LookupUserMembership` node** | New drag-target in the node palette | One node replaces every hand-rolled "is this user on this matter?" FetchXML query. Talks to the same `MembershipResolverService` your tenant configures once for everyone. |
| **`{{joinIds X.ids}}` helper** | Use it inside a downstream FetchXML node | Renders an array of GUIDs as `"guid1,guid2,guid3"` — exactly the shape FetchXML `operator='in'` wants. No hand-written `{{#each}}` loops. |
| **`{{default X 'Y'}}` helper** | Use it inside any template field | Returns `X` if it resolves to a value, else `Y`. Replaces the broken `{{X ?? 'Y'}}` pattern (which used to render the raw `??` text in production — see Pitfall G1). |
| **OutputVariable rename guard** | Pops up when you rename a node's Output Variable | Auto-renames downstream `{{X.output.field}}` references in one click. Never breaks references silently. |
| **Branch wiring picker** | Pops up when you draw an edge from a Condition node | Asks "True / False / Both?" so you don't end up with a Condition node whose branches both fire. |
| **Edge perf hint** | Yellow badge on the source node of an edge | Tells you "this edge forces sequential execution but moves no data — confirm or remove." Advisory only; you can still save. |
| **Canvas↔server drift CI test** | Runs in every CI build (not visible in the UI) | Stops a developer from adding a new node type to the canvas without wiring up the server executor. Prevents an entire class of silent failures. |

---

## Quick-Start Recipe: Notify Me About New Documents on My Matters

This is the canonical R3 recipe. It uses every new building block: `LookupUserMembership` → `QueryDataverse` with `joinIds` → `Condition` → `CreateNotification`. Total time end-to-end: about 10 minutes for a maker who's seen PlaybookBuilder once. The recipe is fully current under R7 — every node carries an `sprk_executortype` value (set automatically by PlaybookBuilder when you drag from the palette).

### Before you start

You need:

- **PlaybookBuilder access** in your environment.
- A few existing matters you've been assigned to (so you have something to look at in smoke-test).
- **System Administrator** OR a role with permission to create `sprk_analysisplaybook` records.
- (Optional but recommended) the `MembershipResolverService` is set up for your tenant — [`MEMBERSHIP-RESOLUTION-GUIDE.md`](MEMBERSHIP-RESOLUTION-GUIDE.md) walks operators through the one-time setup. If it's not set up, your `LookupUserMembership` node will return empty results.

### Step 1 — Create a new playbook

1. Open PlaybookBuilder (it's a Code Page in your model-driven app — usually a tile labelled "Playbook Builder").
2. Click **New playbook**. Give it a name like "New Documents on My Matters" and a description.
3. In the Playbook Properties pane on the right, set:
   - **Playbook Type**: `Notification` (this tells the scheduler to run it on a per-user cadence)
   - **Schedule** (in `sprk_configjson` → `schedule`): `{ "frequency": "daily", "time": "06:00" }`
   - **Category**: `new-documents` (used by notification dedup logic — see Pitfall G4)

### Step 2 — Drop a Start node

1. From the node palette on the left, drag **Start** to the canvas (it usually appears auto-placed in the top-left). PlaybookBuilder sets `sprk_executortype = 0 (Start)` automatically.
2. Click it to open Properties. Default `outputVariable` is `start`. Leave it.

### Step 3 — Drop a LookupUserMembership node

This is the R3 control-flow building block. It answers: "what matters is the executing user a member of?" PlaybookBuilder sets `sprk_executortype = 52 (LookupUserMembership)` automatically when you drag it.

1. From the palette, drag **Lookup User Membership** to the right of the Start node.
2. Click the node to open Properties on the right.
3. Fill in:
   - **Entity Type**: `sprk_matter` (the Dataverse logical name of the entity to resolve memberships on)
   - **Roles** (comma-separated): `owner, assignedAttorney, assignedParalegal` (case-insensitive; matches the role names the `MembershipResolverService` discovered for your tenant — leave empty to get every role)
   - **Output Variable**: `myMatters` (this is the canvas variable name downstream nodes will reference — see [Anti-Patterns](#anti-patterns-to-avoid) about picking a good name)
   - **Include related (1-hop)**: leave **off**. (1-hop transitive memberships are out of R3 scope; the toggle is there for forward compatibility.)
4. Connect Start → Lookup User Membership by dragging from the bottom handle of Start to the top handle of the Lookup node.

> **What the node will produce at runtime**: `myMatters.ids` (a deduped list of matter GUIDs), `myMatters.byRole` (the same IDs grouped by role: `owner`, `assignedAttorney`, `assignedParalegal`), `myMatters.count`. The server resolver does the heavy lifting in one call — you don't need separate FetchXML for each role.

### Step 4 — Drop a QueryDataverse node and use `{{joinIds myMatters.ids}}`

1. From the palette, drag **Update Record** to the right of the Lookup node. (Yes — confusingly, `QueryDataverse` is exposed as the `updateRecord` canvas type with `queryMode: true`. This is a pre-R3 legacy quirk and will be cleaned up in a future release.) PlaybookBuilder sets `sprk_executortype = 10 (QueryDataverse)` when query-mode is on.
2. Click the node and open Properties. Set:
   - **Query Mode**: on
   - **Entity Logical Name**: `sprk_document`
   - **Output Variable**: `newDocsQuery`
   - **FetchXML**:

     ```xml
     <fetch top="50">
       <entity name="sprk_document">
         <attribute name="sprk_name" />
         <attribute name="sprk_filename" />
         <attribute name="createdon" />
         <attribute name="sprk_documentid" />
         <attribute name="sprk_matter" />
         <filter type="and">
           <condition attribute="sprk_matter" operator="in" value="{{joinIds myMatters.ids}}" />
           <condition attribute="createdon" operator="last-x-hours" value="{{timeWindowHours}}" />
           <condition attribute="createdby" operator="ne-userid" />
         </filter>
         <order attribute="createdon" descending="true" />
       </entity>
     </fetch>
     ```

   - **Template Parameters**:
     ```jsonc
     { "timeWindowHours": "{{default userPreferences.timeWindowHours '24'}}" }
     ```
3. Connect Lookup User Membership → Query New Documents.

> **What the `{{joinIds myMatters.ids}}` does at runtime**: rewrites to `"guid1,guid2,guid3"` (a single CSV string) so FetchXML's `operator='in'` accepts it. If `myMatters.ids` is empty (user has zero matters), it renders as `""` → the IN clause matches zero rows → no notifications. That's the **fail-closed** behavior you want.

> **What the `{{default userPreferences.timeWindowHours '24'}}` does**: returns `userPreferences.timeWindowHours` if it resolves to a value, else `'24'`. Replaces the broken `{{userPreferences.timeWindowHours ?? '24'}}` pattern that used to emit raw text.

### Step 5 — Drop a Condition node to short-circuit when there's nothing to notify about

1. From the palette, drag **Condition** to the right of the Query node. PlaybookBuilder sets `sprk_executortype = 30 (Condition)`.
2. Open Properties. Set:
   - **Output Variable**: `hasNewDocs`
   - **Condition expression** (in `conditionJson`): `{{newDocsQuery.output.count}} > 0`
   - **True branch label**: leave as `True` (or rename to something like `Has new docs`)
   - **False branch label**: leave as `False`
3. Connect Query New Documents → Check Results.

### Step 6 — Drop a CreateNotification node on the True branch

1. From the palette, drag **Create Notification** to the right of the Condition node, slightly above (to leave room for a potential False branch later). PlaybookBuilder sets `sprk_executortype = 25 (CreateNotification)`.
2. **Draw the edge from the Condition node's body** to the Create Notification node. At this point, the **Branch Picker dialog** pops up:
   - Choose **True**.
   - Click **Wire branch**.
3. The edge now shows as a green **True** edge. (If you had chosen Both, you'd get TWO edges — one green True + one red False. The picker never invents a "both" edge type.)
4. Open Create Notification properties. Set:
   - **Output Variable**: `notification`
   - **Title**: `{{newDocsQuery.output.count}} new document(s) on your matters`
   - **Body**: `{{#each newDocsQuery.output.items}}{{sprk_filename}} added to {{matterName}} ({{createdon}}).\n{{/each}}`
   - **Category**: `new-documents`
   - **Priority**: `200000000` (Important)
   - **Recipient ID**: `{{run.userId}}`
   - **Iterate Items**: on (creates one notification per item rather than one bulky summary)
   - **Item Notification**: configure the per-item template (see the notification-new-documents.json migrated playbook for the full shape)

### Step 7 — Save and deploy

1. Press **Ctrl+S** (or wait 30 seconds for auto-save).
2. Watch for validation warnings in the bottom-right Notification badge on each node. If the **edge perf hint** fires on any edge ("this edge forces sequential execution but moves no data") — verify the downstream node actually references the upstream node's Output Variable. In our case, every edge moves data (Lookup → joinIds usage → count check → notification creation), so this advisory should NOT fire.
3. Save complete? Now **schedule it**:
   - The notification scheduler picks up `sprk_playbooktype = Notification` playbooks automatically once they're saved. No separate deploy step.
   - The scheduler runs hourly by default; your `schedule: { frequency: "daily", time: "06:00" }` configures the actual cadence.

### What the recipient sees

The next morning (or the next time the scheduler runs after the `time` you configured), every user the playbook applies to will see in-app notifications in their Power Apps notification panel:

> **"3 new document(s) on your matters"** — clicking expands to one notification per document, each clickable to open the document record.

If the user has zero matters they're a member of, OR zero new documents on those matters in the past 24 hours, NOTHING is created — no empty notification, no error. This is the fail-closed behavior built into both `LookupUserMembership` (empty `ids` array) and `joinIds` (empty CSV).

---

## Node Catalog (R3 Update)

The full node catalog with all 33 executor types lives in [`ai-architecture-actions-nodes-scopes.md` §8](../architecture/ai-architecture-actions-nodes-scopes.md). This subsection covers the **R3 addition** that is still current under R7.

### `LookupUserMembership` (ExecutorType 52)

**What it does**: Resolves the executing user's record memberships for a given Dataverse entity type by calling `IMembershipResolverService` in-process (same backing service as `GET /api/users/me/memberships/{entityType}`).

**Canvas type**: `lookupUserMembership` (drag the **Lookup User Membership** palette item). PlaybookBuilder sets `sprk_executortype = 52` on save.

**Action FK**: **prohibited** (pure executor — `Validate()` rejects an Action FK).

**Config (fill in via Properties panel)**:

| Field | Required | Example | Notes |
|---|---|---|---|
| Entity Type | yes | `sprk_matter` | Dataverse logical name; free text, validity is determined by the discovery service at runtime |
| Roles (comma-separated) | no | `owner, assignedAttorney, assignedParalegal` | Case-insensitive; matches roles discovered by the membership service for your tenant; empty = all roles |
| Output Variable | yes | `myMatters` | Canvas variable name; downstream nodes reference as `{{myMatters.ids}}` or `{{joinIds myMatters.ids}}` |
| Include related (1-hop) | no | off | Phase 1D feature, currently accepted-but-ignored — leave off |

**Output shape**:

```jsonc
{
  "entityType": "sprk_matter",
  "count": 47,
  "ids": ["guid1", "guid2", "..."],              // deduped list — use with {{joinIds}}
  "byRole": {                                     // same IDs grouped by role
    "owner": ["guid1"],
    "assignedAttorney": ["guid1", "guid2"],
    "assignedParalegal": ["guid3"]
  },
  "continuationToken": null,
  "cacheExpiresAt": "2026-06-22T15:34:00Z"
}
```

**Where it gets the user identity from**: `NodeExecutionContext.UserId`, set by the scheduler when the playbook runs in per-user mode. If you need to run a playbook in a non-scheduler context, the executor falls back to scanning previous node outputs for a `userId` property — but the scheduler path is what 99% of authors use.

**Requires**: the operator has configured the `MembershipResolverService` for your tenant. If discovery returns no fields for your entity type, the node returns `count: 0` and an empty `ids` array (no error). See [`MEMBERSHIP-RESOLUTION-GUIDE.md`](MEMBERSHIP-RESOLUTION-GUIDE.md) for the one-time operator setup.

---

## Handlebars Template Helpers (R3 Update)

Existing helpers (`safe`, simple variable interpolation, `{{#each}}`, nested property access, etc.) are unchanged — the [architecture doc](../architecture/playbook-architecture.md#templateengine) covers them. R3 added two new helpers, both registered unconditionally — no feature flag needed. Both remain current under R7.

### `joinIds` (NEW in R3)

```handlebars
{{joinIds varName.ids}}
```

**What it does**: Converts an array of GUIDs (or any list of stringifiable values) into a comma-separated string. The output is the exact shape FetchXML's `operator='in'` clause expects.

**Example**:

```xml
<condition attribute="sprk_matter" operator="in" value="{{joinIds myMatters.ids}}" />
```

If `myMatters.ids` is `["a", "b", "c"]`, the rendered XML is:

```xml
<condition attribute="sprk_matter" operator="in" value="a,b,c" />
```

**Behavior with edge cases**:

| Input | Renders as |
|---|---|
| `["a", "b", "c"]` | `"a,b,c"` |
| `[]` (empty list) | `""` — IN clause matches zero rows (fail-closed) |
| `null` or unresolved binding | `""` |
| A scalar (string, number, bool) | `""` (defensive — caller likely passed the wrong shape) |

> **Do NOT** hand-roll a `{{#each ids}}{{this}},{{/each}}` substitute. That pattern leaves a trailing comma, doesn't handle empty lists correctly, and bypasses the unresolved-binding defense. Use `joinIds`.

### `default` (NEW in R3)

```handlebars
{{default varName 'fallback'}}
```

**What it does**: Returns `varName` if it resolves to a non-empty value; otherwise renders `'fallback'`.

**Example**:

```jsonc
"templateParameters": {
  "timeWindowHours": "{{default userPreferences.timeWindowHours '24'}}"
}
```

If `userPreferences.timeWindowHours` is set to `"48"`, the rendered value is `"48"`. If it's missing/null/empty, the rendered value is `"24"`.

**Why this exists**: Handlebars.NET does not support the JavaScript-style `{{X ?? 'Y'}}` null-coalescing operator. Before R3, authors who reflexively used `??` got the literal text `?? 'Y'` rendered in production output — a silent breakage mode (Pitfall G1) that affected 2 of 7 active notification playbooks in R2 UAT. Use `default` instead.

### Runtime unrendered-template warning

If a template variable doesn't resolve at runtime (typo, broken reference, upstream node failed), the engine renders the literal `{{variable}}` string into the output. After R3, the orchestrator detects this case AND emits a structured `unrendered-template-detected` event to the SSE stream + a structured log warning. You'll see it in:

- The PlaybookBuilder run-history view (per-run event log)
- App Insights traces for the run (search for `UnrenderedTemplateDetected`)

This means you find broken references during the FIRST run, not days later when a downstream consumer misbehaves.

---

## Builder UI Safety Affordances (R3)

PlaybookBuilder gained three safety affordances in R3. All of them extend existing PlaybookBuilder components — none introduce new modal frameworks (Q5 owner directive). All remain current under R7.

### a. OutputVariable rename guard

**When it fires**: You change a node's **Output Variable** field AND at least one other node references `{{<oldName>.output.something>}}` in its config.

**What you see**: A Fluent UI dialog titled "Variable referenced by N downstream nodes" with three buttons:

| Button | Effect |
|---|---|
| **Auto-rename references** (default) | Updates the renamed node's Output Variable AND find/replaces every downstream `{{<oldName>.output.*}}` reference in one transaction. |
| **Keep old name** | Reverts the field to its previous value. Downstream references remain valid. |
| **Cancel rename** | Same as Keep old name. Provided as an escape hatch for users who reflexively look for "Cancel." |

**What to do**: 99% of the time, click **Auto-rename references**. The only reason to choose Keep is if you realized mid-typing that you don't actually want to rename.

Closing the dialog with Esc or clicking outside has the same effect as Cancel — closing the dialog never silently breaks a reference.

A complementary rule called `outputvar-collision` fires (severity: error) if two nodes ever share the same Output Variable. That's a save-blocking error — you must rename one of them.

### b. Branch wiring picker

**When it fires**: You draw an edge **from the body of a Condition node** to a downstream node (instead of dragging from one of the True/False handles on the side).

**What you see**: A Fluent UI dialog titled "Wire branch" with three options:

| Option | Effect |
|---|---|
| **True** | Single edge, drawn in green, labelled "True" (or your custom True label if you renamed it in the Condition editor). |
| **False** | Single edge, drawn in red, labelled "False". |
| **Both** | TWO edges — one True (green) + one False (red). The downstream node fires regardless of the Condition's result. |
| **Cancel** | No edge created. |

**What to do**: Pick the branch you want. The dialog reads the True/False labels from the Condition node's `conditionJson.trueBranch` / `falseBranch` fields, so if you renamed them to "Approved" / "Rejected" you'll see those names in the picker.

**Gotcha**: If you already dragged from one of the side handles (the explicit True or False output), the picker is skipped — the edge is wired directly to whichever handle you used. This is the FAST path; the picker exists for users who didn't notice the side handles.

### c. Edge perf hint advisory

**When it fires**: An edge connects two nodes whose configs don't reference each other through Output Variables. Specifically: the target node's serialized config does NOT contain `{{<source.outputVariable>.output.*}}` anywhere.

**What you see**: A yellow warning badge on the **source node's** Properties panel (in the NodeValidationBadge popover):

> "Edge from \"X\" to \"Y\" does not reference {{X.output.*}} in the target's configuration. This edge forces sequential execution. Confirm or remove?"

**What to do**:

- **Most common case**: you wired an edge by accident (intended to enforce ordering when none was needed). Delete the edge. Performance improves immediately — the orchestrator runs nodes in parallel within a batch as long as no edges force serialization.
- **Legitimate case**: you genuinely need side-effect-only sequencing (rare — e.g., one node writes a file that the next node reads via a side channel). Ignore the advisory. It's non-blocking; save succeeds.

The advisory is intentionally NOT a save-blocking error because legitimate cases exist. Use your judgment.

---

## Anti-Patterns to Avoid

These are the recurring mistakes that have broken playbooks in production. Each one has a real R2 incident behind it. All remain current under R7 — the dispatch unification didn't change the data-flow rules.

### 1. Hand-rolling FetchXML against a junction table that doesn't exist

**Anti-pattern**: Writing `<link-entity name="sprk_matterteammember" .../>` because "that's the obvious name for a matter-team-member junction."

**Why it broke**: `sprk_matterteammember` doesn't actually exist in production Dataverse. The query parsed fine, returned zero rows silently, and produced empty notifications for weeks.

**Use instead**: A `LookupUserMembership` node. Let the discovery service tell you which Lookup columns count as membership on `sprk_matter`.

### 2. Using `{{X ?? 'Y'}}` for fallbacks

**Anti-pattern**: `{{userPreferences.timeWindow ?? '24h'}}` (carried over from JavaScript / PCF muscle memory).

**Why it broke**: Handlebars.NET doesn't support `??`. The engine emits the literal text `?? '24h'` as part of the rendered output.

**Use instead**: `{{default userPreferences.timeWindow '24h'}}`.

### 3. Adding edges "to enforce ordering" when no data flows

**Anti-pattern**: Connecting A → B → C → D in a long chain when only D references B's output.

**Why it hurts**: Every edge forces sequential execution. The orchestrator can't run A, B, C in parallel even though A and C produce nothing B consumes.

**Use instead**: Connect only the edges that move data. The orchestrator schedules independent nodes in parallel within a batch (default 3-wide).

### 4. Reusing the same Output Variable name on two nodes

**Anti-pattern**: Two `QueryDataverse` nodes both named with `outputVariable: query`.

**Why it broke**: Downstream `{{query.output.field}}` references are ambiguous; the engine picks whichever happened to execute last.

**R3 protection**: The `outputvar-collision` validation rule fires as an ERROR (save-blocking) when two nodes share an Output Variable.

### 5. Renaming an Output Variable and forgetting downstream consumers

**Anti-pattern**: Rename `result` → `newDocsQuery` without updating the four downstream nodes that reference `{{result.output.*}}`.

**Why it broke**: Downstream nodes render the raw `{{result.output.count}}` literal at runtime.

**R3 protection**: The rename guard dialog auto-renames every downstream reference in one click. Don't bypass it.

### 6. Wiring a Condition node's body edge without specifying True/False

**Anti-pattern**: Drag from the Condition node body (not from a side handle) and accept the default edge type.

**Why it broke (pre-R3)**: The edge was created as a regular `smoothstep` edge with no branch metadata. The downstream node fired regardless of the Condition result — defeating the conditional logic.

**R3 protection**: The Branch Picker dialog forces you to choose True / False / Both before the edge is created.

### 7. Not setting an Output Variable on a node downstream consumers reference

**Anti-pattern**: Leaving Output Variable blank on a `QueryDataverse` node, then writing `{{output.count}}` in the next node hoping it'll work.

**Why it broke**: Without an Output Variable, the engine has no name to bind the result to. The template renders `{{output.count}}` literally.

**Use instead**: Always set an Output Variable. Pick a name that describes what the node produces (e.g., `myMatters`, `newDocsQuery`, `hasNewDocs`) — not `data` or `result`.

### 8. Assuming `iterateItems: true` means "one notification, summarizing all items"

**Anti-pattern**: Treating the `iterateItems` flag as a summary toggle.

**Why it broke**: `iterateItems: true` produces ONE notification per item (using the `itemNotification` template). If you want a single rolled-up notification, set `iterateItems: false` and use a `{{#each}}` loop in the body template.

**Use the right one for your case**: per-item notifications surface higher in the user's notification panel (each is individually clickable to the source record); rolled-up notifications keep the panel less cluttered when item counts are high.

### 9. Forgetting that idempotency dedupes on UNREAD only

**Anti-pattern**: Assuming "once per notification key, ever" semantics for `CreateNotification`.

**Why it surprises authors**: Once a user reads / dismisses the notification, the next scheduler tick will create a fresh notification with the same key (because the prior one is no longer unread). This is intentional — desirable for daily-update playbooks — but surprising if you wanted "send only once."

**Use instead**: If you genuinely need "once ever" semantics, implement it at the data layer (e.g., set a `notified=true` field on the source record after sending). Pitfall G4 in the architecture doc has the full discussion.

### 10. Mixing free-text display names with identity-typed Lookups

**Anti-pattern**: Trying to filter `sprk_matter` by `sprk_assignedattorney_displayname = 'Jane Doe'`.

**Why it doesn't work**: The membership service intentionally does NOT support matching against free-text display-name fields (explicitly out of scope). Display names aren't unique, aren't normalized, and aren't authoritative.

**Use instead**: The `LookupUserMembership` node resolves by `systemuserid` (the authoritative identifier). Let the resolver do its job.

### 11. (R7) Attaching an Action FK to a pure executor

**Anti-pattern**: Setting `sprk_actionid` on a `Condition` / `Start` / `ReturnResponse` / `QueryDataverse` node "in case the executor wants a prompt later."

**Why it broke**: The executor's `Validate()` rejects an Action FK on pure executors. The error message is literal and intentionally blunt (e.g., `Condition node MUST NOT have an Action FK`). Surfaced in PlaybookBuilder validation badges + at deploy time.

**Use instead**: Leave `sprk_actionid` null on pure executors. The runtime ignores Action FK on pure executors; only prompt-driven executors (AiAnalysis / AiCompletion / AiEmbedding) consume it.

### 12. (R7) Omitting `sprk_executortype` on a node

**Anti-pattern**: Hand-authoring a JSON input file for `Deploy-Playbook.ps1` that lists nodes but doesn't set `sprk_executortype` on each one, expecting the script to infer it.

**Why it broke**: Per [FR-20](../../projects/spaarke-ai-platform-unification-r7/spec.md), the deploy script writes `sprk_executortype` **explicitly** on every node — no name-detection, no inference. A node missing `sprk_executortype` fails at the deploy script's step-1 lint with a clear error.

**Use instead**: Set `sprk_executortype` explicitly on every node in your input JSON. Use PlaybookBuilder's executor-type dropdown OR look up the right enum value from [`ai-architecture-actions-nodes-scopes.md` §8](../architecture/ai-architecture-actions-nodes-scopes.md).

---

## Migration: Replacing Broken FetchXML with LookupUserMembership

If you authored playbooks before R3 and they need this update, here's the worked diff. The three migrated R3 reference playbooks live at `projects/spaarke-daily-update-service/notes/playbooks/`.

### The A1 defect — what we're fixing

Three pre-R3 playbooks shared a common defect class: their "user's matters" filter either joined through a non-existent `sprk_matterteammember` table (silently returning zero rows) OR had no user-membership filter at all (returning every matter in the tenant). Both modes produced misleading results in production.

### Before / After diff — `notification-new-documents.json`

**Before** (R2): a single `QueryDataverse` node trying to join through a non-existent junction table.

```jsonc
// Old node — DOES NOT WORK
{
  "name": "Query New Documents",
  "canvasType": "updateRecord",
  "configJson": {
    "queryMode": true,
    "entityLogicalName": "sprk_document",
    "fetchXml": "<fetch>...<link-entity name='sprk_matterteammember' ...><filter><condition attribute='systemuserid' operator='eq-userid' /></filter></link-entity>...</fetch>"
  }
}
```

**After** (R3 + R7): a `LookupUserMembership` node feeding a downstream FetchXML via `{{joinIds}}`. Both nodes carry explicit `sprk_executortype` per R7.

```jsonc
// New node 1 — resolve memberships
{
  "name": "Lookup My Matters",
  "canvasType": "lookupUserMembership",
  "sprk_executortype": 52,                       // LookupUserMembership (R7 single-hop)
  "sprk_actionid": null,                         // pure executor — no Action FK
  "outputVariable": "myMatters",
  "configJson": {
    "entityType": "sprk_matter",
    "roles": ["owner", "assignedAttorney", "assignedParalegal"],
    "includeRelated": false
  }
},

// New node 2 — query using the resolved IDs
{
  "name": "Query New Documents",
  "canvasType": "updateRecord",
  "sprk_executortype": 10,                       // QueryDataverse (R7 single-hop)
  "sprk_actionid": null,                         // pure executor
  "outputVariable": "newDocsQuery",
  "configJson": {
    "queryMode": true,
    "entityLogicalName": "sprk_document",
    "fetchXml": "<fetch top='50'><entity name='sprk_document'>...<filter><condition attribute='sprk_matter' operator='in' value='{{joinIds myMatters.ids}}' /><condition attribute='createdon' operator='last-x-hours' value='{{timeWindowHours}}' /></filter>...</entity></fetch>",
    "templateParameters": {
      "timeWindowHours": "{{default userPreferences.timeWindowHours '24'}}"
    }
  }
}
```

The other two migrated playbooks (`notification-new-emails.json`, `notification-new-events.json`) follow the same shape: `Start → LookupUserMembership → QueryDataverse with joinIds → Condition → CreateNotification`, with explicit `sprk_executortype` on every node.

### How to audit your existing playbooks for similar issues

1. **Find candidate playbooks**: search your environment for `sprk_analysisplaybook` records where `sprk_canvaslayoutjson` contains `sprk_matterteammember` OR any other junction-table name you're not 100% certain exists.
2. **Verify the junction exists**: open the Power Apps maker portal → Tables → search. If the table isn't there, your FetchXML is silently failing.
3. **Even if junctions exist**, prefer `LookupUserMembership` over hand-rolled joins. The membership service auto-discovers every Lookup column on the parent entity that points to an identity table — so adding a new "assigned" column in Dataverse appears in your playbook results automatically (within an hour, or immediately after `POST /api/admin/membership/refresh-metadata`).
4. **Audit playbooks with NO user filter at all**: these silently iterate every row in the tenant. If a `Notification`-type playbook fans out a notification per row, it'll spam every user. Confirm the filter is there.
5. **(R7) Verify every node carries `sprk_executortype`**: per [FR-19](../../projects/spaarke-ai-platform-unification-r7/spec.md), the 94 pre-R7 nodes in spaarkedev1 undergo owner-reviewed backfill. If your env has playbooks predating R7, expect a one-time backfill — the migration script (Wave 5) lists each node with its inferred executor + space for owner override.

---

## Smoke-Test a Playbook Before Deploying

Before declaring a new playbook done, run it manually and inspect the result. This catches the 5% of issues the canvas validation can't predict (e.g., the Membership service isn't configured for the entity type you picked, or an executor's `Validate()` rejects an unexpected payload).

### Step 1 — Save the playbook

Press **Ctrl+S** in PlaybookBuilder. Confirm no save-blocking errors fire. (R7 adds executor-type + Action-FK consistency checks to the save-blocking validation set — e.g., a Condition node with an Action FK will fail save.)

### Step 2 — Trigger the scheduler manually

The notification scheduler job is registered as `notification-playbook-scheduler`. Trigger it out-of-band via:

```http
POST /api/admin/jobs/notification-playbook-scheduler/trigger
Authorization: Bearer <SystemAdmin token>
```

This dispatches the scheduler immediately (independent of its hourly cron). Returns `202 Accepted` with a `runId`.

### Step 3 — Check the run status

```http
GET /api/admin/jobs/notification-playbook-scheduler/status
Authorization: Bearer <SystemAdmin token>
```

Look at the most recent run. Expected:

- `success`: `true`
- `errors`: `0`
- `processedItems > 0` if any user has memberships matching your playbook's criteria

If `processedItems = 0`:

- Either no user has memberships matching the playbook's filter (genuine zero state — your filter is too narrow for the test environment), OR
- The membership service isn't configured for the entity type — call `GET /api/admin/membership/discovered/{entityType}` and confirm `discoveredFields[]` includes the columns you expect

### Step 4 — Check the run history detail

```http
GET /api/admin/jobs/notification-playbook-scheduler/history?limit=5
Authorization: Bearer <SystemAdmin token>
```

Look for `UnrenderedTemplateDetected` events in the per-run log. If you see them, you have a `{{...}}` reference that didn't resolve — fix the reference and re-run.

### Step 5 — Verify the membership endpoint returns expected IDs

For one of the users the scheduler processed, impersonate (or grab their token) and call:

```http
GET /api/users/me/memberships/sprk_matter
Authorization: Bearer <user token>
```

Confirm the `ids[]` matches what you can verify by hand in the Dataverse model-driven app. If the endpoint returns IDs but your playbook's `LookupUserMembership` node returned empty — you have a config mismatch (different entity type, different role filter). Cross-check.

### Step 6 — Verify a notification was actually created

Open the user's notification panel in Power Apps (the bell icon top-right). Newly-created notifications appear within seconds of the scheduler run.

If notifications were created but the user can't see them: check the `recipientId` template in your `CreateNotification` node — usually `{{run.userId}}`. If the value is wrong, notifications get created against the wrong recipient.

---

## Related Documentation

- **JPS authoring (prompt-template level — what lives inside the Action row)**: [`JPS-AUTHORING-GUIDE.md`](JPS-AUTHORING-GUIDE.md) — the sibling guide; covers JPS DSL grammar, sections, `$ref`, `$choices`, structured output, validation.
- **Architecture — actions / nodes / scopes / configjson boundary**: [`docs/architecture/ai-architecture-actions-nodes-scopes.md`](../architecture/ai-architecture-actions-nodes-scopes.md) — includes the rewritten decision tree + full 33-executor enumeration with cluster ranges (§8).
- **Architecture — playbook runtime semantics**: [`docs/architecture/ai-architecture-playbook-runtime.md`](../architecture/ai-architecture-playbook-runtime.md).
- **AI Architecture overview**: [`docs/architecture/AI-ARCHITECTURE.md`](../architecture/AI-ARCHITECTURE.md).
- **Deploy recipe (script input file format + 12-step procedure)**: [`ai-guide-playbook-deploy-recipe.md`](ai-guide-playbook-deploy-recipe.md).
- **Consumer wiring (how to wire a new consumer into a playbook)**: [`ai-guide-consumer-wiring.md`](ai-guide-consumer-wiring.md).
- **Membership Resolution operator guide** (one-time tenant setup for `LookupUserMembership`): [`docs/guides/MEMBERSHIP-RESOLUTION-GUIDE.md`](MEMBERSHIP-RESOLUTION-GUIDE.md).
- **Pattern doc for developers adding NEW node executor types**: [`.claude/patterns/ai/node-executor-authoring.md`](../../.claude/patterns/ai/node-executor-authoring.md).
- **Playbook vs RAG decision tree**: [`INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md`](INSIGHTS-PLAYBOOK-VS-RAG-DECISION-TREE.md).
- **BFF Hygiene §10** (binding governance for BFF additions): root [`CLAUDE.md` §10](../../CLAUDE.md#10-bff-hygiene--binding-governance-read-before-adding-to-sprkbffapi).
- **BFF extensions constraints §G** (actions / nodes / scopes / configjson boundary, binding): [`.claude/constraints/bff-extensions.md` §G](../../.claude/constraints/bff-extensions.md#g-actions--nodes--scopes--configjson-boundary-binding-per-r4-canonical-truth-loop-2026-06-26).
- **R3 reference playbooks**: `projects/spaarke-daily-update-service/notes/playbooks/` — `notification-new-documents.json`, `notification-new-emails.json`, `notification-new-events.json`.
- **ADR-013 BFF AI Architecture**: [`docs/adr/ADR-013-bff-ai-architecture.md`](../adr/ADR-013-bff-ai-architecture.md).
- **ADR-034 user-record membership** (binding rules for `LookupUserMembership`): [`.claude/adr/ADR-034-user-record-membership.md`](../../.claude/adr/ADR-034-user-record-membership.md).

---
