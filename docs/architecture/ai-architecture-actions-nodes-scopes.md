# AI Architecture — Actions, Nodes, Scopes & the Config-Bag Boundary

> **Last reviewed**: 2026-06-26
> **Authored by**: canonical-truth loop step 3 (spaarke-daily-update-service-r4)
> **Status**: Canonical. **Binding** for every project that adds a new column, JSON field, scope, action, or node-config knob to the playbook surface. Companion to `.claude/constraints/bff-extensions.md` §G.
> **Scope**: The "where does this config live?" decision tree. Action-intrinsic behaviour vs playbook header metadata vs per-node runtime config vs playbook-level resource scope. Anti-patterns that conflate the four.
> **NOT in scope**: Runtime mechanics (see `ai-architecture-playbook-runtime.md`), JPS schema (see `ai-guide-jps-authoring.md`), maker-facing recipes (see `PLAYBOOK-AUTHOR-GUIDE.md`).

---

## 1. Why this doc exists

R4 surfaced a design smell: across the codebase, configuration that *describes the playbook* gets stuffed into the wrong column. Three concrete instances:

1. **Node-level wire-up in Action's config blob** — temptation to hardcode an input binding ("read parameter X from the request") onto `sprk_analysisaction` so a node doesn't have to set it, defeating Action reusability across playbooks.
2. **Playbook-level scope decisions in node configJson** — temptation to list which Skills are "available" on the node row, when scope arrays belong on the playbook (N:N) or on the node (N:N) — not as inline JSON fields.
3. **Node-graph data in `sprk_analysisplaybook.sprk_configjson`** — the R4 deploy bug. Runtime reads `sprk_playbooknode` rows, not the playbook-level configjson. Stuffing nodes here means runtime ignores them.

This doc establishes the boundary so the next project doesn't relearn it.

---

## 2. The four homes for playbook config

Every piece of playbook configuration belongs in exactly one of these four homes. Pick the right home before you write the column or JSON field.

| Home | What lives here | Concrete examples |
|---|---|---|
| **A. Action row** (`sprk_analysisaction`) | Action-INTRINSIC behaviour: how this Action behaves wherever it's used | `sprk_systemprompt` (LLM prompt), `sprk_temperature` (LLM temp), `sprk_outputschemajson` (structured output shape), `sprk_ActionTypeId` FK (executor selector), `sprk_actioncode` (alternate key) |
| **B. Playbook row direct columns** (`sprk_analysisplaybook`) | Playbook-header metadata: identity, scheduling, capabilities, builder hints | `sprk_name`, `sprk_description`, `sprk_ispublic`, `sprk_issystemplaybook`, `sprk_playbooktype` (option-set 0=AiAnalysis, 2=Notification), `sprk_playbookcapabilities` (multi-select), `sprk_canvaslayoutjson` (UI rehydration), `triggerPhrases`, `recordType`, `entityType`, `sprk_jps_matching_metadata`, R1 chat-routing index lifecycle (`sprk_indexstatus`, `sprk_indexhash`, `sprk_lastindexedat`, `sprk_lastindexerror`) |
| **C. Node row** (`sprk_playbooknode`) | Per-node runtime config: which Action, which inputs, where outputs go, when to run | `sprk_actionid` (the canonical dispatch FK), `sprk_outputvariable`, `sprk_position_x`/`y` (UI metadata), `sprk_dependsonjson`, `sprk_conditionjson` (conditional execution guard), `sprk_modeldeploymentid` (override model selection), `sprk_executionorder`, `sprk_isactive` (MUST be true), `sprk_timeoutseconds`, `sprk_retrycount`, and `sprk_configjson` for executor-specific fields (see §3) |
| **D. Playbook-level N:N scopes** (relationships) | Declarative resource scope: what this playbook is allowed/expected to use | `sprk_analysisplaybook_action` (which Actions belong to the playbook), `sprk_playbook_skill`, `sprk_playbook_knowledge`, `sprk_playbook_tool` (N:N constants at `PlaybookService.cs:29-30`) |

Node-level scopes (`sprk_playbooknode_{skill,knowledge,tool}` at `NodeService.cs:27-29`) are a sub-category of Home D — the **per-node** instance of scope declaration.

---

## 3. `sprk_playbooknode.sprk_configjson` — the canonical per-node config column

