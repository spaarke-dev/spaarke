# Playbook Design Guide

> **Audience**: AI Business Analysts, Solution Architects
> **Last Updated**: March 2026
> **Related**: [playbook-architecture.md](../architecture/playbook-architecture.md) (technical internals), [PLAYBOOK-BUILDER-GUIDE.md](PLAYBOOK-BUILDER-GUIDE.md) (builder UI)

---

## Overview

This guide explains how to create AI analysis playbooks using Claude Code. A playbook is a multi-node pipeline that processes documents through a series of AI analysis steps — classification, extraction, analysis, summarization — with each step using the optimal AI model and scope configuration.

---

## Quick Start

### Option 1: Natural Language (Recommended)

Tell Claude Code what you need in plain English:

```
I need a playbook that reviews commercial lease agreements.
It should extract key dates, financial terms, and obligations,
then flag any non-standard clauses and produce an executive summary.
```

Claude Code will:
1. Design a node graph based on your description
2. Select the right analysis actions, skills, knowledge, and tools from the scope catalog
3. Choose the optimal AI model for each node (gpt-4o for deep analysis, gpt-4o-mini for triage)
4. Ask you to confirm the design
5. Generate the playbook definition
6. Deploy everything to Dataverse
7. Verify it appears in the Playbook Builder canvas

### Option 2: Slash Command

```
/jps-playbook-design
```

This invokes the structured 13-step workflow with prompts at each decision point.

### Option 3: Definition File (Advanced)

Write a playbook definition JSON directly and deploy with:

```powershell
.\scripts\Deploy-Playbook.ps1 -DefinitionFile "path/to/my-playbook.json"
```

---

## How It Works

### Step-by-Step Process

```
YOU                          CLAUDE CODE                    DATAVERSE
 │                              │                              │
 │  Describe your playbook      │                              │
 │ ───────────────────────────► │                              │
 │                              │  1. Load scope catalog       │
 │                              │  2. Design node graph        │
 │                              │  3. Select scopes & models   │
 │                              │                              │
 │  ◄─── "Here's my plan:      │                              │
 │        3 nodes, 5 skills,    │                              │
 │        estimated $3/1M tok"  │                              │
 │                              │                              │
 │  "Looks good, deploy it"     │                              │
 │ ───────────────────────────► │                              │
 │                              │  4. Generate definition JSON │
 │                              │  5. Run Deploy-Playbook.ps1  │
 │                              │ ────────────────────────────► │
 │                              │     Create playbook record   │
 │                              │     Create nodes             │
 │                              │     Link scopes (N:N)        │
 │                              │     Save canvas layout       │
 │                              │ ◄──────────────────────────── │
 │  ◄─── "Deployed! Open in    │                              │
 │        Playbook Builder to   │                              │
 │        view on canvas"       │                              │
```

### What Claude Code Selects Automatically

| Component | What It Does | How Claude Code Chooses |
|-----------|-------------|----------------------|
| **Action** | Base analysis instruction | Matches your document type and analysis goal |
| **Skills** | Domain expertise additions | Selects skills compatible with the chosen action |
| **Knowledge** | Reference materials | Matches by document type and analysis domain |
| **Tools** | Executable handlers | Based on required capabilities (search, extract, detect) |
| **Model** | AI model per node | Task complexity → model selection rules (see below) |

---

## Understanding Scopes

Playbooks are composed from four types of **scope primitives**:

### Actions (ACT-*)

The primary analysis instruction for a node. Each AI node uses exactly one action.

| Code | Name | Best For |
|------|------|----------|
| ACT-001 | Contract Review | MSAs, PSAs, vendor agreements |
| ACT-002 | NDA Analysis | NDAs, CDAs, confidentiality agreements |
| ACT-003 | Lease Agreement Review | Commercial and residential leases |
| ACT-004 | Invoice Processing | Vendor invoices, utility bills |
| ACT-005 | SLA Analysis | Service level agreements |
| ACT-006 | Employment Agreement Review | Offer letters, employment contracts |
| ACT-007 | Statement of Work Analysis | SOWs, work orders |
| ACT-008 | General Legal Document Review | Any legal document (catch-all) |

### Skills (SKL-*)

Composable expertise that enriches the analysis. Each node can use multiple skills.

