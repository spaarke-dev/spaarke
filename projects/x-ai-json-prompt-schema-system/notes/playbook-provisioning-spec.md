# Playbook Provisioning System — Design Specification

> **Status**: In Progress
> **Author**: Ralph Schroeder + Claude Code
> **Created**: 2026-03-05
> **Branch**: `work/ai-json-prompt-schema`
> **PR**: #207

---

## 1. Executive Summary

This specification defines an end-to-end playbook provisioning system that enables AI business analysts to create fully functional playbooks using Claude Code. The system consists of three integrated components:

1. **Scope & Model Index** — A machine-readable JSON catalog of all available scopes (actions, skills, knowledge, tools) and AI models, enabling Claude Code to select the right primitives for any playbook
2. **Unified Provisioning Script** — A single PowerShell script that creates a complete playbook in Dataverse from a definition file, including the playbook record, all nodes, N:N scope associations, and canvas layout
3. **Enhanced `jps-playbook-design` Skill** — An upgraded Claude Code skill that goes from design-only to end-to-end: requirements → scope selection → model selection → definition generation → Dataverse deployment → canvas verification

### User Story

> As an AI business analyst, I want to describe a required playbook to Claude Code in natural language, and have it create the complete playbook including selecting appropriate scope primitives, choosing the best AI model per node, and deploying everything to Dataverse — with the result visible in the Playbook Builder canvas.

---

## 2. Architecture Context

### Current State

The Spaarke AI platform has mature playbook infrastructure:

- **PlaybookService** (`PlaybookService.cs`) — CRUD for playbook records + N:N scope associations
- **NodeService** (`NodeService.cs`) — Node lifecycle, N:N scope associations, canvas-to-node sync
- **PlaybookCanvas** (`PlaybookCanvas.tsx`) — React Flow visual builder reading from Dataverse
- **Scope Resolution** — `ActionLookupService`, `SkillLookupService`, `ToolLookupService` with alternate key lookups (`sprk_actioncode`, `sprk_skillcode`, `sprk_toolcode`)
- **Ad hoc scripts** — `Create-PlaybookSeedRecords.ps1`, `Create-FinancePlaybooks.ps1`, `Deploy-Playbooks.ps1`

### Gap

No unified process connects "business analyst describes a playbook" to "playbook exists in Dataverse with all scopes and nodes, visible on canvas." Each project creates its own script. Claude Code's `jps-playbook-design` skill stops at producing a design document.

### Target State

```
Business Analyst                Claude Code                    Dataverse
     │                              │                              │
     │  "I need a playbook that     │                              │
     │   reviews lease agreements"  │                              │
     │ ─────────────────────────►   │                              │
     │                              │  Load scope-model-index.json │
     │                              │  Select: ACT-003, SKL-003,   │
     │                              │  SKL-006, KNW-003, TL-001    │
     │                              │  Model: gpt-4o-mini (triage) │
     │                              │         gpt-4o (analysis)    │
     │  ◄── "Here's my plan..."     │                              │
     │  ──► "Looks good, deploy"    │                              │
     │                              │  Generate definition JSON    │
     │                              │  Run Deploy-Playbook.ps1     │
     │                              │ ────────────────────────────► │
     │                              │           Create playbook    │
     │                              │           Create nodes       │
     │                              │           Associate scopes   │
     │                              │           Save canvas layout │
     │                              │ ◄──────── Playbook ID        │
     │  ◄── "Deployed! Open in      │                              │
     │       Playbook Builder"      │                              │
```

---

## 3. Dataverse Entity Model

### Entities

| Entity | Logical Name | Entity Set | Alternate Key |
|--------|-------------|-----------|---------------|
| Playbook | `sprk_analysisplaybook` | `sprk_analysisplaybooks` | `sprk_externalid` |
| Playbook Node | `sprk_playbooknode` | `sprk_playbooknodes` | (primary key only) |
| Action | `sprk_analysisaction` | `sprk_analysisactions` | `sprk_actioncode` |
| Skill | `sprk_analysisskill` | `sprk_analysisskills` | `sprk_skillcode` |
| Knowledge | `sprk_analysisknowledge` | `sprk_analysisknowledges` | `sprk_externalid` |
| Tool | `sprk_analysistool` | `sprk_analysistools` | `sprk_toolcode` |
| Model Deployment | `sprk_aimodeldeployment` | `sprk_aimodeldeployments` | (name filter) |

### N:N Relationships