The node configjson is **the right place** for executor-specific runtime fields that vary per-instance of the action.

**Canonical contents** (the orchestrator + executors actually read these):

- `__actionType` — structural fallback for action-type detection when `sprk_actionid` is empty (see `ai-architecture-playbook-runtime.md` §5). In normal operation the FK wins; this is the safety net.
- Executor-specific input bindings:
  - `QueryDataverseNodeExecutor`: `entityType`, `fetchXml`, `parameters` template fields
  - `CreateNotificationNodeExecutor`: `recipientType`, `categoryRule`, `messageTemplate`, `ttlinseconds`
  - `LookupUserMembershipNodeExecutor`: `userIdParameter`, `entityTypes[]`
  - `ConditionNodeExecutor`: `conditionExpression`
  - `DeliverCompositeNodeExecutor`: `sections[]` (per ADR-037)
  - `DeliverOutputNodeExecutor`: `outputTarget`, `format`
- Routing fields (current state per `node-routing-config.schema.json`): `destination`, `widgetType`, `deliveryType` — **flagged as tech debt** (see §5).

**Read sites**: every executor's `Validate` / `ExecuteAsync` consumes `context.Node.ConfigJson`. The orchestrator extracts `__actionType` at `PlaybookOrchestrationService.cs:867`.

**Writers**: `NodeService.CreateNodeAsync:170` and `Deploy-Playbook.ps1:831`.

---

## 4. Where dispatch, prompt, and categorization live (single-hop model)

Pre-R7, this section walked makers through a multi-rung lookup ladder ("which of four homes carries the executor selector?"). R7 collapses the question. **Dispatch reads `node.sprk_executortype` directly — a single hop, no fallback chain.** Action FK is optional and carries the prompt template only when an executor is prompt-driven. The lookup table (`sprk_analysisactiontype`) is decorative for maker browsing (see §8a for the binding disposition).

The decision is no longer "which home wins?" but "what are you trying to express?"

### What lives where

| What you need to express | Where it lives | Required? |
|---|---|---|
| Which executor handles this node (dispatch identity) | `sprk_playbooknode.sprk_executortype` (Choice column on the node row) | **Required** on every node (FR-07; FR-19 backfill) |
| Prompt template for prompt-driven executors (SystemPrompt, OutputSchemaJson, Temperature) | Action row (`sprk_analysisaction`) via `sprk_playbooknode.sprk_actionid` FK | Required only for prompt-driven executors (AiAnalysis, AiCompletion, AiEmbedding); enforced at executor `Validate()` (FR-06) |
| Per-node runtime config (input bindings, output variable, conditional guard, model deployment override, scope arrays) | `sprk_playbooknode.sprk_configjson` + first-class node columns where they exist (see §3 for canonical contents) | Per-node, per-executor |
| Playbook-header metadata (name, type, capabilities, scheduling, canvas layout) | `sprk_analysisplaybook` row columns (Home B in §2) | Per playbook |
| Declarative resource scope (which Actions / Skills / Knowledge / Tools this playbook depends on) | N:N relationships (Home D in §2) | Per playbook |
| Maker categorization / browsing of "what kinds of actions exist" | `sprk_analysisactiontype` lookup table | **Advisory only — runtime ignores it** (see §8a) |

### Two worked examples

**Example 1 — Prompt-driven node (`AiCompletion`)**: a node in `DAILY-BRIEFING-NARRATE` runs the `BRIEF-NARRATE-TLDR` prompt.

```
sprk_playbooknode row
├─ sprk_executortype = 1 (AiCompletion)             ← single-hop dispatch identity
├─ sprk_actionid = {BRIEF-NARRATE-TLDR Action row}  ← Action FK supplies prompt template
└─ sprk_configjson = { /* template parameters, etc. */ }

sprk_analysisaction row (BRIEF-NARRATE-TLDR)
├─ sprk_systemprompt = "..."                        ← prompt-driven payload only
├─ sprk_outputschemajson = "..."                    ← structured output shape
└─ sprk_temperature = 0
```

Runtime: orchestrator reads `node.sprk_executortype = 1` → routes to `AiCompletionNodeExecutor` (one hop). Executor's `Validate()` then asserts Action FK is present and reads SystemPrompt + OutputSchemaJson from the Action row.

**Example 2 — Pure executor node (`Condition`)**: a node that evaluates a branching condition. No prompt; no Action FK needed.

