# jps-playbook-design

---
description: Design a complete AI playbook with JPS nodes, scopes, and routing
tags: [ai, jps, playbook, architecture, design]
techStack: [azure-openai, aspnet-core, dataverse]
appliesTo: ["design playbook", "create playbook", "new AI playbook", "playbook architecture"]
alwaysApply: false
---

## Purpose

**Tier 2 Orchestrator Skill** — Designs a complete AI playbook consisting of multiple analysis nodes, shared scopes, conditional routing via $choices, and template parameters. Produces a playbook architecture document and JPS definitions for each node.

**Why This Skill Exists**:
- Playbooks involve multiple interconnected nodes with data flowing between them
- Routing logic ($choices) must align between node outputs and downstream connections
- Shared scopes must be defined once and referenced consistently across nodes
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
4. **Any conditional routing?** (e.g., "if contract → run clause analysis; if invoice → run financial extraction")
5. **What data needs to flow between nodes?** (output of one → input of another)
6. **What shared knowledge is needed across nodes?** (standard clauses, regulatory frameworks, etc.)

### Step 2: Load Architecture Context

```
LOAD knowledge files:
  - docs/guides/SPAARKE-AI-ARCHITECTURE.md (pipeline architecture, Section 19)
  - docs/guides/JPS-AUTHORING-GUIDE.md (JPS schema reference)
  - docs/guides/JPS-COMPREHENSIVE-GUIDE.md (patterns, deployment)
  - .claude/patterns/ai/analysis-scopes.md (scope patterns)

LOAD example JPS files for pattern matching:
  - projects/ai-json-prompt-schema-system/notes/jps-conversions/ (all available)
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
        │
        ▼
┌─────────────────┐
│ Document Profiler│  ← Entry node (always runs)
│ (Profile Action) │
└────────┬────────┘
         │ sprk_documenttype
         ▼
┌─────────────────┐
│  Classifier      │  ← Routing node
│  $choices: type  │
└──┬─────┬─────┬──┘
   │     │     │
   ▼     ▼     ▼
[Contract] [Invoice] [General]
   │         │         │
   ▼         ▼         ▼
[Clause   [Financial [Summary
 Analysis] Extraction] Generation]
```

### Step 4: Define Each Node

For each node in the graph:

```
NODE DEFINITION:
  - Name: {descriptive-name}
  - Action: {Dataverse Action sprk_name}
  - JPS: {file reference or "create new"}
  - Input: {what document/data it receives}
  - Output fields: {what it produces}
  - $choices: {if routing node, what values and where they route}
  - Scopes: {$ref knowledge or skills needed}
  - Template params: {runtime customization}
  - ConfigJson: {node-level overrides if any}
```

If a node needs a new JPS definition, invoke `jps-action-create` for it.
If a node reuses an existing JPS, reference the existing file.

### Step 5: Define Scope Records

```
FOR each shared scope:
  DOCUMENT:
    - Record type: Knowledge or Skill
    - sprk_name: {name used in $ref}
    - Content: {what the scope contains}
    - Used by: {which nodes reference it}

EXAMPLE:
  Knowledge: "standard-contract-clauses"
    - Contains: Library of standard clause templates
    - Referenced by: Clause Analyzer, Clause Comparison
    - $ref: "knowledge:standard-contract-clauses"
```

### Step 6: Define $choices Routing

```
FOR each routing node:
  DOCUMENT:
    - Source field: {output field that drives routing}
    - Possible values: {enum values}
    - Routing table:
      | Value | Downstream Node | Output Variable |
      |-------|----------------|-----------------|
      | "contract" | ContractAnalysis | classificationResult |
      | "invoice"  | FinancialExtraction | classificationResult |
      | "other"    | GeneralSummary | classificationResult |

  CORRESPONDING $choices in downstream nodes:
    "$choices": "downstream:classificationResult.documentType"
```

### Step 7: Generate ConfigJson Templates

For each node, produce the ConfigJson structure:

```json
{
  "actionId": "{action-guid}",
  "outputVariable": "{variable-name}",
  "templateParameters": {
    "param1": "value1"
  },
  "promptSchemaOverride": {
    "instruction": {
      "constraints": ["Additional constraint for this node instance"]
    }
  },
  "fieldMappings": {
    "{fieldName}": {
      "options": {
        "option1": "Display Label 1",
        "option2": "Display Label 2"
      }
    }
  }
}
```

### Step 8: Document the Playbook

Create a playbook design document at `projects/{project}/notes/playbook-{name}.md`:

```markdown
# Playbook: {Name}

## Overview
{Goal and purpose}

## Node Graph
{ASCII diagram from Step 3}

## Nodes
{Table of all nodes with actions, JPS files, inputs/outputs}

## Scopes
{Table of shared knowledge and skill records}

## Routing
{$choices routing table from Step 6}

## Variable Flow
{How data flows between nodes}

## ConfigJson Templates
{Per-node ConfigJson}

## Deployment Checklist
- [ ] All JPS files created and validated
- [ ] Scope records exist in Dataverse
- [ ] Action records exist in Dataverse
- [ ] JPS seeded via Seed-JpsActions.ps1
- [ ] Playbook configured in Dataverse
- [ ] BFF API deployed with latest code
- [ ] End-to-end test with sample document
```

### Step 9: Offer Next Steps

- Validate all JPS definitions (invoke `jps-validate` for each)
- Add to Seed-JpsActions.ps1 for deployment
- Create Dataverse scope records
- Test the playbook end-to-end

---

## Conventions

- Playbook designs stored in `projects/{project}/notes/playbook-{name}.md`
- JPS files stored in `projects/ai-json-prompt-schema-system/notes/jps-conversions/`
- Use consistent `outputVariable` names across the playbook
- $choices routing values must exactly match downstream node ConfigJson fieldMappings
- Shared scopes use $ref (never inline the same content in multiple JPS files)

---

## Playbook Design Patterns

### Pattern 1: Classification + Routing
```
Classifier → $choices → Specialized Analyzers
```
Best for: Document triage, multi-type processing

### Pattern 2: Pipeline (Sequential)
```
Profiler → Extractor → Summarizer
```
Best for: Single document type, progressive enrichment

### Pattern 3: Fan-Out + Aggregation
```
Splitter → [Analyzer A, Analyzer B, Analyzer C] → Aggregator
```
Best for: Multi-faceted analysis (risk + compliance + financial)

### Pattern 4: RAG-Augmented
```
Search (semantic) → Inject results as scope → Analysis
```
Best for: Knowledge-intensive tasks needing dynamic context

---

## Output Format

```
✅ Playbook Designed: {Name}

📊 Nodes: {N} analysis actions
🔀 Routing: {N} $choices decision points
📚 Scopes: {N} shared knowledge/skill records
📄 JPS Files: {N} created, {M} existing

📁 Design doc: projects/{project}/notes/playbook-{name}.md
📁 JPS files: projects/ai-json-prompt-schema-system/notes/jps-conversions/

Next steps:
1. Validate all JPS files: /jps-validate
2. Seed to Dataverse: scripts/Seed-JpsActions.ps1
3. Deploy BFF API: scripts/Deploy-BffApi.ps1
4. Test end-to-end
```

---

## Error Handling

| Situation | Response |
|-----------|----------|
| Circular routing (A → B → A) | Warn user, suggest breaking the cycle |
| Missing downstream node for $choices | Create the missing node or ask user |
| Scope referenced but not defined | Prompt user to define scope content |
| Too many nodes (>10) | Suggest splitting into sub-playbooks |

---

## Related Skills

- `jps-action-create` — Create individual JPS definitions (called by this skill)
- `jps-validate` — Validate JPS files after creation
- `dataverse-deploy` — Deploy scope records and actions

---

## Tips for AI

- Start with the node graph (Step 3) before creating any JPS files — get user buy-in on architecture first
- Use AskUserQuestion to confirm routing logic before generating $choices
- Always trace variable flow end-to-end — ensure every $choices reference has a matching source
- Reuse existing JPS definitions whenever possible (check jps-conversions/ folder first)
- Keep playbooks under 8 nodes — larger workflows should be split into sub-playbooks