| Level | Relationship Name | Source → Target |
|-------|------------------|-----------------|
| Playbook → Action | `sprk_analysisplaybook_action` | `sprk_analysisplaybooks` → `sprk_analysisactions` |
| Playbook → Skill | `sprk_playbook_skill` | `sprk_analysisplaybooks` → `sprk_analysisskills` |
| Playbook → Knowledge | `sprk_playbook_knowledge` | `sprk_analysisplaybooks` → `sprk_analysisknowledges` |
| Playbook → Tool | `sprk_playbook_tool` | `sprk_analysisplaybooks` → `sprk_analysistools` |
| Node → Skill | `sprk_playbooknode_skill` | `sprk_playbooknodes` → `sprk_analysisskills` |
| Node → Knowledge | `sprk_playbooknode_knowledge` | `sprk_playbooknodes` → `sprk_analysisknowledges` |
| Node → Tool | `sprk_playbooknode_tool` | `sprk_playbooknodes` → `sprk_analysistools` |

### Playbook Fields

| Field | Type | Description |
|-------|------|-------------|
| `sprk_name` | String (200) | Display name |
| `sprk_description` | String (4000) | Purpose and overview |
| `sprk_ispublic` | Boolean | Visible to all users |
| `sprk_istemplate` | Boolean | Marks template playbooks |
| `sprk_playbookcapabilities` | Multi-select choice | Feature flags (analyze, search, write_back, etc.) |
| `sprk_canvaslayoutjson` | String (max) | React Flow canvas state JSON |
| `sprk_externalid` | String (100) | Alternate key for idempotent operations |

### Node Fields

| Field | Type | Description |
|-------|------|-------------|
| `sprk_name` | String (200) | Display name |
| `sprk_playbookid` | Lookup | Parent playbook |
| `sprk_actionid` | Lookup | Action to execute |
| `sprk_nodetype` | Choice | AIAnalysis (100000000), Output (100000001), Control (100000002) |
| `sprk_modeldeploymentid` | Lookup | Per-node AI model override |
| `sprk_executionorder` | Int | Sequential position |
| `sprk_outputvariable` | String (100) | Variable name for downstream reference |
| `sprk_dependsonjson` | String (max) | JSON array of upstream node GUIDs |
| `sprk_conditionjson` | String (max) | Conditional execution expression |
| `sprk_configjson` | String (max) | Action-specific configuration |
| `sprk_positionx` | Int | Canvas X coordinate |
| `sprk_positiony` | Int | Canvas Y coordinate |
| `sprk_isactive` | Boolean | Enable/disable node |

---

## 4. Scope Inventory (Current State)

### Actions (8)

| Code | Name | Document Types | Key Extractions |
|------|------|---------------|-----------------|
| ACT-001 | Contract Review | Contract, MSA, PSA | Parties, obligations, dates, payment, termination, IP, liability |
| ACT-002 | NDA Analysis | NDA, CDA | Confidential info, obligations, exclusions, duration, remedies |
| ACT-003 | Lease Agreement Review | Commercial/residential leases | Rent, escalation, renewal, permitted use, obligations |
| ACT-004 | Invoice Processing | Invoices, utility bills | Vendor info, line items, totals, payment terms |
| ACT-005 | SLA Analysis | SLAs | SLOs, methodology, credits, escalation |
| ACT-006 | Employment Agreement Review | Offer letters, employment contracts | Compensation, equity, IP assignment, non-compete |
| ACT-007 | Statement of Work Analysis | SOWs, work orders | Deliverables, milestones, acceptance criteria, fees |
| ACT-008 | General Legal Document Review | Any legal document | Classification, parties, dates, obligations, risk |

### Skills (10)

| Code | Name | Description |
|------|------|-------------|
| SKL-001 | Citation Extraction | Cite claims with [Section: X, Page Y] format |
| SKL-002 | Risk Flagging | Highlight risk clauses with [RISK: HIGH/MEDIUM/LOW] |
| SKL-003 | Summary Generation | Executive summary in 3-5 bullet points |
| SKL-004 | Date Extraction | Extract/normalize all dates to ISO 8601 |
| SKL-005 | Party Identification | All parties with legal names, roles, contact |
| SKL-006 | Obligation Mapping | Party/Obligation/Condition/Deadline table |
| SKL-007 | Defined Terms | Extract defined terms into glossary |
| SKL-008 | Financial Terms | Monetary amounts, schedules, rates |
| SKL-009 | Termination Analysis | Triggers, notice periods, cure periods, consequences |
| SKL-010 | Jurisdiction and Governing Law | Applicable law, jurisdiction, dispute resolution |

### Knowledge Sources (10)