```
sprk_playbooknode row
├─ sprk_executortype = 30 (Condition)               ← single-hop dispatch identity
├─ sprk_actionid = null                             ← no Action FK (Validate() prohibits it)
└─ sprk_configjson = { "condition": "...", "trueBranch": "...", "falseBranch": "..." }
```

Runtime: orchestrator reads `node.sprk_executortype = 30` → routes to `ConditionNodeExecutor` (one hop). Executor's `Validate()` prohibits Action FK presence and reads the branching expression from `sprk_configjson`.

### What was removed

Three pre-R7 dispatch artifacts no longer exist at runtime:
- The structural-fallback ladder (`IsDeployedStartNode`, `IsDeployedLoadKnowledgeNode`, `IsDeployedReturnResponseNode`) — deleted per FR-08.
- The 3-rung lookup chain (`node.actionid → Action.actiontypeid → lookup_row.executoractiontype`) — replaced by the single `node.sprk_executortype` read per FR-07.
- The `__actionType` discriminator in `sprk_playbooknode.sprk_configjson` — no longer read at runtime per FR-08 (see §3).

### Cross-references

- Runtime mechanics (single-hop dispatch contract, executor registry, validation order): `ai-architecture-playbook-runtime.md`.
- Spec authority: FR-07 (single-hop dispatch), FR-12 / FR-13 (AiCompletion executor + Validate contract), FR-19 (`sprk_executortype` backfill on existing 94 nodes).
- BFF placement decision criteria (config boundary): root `CLAUDE.md` §10 BFF Hygiene + `.claude/constraints/bff-extensions.md` §G.
- Lookup-table disposition (advisory only): §8a below.

---

## 5. Anti-patterns — what to avoid (with evidence)

The following all violate the boundary. Code-archaeology §7 catalogs each.

### ❌ Stuffing node-level wire-up into `sprk_analysisaction.sprk_configjson`

**Why it's wrong**: Actions are designed to be reusable across playbooks. An Action's row carries its intrinsic behaviour (prompt, temperature, output schema). The moment you put a *binding* on the Action row ("read parameter X from request"), the Action is no longer reusable — every playbook that uses it inherits the same binding.

**Note on terminology**: `sprk_configurationjson` (with "uration") does NOT exist on `sprk_analysisaction`. Repo-wide grep returns zero C# references. The owner's terminology is `sprk_configjson` (on node + playbook). When discussing "Action config" only the columns listed in Home A above are real.

**Where to put it instead**: on the *node* (Home C). The Action says "I do X with prompt P at temperature T"; each node says "in this playbook, the input for X comes from parameter Y."

### ❌ Stuffing playbook-level scope decisions into `sprk_playbooknode.sprk_configjson`

**Why it's wrong**: Scopes are a *declarative* layer that lets audit tooling (`jps-playbook-audit`) ask "which Skills does this playbook depend on?" without parsing executor-specific JSON. If you list Skills inline in a node's configjson, the audit cannot find them, and `jps-scope-refresh` cannot refresh them.

**Where to put it instead**: in the N:N (Home D). `sprk_playbooknode_skill` is the per-node skill declaration; `sprk_playbook_skill` is the playbook-level declaration.

### ❌ Using `sprk_analysisplaybook.sprk_configjson` to carry node-graph data when `sprk_playbooknode` rows exist (the R4 deploy bug)

**Why it's wrong**: runtime reads node rows (`PlaybookOrchestrationService.cs:244` → `_nodeService.GetNodesAsync`), not playbook configjson. The R4 UAT defect was a playbook deployed with canvas-only state (configjson populated, no node rows). The orchestrator saw `Length == 0` → Legacy mode → IOORE (pre-hotfix) / clean 503 (post-hotfix).

**Where to put it instead**: deploy `sprk_playbooknode` rows. The deploy script's per-node loop (`Deploy-Playbook.ps1:765-853`) is the canonical writer. `sprk_canvaslayoutjson` and `sprk_analysisplaybook.sprk_configjson` are not substitutes.

### ❌ Routing config (destination, widgetType, deliveryType) in node configJson (current tech debt)

Per `node-routing-config.schema.json` ("additive properties on the existing node-config blob"), routing fields currently live in the node configjson. This was a pragmatic choice — it kept the schema additive — but it conflates "what does this node do" with "where does this node's output go."