| Code | Name | What It Adds |
|------|------|-------------|
| SKL-001 | Citation Extraction | Section and page citations for every claim |
| SKL-002 | Risk Flagging | [RISK: HIGH/MEDIUM/LOW] annotations |
| SKL-003 | Summary Generation | Executive summary in 3-5 bullets |
| SKL-004 | Date Extraction | All dates normalized to ISO 8601 |
| SKL-005 | Party Identification | Full legal names, roles, contacts |
| SKL-006 | Obligation Mapping | Structured obligation table |
| SKL-007 | Defined Terms | Alphabetical glossary of defined terms |
| SKL-008 | Financial Terms | Monetary amounts, schedules, rates |
| SKL-009 | Termination Analysis | Triggers, notice periods, consequences |
| SKL-010 | Jurisdiction & Governing Law | Applicable law, dispute resolution |

### Knowledge Sources (KNW-*)

Reference materials injected into the prompt for context.

| Code | Name | Content |
|------|------|---------|
| KNW-001 | Contract Terms Glossary | 50+ standard term definitions |
| KNW-002 | NDA Review Checklist | 20-item NDA checklist |
| KNW-003 | Lease Standards | Commercial lease provisions |
| KNW-004 | Invoice Processing Guide | AP rules and fraud indicators |
| KNW-005 | SLA Metrics Reference | SLA/SLO/SLI definitions |
| KNW-006 | Employment Law Reference | US employment fundamentals |
| KNW-007 | IP Assignment Clause Library | Annotated IP clauses |
| KNW-008 | Termination Provisions | Triggers, damages, survival |
| KNW-009 | Jurisdiction Guide | Governing law, arbitration |
| KNW-010 | Red Flags Catalog | 32 red flags across 10 categories |

### Tools (TL-*)

Executable handlers that perform specific operations.

| Code | Name | What It Does |
|------|------|-------------|
| TL-001 | DocumentSearch | Search knowledge base and document index |
| TL-002 | AnalysisRetrieval | Retrieve prior analysis results |
| TL-003 | KnowledgeRetrieval | Retrieve knowledge source content |
| TL-004 | TextRefinement | AI-assisted text editing |
| TL-005 | CitationExtractor | Extract citation references |
| TL-006 | SummaryGenerator | Generate structured summaries |
| TL-007 | RedFlagDetector | Detect risk/compliance issues |
| TL-008 | PartyExtractor | Extract party information |

---

## Understanding Model Selection

Claude Code automatically selects the best AI model for each node based on the task:

| Node Purpose | Model Selected | Why |
|-------------|---------------|-----|
| Document classification | gpt-4o-mini | Simple categorical decision — fast and cheap |
| Document triage | gpt-4o-mini | Binary/bounded decision — speed matters |
| Deep contract analysis | gpt-4o | Complex reasoning, nuanced interpretation |
| Entity extraction | gpt-4o | Accuracy critical for structured output |
| Simple summary (TL;DR) | gpt-4o-mini | Bullet summaries don't need depth |
| Detailed summary with citations | gpt-4o | Cross-referencing requires full model |
| Legal reasoning | gpt-4o | Interpretive analysis needs depth |
| Financial calculation | gpt-4o | Multi-step computation needs accuracy |
| Condition routing | gpt-4o-mini | Simple boolean evaluation |

### Cost Impact

| Strategy | Estimated Cost (per 1M tokens) |
|----------|-------------------------------|
| All gpt-4o | ~$7.50 |
| Optimized (mix) | ~$3.00 |
| All gpt-4o-mini | ~$0.45 |

Claude Code will show you the estimated cost breakdown before deploying.

---

## Playbook Patterns

### Pattern 1: Classification + Routing

Best for: Document triage, multi-type processing

```
┌─────────────────┐
│ Document Classifier│  ← gpt-4o-mini
│ (ACT-008)        │
└──┬─────┬─────┬──┘
   │     │     │
   ▼     ▼     ▼
[Contract] [Invoice] [General]   ← gpt-4o each
```

**Example prompt**: "Create a playbook that classifies incoming documents, then routes contracts to clause analysis and invoices to financial extraction"

### Pattern 2: Sequential Pipeline

Best for: Single document type, progressive enrichment