| Code | Name | Summary |
|------|------|---------|
| KNW-001 | Common Contract Terms Glossary | 50+ standard term definitions |
| KNW-002 | NDA Review Checklist | 20-item NDA review checklist |
| KNW-003 | Lease Agreement Standards | Commercial lease standards |
| KNW-004 | Invoice Processing Guide | AP invoice processing rules |
| KNW-005 | SLA Metrics Reference | SLA/SLO/SLI definitions |
| KNW-006 | Employment Law Quick Reference | US employment law fundamentals |
| KNW-007 | IP Assignment Clause Library | Annotated IP assignment clauses |
| KNW-008 | Termination and Remedy Provisions | Termination triggers, damages |
| KNW-009 | Governing Law and Jurisdiction Guide | Governing law, arbitration |
| KNW-010 | Legal Document Red Flags Catalog | 32 red flags, 10 categories |

### Tools (8)

| Code | Name | Handler | Description |
|------|------|---------|-------------|
| TL-001 | DocumentSearch | DocumentSearchHandler | Search knowledge base/document index |
| TL-002 | AnalysisRetrieval | AnalysisQueryHandler | Retrieve prior analysis results |
| TL-003 | KnowledgeRetrieval | KnowledgeRetrievalHandler | Retrieve knowledge source content |
| TL-004 | TextRefinement | TextRefinementHandler | AI-assisted text editing |
| TL-005 | CitationExtractor | CitationExtractorHandler | Extract citation references |
| TL-006 | SummaryGenerator | SummaryGeneratorHandler | Generate structured summaries |
| TL-007 | RedFlagDetector | RedFlagDetectorHandler | Detect risk/compliance issues |
| TL-008 | PartyExtractor | PartyExtractorHandler | Extract party information |

### Existing Playbook Compositions (10)

| Code | Name | Action | Skills | Knowledge | Tools |
|------|------|--------|--------|-----------|-------|
| PB-001 | Standard Contract Review | ACT-001 | SKL-001,002,003 | KNW-001 | TL-001,002 |
| PB-002 | NDA Deep Review | ACT-002 | SKL-001,004,005 | KNW-002 | TL-001,002,007 |
| PB-003 | Commercial Lease Analysis | ACT-003 | SKL-003,006 | KNW-003 | TL-001,002 |
| PB-004 | Invoice Validation | ACT-004 | SKL-004,008 | KNW-004 | TL-001,003 |
| PB-005 | SLA Compliance Review | ACT-005 | SKL-002,006 | KNW-005 | TL-001,002,007 |
| PB-006 | Employment Agreement Review | ACT-006 | SKL-005,007,009 | KNW-006,007 | TL-001,002 |
| PB-007 | Statement of Work Analysis | ACT-007 | SKL-003,006 | KNW-005 | TL-001,002 |
| PB-008 | IP Assignment Review | ACT-001 | SKL-007,009 | KNW-007 | TL-001,002,008 |
| PB-009 | Termination Risk Assessment | ACT-001 | SKL-002,009 | KNW-008 | TL-001,007 |
| PB-010 | Quick Legal Scan | ACT-008 | SKL-002,010 | KNW-010 | TL-001,007 |

---

## 5. Model Selection Matrix

### Available Models

| Model | Provider | Cost Tier | Speed Tier | Context Window |
|-------|----------|----------|-----------|---------------|
| gpt-4o | Azure OpenAI | High | Medium | 128K tokens |
| gpt-4o-mini | Azure OpenAI | Low | Fast | 128K tokens |
| gpt-4-turbo | Azure OpenAI | High | Slow | 128K tokens |

### Selection Rules

| Task Type | Recommended Model | Rationale |
|-----------|------------------|-----------|
| Classification / Triage | gpt-4o-mini | Bounded decision with well-defined categories; speed > depth |
| Deep Analysis | gpt-4o | Multi-faceted reasoning requires full model capability |
| Entity Extraction | gpt-4o | Accuracy critical for structured output |
| Simple Summarization | gpt-4o-mini | Sufficient for TL;DR and bullet summaries |
| Detailed Summarization | gpt-4o | Cross-referencing and citation requires depth |
| Legal Reasoning | gpt-4o | Interpretive, nuanced analysis |
| Financial Calculation | gpt-4o | Accuracy and multi-step computation |
| Condition / Routing | gpt-4o-mini | Simple boolean or categorical evaluation |

### Cost Impact Example