**Status**: tech debt. Schema validation at `Deploy-Playbook.ps1:789` keeps it consistent today. R5/R6 should consider promoting routing to first-class columns (`sprk_destination`, `sprk_widgettype`, `sprk_deliverytype`) so:
- Audit tooling can query by destination without parsing JSON.
- Maker UI can present routing as a typed picker rather than a JSON editor.
- The configjson stays focused on executor-specific input/output bindings.

**For now**: routing-in-configjson is acceptable but should be called out in `design.md` Placement Justification (see `.claude/constraints/bff-extensions.md` §G) so reviewers see the choice consciously.

### ❌ DeliverComposite's `sections[]` in configJson (also tech debt, but justified)

`DeliverCompositeNodeExecutor` reads `sections[]` from configjson. The Deploy script lint exempts these nodes from the `actionCode` FK requirement at `Deploy-Playbook.ps1:333` because the executor is registered by ActionType, not FK. ADR-037 documents the design.

The justification: `sections[]` is a per-instance composition decision (which playbook sections to package), not Action-intrinsic. It cannot live on the Action row. Whether it deserves first-class columns (`sprk_deliverysectionjson` or a child entity) is open for R6.

---

## 6. Scope-array semantics — declarative, not enforced

Re-stating from `ai-architecture-playbook-runtime.md` §6 with the boundary lens:

- The four Home D scope relationships are **declarative resource declarations**.
- They are NOT enforced at runtime — the BFF will execute a node whose Action references a Skill not in `playbook.scopes.skills`.
- They ARE consumed by audit + customer + automation tooling as authoritative — "what does this playbook need to operate?"
- Updating scope arrays without runtime impact is safe; this means audit-pass before deploy is the only gate.

**Practical rule**: when you add a Skill / Knowledge / Tool dependency to a playbook, add the N:N row. When you remove a dependency, also remove the N:N row. Treat the N:N as the source of truth even though runtime doesn't enforce it.

---

## 7. Cross-check examples (using R4 surfaces)

| Config item | Home | Why |
|---|---|---|
| `BRIEF-NARRATE` Action's system prompt | A — `sprk_analysisaction.sprk_systemprompt` | Action-intrinsic LLM behaviour |
| `BRIEF-NARRATE` Action's temperature (= 0) | A — `sprk_analysisaction.sprk_temperature` | Action-intrinsic LLM behaviour |
| `EntityNameValidator` Action's allowed-names allowlist | A — `sprk_analysisaction.sprk_systemprompt` OR `sprk_outputschemajson` | Action-intrinsic (per the action's nature) |
| `DAILY-BRIEFING-NARRATE` playbook's `triggerPhrases` (R2 chat-routing) | B — `sprk_analysisplaybook.triggerPhrases` | Playbook-header metadata |
| Which Action a node uses | C — `sprk_playbooknode.sprk_actionid` FK | Per-node runtime config |
| The node's output variable name | C — `sprk_playbooknode.sprk_outputvariable` | Per-node runtime config |
| The node's input parameter binding (which payload field feeds this node) | C — `sprk_playbooknode.sprk_configjson` | Per-node runtime config (executor-specific) |
| The playbook depends on `EntityNameValidator` Action | D — `sprk_analysisplaybook_action` N:N row | Declarative resource scope |
| The playbook depends on the LegalGenreNamesKnowledge entity | D — `sprk_playbook_knowledge` N:N row | Declarative resource scope |
| `customData.category` enrichment on `appnotification` | (none of the playbook homes) — it's on `appnotification.customData` JSON + `appnotification.sprk_category` dual-write column | Notification-output target, not playbook input |

---

## 8. ActionType allocation policy (R5 will codify)

R4 adds `EntityNameValidator = 141` to the ActionType enum (`INodeExecutor.cs:251`). The slot was chosen per R4 spec "slots into post-LLM cluster (Sanitization=130, ObservationEmit=140)."

**Current allocation ranges** (`INodeExecutor.cs:97-252`):

| Range | Cluster |
|---|---|
| 0, 1, 2 | Generic / system |
| 10-12 | Analysis primitives |
| 20-24 | Document operations |
| 30-33 | Control flow |
| 40-42 | Output / delivery |
| 50-52 | User context + membership |
| 60, 70, 80, 90 | Workflow primitives |
| 100, 110, 120 | Integration |
| 130, 140, 141 | Post-LLM (sanitization, observation, name validation) |

