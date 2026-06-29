---
description: Design and deploy a complete AI playbook — from requirements through Dataverse deployment, orchestrating multi-node analysis with JPS-defined actions
tags: [ai, jps, playbook, architecture, design, deployment]
techStack: [azure-openai, aspnet-core, dataverse]
appliesTo: ["design playbook", "create playbook", "new AI playbook", "playbook architecture"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-06-29
---

# jps-playbook-design

> **Last Reviewed**: 2026-06-29
> **Reviewed By**: spaarke-ai-platform-unification-r7 task 071 (FR-32 — node-first dispatch rewrite). Workflow reframed: pick `sprk_executortype` Choice FIRST per node, then optional Action for prompt-driven executors. Step 1.5 item 3 + Step 10 deployment-verify query updated to remove the legacy 3-layer precedence + `sprk_nodetype` projection — both are gone post-R7. Step 2 + ai-architecture-playbook-runtime §5 references corrected (the §5 lookup-precedence section was DELETED in Wave 6 task 061). Prior review: ai-procedure-quality-r1 (Phase 2b Wave 2d).
> **Exemplar rationale**: Playbook designs are project-specific; the JPS schema + Dataverse table model is the contract.
> **Justified length** (516 lines): Tier 2 orchestrator chaining `jps-action-create`, `jps-scope-refresh`, `jps-validate`. The 13-step design procedure + scope/model selection logic + ASCII canvas layout must remain inline for the agent executing the design to follow accurately. Splitting risks dereferencing reliability.
> **Drift note**: The singular `sprk_analysisaction` (logical name) vs plural `sprk_analysisactions` (Web API collection endpoint) — both are correct in their respective contexts. This skill uses plural in OData URLs; `jps-scope-refresh` uses singular as the logical table name.

## Purpose

**Tier 2 Orchestrator Skill** — End-to-end playbook creation: gathers requirements, designs the node graph, selects scopes and models from the catalog, generates a deployable definition JSON, deploys to Dataverse via `Deploy-Playbook.ps1`, and verifies the result.

**Why This Skill Exists**:
- Playbooks involve multiple interconnected nodes with data flowing between them
- Routing logic ($choices) must align between node outputs and downstream connections
- Shared scopes must be defined once and referenced consistently across nodes
- Scope and model selection should be driven by the catalog (`scope-model-index.json`), not guesswork
- Deployment should be automated and verified, not a manual checklist
- Without systematic design, playbooks become fragile and hard to maintain

## Applies When

- User says "design playbook", "create playbook", "new AI playbook"
- User describes a multi-step analysis workflow
- Explicitly invoked with `/jps-playbook-design`
- NOT for single-action JPS creation (use `jps-action-create` instead)

---

## Workflow

### Step 1: Gather Playbook Requirements

Ask the user:

1. **What is the playbook's goal?** (e.g., "comprehensive contract analysis", "document triage")
2. **What document types does it process?**
3. **What analysis steps are needed?** (classification, extraction, comparison, summarization, etc.)
4. **Any conditional routing?** (e.g., "if contract -> run clause analysis; if invoice -> run financial extraction")
5. **What data needs to flow between nodes?** (output of one -> input of another)
6. **What shared knowledge is needed across nodes?** (standard clauses, regulatory frameworks, etc.)

### Step 1.5: Runtime Contract Reminder (BINDING per canonical-truth loop 2026-06-26; updated for R7 single-hop dispatch 2026-06-29)

Before designing, internalize these runtime rules from [`docs/architecture/ai-architecture-playbook-runtime.md`](../../../docs/architecture/ai-architecture-playbook-runtime.md):

1. **Mode is emergent, not declarative**. There is NO `sprk_playbookmode` column read at runtime. The orchestrator (`PlaybookOrchestrationService.cs:246-253`) checks for `sprk_playbooknode` rows; zero rows → Legacy mode log + fallback. **Do NOT instruct the user to set a "mode" field on the playbook row** — instead instruct them to deploy node rows.
2. **The playbook's `sprk_configjson` is NOT the node graph**. The node graph MUST be deployed as `sprk_playbooknode` rows via `Deploy-Playbook.ps1` per [`ai-guide-playbook-deploy-recipe.md`](../../../docs/guides/ai-guide-playbook-deploy-recipe.md). Putting nodes inline in playbook `sprk_configjson` is the R4 deploy bug — runtime ignores it.
3. **Dispatch is SINGLE-HOP at runtime** (R7 FR-07 + FR-08 + FR-09, 2026-06-29). `PlaybookOrchestrationService.ExecuteNodeAsync` reads `node.sprk_executortype` (Choice) directly and routes to the matching `INodeExecutor`. **There is no precedence ladder; there is no structural fallback; the Action's ActionType override branch is deleted.** Pre-R7 this skill described a 3-tier ladder (Action FK → ConfigJson `__actionType` → NodeType-default) — that ladder is GONE, and the deploy script no longer needs to "lint actionCode for dispatch reasons." Action FK is still required for prompt-driven executors (carries SystemPrompt + OutputSchema + Temperature), but it is no longer a dispatch signal. The previously-referenced `ai-architecture-playbook-runtime.md` §5 "Action-Lookup Precedence" section was DELETED in Wave 6 task 061 (it described the now-removed ladder).
4. **Empty-payload contract** (Path A.5): if the playbook is a non-document dispatch (e.g., `/narrate`, `/summarize` invoked via `IInvokePlaybookAi` facade), the playbook **MUST** have nodes — otherwise Legacy fallback fires + returns 503 `PLAYBOOK_INVOCATION_FAILED`. See [`ai-architecture-consumer-routing.md`](../../../docs/architecture/ai-architecture-consumer-routing.md) §3.
5. **Config-bag boundary** (from [`ai-architecture-actions-nodes-scopes.md`](../../../docs/architecture/ai-architecture-actions-nodes-scopes.md) §4): every config field has a single Home — Action / Playbook columns / Node row / N:N scopes. **Dispatch identity is a Home C concern** (lives on `sprk_playbooknode.sprk_executortype`), not a Home A concern. When designing nodes, do NOT inline scope decisions in node configJson; use the N:N relationships.

### Step 2: Load Architecture Context

```
LOAD knowledge files:
  - .claude/catalogs/scope-model-index.json (REQUIRED — scope catalog + model rules)
  - docs/architecture/ai-architecture-playbook-runtime.md (CANONICAL runtime contract — mode-emergent, action precedence, scope advisory)
  - docs/architecture/ai-architecture-consumer-routing.md (Path A.5 dispatch for non-document playbooks)
  - docs/architecture/ai-architecture-actions-nodes-scopes.md (config-home decision tree §4 — load BEFORE Step 4)
  - docs/guides/ai-guide-playbook-deploy-recipe.md (Deploy-Playbook.ps1 12-step contract)
  - docs/guides/JPS-AUTHORING-GUIDE.md (JPS schema reference — trimmed to schema-only)

LOAD example JPS files for pattern matching:
  - .claude/skills/jps-action-create/examples/ (all available)

PARSE scope-model-index.json:
  - actions[]: Available analysis actions with document types and tags
  - skills[]: Available skills with compatible action mappings
  - knowledge[]: Available knowledge sources with tags
  - tools[]: Available tools with handlers
  - models[]: Available AI models with cost/speed tiers
  - modelSelectionRules[]: Task type -> model mapping
  - compositions{}: Existing playbook patterns for reference
```

### Step 3: Design the Playbook Graph

Map out the complete playbook structure:

```
IDENTIFY:
  1. Entry node (first analysis action — often a classifier or profiler)
  2. Routing nodes (actions that produce $choices for conditional branching)
  3. Specialized nodes (actions triggered by routing decisions)
  4. Aggregation nodes (actions that combine results from multiple paths)
  5. Terminal nodes (final output — UpdateRecord or summary)

DESIGN variables flow:
  - Which output fields from Node A feed into Node B?
  - Which output fields use $choices (Dataverse lookups, option sets, or downstream routing)?
  - What template parameters customize nodes at runtime?

IDENTIFY shared scopes:
  - Knowledge records reused across multiple nodes
  - Skill records shared between nodes
  - Use $ref for shared scopes (avoid inline duplication)
```

**Output a playbook graph (ASCII)**:

```
[Document Upload]
        |
        v
+-----------------+
| Document Profiler|  <- Entry node (always runs)
| (Profile Action) |
+--------+--------+
         | sprk_documenttype
         v
+-----------------+
|  Classifier      |  <- Routing node
|  $choices: type  |
+--+-----+-----+--+
   |     |     |
   v     v     v
[Contract] [Invoice] [General]
   |         |         |
   v         v         v
[Clause   [Financial [Summary
 Analysis] Extraction] Generation]
```

### Step 4: Select Scopes from Index

```
FOR each node in the graph:
  1. IDENTIFY node purpose:
     - Classification/triage -> look for classification-capable actions
     - Deep analysis -> match by document type
     - Extraction -> match by key extraction types
     - Summarization -> match summary-capable actions

  2. SELECT action from index:
     - Match by documentTypes array
     - Match by tags
     - If no match -> flag for new action creation (offer jps-action-create)

  3. SELECT skills from index:
     - Filter by compatibleActions containing the selected action code
     - Match by tags relevant to node purpose
     - Select 2-4 most relevant skills (don't overload)

  4. SELECT knowledge from index:
     - Match by tags relevant to document type
     - Select 1-2 most relevant knowledge sources

  5. SELECT tools from index:
     - DocumentSearch (TL-001) for most nodes
     - RedFlagDetector (TL-007) for risk-focused nodes
     - SummaryGenerator (TL-006) for summary nodes
     - PartyExtractor (TL-008) for entity-focused nodes

PRESENT scope selection to user:
  "Node 1: Classify Document
   Action: ACT-008 (General Legal Document Review)
   Skills: SKL-003 (Summary Generation)
   Knowledge: (none)
   Tools: TL-001 (DocumentSearch)

   Node 2: Deep Contract Review
   Action: ACT-001 (Contract Review)
   Skills: SKL-001 (Citation), SKL-002 (Risk Flagging), SKL-009 (Termination)
   Knowledge: KNW-001 (Contract Terms Glossary), KNW-008 (Termination Provisions)
   Tools: TL-001 (DocumentSearch), TL-002 (AnalysisRetrieval)"
```

### Step 5: Select Model per Node

```
FOR each node:
  1. CLASSIFY task type from node purpose:
     - Classification/triage -> "classification"
     - Deep analysis -> "deep-analysis"
     - Entity extraction -> "extraction"
     - Simple summary -> "summarization-simple"
     - Detailed summary -> "summarization-detailed"
     - Legal analysis -> "legal-reasoning"
     - Financial analysis -> "financial-calculation"
     - Routing condition -> "condition-routing"

  2. LOOKUP modelSelectionRules in index

  3. ASSIGN model

PRESENT model selection with cost estimate:
  "Model Selection:
   Node 1 (Classify): gpt-4o-mini (fast, low cost)
   Node 2 (Analyze):  gpt-4o (thorough, higher cost)
   Node 3 (Summary):  gpt-4o-mini (fast, low cost)

   Estimated cost: ~$2.80 per 1M input tokens
   (vs $7.50 if all gpt-4o — 63% savings)"
```

### Step 6: User Confirmation

```
USE AskUserQuestion:
  "Here are the scope and model selections for your playbook:
   [scope + model summary from Steps 4-5]

   Options:
   1. Approve and proceed to deployment
   2. Modify scope selections
   3. Change model assignments
   4. Redesign the node graph"
```

If the user selects option 2, return to Step 4. If option 3, return to Step 5. If option 4, return to Step 3.

### Step 7: Generate Playbook Definition JSON

```
GENERATE definition file at:
  projects/{project}/notes/playbook-definitions/{playbook-name}.json

STRUCTURE:
{
  "$schema": "https://spaarke.com/schemas/playbook-definition/v1",
  "playbook": {
    "name": "...",
    "description": "...",
    "isPublic": true,
    "capabilities": [...],
    "scopes": {
      "actions": ["ACT-xxx"],
      "skills": ["SKL-xxx"],
      "knowledge": ["KNW-xxx"],
      "tools": ["TL-xxx"]
    }
  },
  "nodes": [...],
  "edges": [...]
}

AUTO-LAYOUT positions:
  - Entry node at (100, 200)
  - Sequential nodes: +300px X per step
  - Parallel nodes: +150px Y at same X
  - Fan-out: branches at Y offsets from center
```

### Step 8: Validate Definition

```
VALIDATE (offline — index check):
  1. All scope codes exist in scope-model-index.json
  2. No circular dependencies in node graph
  3. All dependsOn references match existing node names
  4. Canvas positions don't overlap (min 200px apart)
  5. All required fields present per node
  6. outputVariable names are unique

VALIDATE (live — Dataverse cross-check):
  7. Run Deploy-Playbook.ps1 with -DryRun first (Step 9 does this)
     BUT ALSO run a lightweight scope check BEFORE DryRun:

  FOR each unique scope code in the definition (actions, skills, knowledge, tools):
    QUERY Dataverse to verify the record exists:
      - Actions:   sprk_analysisactions?$filter=sprk_actioncode eq '{code}'&$top=1
      - Skills:    sprk_analysisskills?$filter=sprk_skillcode eq '{code}'&$top=1
      - Knowledge: sprk_analysisknowledges?$filter=sprk_externalid eq '{code}'&$top=1
      - Tools:     sprk_analysistools?$filter=sprk_toolcode eq '{code}'&$top=1

    IF record not found:
      ADD to missing_scopes list

  FOR each unique model name in the definition:
    QUERY Dataverse: sprk_aimodeldeployments?$filter=sprk_name eq '{name}'&$top=1

    IF not found:
      ADD to missing_models list

  IF missing_scopes or missing_models:
    REPORT all missing items to user:
      "⚠️ The following references are in scope-model-index.json but NOT in Dataverse:
       Missing scopes: KNW-001 (Common Contract Terms Glossary)
       Missing models: (none)

       Options:
       1. Remove missing references from definition and deploy
       2. Seed missing records first (run Seed-JpsActions.ps1 or create manually)
       3. Abort deployment"

    USE AskUserQuestion to let user decide
    IF user chooses option 1 → remove codes from definition JSON and continue
    IF user chooses option 2 → STOP and let user seed records
    IF user chooses option 3 → STOP

  IF all references resolve:
    → "✅ All scope codes and models verified in Dataverse. Ready to deploy."

IF offline validation fails:
  -> Report issues
  -> Offer to fix automatically or return to Step 4
```

### Step 9: Deploy to Dataverse

```
FIRST run DryRun:
  powershell -ExecutionPolicy Bypass -File scripts/Deploy-Playbook.ps1 \
    -DefinitionFile "{path}" -Environment dev -DryRun

PRESENT results to user:
  "DryRun shows {N} scope resolutions, {M} nodes to create.
   All codes resolved successfully. Deploy now?"

IF user confirms:
  powershell -ExecutionPolicy Bypass -File scripts/Deploy-Playbook.ps1 \
    -DefinitionFile "{path}" -Environment dev
```

### Step 9.5: Refresh Scope Index (if new scopes were created)

```
IF any new scopes were created during this workflow (via jps-action-create in Step 4):
  RUN: powershell -ExecutionPolicy Bypass -File scripts/Refresh-ScopeModelIndex.ps1 -Environment dev

  This ensures scope-model-index.json reflects the newly created scopes
  so future playbook designs will find them automatically.

  REPORT: "Scope index refreshed — {N} new entries added."

IF no new scopes were created:
  → SKIP (index is already current)
```

### Step 10: Verify Deployment

```
QUERY Dataverse via MCP read_query to verify (BINDING — sprk_playbooknode rows are the runtime read path, NOT canvas JSON):

  1. mcp__dataverse__read_query against sprk_analysisplaybook
       filter: sprk_name eq '{playbookName}'
       select: sprk_name, sprk_analysisplaybookid, sprk_canvaslayoutjson
     EXPECT 1 row — capture sprk_analysisplaybookid

  2. mcp__dataverse__read_query against sprk_playbooknode
       filter: _sprk_playbookid_value eq <captured Guid>
       select: sprk_name, sprk_executortype, sprk_isactive, _sprk_actionid_value, sprk_executionorder
       orderby: sprk_executionorder asc
     EXPECT N rows where N == definition.nodes.length
     EXPECT every row has sprk_isactive = true (else runtime sees zero nodes → Legacy mode)
     EXPECT every row has sprk_executortype set to a valid Choice value (single-hop dispatch axis per R7 FR-07; `sprk_nodetype` column was removed in the pre-R7 schema cleanup and `sprk_executortype` is the sole node-level dispatch signal post-R7)
     EXPECT every prompt-driven node (executorType ∈ {AiAnalysis=0, AiCompletion=1, AiEmbedding=2}) has _sprk_actionid_value non-null (Action FK carries SystemPrompt + OutputSchema + Temperature for prompt-driven executors only — pure executors leave it NULL)

  3. mcp__dataverse__read_query for N:N associations
       Verify sprk_analysisplaybook_action, sprk_playbook_skill, sprk_playbook_knowledge, sprk_playbook_tool match scopes

  4. Canvas layout JSON saved (informational — runtime does NOT read this)

REPORT:
  "Deployed successfully
   Playbook: {name} ({guid})
   Nodes: {count} created (all sprk_isactive=true)
   FKs: {count}/{count} dispatchable nodes have _sprk_actionid_value
   Scopes: {count} associations
   Canvas: layout saved
   Runtime mode prediction: NodeBased (nodes present, FKs resolved)"

IF node count mismatches OR any sprk_isactive=false OR any dispatchable node missing FK:
  WARN — playbook will hit Legacy mode log at PlaybookOrchestrationService.cs:250
  OFFER to re-run Deploy-Playbook.ps1 -Force
```

### Step 11: Document the Playbook

Create a playbook design document at `projects/{project}/notes/playbook-{name}.md`:

```markdown
# Playbook: {Name}

## Overview
{Goal and purpose}

## Node Graph
{ASCII diagram from Step 3}

## Nodes
{Table of all nodes with actions, JPS files, inputs/outputs}

## Scope Assignments
{Per-node scope selections from Step 4}

## Model Assignments
{Per-node model selections from Step 5 with cost estimate}

## Routing
{$choices table — include prefix type (lookup/optionset/multiselect/boolean/downstream) and resolution source}

## Variable Flow
{How data flows between nodes}

## Definition File
Path: `projects/{project}/notes/playbook-definitions/{playbook-name}.json`

## Deployment Results
- Playbook ID: {guid}
- Nodes created: {count}
- Scope associations: {count}
- Environment: dev
- Deployed at: {timestamp}
```

### Step 12: Canvas Verification

```
INFORM user:
  "Open the Playbook Builder in Dataverse to verify the canvas:
   1. Navigate to Analysis Playbooks
   2. Open '{playbook name}'
   3. Click 'Analysis Builder' -> 'Open'
   4. Verify nodes appear at correct positions with edges"
```

### Step 13: Offer Next Steps

- Test end-to-end with a sample document
- Create new scope primitives if needed (invoke `jps-action-create`)
- Refine the playbook based on test results
- Deploy to additional environments (staging, production)
- Add monitoring and alerting for playbook execution

---

## The R7 dispatch model — what changed and why

> **Read this once.** It anchors why "Pick Executor Type FIRST per node" is the correct mental model in R7+.

**Before R7**, the orchestrator resolved a node's executor via a 3-layer ladder: `node.sprk_nodetype` → `Action.sprk_executoractiontype` INT → `Action.sprk_actiontypeid` lookup → ActionType enum. None of the layers was enforced to agree, so they drifted across releases and every release shipped a different version of the same "wrong executor ran" bug (see design.md §3.1 for the WHY history).

**After R7** (design.md §2 Invariants 1-3):
- **Invariant 1**: Every node has `sprk_executortype` (Choice) set explicitly.
- **Invariant 2**: `PlaybookOrchestrationService.ExecuteNodeAsync` reads `node.sprk_executortype` once. Single hop. No ladder.
- **Invariant 3**: Action is a reusable prompt template (`SystemPrompt + OutputSchema + Temperature`) for prompt-driven executors. It does NOT carry dispatch identity.

**The 33-executor catalog** (Choice values 0-143; categorized by tier per design.md §11):
- AI tier (prompt-driven, Action FK REQUIRED): AiAnalysis (0), AiCompletion (1), AiEmbedding (2)
- Workflow tier (pure executors, Action FK NULL): RuleEngine (10), Calculation (11), DataTransform (12), Condition (30), Parallel (31), Wait (32), Start (33)
- Action tier (pure executors): CreateTask (20), SendEmail (21), UpdateRecord (22), CallWebhook (23), SendTeamsMessage (24)
- Delivery tier (pure): DeliverOutput (40), DeliverToIndex (41), DeliverComposite (42)
- Notification (pure): CreateNotification (50)
- Data tier (pure): QueryDataverse (51), LookupUserMembership (52)
- Reasoning tier: AgentService (60), GroundingVerify (70), LiveFact (80), IndexRetrieve (90), EvidenceSufficiency (100), DeclineToFind (110), ReturnInsightArtifact (120), Sanitization (130), ObservationEmit (140)
- Validation/IO (pure): EntityNameValidator (141), LoadKnowledge (142), ReturnResponse (143)

**Author workflow under R7**:
1. **Pick `sprk_executortype` FIRST** per node — this is the dispatch decision.
2. If executor is **prompt-driven** (AiAnalysis / AiCompletion / AiEmbedding) → pick or author an Action FK using `jps-action-create`.
3. If executor is **pure** → leave Action FK NULL; configure via `sprk_configjson` (Home C).
4. The Wave 8 PlaybookBuilder UI (FR-21 to FR-27) renders the 33 executors in a categorized selector (FR-22), enables the Action tab only for prompt-driven executors (FR-24), and shows typed config forms per executor (FR-23) — so the UI nudges you toward the correct mental model.

**Cross-references**:
- design.md §2 (Invariants 1-3) — binding rules
- design.md §3.1 (R3.1 WHY history) — failure modes that motivated the rewrite
- design.md §11 (executor categorization) — tier mapping
- spec.md FR-07 / FR-08 / FR-09 — runtime reform
- spec.md FR-21 to FR-27 — Wave 8 UI shape
- `PlaybookOrchestrationService.ExecuteNodeAsync` — runtime source of truth (Wave 2 task 024)

---

## Conventions

- Playbook designs stored in `projects/{project}/notes/playbook-{name}.md`
- Playbook definitions stored in `projects/{project}/notes/playbook-definitions/{playbook-name}.json`
- JPS files stored in `.claude/skills/jps-action-create/examples/`
- Use consistent `outputVariable` names across the playbook
- $choices values must use a supported prefix: `lookup:`, `optionset:`, `multiselect:`, `boolean:` (Dataverse-resolved), or `downstream:` (routing). Downstream routing values must exactly match downstream node ConfigJson fieldMappings
- Shared scopes use $ref (never inline the same content in multiple JPS files)
- Scope codes (ACT-xxx, SKL-xxx, KNW-xxx, TL-xxx) must come from `scope-model-index.json`
- Model assignments must follow `modelSelectionRules` from the index unless explicitly overridden

---

## Playbook Design Patterns

### Pattern 1: Classification + Routing
```
Classifier -> $choices -> Specialized Analyzers
```
Best for: Document triage, multi-type processing

### Pattern 2: Pipeline (Sequential)
```
Profiler -> Extractor -> Summarizer
```
Best for: Single document type, progressive enrichment

### Pattern 3: Fan-Out + Aggregation
```
Splitter -> [Analyzer A, Analyzer B, Analyzer C] -> Aggregator
```
Best for: Multi-faceted analysis (risk + compliance + financial)

### Pattern 4: RAG-Augmented
```
Search (semantic) -> Inject results as scope -> Analysis
```
Best for: Knowledge-intensive tasks needing dynamic context

---

## Output Format

```
Playbook Designed and Deployed: {Name}

Nodes: {N} analysis actions
Routing: {N} $choices decision points
Scopes: {N} shared knowledge/skill records
Models: {N} assignments ({fast count} fast, {thorough count} thorough)
Cost estimate: ~${X} per 1M input tokens

Definition: projects/{project}/notes/playbook-definitions/{playbook-name}.json
Design doc: projects/{project}/notes/playbook-{name}.md
Dataverse ID: {playbook-guid}

Deployment:
  Environment: dev
  Nodes created: {count}
  Scope associations: {count}
  Canvas layout: saved

Next steps:
1. Verify canvas in Playbook Builder
2. Test end-to-end with sample document
3. Deploy to staging when ready
```

---

## Error Handling

| Situation | Response |
|-----------|----------|
| Circular routing (A -> B -> A) | Warn user, suggest breaking the cycle |
| Missing downstream node for $choices (downstream:) | Create the missing node or ask user |
| Dataverse entity/field not found for $choices (lookup:/optionset:/etc.) | Verify entity logical name and field name in Dataverse |
| Scope referenced but not defined | Prompt user to define scope content |
| Too many nodes (>10) | Suggest splitting into sub-playbooks |
| Scope code not found in index | Flag for new scope creation via `jps-action-create` |
| Scope code in index but NOT in Dataverse | Step 8 Dataverse cross-check catches this. Offer: remove from definition, seed record, or abort |
| Model deployment not in Dataverse | Step 8 cross-check catches this. Check `sprk_aimodeldeployments` table |
| Model rule missing for task type | Default to gpt-4o and warn user |
| scope-model-index.json not found | STOP — file is required, inform user to create it |
| Definition validation fails | Report specific issues, offer auto-fix or return to Step 4 |
| DryRun fails | Show error output, do NOT proceed to actual deploy |
| Deploy-Playbook.ps1 not found | STOP — script is required for deployment |
| Deploy-Playbook.ps1 pre-flight fails | Script reports ALL missing scopes + models together before creating anything. Fix missing records and re-run |
| Dataverse connection fails | Check auth, suggest `dev-cleanup` skill for credential refresh |
| Node count mismatch after deploy | Warn user, suggest re-deploy or manual verification |

---

## Related Skills

- `jps-action-create` — Create individual JPS definitions (called by this skill when new actions needed)
- `jps-scope-refresh` — Refresh scope-model-index.json from Dataverse (called at Step 9.5 if new scopes created)
- `jps-validate` — Validate JPS files after creation
- `dataverse-deploy` — Deploy scope records and actions
- `Deploy-Playbook.ps1` — PowerShell script for automated playbook deployment to Dataverse

---

## Tips for AI

- Start with the node graph (Step 3) before selecting scopes — get user buy-in on architecture first
- Always load `scope-model-index.json` in Step 2 — it is the single source of truth for available scopes and model rules
- Use AskUserQuestion to confirm routing logic before generating $choices
- Always trace variable flow end-to-end — ensure every `downstream:` $choices has a matching source node, and every `lookup:`/`optionset:`/`multiselect:`/`boolean:` $choices references a valid Dataverse entity and field
- Reuse existing JPS definitions whenever possible (check examples/ folder first)
- Keep playbooks under 8 nodes — larger workflows should be split into sub-playbooks
- When selecting scopes (Step 4), prefer exact `documentTypes` matches over broad tag matches
- Do not overload nodes with skills — 2-4 skills per node is the sweet spot
- Always run the Dataverse cross-check in Step 8 before deployment — scope-model-index.json can be out of sync with actual Dataverse records
- Always run DryRun before actual deployment — never skip Step 9's dry run phase
- Deploy-Playbook.ps1 has a pre-flight check that reports ALL missing scopes + models before creating any records — no orphaned records on failure
- If a scope code is missing from the index, offer to create it via `jps-action-create` rather than inventing codes
- If a scope code is in the index but missing from Dataverse, offer to remove it from the definition or seed the record first
- Model selection should optimize for cost — use gpt-4o-mini for classification and simple summarization, reserve gpt-4o for deep analysis and legal reasoning
- Canvas auto-layout: keep 300px horizontal spacing and 150px vertical spacing to avoid overlap

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Designed playbook references scope codes that don't exist in Dataverse | `scope-model-index.json` stale OR designer invented codes | Step 9.5 invokes `jps-scope-refresh` to re-sync. If scope still missing AFTER refresh, scope record was never created — invoke `jps-action-create`. |
| Deploy-Playbook.ps1 fails pre-flight with "missing model deployment" | Azure OpenAI deployment doesn't exist in target environment | Verify deployments via Azure Portal OR `az cognitiveservices account deployment list`. Either add the deployment OR change the playbook to use an existing model. |
| Cost runaway — playbook uses gpt-4o for every node | Model selection in Step 5 was lazy ("just use the most capable") | Apply cost optimization: gpt-4o-mini for classification/summarization (~10x cheaper); reserve gpt-4o for deep analysis. Step 5's model-selection logic enforces this. |
| Canvas layout has overlapping nodes | Auto-layout spacing not followed (300px H, 150px V) | Re-run auto-layout step. If still overlapping, manually adjust positions in canvas JSON. |
| Playbook deploys but execution errors out at validation | `jps-validate` step in design wasn't run before deploy | ALWAYS run `jps-validate` against each JPS definition BEFORE Deploy-Playbook.ps1. Catches schema errors early. |
| Scope `sprk_analysisaction` vs `sprk_analysisactions` ambiguity | Logical name (singular) vs Web API endpoint (plural) | Both correct in context. Logical name in references to the table; plural in OData URLs. Verify via `mcp__dataverse__list_tables`. |