A 3-node playbook (classify → analyze → summarize):
- **All gpt-4o**: ~$7.50 per 1M input tokens
- **Optimized**: classify (mini) + analyze (4o) + summarize (mini) → ~$3.00 per 1M input tokens
- **Savings**: ~60% cost reduction with negligible quality impact

---

## 6. Playbook Definition Schema

The provisioning script input format:

```json
{
  "$schema": "https://spaarke.com/schemas/playbook-definition/v1",

  "playbook": {
    "name": "string (required, max 200 chars)",
    "description": "string (optional, max 4000 chars)",
    "externalId": "string (optional, alternate key for idempotent ops)",
    "isPublic": "boolean (default: false)",
    "capabilities": ["analyze", "search", "write_back", "reanalyze", "selection_revise", "web_search", "summarize"],
    "scopes": {
      "actions": ["ACT-xxx"],
      "skills": ["SKL-xxx"],
      "knowledge": ["KNW-xxx"],
      "tools": ["TL-xxx"]
    }
  },

  "nodes": [
    {
      "name": "string (required, max 200 chars)",
      "actionCode": "ACT-xxx (required for AIAnalysis nodes)",
      "nodeType": "AIAnalysis | Output | Control",
      "outputVariable": "string (required, max 100 chars)",
      "model": "gpt-4o | gpt-4o-mini | gpt-4-turbo (optional)",
      "positionX": "int (canvas position)",
      "positionY": "int (canvas position)",
      "dependsOn": ["node name references"],
      "configJson": {},
      "conditionJson": "expression (for Control nodes)",
      "scopes": {
        "skills": ["SKL-xxx"],
        "knowledge": ["KNW-xxx"],
        "tools": ["TL-xxx"]
      }
    }
  ],

  "edges": [
    {
      "source": "node name",
      "target": "node name",
      "type": "smoothstep | bezier (default: smoothstep)"
    }
  ]
}
```

### Design Principles

1. **Scope codes, not GUIDs** — Uses ACT-001/SKL-001/KNW-001/TL-001 codes, making definitions environment-portable across dev/test/prod
2. **Node names for dependencies** — `dependsOn: ["Classify Document"]` resolved to GUIDs at deploy time
3. **Model names, not GUIDs** — `"model": "gpt-4o"` resolved via `sprk_aimodeldeployments` query
4. **Both playbook-level and node-level scopes** — Playbook-level scopes apply to all nodes; node-level scopes add specificity

---

## 7. Canvas Layout Integration

### How Backend Creation Syncs to Canvas

The PlaybookCanvas reads from the same Dataverse records:

```
Deploy-Playbook.ps1
    │
    ├─ Creates sprk_analysisplaybook record
    ├─ Creates sprk_playbooknode records (with positionX/Y)
    ├─ Associates N:N scopes
    └─ Saves sprk_canvaslayoutjson (React Flow state)
           │
           ▼
PlaybookService.GetCanvasLayoutAsync()
           │
           ▼
PlaybookCanvas.tsx (@xyflow/react)
    → Renders nodes at specified positions
    → Renders edges between connected nodes
```

### Canvas Layout JSON Format

```json
{
  "viewport": { "x": 0, "y": 0, "zoom": 1.0 },
  "nodes": [
    {
      "id": "{node-guid}",
      "type": "aiAnalysis",
      "position": { "x": 100, "y": 200 },
      "data": { "label": "Classify Document", "actionCode": "ACT-008" }
    }
  ],
  "edges": [
    {
      "id": "e-{guid1}-{guid2}",
      "source": "{guid1}",
      "target": "{guid2}",
      "type": "smoothstep",
      "animated": false
    }
  ],
  "version": 1
}
```

### Auto-Layout Algorithm

```
Dagre-style positioning:
- Entry node: (100, 200)
- Horizontal spacing: 300px between sequential nodes
- Vertical spacing: 150px between parallel nodes
- Sequential flow: left-to-right
- Parallel branches: stack vertically at same X coordinate
```

---

## 8. Provisioning Script Flow

```
Deploy-Playbook.ps1 -DefinitionFile playbook.json -Environment dev

Step 1: Authenticate (az account get-access-token)
Step 2: Load & validate definition JSON
Step 3: Resolve scope codes → Dataverse GUIDs
         sprk_analysisactions(sprk_actioncode='ACT-001')
         sprk_analysisskills(sprk_skillcode='SKL-001')
         sprk_analysistools(sprk_toolcode='TL-001')
         sprk_analysisknowledges by sprk_externalid
Step 4: Resolve model names → sprk_aimodeldeployments GUIDs
Step 5: Check for existing playbook (by externalId or name)
Step 6: Create sprk_analysisplaybook record
Step 7: Associate playbook-level N:N scopes (POST $ref)
Step 8: Create nodes in execution order (POST sprk_playbooknodes)
Step 9: Second pass — set sprk_dependsonjson with resolved node GUIDs
Step 10: Associate node-level N:N scopes (POST $ref per node)
Step 11: Build canvas layout JSON, PATCH to playbook
Step 12: Print summary (playbook ID, node count, associations)
```