**Allocation policy** (proposed, to be codified in R5):

1. New ActionType integers should slot into the appropriate cluster (don't scatter).
2. Reserve gaps within clusters for future expansion (don't fill consecutively unless the new ActionType is conceptually adjacent).
3. Update `INodeExecutor.cs` enum + `NodeExecutorRegistry` registration + Deploy-Playbook.ps1 reference in the same PR.
4. New ActionType requires a corresponding `sprk_actiontype` Dataverse row deployed via `Seed-JpsActions.ps1`.

The policy is not yet binding — capture in this doc so it's not lost between R4 and R5.

---

## 8a. `sprk_analysisactiontype` lookup table — R7 disposition (FR-05)

> **Added by**: spaarke-ai-platform-unification-r7 Wave 4 task 045 (FR-05)
> **Status**: PRESERVED, repurposed as decorative. Wave 6 task 062 may rewrite the surrounding §8 "ActionType allocation policy" treatment to fully reflect the new model; this section is the binding interim disposition note.

The `sprk_analysisactiontype` Dataverse lookup table is KEPT in R7 (not dropped) but its role changes fundamentally:

- **Decorative / maker categorization only**: the table exists so makers can browse "what kinds of actions exist" in the maker portal. It is convenience metadata for human navigation.
- **Runtime ignores it**: `PlaybookOrchestrationService.ExecuteNodeAsync` does NOT read the lookup table or any FK to it for dispatch decisions. Per FR-07, dispatch identity lives ONLY on `sprk_playbooknode.sprk_executortype` (a Choice column directly on the node row) — a single-hop read.
- **The `sprk_executoractiontype` field on lookup-table ROWS remains, but is advisory only**: the column persists on the lookup table for maker readability (so a maker viewing a lookup row can see "this category corresponds to executor type X"), but it is NOT load-bearing. The runtime never reads it. Treat it as informational documentation about the row's intent.
- **The Action row's prior `sprk_actiontypeid` FK is dropped** (FR-03 + FR-04, executed in tasks 042-044). The Action row no longer carries an ActionType pointer at all. Per FR-05, this is the binding state: dispatch is on `node.sprk_executortype`; Actions are prompt templates (Home A — prompt, temperature, output schema), not dispatch markers.

**Traceability**: this disposition implements spec **FR-03** (drop FK on Action), **FR-04** (drop INT field on Action), **FR-05** (KEEP lookup table; field on rows is advisory), and **FR-07** (single-hop dispatch on `node.sprk_executortype`).

**Practical consequence for the §2 four-home model**: the row in §2's Home A table listing `sprk_ActionTypeId FK (executor selector)` reflects the PRE-R7 state. As of R7, that FK is gone; Actions in Home A carry only Action-intrinsic LLM behaviour (prompt, temperature, output schema). The "executor selector" responsibility moves to Home C — the node row's `sprk_executortype` Choice column. Wave 6 task 062 will refresh §2 + §8 to align fully.

---

## 9. Relationship to other canonical docs

| Question | Read |
|---|---|
| How runtime reads these columns (precedence, mode detection) | `ai-architecture-playbook-runtime.md` §3-5 |
| How a consumer surface dispatches a playbook | `ai-architecture-playbook-consumer-routing.md` |
| How to author the JSON file the deploy script consumes | `ai-guide-playbook-deploy-recipe.md` |
| JPS schema for action prompts + structured output | `ai-guide-jps-authoring.md` |
| Maker recipe for a real `sprk_event` notification playbook | `PLAYBOOK-AUTHOR-GUIDE.md` |
| BFF placement decision criteria | `.claude/constraints/bff-extensions.md` (§G now points back here) |

---

## 10. Binding summary

For every new config field, the project's `design.md` **MUST** state:
- Which Home (A, B, C, D, or external) the field belongs to.
- Why it does NOT belong to any of the other three Homes.
- If it lives in `sprk_configjson` (per-node executor-specific), why first-class columns weren't justified (acceptable answer: "MVP; promote in R-X").

This pairs with the `.claude/constraints/bff-extensions.md` §A pre-merge checklist (question 1: "Is the new config field on the right entity? Cross-check against the decision tree.").