```
[Profile] → [Extract] → [Analyze] → [Summarize]
 mini         4o          4o          mini
```

**Example prompt**: "Create a pipeline that profiles a contract, extracts key entities, analyzes risk, and generates an executive summary"

### Pattern 3: Fan-Out + Aggregation

Best for: Multi-faceted analysis (risk + compliance + financial)

```
          [Dispatcher]
         /     |      \
[Risk]  [Compliance]  [Financial]
         \     |      /
          [Aggregator]
```

**Example prompt**: "Create a playbook that analyzes contracts from three angles simultaneously: risk assessment, compliance check, and financial impact — then combines results"

### Pattern 4: RAG-Augmented

Best for: Knowledge-intensive tasks needing dynamic context

```
[Semantic Search] → [Inject results] → [Analysis with context]
```

**Example prompt**: "Create a playbook that searches our knowledge base for relevant precedents before analyzing a contract"

---

## Example: Creating a Lease Review Playbook

### What You Say

```
I need a playbook for reviewing commercial lease agreements.
It should:
- Identify parties and key dates
- Extract financial terms (rent, escalation, CAM charges)
- Map tenant and landlord obligations
- Flag any non-standard provisions
- Produce an executive summary for the business team
```

### What Claude Code Designs

```
Node Graph:
  [Lease Profiler] → [Clause Analysis] → [Summary]
      ACT-003           ACT-003           ACT-003
      gpt-4o-mini       gpt-4o            gpt-4o-mini

Scopes Selected:
  Skills: SKL-003 (Summary), SKL-004 (Dates), SKL-005 (Parties),
          SKL-006 (Obligations), SKL-008 (Financial)
  Knowledge: KNW-003 (Lease Standards)
  Tools: TL-001 (DocumentSearch), TL-007 (RedFlagDetector)

Estimated cost: ~$2.80 per 1M input tokens
```

### What You Confirm

Claude Code asks: "Does this design match your requirements? Shall I deploy?"

### What Gets Created in Dataverse

- 1 playbook record with 5 skill + 1 knowledge + 2 tool associations
- 3 node records with per-node scopes and model overrides
- Canvas layout JSON with node positions and edges
- Visible in Playbook Builder immediately

---

## Advanced: Writing Definition Files Manually

For full control, write the definition JSON directly:

```json
{
  "$schema": "https://spaarke.com/schemas/playbook-definition/v1",
  "playbook": {
    "name": "Custom Lease Review",
    "description": "Three-stage lease analysis pipeline",
    "isPublic": true,
    "capabilities": ["analyze", "search"],
    "scopes": {
      "actions": ["ACT-003"],
      "skills": ["SKL-003", "SKL-004", "SKL-005", "SKL-006", "SKL-008"],
      "knowledge": ["KNW-003"],
      "tools": ["TL-001", "TL-007"]
    }
  },
  "nodes": [
    {
      "name": "Lease Profiler",
      "actionCode": "ACT-003",
      "nodeType": "AIAnalysis",
      "outputVariable": "profile",
      "model": "gpt-4o-mini",
      "positionX": 100,
      "positionY": 200,
      "dependsOn": [],
      "scopes": {
        "skills": ["SKL-004", "SKL-005"],
        "knowledge": [],
        "tools": ["TL-001"]
      }
    },
    {
      "name": "Deep Clause Analysis",
      "actionCode": "ACT-003",
      "nodeType": "AIAnalysis",
      "outputVariable": "analysis",
      "model": "gpt-4o",
      "positionX": 400,
      "positionY": 200,
      "dependsOn": ["Lease Profiler"],
      "scopes": {
        "skills": ["SKL-006", "SKL-008"],
        "knowledge": ["KNW-003"],
        "tools": ["TL-001", "TL-007"]
      }
    },
    {
      "name": "Executive Summary",
      "actionCode": "ACT-003",
      "nodeType": "AIAnalysis",
      "outputVariable": "summary",
      "model": "gpt-4o-mini",
      "positionX": 700,
      "positionY": 200,
      "dependsOn": ["Deep Clause Analysis"],
      "scopes": {
        "skills": ["SKL-003"],
        "knowledge": [],
        "tools": ["TL-006"]
      }
    }
  ],
  "edges": [
    { "source": "Lease Profiler", "target": "Deep Clause Analysis" },
    { "source": "Deep Clause Analysis", "target": "Executive Summary" }
  ]
}
```