---

## 9. Enhanced Skill Workflow (13 Steps)

| Step | Phase | Description |
|------|-------|-------------|
| 1 | Requirements | Gather playbook goal, document types, analysis steps |
| 2 | Context | Load `scope-model-index.json` + architecture docs |
| 3 | Design | Design node graph (ASCII diagram), present to user |
| 4 | Scope Selection | Match nodes to actions/skills/knowledge/tools from index |
| 5 | Model Selection | Apply modelSelectionRules, show cost/speed tradeoffs |
| 6 | Confirmation | Present selections via AskUserQuestion |
| 7 | Generate | Produce playbook definition JSON file |
| 8 | Validate | Verify codes exist, no circular deps, valid layout |
| 9 | Deploy | Run Deploy-Playbook.ps1 (DryRun first, then live) |
| 10 | Verify | Query Dataverse to confirm creation |
| 11 | Document | Write playbook design doc |
| 12 | Canvas | Confirm nodes render in PlaybookCanvas |
| 13 | Next Steps | Suggest testing with sample document |

### Decision Points (User Input Required)

1. After Step 3 — "Does this node graph match your requirements?"
2. After Step 5 — "Confirm scope and model selections"
3. After Step 8 — "Definition is valid. Deploy to Dataverse?"

---

## 10. File Manifest

### Files to Create

| File | Purpose |
|------|---------|
| `docs/ai-knowledge/catalogs/scope-model-index.json` | Machine-readable scope & model catalog |
| `scripts/Deploy-Playbook.ps1` | Unified playbook provisioning script |
| `scripts/Refresh-ScopeModelIndex.ps1` | Regenerate index from Dataverse |
| `docs/guides/PLAYBOOK-CREATION-GUIDE.md` | User guide for creating playbooks |

### Files to Modify

| File | Change |
|------|--------|
| `.claude/skills/jps-playbook-design/SKILL.md` | Enhance from 9-step to 13-step |

### Reference Files (Read-Only)

| File | Purpose |
|------|---------|
| `projects/ai-spaarke-platform-enhancements-r1/notes/design/scope-library-catalog.md` | Source data for index |
| `projects/ai-spaarke-platform-enhancements-r1/scripts/Create-PlaybookSeedRecords.ps1` | Pattern: playbook creation |
| `src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs` | Pattern: node creation + N:N |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookService.cs` | Pattern: playbook CRUD |
| `src/server/api/Sprk.Bff.Api/Services/Ai/ActionLookupService.cs` | Pattern: alternate key lookup |
| `scripts/Seed-JpsActions.ps1` | Pattern: seeding script conventions |

---

## 11. Task List

| # | Task | Status | Deliverable |
|---|------|--------|-------------|
| 1 | Create `scope-model-index.json` with all 8 actions, 10 skills, 10 knowledge, 8 tools, models, rules, compositions | Pending | Index |
| 2 | Create `Deploy-Playbook.ps1` provisioning script | Pending | Script |
| 3 | Create `Refresh-ScopeModelIndex.ps1` to regenerate index from Dataverse | Pending | Script |
| 4 | Update `jps-playbook-design` SKILL.md from 9-step to 13-step | Pending | Skill |
| 5 | Create `PLAYBOOK-CREATION-GUIDE.md` for business analysts | Pending | Guide |
| 6 | Create test definition JSON for PB-001 and run Deploy-Playbook.ps1 | Pending | Test |
| 7 | Verify playbook appears in Playbook Builder canvas | Pending | Verification |
| 8 | Commit and push all changes | Pending | Git |

---

## 12. Success Criteria

1. Business analyst can describe a playbook in natural language to Claude Code
2. Claude Code loads the scope index, selects appropriate scopes and models
3. Claude Code asks clarifying questions when requirements are ambiguous
4. A valid playbook definition JSON is generated
5. `Deploy-Playbook.ps1` creates all records in Dataverse (playbook, nodes, N:N scopes, canvas layout)
6. The playbook appears correctly in the Playbook Builder canvas
7. The playbook can execute against a sample document
8. The process is documented and repeatable across sessions
