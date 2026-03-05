# jps-playbook-design

---
description: Design and deploy a complete AI playbook — from requirements through Dataverse deployment
tags: [ai, jps, playbook, architecture, design, deployment]
techStack: [azure-openai, aspnet-core, dataverse]
appliesTo: ["design playbook", "create playbook", "new AI playbook", "playbook architecture"]
alwaysApply: false
---

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

### Step 2: Load Architecture Context

```
LOAD knowledge files:
  - docs/ai-knowledge/catalogs/scope-model-index.json (REQUIRED — scope catalog + model rules)
  - docs/guides/SPAARKE-AI-ARCHITECTURE.md (pipeline architecture, Section 19)
  - docs/guides/JPS-AUTHORING-GUIDE.md (JPS schema reference)
  - docs/guides/JPS-COMPREHENSIVE-GUIDE.md (patterns, deployment)

LOAD example JPS files for pattern matching:
  - projects/ai-json-prompt-schema-system/notes/jps-conversions/ (all available)

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
  - Which output fields drive $choices routing?
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
VALIDATE:
  1. All scope codes exist in scope-model-index.json
  2. No circular dependencies in node graph
  3. All dependsOn references match existing node names
  4. Canvas positions don't overlap (min 200px apart)
  5. All required fields present per node
  6. outputVariable names are unique

IF validation fails:
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

### Step 10: Verify Deployment

```
QUERY Dataverse to verify:
  1. Playbook record exists with correct name
  2. Node count matches definition
  3. N:N scope associations are correct
  4. Canvas layout JSON is saved

REPORT:
  "Deployed successfully
   Playbook: {name} ({guid})
   Nodes: {count} created
   Scopes: {count} associations
   Canvas: layout saved"
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
{$choices routing table}

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

## Conventions

- Playbook designs stored in `projects/{project}/notes/playbook-{name}.md`
- Playbook definitions stored in `projects/{project}/notes/playbook-definitions/{playbook-name}.json`
- JPS files stored in `projects/ai-json-prompt-schema-system/notes/jps-conversions/`
- Use consistent `outputVariable` names across the playbook
- $choices routing values must exactly match downstream node ConfigJson fieldMappings
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
| Missing downstream node for $choices | Create the missing node or ask user |
| Scope referenced but not defined | Prompt user to define scope content |
| Too many nodes (>10) | Suggest splitting into sub-playbooks |
| Scope code not found in index | Flag for new scope creation via `jps-action-create` |
| Model rule missing for task type | Default to gpt-4o and warn user |
| scope-model-index.json not found | STOP — file is required, inform user to create it |
| Definition validation fails | Report specific issues, offer auto-fix or return to Step 4 |
| DryRun fails | Show error output, do NOT proceed to actual deploy |
| Deploy-Playbook.ps1 not found | STOP — script is required for deployment |
| Dataverse connection fails | Check auth, suggest `dev-cleanup` skill for credential refresh |
| Node count mismatch after deploy | Warn user, suggest re-deploy or manual verification |

---

## Related Skills

- `jps-action-create` — Create individual JPS definitions (called by this skill when new actions needed)
- `jps-validate` — Validate JPS files after creation
- `dataverse-deploy` — Deploy scope records and actions
- `Deploy-Playbook.ps1` — PowerShell script for automated playbook deployment to Dataverse

---

## Tips for AI

- Start with the node graph (Step 3) before selecting scopes — get user buy-in on architecture first
- Always load `scope-model-index.json` in Step 2 — it is the single source of truth for available scopes and model rules
- Use AskUserQuestion to confirm routing logic before generating $choices
- Always trace variable flow end-to-end — ensure every $choices reference has a matching source
- Reuse existing JPS definitions whenever possible (check jps-conversions/ folder first)
- Keep playbooks under 8 nodes — larger workflows should be split into sub-playbooks
- When selecting scopes (Step 4), prefer exact `documentTypes` matches over broad tag matches
- Do not overload nodes with skills — 2-4 skills per node is the sweet spot
- Always run DryRun before actual deployment — never skip Step 9's dry run phase
- If a scope code is missing from the index, offer to create it via `jps-action-create` rather than inventing codes
- Model selection should optimize for cost — use gpt-4o-mini for classification and simple summarization, reserve gpt-4o for deep analysis and legal reasoning
- Canvas auto-layout: keep 300px horizontal spacing and 150px vertical spacing to avoid overlap