Deploy with:

```powershell
.\scripts\Deploy-Playbook.ps1 -DefinitionFile "my-lease-playbook.json"

# Preview first without creating:
.\scripts\Deploy-Playbook.ps1 -DefinitionFile "my-lease-playbook.json" -DryRun
```

---

## Creating New Scope Primitives

If your playbook needs a scope that doesn't exist yet:

### New Action

Tell Claude Code: "I need a new action for analyzing insurance policies"

Claude Code will:
1. Run `/jps-action-create` to generate the JPS definition
2. Add it to `Seed-JpsActions.ps1` and seed to Dataverse
3. Add it to `scope-model-index.json`

### New Skill

Tell Claude Code: "I need a skill for extracting coverage limits from insurance documents"

Claude Code will:
1. Create the prompt fragment content
2. Add it to `Seed-AnalysisSkills.ps1` and update Dataverse
3. Add it to `scope-model-index.json`

### New Knowledge Source

Tell Claude Code: "I need a knowledge source with standard insurance policy terms"

Claude Code will:
1. Create the reference content
2. Add it to `Seed-KnowledgeScopes.ps1` and seed to Dataverse
3. Add it to `scope-model-index.json`

---

## Updating the Scope Index

After creating new scopes, refresh the index:

```powershell
# Regenerate scope-model-index.json from current Dataverse state
.\scripts\Refresh-ScopeModelIndex.ps1 -Environment dev
```

This ensures Claude Code always has the latest scope catalog when designing playbooks.

---

## Troubleshooting

| Issue | Cause | Fix |
|-------|-------|-----|
| "Scope code not found" during deploy | Code doesn't exist in Dataverse | Run the appropriate seed script first |
| Nodes overlap on canvas | Default positions conflict | Edit positionX/Y in definition, or drag in canvas |
| Playbook doesn't appear in canvas | Canvas layout JSON not saved | Re-run Deploy-Playbook.ps1 with the same definition |
| Wrong model being used | Model deployment not found | Check `sprk_aimodeldeployments` in Dataverse |
| Node scopes not linked | N:N association failed | Check script output for errors; verify relationship names |

---

## Output Nodes

Playbooks support two output node types for delivering results:

### DeliverOutput (ActionType 40)

The standard output node. Assembles results from upstream nodes into a structured JSON output, optionally writing fields to the triggering Dataverse record via UpdateRecord-style field mappings.

**When to use**: Displaying results in UI, writing analysis output back to the source record.

### DeliverToIndex (ActionType 41)

Indexes upstream node results into Azure AI Search for semantic retrieval. Enqueues a `RagIndexing` background job via Service Bus — processing is asynchronous.

**When to use**: Making playbook output searchable via Semantic Search. Common pattern: Document Profile playbooks that index document metadata for later retrieval.

**Configuration**:

| Property | Default | Description |
|----------|---------|-------------|
| `indexName` | `"knowledge"` | Target Azure AI Search index |
| `indexSource` | `"document"` | Source type: `"document"` (full doc) or `"field"` (specific field) |

**Design tip**: Use DeliverToIndex alongside DeliverOutput when you want both UI display AND search indexing. They can run in parallel as separate output branches.

For full technical details, see [playbook-architecture.md](../architecture/playbook-architecture.md).

---

## Related Resources

- [JPS Authoring Guide](JPS-AUTHORING-GUIDE.md) — Creating JPS definitions for actions
- [Playbook JPS Prompt Schema Guide](PLAYBOOK-JPS-PROMPT-SCHEMA-GUIDE.md) — Full JPS schema reference
- [AI Architecture](../architecture/AI-ARCHITECTURE.md) — Platform architecture overview
- [Playbook Architecture](../architecture/playbook-architecture.md) — Playbook internals, node executors, execution engine
- [AI Implementation Reference](../architecture/ai-implementation-reference.md) — Working code examples
- **Scope Catalog**: `docs/ai-knowledge/catalogs/scope-model-index.json`
- **Provisioning Script**: `scripts/Deploy-Playbook.ps1`
- **Claude Code Skill**: `/jps-playbook-design`
