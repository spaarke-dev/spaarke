# Scope Configuration Guide

> **Version**: 1.1 (verified)
> **Date**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified (Production)
> **Audience**: Dataverse Administrators, Power Users, Engineers, End Users
> **Supersedes**: PLAYBOOK-SCOPE-CONFIGURATION-GUIDE.md, PLAYBOOK-BUILDER-GUIDE.md, PLAYBOOK-PRE-FILL-INTEGRATION-GUIDE.md
> **Prerequisites**: Access to Dataverse environment with Spaarke AI solution installed
> **Related**: [JPS-AUTHORING-GUIDE.md](JPS-AUTHORING-GUIDE.md) (JPS schema and prompt authoring), [playbook-architecture.md](../architecture/playbook-architecture.md) (technical internals)

---

## Table of Contents

1. [Overview](#overview)
2. [Understanding Scopes](#understanding-scopes)
3. [Creating Tools](#creating-tools)
4. [Creating Skills](#creating-skills)
5. [Creating Knowledge Sources](#creating-knowledge-sources)
6. [Creating Actions](#creating-actions)
7. [Building Playbooks in the Visual Canvas (Playbook Builder)](#building-playbooks-in-the-visual-canvas-playbook-builder)
8. [Multi-Node Playbook Design](#multi-node-playbook-design)
9. [Configuring Deliver Output Nodes](#configuring-deliver-output-nodes)
10. [Configuring Update Record Nodes](#configuring-update-record-nodes)
11. [Performance Optimization](#performance-optimization)
12. [Pre-Fill Integration](#pre-fill-integration)
13. [Troubleshooting](#troubleshooting)
14. [Additional Resources](#additional-resources)

---

## Overview

**Playbook Scopes** are reusable building blocks for AI analysis workflows. This guide covers:

- How to create configuration-driven tools, skills, knowledge sources, and actions in Dataverse
- How to use the Playbook Builder visual canvas to compose playbooks
- How pre-fill integration uses playbooks to populate wizard forms

**Key Benefit — Configuration-Driven**: Once you create a scope in Dataverse, it works immediately in playbooks — no code deployment required.

---

## Understanding Scopes

### Scope Types

| Scope | Purpose | Example |
|-------|---------|---------|
| **Tools** | Execute AI analysis and process results | "Entity Extractor", "Document Summarizer" |
| **Skills** | Add specialized instructions to prompts | "Contract Analysis", "Risk Assessment" |
| **Knowledge** | Provide domain context via RAG or inline text | "Standard Contract Clauses", "Company Policies" |
| **Actions** | Define LLM behavior with system prompts | "Extract Entities", "Summarize Content" |

### How Scopes Are Assigned to Nodes

Each AI Analysis node in a playbook has its own scope assignments:

| Scope | Cardinality | Relationship | Purpose |
|-------|-------------|-------------|---------|
| **Action** | **1 per node** | Lookup (`sprk_actionid`) | Primary AI instruction — "what to do" |
| **Tool** | **1 per node** | N:N (`sprk_playbooknode_tool`) | Execution handler — "how to do it" |
| **Skills** | **Many per node** | N:N (`sprk_playbooknode_skill`) | Prompt modifiers — "pay attention to…" |
| **Knowledge** | **Many per node** | N:N (`sprk_playbooknode_knowledge`) | Reference context — "here's background…" |

> **Important**: Each AI node executes exactly **one tool**. If your playbook needs to classify, summarize, and extract entities, create **three separate AI Analysis nodes** — each with its own Action + Tool pair.

### How Scopes Combine Into a Prompt

When a node executes, the server assembles all scopes into a single prompt sent to Azure OpenAI. Exact assembly sequence:

```
┌─────────────────────────────────────────────────────────────┐
│                    FINAL PROMPT TO AI MODEL                 │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─ 1. PRIMARY INSTRUCTION (first non-empty wins) ──────┐  │
│  │   Priority A: Action.SystemPrompt                     │  │
│  │   Priority B: Tool.Configuration.PromptTemplate       │  │
│  │   Priority C: Tool operation default (built-in)       │  │
│  └───────────────────────────────────────────────────────┘  │
│                          ↓                                  │
│  ┌─ 2. PLACEHOLDER SUBSTITUTION ────────────────────────┐  │
│  │   {document}    → full extracted document text         │  │
│  │   {parameters}  → Tool Configuration.Parameters JSON   │  │
│  │   {tool_name}   → Tool record name                     │  │
│  │   {tool_description} → Tool record description          │  │
│  └───────────────────────────────────────────────────────┘  │
│                          ↓                                  │
│  ┌─ 3. SKILLS (appended to prompt) ─────────────────────┐  │
│  │   ## Additional Analysis Instructions                 │  │
│  │   [Contract Analysis]                                 │  │
│  │   Focus on liability clauses, indemnification...      │  │
│  └───────────────────────────────────────────────────────┘  │
│                          ↓                                  │
│  ┌─ 4. KNOWLEDGE (appended to prompt) ──────────────────┐  │
│  │   ## Reference Knowledge                              │  │
│  │   [Standard Contract Clauses]                         │  │
│  │   Force Majeure: Unforeseeable circumstances...       │  │
│  └───────────────────────────────────────────────────────┘  │
│                          ↓                                  │
│  ┌─ 5. DOCUMENT (auto-appended if needed) ──────────────┐  │
│  │   ## Document                                         │  │
│  │   [full extracted document text]                       │  │
│  └───────────────────────────────────────────────────────┘  │
│                          ↓                                  │
│  ┌─ 6. OUTPUT SCHEMA (from Tool Configuration) ─────────┐  │
│  │   Return your response as valid JSON matching:        │  │
│  │   { "type": "object", "properties": { ... } }        │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

### Scope Roles Explained

| Scope | Role | Analogy | Example |
|-------|------|---------|---------|
| **Action** | "What to do" — the primary instruction | The job description | "Summarize this document with key points and takeaways" |
| **Tool** | "How to do it" — handler mechanism + output format | The toolbox and blueprint | GenericAnalysisHandler with `output_schema` |
| **Skills** | "Pay attention to…" — modifiers that focus the analysis | Specialized lenses | "Focus on liability clauses and indemnification" |
| **Knowledge** | "Here's context…" — reference material injected into prompt | Reference library | "Standard contract clause definitions" |

### Key Principle: Action = What, Tool = How

The **Action** record's SystemPrompt is the primary AI instruction. When an Action is assigned to a node, its prompt takes precedence over the Tool's built-in prompt template. The Tool provides the execution mechanism (handler class) and output format (JSON Schema).

**Without Action**: Tool's prompt template or built-in default runs → output matches tool's default behavior.

**With Action**: Action's SystemPrompt runs → Tool provides handler and output format → output matches Action's instructions.

---

## Creating Tools

Tools are the executable components that call Azure OpenAI and process responses.

### Method 1: Configuration-Driven Tool (Recommended)

**Use when:** You want to create a custom analysis tool without writing code.

#### Step 1: Open Analysis Tool Entity

1. Navigate to **Advanced Find** in Dataverse
2. Look for: **Analysis Tools** (table: `sprk_analysistool`)
3. Click **New**

#### Step 2: Fill Basic Information

| Field | What to Enter | Example |
|-------|---------------|---------|
| **Name** | Descriptive tool name | "Technical Requirements Extractor" |
| **Description** | What the tool does | "Extracts technical requirements from documents with priority levels" |
| **Tool Type** | Select category | "01 - Entity Extraction" |
| **Handler Class** | **Leave empty** | (blank) |

**Important**: Leaving **Handler Class** empty tells the system to use the **GenericAnalysisHandler**, which works entirely from configuration.

#### Step 3: Configure the Tool (JSON Configuration)

Click into the **Configuration** field and enter JSON:

```json
{
  "operation": "extract",
  "prompt_template": "You are a technical requirements analyst. Extract all technical requirements from the following document.\n\nFor each requirement, provide:\n- Description: Clear statement of the requirement\n- Priority: Must/Should/Could/Won't\n\nDocument:\n{document}\n\nReturn your analysis as structured JSON.",
  "output_schema": {
    "type": "object",
    "properties": {
      "requirements": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "description": { "type": "string" },
            "priority": { "type": "string", "enum": ["Must", "Should", "Could", "Won't"] }
          },
          "required": ["description", "priority"]
        }
      }
    },
    "required": ["requirements"]
  },
  "temperature": 0.2,
  "maxTokens": 2000
}
```

**Configuration Fields**:

| Field | Description | Valid Values |
|-------|-------------|--------------|
| `operation` | Type of operation to perform | `extract`, `classify`, `validate`, `generate`, `transform`, `analyze` |
| `prompt_template` | Custom prompt with placeholders | Use `{document}` for document text, `{parameters}` for runtime params |
| `output_schema` | JSON Schema for expected output | Valid JSON Schema (Draft 07) |
| `temperature` | AI creativity (0=deterministic, 1=creative) | 0.0 - 1.0 (recommended: 0.2-0.3 for extraction) |
| `maxTokens` | Maximum response length | 100 - 8000 (recommended: 2000) |

#### Step 4: Save and Test

1. Click **Save**
2. Add the tool to a playbook
3. Execute the playbook
4. Review results in Analysis Output records

---

### Method 2: Custom Handler Tool (For Complex Scenarios)

**Use when:** You need specialized processing logic that can't be achieved with configuration alone.

Fill **Handler Class** with an exact handler name:

| Handler Class | Purpose | Configuration Example |
|---------------|---------|----------------------|
| `EntityExtractorHandler` | Extract entities (Person, Organization, Date, etc.) | `{"entityTypes": ["Person", "Organization"], "confidenceThreshold": 0.7}` |
| `SummaryHandler` | Generate document summaries | `{"maxWords": 500, "format": "structured"}` |
| `DocumentClassifierHandler` | Classify document types | `{"categories": ["Contract", "Invoice", "Report"]}` |
| `ClauseAnalyzerHandler` | Analyze contract clauses | `{"clauseTypes": ["Termination", "Liability"]}` |
| `RiskDetectorHandler` | Detect and categorize risks | `{"severityLevels": ["High", "Medium", "Low"]}` |
| `DateExtractorHandler` | Extract and normalize dates | `{"includeRelativeDates": true}` |
| `FinancialCalculatorHandler` | Extract financial data | `{"currencies": ["USD", "EUR", "GBP"]}` |
| `GenericAnalysisHandler` | Configuration-driven (default) | See Method 1 |

**How to Find Available Handlers**: Call API: `GET /api/ai/handlers` (requires authentication).

> **Note**: Invalid handler names fall back to GenericAnalysisHandler with a warning in logs.

---

### Tool Configuration JSON Reference

#### GenericAnalysisHandler Configuration (Most Common)

```json
{
  "operation": "extract",
  "prompt_template": "Extract {parameters} from the document:\n{document}",
  "output_schema": {
    "type": "object",
    "properties": {
      "requirements": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "description": { "type": "string" },
            "priority": { "type": "string" }
          }
        }
      },
      "confidence": { "type": "number" }
    }
  },
  "parameters": {
    "types": ["Person", "Organization", "Date"]
  },
  "temperature": 0.2,
  "max_tokens": 2000
}
```

| Field | Required | Description |
|-------|----------|-------------|
| `operation` | **Yes** | Operation type. Determines the built-in default prompt if no Action or prompt_template is set. |
| `prompt_template` | No | Custom prompt with `{document}` and `{parameters}` placeholders. Only used when no Action SystemPrompt is assigned. |
| `output_schema` | No | JSON Schema defining the expected output structure. If omitted, defaults to `{ "result": ..., "confidence": 0.0-1.0 }`. |
| `parameters` | No | Arbitrary JSON injected into `{parameters}` placeholder. |
| `temperature` | No | AI creativity (0.0=deterministic, 1.0=creative). Default: 0.3. |
| `max_tokens` | No | Maximum response tokens. Default: 2000. Range: 100-8000. |

#### Understanding output_schema

The `output_schema` is a standard JSON Schema that controls what the LLM returns:

- **Add any fields** you need (strings, numbers, booleans, nested objects, arrays)
- **Use enums** to constrain values: `"enum": ["High", "Medium", "Low"]`
- **Require fields**: `"required": ["summary", "confidence"]`

Common schemas:

**Classification:**
```json
{
  "output_schema": {
    "type": "object",
    "properties": {
      "classification": { "type": "string" },
      "subtype": { "type": "string" },
      "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
    },
    "required": ["classification", "confidence"]
  }
}
```

**Multi-entity extraction:**
```json
{
  "output_schema": {
    "type": "object",
    "properties": {
      "entities": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "name": { "type": "string" },
            "type": { "type": "string", "enum": ["Person", "Organization", "Date", "Amount"] },
            "context": { "type": "string" }
          }
        }
      },
      "confidence": { "type": "number" }
    }
  }
}
```

#### Understanding Prompt Priority

When a node executes, the prompt is selected using this priority:

1. **Action SystemPrompt** (if an Action is assigned to the node) — **always wins**
2. **Tool prompt_template** (from Configuration JSON) — used only if no Action is assigned
3. **Operation default** (built-in to GenericAnalysisHandler) — used if neither exists

---

## Creating Skills

Skills are prompt fragments that add specialized instructions to analysis workflows.

### When to Use Skills

- Add domain expertise (legal, financial, technical)
- Refine behavior for specific document types
- Provide formatting instructions
- Add quality checks or validation rules

### Step-by-Step: Create a Skill

1. Navigate to **Advanced Find** in Dataverse → **Analysis Skills** (`sprk_analysisskill`) → **New**
2. Fill basic information:

| Field | What to Enter | Example |
|-------|---------------|---------|
| **Name** | Descriptive skill name | "Contract Analysis" |
| **Description** | What the skill adds | "Adds specialized instructions for analyzing legal contracts" |
| **Skill Type** | Select category | "01 - Document Analysis" |

3. Write the **Prompt Fragment**:

```markdown
## Contract Analysis Instructions

When analyzing contracts, focus on:

1. **Parties Involved** — Identify all parties to the agreement; note their roles and obligations
2. **Key Terms** — Effective date and termination conditions; payment terms; renewal and cancellation clauses
3. **Obligations and Responsibilities** — List obligations for each party; identify deliverables and deadlines
4. **Risk Factors** — Highlight liability limitations; note indemnification clauses; flag unusual or high-risk terms
5. **Compliance Requirements** — Identify regulatory or legal compliance obligations; note jurisdictional considerations

Format your analysis with clear headings and bullet points for easy scanning.
```

### Best Practices for Skills

**DO**:
- Use clear, structured formatting (headings, numbered lists)
- Be specific about what to look for
- Keep focused on one domain or aspect

**DON'T**:
- Make them too generic ("analyze the document carefully")
- Duplicate instructions already in the Action
- Make them too long (>2000 words)

### Examples by Domain

**Financial Analysis Skill**:
```markdown
## Financial Analysis Focus

Analyze documents for:
- Revenue and expense figures
- Budget allocations and variances
- Financial projections and forecasts
- Key financial ratios (if calculable)

Present findings in a structured format with:
1. Executive summary of financial health
2. Key metrics table
3. Notable trends or concerns
```

**Risk Assessment Skill**:
```markdown
## Risk Assessment Framework

Evaluate each identified risk using:
- **Severity**: Low / Medium / High / Critical
- **Likelihood**: Unlikely / Possible / Likely / Almost Certain
- **Impact**: Minimal / Moderate / Major / Severe

For high-severity risks: describe the risk clearly, explain potential consequences, suggest mitigation strategies.
```

---

## Creating Knowledge Sources

Knowledge sources provide domain-specific context via Retrieval-Augmented Generation (RAG) or inline text.

### Types of Knowledge

| Type | Use Case | Storage |
|------|----------|---------|
| **Inline** | Short reference text (policies, definitions) | Stored directly in `sprk_content` field |
| **RAG Index** | Large document collections (contracts, regulations) | Referenced via `sprk_deploymentid` |

### Method 1: Inline Knowledge (For Short References)

1. Navigate to **Advanced Find** → **Analysis Knowledge** (`sprk_analysisknowledge`) → **New**
2. Fill basic information:

| Field | What to Enter | Example |
|-------|---------------|---------|
| **Name** | Descriptive name | "Standard Contract Clauses Reference" |
| **Description** | What it contains | "Common boilerplate clauses and their standard meanings" |
| **Knowledge Type** | Select "Inline" or equivalent | "01 - Standards" |

3. Add **Content** (Markdown or plain text):

```markdown
# Standard Contract Clauses

## Force Majeure
Definition: Unforeseeable circumstances that prevent fulfillment of contract
Standard language: "Neither party shall be liable for failure to perform due to causes beyond reasonable control..."

## Indemnification
Definition: Agreement to compensate for loss or damage
Standard language: "Each party agrees to indemnify and hold harmless the other from claims arising from their negligence..."

## Termination for Convenience
Standard language: "Either party may terminate this agreement with 30 days written notice."
```

**Formatting Tips**: Use Markdown formatting; keep organized and scannable; limit to 10,000 words for inline content.

### Method 2: RAG Knowledge (For Large Collections)

**Note**: RAG deployments are typically configured by admins/engineers.

| Field | What to Enter | Example |
|-------|---------------|---------|
| **Name** | Descriptive name | "Company Knowledge Base" |
| **Knowledge Type** | Select "RAG" or equivalent | "02 - Regulations" |
| **Deployment ID** | Select deployment | "Company-Policies-RAG-2026" |
| **Content** | Optional JSON config | `{"topK": 5, "similarityThreshold": 0.7}` |

**RAG Configuration Options**:
```json
{
  "topK": 5,                    // Number of most relevant chunks to retrieve
  "similarityThreshold": 0.7,   // Minimum similarity score (0.0-1.0)
  "includeMetadata": true       // Include source document metadata
}
```

---

## Creating Actions

Actions are system prompt templates that define how the LLM behaves.

### When to Create Actions

- Define a new analysis type (e.g., "Compliance Review")
- Specify response format and structure
- Set tone and expertise level

### Step-by-Step: Create an Action

1. Navigate to **Advanced Find** → **Analysis Actions** (`sprk_analysisaction`) → **New**
2. Fill basic information:

| Field | What to Enter | Example |
|-------|---------------|---------|
| **Name** | Action name (verb-based) | "Summarize Content" |
| **Description** | What behavior it defines | "Generate concise summaries with key points and takeaways" |
| **Action Type** | Select category | "03 - Summarization" |

3. Write the **System Prompt** (see Action Template Structure below).

### Action Template Structure

**Recommended sections**:
1. **# Role** — Who is the LLM in this scenario? (domain expert, not generic assistant)
2. **# Task** — What should it do? (actionable and unambiguous)
3. **# Guidelines** — Structure requirements, writing style, quality standards
4. **# Output Format** — Exact JSON or text format
5. **# Document** — Placeholder: `{document}`

### Best Practices for Actions

**DO**:
- Define the LLM's role and expertise clearly
- Specify exact output format (JSON schema)
- Include quality standards and examples
- Use placeholders like `{document}` for dynamic content

**DON'T**:
- Make assumptions about document type (that's what Skills do)
- Write generic prompts ("analyze the document")
- Forget to specify output format

> **Note**: For JPS-format actions (structured JSON output, `$choices`, scope `$ref`), see [JPS-AUTHORING-GUIDE.md](JPS-AUTHORING-GUIDE.md).

---

## Building Playbooks in the Visual Canvas (Playbook Builder)

The **Playbook Builder** is a visual node-based editor for creating AI analysis workflows accessible via AI chat assistant (`Cmd/Ctrl+K`) or directly on the playbook canvas.

### Getting Started

1. Navigate to a playbook record in Dataverse
2. Click the **AI Assistant** button in the toolbar, or press **Cmd/Ctrl+K**
3. The AI chat panel will open on the side

**Your First Playbook** — try saying:

> "Create a lease analysis playbook that extracts key dates, financial terms, and identifies risks"

The AI will understand your intent, create appropriate analysis nodes, connect them in a logical flow, and add relevant scopes.

### AI Assistant Commands

| What to Say | What Happens |
|-------------|--------------|
| "Create a contract review playbook" | Creates a new playbook with contract analysis nodes |
| "Add a clause extraction step" | Adds a clause extraction node |
| "Add risk detection" | Adds a risk analysis node |
| "Connect extraction to analysis" | Creates an edge between nodes |
| "Remove the risk node" | Deletes the specified node |
| "Run a mock test" | Simulated execution with sample data |
| "Quick test with a document" | Real AI processing with uploaded document |

### Node Types (Palette)

Drag node types from the left sidebar palette onto the canvas:

| Node Type | Category | Purpose | Requires Scopes? |
|-----------|----------|---------|-------------------|
| **AI Analysis** | AI | LLM-powered analysis using action + skills + knowledge + tool | Yes (full scopes) |
| **AI Completion** | AI | Raw LLM completion with system prompt and user template | Yes (full scopes) |
| **Condition** | Logic | Branch execution based on expression (true/false paths) | No |
| **Wait** | Logic | Pause for duration, until datetime, or condition met | No |
| **Deliver Output** | Output | Format and assemble results from upstream nodes | No |
| **Deliver To Index** | Output | Index results into Azure AI Search for semantic retrieval | No |
| **Create Task** | Action | Create a Dataverse task record | No |
| **Send Email** | Action | Send email via Microsoft Graph | No |

**Deliver To Index** is useful when playbook output should be searchable. Configure `indexName` (default: `"knowledge"`) and `indexSource` (`"document"` or `"field"`) in the node properties panel.

### Configuring a Node

Click a node on the canvas to open the Properties Panel (right sidebar).

**For AI Analysis / AI Completion nodes:**

| Section | Control | Purpose |
|---------|---------|---------|
| **Basic** | Name, Output Variable | Display label and variable name for downstream references |
| **Action** | Dropdown (single-select) | Select the analysis action (defines primary AI instruction) |
| **AI Model** | Dropdown | Select Azure OpenAI model deployment |
| **Skills** | Checkboxes (multi-select) | Add prompt fragment modifiers for domain expertise |
| **Knowledge** | Checkboxes (multi-select) | Add reference context (inline text, RAG) |
| **Tool** | Radio buttons (single-select) | Select one execution handler |
| **Runtime** | Timeout, Retry Count | Execution constraints |

### Connecting Nodes (Edges)

- **Drag** from a node's output handle to another node's input handle to create an edge
- Edges define **execution dependencies**: target node waits for source to complete
- **Condition nodes** have two output handles: `true` branch and `false` branch
- Nodes without incoming edges run first (batch 1)

### Saving

- **Ctrl+S** or click Save button — saves canvas JSON + syncs nodes to Dataverse
- **Auto-save**: 30-second debounce after changes
- On save, the builder:
  1. Stores canvas layout in `sprk_canvaslayoutjson`
  2. Creates/updates/deletes `sprk_playbooknode` records in Dataverse
  3. Computes execution order and writes `sprk_dependsonjson`
  4. Writes `sprk_nodetype` and `__actionType` for executor dispatch
  5. Syncs N:N relationships (skills, knowledge, tools)

### Referencing Upstream Outputs

Nodes reference outputs from previously completed nodes using **Handlebars template variables**. The variable name is the **Output Variable** set on the upstream node.

**Available template paths per node output:**

| Path | Type | Description |
|------|------|-------------|
| `{{varName.text}}` | string | The raw text output from the node |
| `{{varName.output.fieldName}}` | any | A specific field from the structured JSON output |
| `{{varName.success}}` | boolean | Whether the node executed successfully |

**Built-in context variables** (available in all nodes):

| Path | Type | Description |
|------|------|-------------|
| `{{document.id}}` | GUID string | The Dataverse document ID being analyzed |
| `{{document.name}}` | string | Document display name |
| `{{document.fileName}}` | string | Original file name with extension |
| `{{run.id}}` | GUID string | The current playbook run ID |
| `{{run.playbookId}}` | GUID string | The playbook being executed |
| `{{run.tenantId}}` | string | Tenant ID for multi-tenant isolation |

**Examples:**
```handlebars
{{summary.text}}                        — full text from the "summary" node
{{extract_entities.output.parties}}     — "parties" field from structured JSON
{{classify.output.documentType}}        — "documentType" from classifier output
{{document.id}}                         — Dataverse document GUID
```

### Scope Management in the Builder

**What are Scopes?** Scopes define what capabilities your playbook has: Actions, Skills, Tools, and Knowledge.

| Type | Prefix | Can Edit | Can Delete |
|------|--------|----------|------------|
| System | SYS- | No | No |
| Custom | CUST- | Yes | Yes |

**Creating Custom Scopes** via Scope Browser (View > Scopes): Click **Create New**, choose type, fill in details.

**Save As (Copying System Scopes)**: Select the system scope → **Save As** → enter a new name (will have CUST- prefix).

**Extending Scopes (Inheritance)**: Select parent scope → **Extend** → enter child name → override specific fields.

### Test Execution

| Mode | Description | Best For |
|------|-------------|----------|
| **Mock Test** | Simulated execution with sample data | Quick validation of playbook structure |
| **Quick Test** | Real AI processing with uploaded document (24-hour storage) | Testing analysis quality |
| **Production Test** | Full production workflow with real storage | Final validation before deployment |

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| **Cmd/Ctrl+K** | Toggle AI Assistant |
| **Escape** | Close modal/dialog |
| **Enter** | Send message |
| **Shift+Enter** | New line in message |
| **Ctrl+S** | Save playbook |

---

## Multi-Node Playbook Design

### One Action Per Node

Each node performs exactly one task. A playbook that needs to summarize, classify, extract entities, and match to a matter requires **4 separate AI Analysis nodes** — not one node doing everything.

### Example: Full Contract Review (4 AI nodes + 1 output)

```
┌─────────────┐   ┌─────────────┐
│  Summarize  │   │  Classify   │
│  (AI Node)  │   │  (AI Node)  │
│  var: summ  │   │  var: class │
└──────┬──────┘   └──────┬──────┘
       │                 │
       │   ┌─────────────┐   ┌─────────────┐
       │   │   Extract   │   │ Match Matter│
       │   │  (AI Node)  │   │  (AI Node)  │
       │   │  var: ents  │   │  var: match │
       │   └──────┬──────┘   └──────┬──────┘
       │          │                 │
       └──────────┼─────────────────┘
                  │
           ┌──────▼──────┐
           │   Deliver   │
           │   Output    │
           │ (assembles  │
           │  all results)│
           └─────────────┘
```

### Configuration for Each Node

| Node | Action | Skills | Knowledge | Tool | Output Variable |
|------|--------|--------|-----------|------|-----------------|
| Summarize | ACT-004 Summarize Content | SKL-008 Executive Summary | KNW-003 Best Practices | TL-004 SummaryHandler | `summary` |
| Classify | ACT-003 Classify Document | SKL-001 Contract Analysis | KNW-005 Document Types | TL-003 DocumentClassifier | `classification` |
| Extract | ACT-001 Extract Entities | SKL-001 Contract Analysis | KNW-001 Standard Terms | TL-001 EntityExtractor | `entities` |
| Match | Custom action | SKL-009 Risk Assessment | KNW-004 Risk Categories | GenericAnalysisHandler | `match_result` |
| Deliver | (no action) | (no scopes) | (no scopes) | (no tool) | `final_output` |

---

## Configuring Deliver Output Nodes

The **Deliver Output** node is typically the final node in a playbook. It assembles results from all upstream AI nodes into a single formatted deliverable.

### Two Modes of Operation

| Mode | When Used | Behavior |
|------|-----------|----------|
| **Auto-Assembly** | Template field is **empty** | Concatenates all upstream node outputs as-is. Fast but unformatted. |
| **Template** | Template field has content | Renders a Handlebars template with variable references. Produces clean output. |

**Recommendation**: Always use a template for production playbooks.

### Delivery Format

| Format | Use Case | Rendering |
|--------|----------|-----------|
| **Markdown** (default) | Human-readable reports, summaries | Rendered as styled Markdown in the Analysis Workspace |
| **HTML** | Rich formatting, tables, embedded links | Rendered as HTML |
| **Plain Text** | Simple text output, logs | Rendered as monospace text |
| **JSON** | Machine-readable structured data | Renders all node outputs as structured JSON |

### Template Example: Contract Review Playbook

```handlebars
# Contract Review Report

## Executive Summary
{{summary.text}}

## Document Classification
{{classify.text}}

## Parties & Key Entities
{{entities.text}}

## Risk Assessment
{{risk.text}}

---
*Generated by Spaarke AI Playbook*
```

### Accessing Structured Output Fields

When an upstream tool uses an Output Schema, the AI returns structured JSON. Access individual fields:

```handlebars
## Document Profile

| Field | Value |
|-------|-------|
| **Type** | {{profile.output.documentType}} |
| **Date** | {{profile.output.documentDate}} |
| **Parties** | {{profile.output.parties}} |
```

### Output Format Options

| Option | Default | Purpose |
|--------|---------|---------|
| **Include Metadata** | Off | Appends execution metadata (run ID, timestamps, node count, confidence scores) |
| **Include Source Citations** | Off | Appends source citation references |
| **Max Output Length** | 0 (unlimited) | Maximum characters — content beyond limit is truncated |

### Common Mistakes

| Mistake | Symptom | Fix |
|---------|---------|-----|
| Empty template | Output shows raw JSON dump of all node data | Write a template with `{{varName.text}}` references |
| Wrong variable name | `{{summary.text}}` shows blank | Check the upstream node's **Output Variable** field matches exactly |
| Using `.TextContent` instead of `.text` | Template renders literally | Use `.text` (lowercase) |
| Using `.StructuredData` instead of `.output` | Template renders literally | Use `.output.fieldName` |
| No edges to Deliver Output | Output is empty | Connect upstream nodes to the Deliver Output node |

---

## Configuring Update Record Nodes

The **Update Record** node writes AI analysis results back to Dataverse entity records — fully configuration-driven.

### Typed Field Mappings (Recommended)

```json
{
  "entityLogicalName": "sprk_document",
  "recordId": "{{document.id}}",
  "fieldMappings": [
    {
      "field": "sprk_filesummary",
      "type": "string",
      "value": "{{aiAnalysis.text}}"
    },
    {
      "field": "sprk_documenttype",
      "type": "choice",
      "value": "{{aiAnalysis.output.documentType}}",
      "options": {
        "contract": 100000000,
        "invoice": 100000001,
        "proposal": 100000002,
        "report": 100000003,
        "other": 100000012
      }
    },
    {
      "field": "sprk_isconfidential",
      "type": "boolean",
      "value": "{{aiAnalysis.output.isConfidential}}"
    },
    {
      "field": "sprk_pagecount",
      "type": "number",
      "value": "{{aiAnalysis.output.pageCount}}"
    }
  ]
}
```

### Field Mapping Types

| Type | Dataverse Column Type | Coercion Behavior |
|------|----------------------|-------------------|
| **string** | Single/Multi-line Text | Pass through as-is |
| **choice** | Choice (OptionSet) | Case-insensitive label lookup in `options` map → integer value |
| **boolean** | Yes/No (Two Option) | Parses common truthy/falsy strings (`"yes"`, `"true"`, `"1"` → `true`) |
| **number** | Whole Number / Decimal | Parses as integer, then decimal |

### Lookup Fields

```json
{
  "entityLogicalName": "sprk_document",
  "recordId": "{{document.id}}",
  "fieldMappings": [ ... ],
  "lookups": {
    "sprk_relatedmatter": {
      "targetEntity": "sprk_matter",
      "targetId": "{{match.output.matterId}}"
    }
  }
}
```

### Legacy Format (Backward Compatible)

The original `fields` dictionary format still works for existing playbooks:

```json
{
  "entityLogicalName": "sprk_document",
  "recordId": "{{document.id}}",
  "fields": {
    "sprk_filesummary": "{{summary.text}}",
    "sprk_filekeywords": "{{entities.output.keywords}}"
  }
}
```

> **Recommendation**: Migrate to `fieldMappings` for explicit type control. The Playbook Builder UI automatically migrates legacy `fields` to `fieldMappings` when you open and save a node.

---

## Performance Optimization

### How Parallel Execution Works

The execution engine groups nodes into **batches** based on dependencies:
- Nodes in the **same batch** have no dependencies on each other → run **in parallel**
- Batches run **sequentially** (batch 2 waits for batch 1 to finish)
- Total execution time = sum of the slowest node in each batch

### Three Structure Patterns

**Pattern A: Fully Sequential** (slowest)
```
Summarize → Classify → Extract → Match → Output
  Batch 1    Batch 2   Batch 3   Batch 4  Batch 5
Total time = sum of all node durations
```

**Pattern B: Fully Parallel** (fastest)
```
Summarize ─┐
Classify  ─┤
Extract   ─┼─→ Output
Match     ─┘
  Batch 1      Batch 2
Total time = max(Summarize, Classify, Extract, Match) + Output
```

**Pattern C: Partial Dependencies** (when some nodes need prior output)
```
Summarize ──→ Match ─┐
Classify  ───────────┤
Extract   ───────────┼─→ Output
  Batch 1    Batch 2     Batch 3
```

### The Optimization Rule

> **Only add edges where data actually flows.** If a node does not reference `{{upstream.output}}` in its prompt, it should NOT have a dependency edge.

### Estimating Execution Time

- Network overhead: ~200-500ms per node
- Token processing: ~50-80 tokens/sec output for GPT-4o class models
- A node producing 500 output tokens ≈ 7-10 seconds
- Four AI nodes in parallel ≈ 7-10 seconds total. Four sequential ≈ 28-40 seconds.

---

## Pre-Fill Integration

The pre-fill system uses playbooks to automatically populate wizard form fields when users upload documents.

### How It Works

When a user uploads files in the Create New Matter (or Create New Project) wizard:

1. Frontend POSTs files to BFF API (`POST /api/workspace/matters/pre-fill`)
2. BFF extracts text from files
3. BFF invokes a **playbook** (configured by GUID) via Azure OpenAI
4. **$choices resolution**: `LookupChoicesResolver` pre-resolves Dataverse lookup/optionset values and injects them as `enum` constraints in the JSON Schema — the AI is forced to return exact Dataverse values
5. AI returns flat JSON with display names (constrained to exact Dataverse values for `$choices` fields)
6. BFF passes the JSON back to the frontend
7. Frontend resolves lookup GUIDs — exact match for `$choices`-constrained fields, fuzzy match for free-text fields
8. Form fields are pre-filled with resolved values + "AI Pre-filled" badges

### AI Playbook Output Contract

The playbook returns a **flat JSON object with display names**. For fields constrained by `$choices`, the AI returns exact Dataverse values. For free-text fields, fuzzy matching resolves on the frontend.

#### Create New Matter Pre-Fill

```json
{
  "matterTypeName": "Patent",
  "practiceAreaName": "Intellectual Property",
  "matterName": "Smith v. Acme Corp - Wrongful Termination",
  "summary": "Employment dispute involving wrongful termination claim...",
  "assignedAttorneyName": "Jane Smith",
  "assignedParalegalName": "Bob Jones",
  "assignedOutsideCounselName": "Wilson & Partners LLP",
  "confidence": 0.85
}
```

#### Create New Project Pre-Fill

```json
{
  "projectTypeName": "Contract Review",
  "practiceAreaName": "Corporate Law",
  "projectName": "Acme Corp - Vendor Agreement Review",
  "description": "Review and analysis of vendor service agreements...",
  "assignedAttorneyName": "Jane Smith",
  "assignedParalegalName": "Bob Jones",
  "confidence": 0.82
}
```

### Field Rules for Playbook Prompts

| Field | Type | $choices | Resolution | Notes |
|-------|------|----------|------------|-------|
| `matterTypeName` / `projectTypeName` | string | `lookup:sprk_mattertype_ref.sprk_mattertypename` / `lookup:sprk_projecttype_ref.sprk_projecttypename` | Exact match (constrained) | AI forced to pick from Dataverse values |
| `practiceAreaName` | string | `lookup:sprk_practicearea_ref.sprk_practiceareaname` | Exact match (constrained) | Same lookup table for both matters and projects |
| `matterName` / `projectName` | string | — | Direct text (no lookup) | Max 10 words. Format: "Party v. Party - Topic" |
| `summary` / `description` | string | — | Direct text (no lookup) | Max 500 words |
| `assignedAttorneyName` | string | — | Fuzzy match to `contact.fullname` | "Jane Smith", not "J. Smith" |
| `assignedParalegalName` | string | — | Fuzzy match to `contact.fullname` | Same rule |
| `assignedOutsideCounselName` | string | — | Fuzzy match to `sprk_organization.sprk_organizationname` | Matters only |
| `confidence` | number | — | Direct value | 0.0 to 1.0 |

### Fuzzy Match Thresholds (for non-$choices fields)

| Score | Condition |
|-------|-----------|
| `1.0` | Exact match (case-insensitive) |
| `0.8` | One string starts with the other ("Corporate" ↔ "Corporate Law") |
| `0.7` | One contains the other ("Trans" in "Transactional") |
| `0.5` | Single result from Dataverse search (implicit relevance) |
| `0.4` | Minimum acceptance threshold (below this = no match) |

### JPS Output Fields Configuration for Pre-Fill

```json
{
  "output": {
    "structuredOutput": true,
    "fields": [
      {
        "name": "matterTypeName",
        "type": "string",
        "description": "The matter type that best matches this document",
        "$choices": "lookup:sprk_mattertype_ref.sprk_mattertypename"
      },
      {
        "name": "practiceAreaName",
        "type": "string",
        "description": "The practice area for this matter",
        "$choices": "lookup:sprk_practicearea_ref.sprk_practiceareaname"
      },
      {
        "name": "matterName",
        "type": "string",
        "description": "A concise matter name (max 10 words)",
        "maxLength": 100
      },
      {
        "name": "confidence",
        "type": "number",
        "description": "Overall extraction confidence (0.0 to 1.0)"
      }
    ]
  }
}
```

For full `$choices` documentation and supported prefixes, see [JPS-AUTHORING-GUIDE.md](JPS-AUTHORING-GUIDE.md#choices-resolution).

### Dataverse Lookup Tables

| AI Field | Dataverse Table | Search Field | ID Field | $choices | Used By |
|----------|----------------|--------------|----------|----------|---------|
| `matterTypeName` | `sprk_mattertype_ref` | `sprk_mattertypename` | `sprk_mattertype_refid` | Yes — exact match | Matter |
| `projectTypeName` | `sprk_projecttype_ref` | `sprk_projecttypename` | `sprk_projecttype_refid` | Yes — exact match | Project |
| `practiceAreaName` | `sprk_practicearea_ref` | `sprk_practiceareaname` | `sprk_practicearea_refid` | Yes — exact match | Both |
| `assignedAttorneyName` | `contact` | `fullname` | `contactid` | No — fuzzy match | Both |
| `assignedParalegalName` | `contact` | `fullname` | `contactid` | No — fuzzy match | Both |
| `assignedOutsideCounselName` | `sprk_organization` | `sprk_organizationname` | `sprk_organizationid` | No — fuzzy match | Matter |

### BFF API Configuration

| Item | Value |
|------|-------|
| **Matter Endpoint** | `POST /api/workspace/matters/pre-fill` |
| **Project Endpoint** | `POST /api/workspace/projects/pre-fill` |
| **Matter Service** | `MatterPreFillService` (scoped) |
| **Project Service** | `ProjectPreFillService` (scoped) |
| **Matter Config Key** | `Workspace:PreFillPlaybookId` |
| **Project Config Key** | `Workspace:ProjectPreFillPlaybookId` |
| **Default Matter Playbook GUID** | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` |
| **Timeout** | 45s (BFF), 60s (frontend) |
| **Rate Limit** | 10 req/min per user (`ai-stream` policy) |

### Frontend — `useAiPrefill` Hook

All entity wizards use the shared `useAiPrefill` hook from `@spaarke/ui-components`.

**Location**: `src/client/shared/Spaarke.UI.Components/src/hooks/useAiPrefill.ts`

**Usage Pattern:**

```typescript
import { useAiPrefill, type IResolvedPrefillFields } from '@spaarke/ui-components';

const prefill = useAiPrefill({
  endpoint: '/workspace/matters/pre-fill',
  uploadedFiles,
  authenticatedFetch,
  bffBaseUrl: getBffBaseUrl(),
  fieldExtractor: (data) => ({
    textFields: { matterName: data.matterName as string | undefined },
    lookupFields: { matterTypeName: data.matterTypeName as string | undefined },
  }),
  lookupResolvers: {
    matterTypeName: (v) => searchMatterTypes(webApi, v),
    practiceAreaName: (v) => searchPracticeAreas(webApi, v),
  },
  onApply: handlePrefillApply,
  skipIfInitialized: !!hasInitialValues,
  logPrefix: 'CreateMatter',
});
```

### Configuration Wiring Checklist

After creating playbook records in Dataverse:

1. **Note the GUIDs** of the created playbook records

2. **Update BFF Configuration**:
   ```json
   {
     "Workspace": {
       "PreFillPlaybookId": "18cf3cc8-02ec-f011-8406-7c1e520aa4df",
       "ProjectPreFillPlaybookId": "<NEW-GUID-HERE>"
     }
   }
   ```

3. **Or set via Azure CLI**:
   ```bash
   az webapp config appsettings set \
     -g spe-infrastructure-westus2 \
     -n spe-api-dev-67e2xz \
     --settings Workspace__ProjectPreFillPlaybookId=<NEW-GUID-HERE>
   ```
   Note: Use `__` (double underscore) for nested config keys in environment variables.

4. **Ensure JPS has $choices** on the playbook's Action `sprk_systemprompt`:
   - `matterTypeName` / `projectTypeName` field: `"$choices": "lookup:sprk_mattertype_ref.sprk_mattertypename"`
   - `practiceAreaName` field: `"$choices": "lookup:sprk_practicearea_ref.sprk_practiceareaname"`
   - `structuredOutput: true` must be enabled

5. **Deploy BFF API** after code changes:
   ```powershell
   .\scripts\Deploy-BffApi.ps1
   ```

6. **Verify endpoint responds** (expect 401 = route registered, needs auth):
   ```bash
   curl -s -o /dev/null -w "%{http_code}" \
     https://spe-api-dev-67e2xz.azurewebsites.net/api/workspace/projects/pre-fill
   ```

### Shared Library Components

| Component | Location | Purpose |
|-----------|----------|---------|
| `useAiPrefill` hook | `src/hooks/useAiPrefill.ts` | Reusable pre-fill orchestration |
| `findBestLookupMatch()` | `src/utils/lookupMatching.ts` | Fuzzy match AI display names against Dataverse lookups |
| `AiFieldTag` | `src/components/AiFieldTag/AiFieldTag.tsx` | "AI" sparkle badge for pre-filled form fields |
| `EntityCreationService` | `src/services/EntityCreationService.ts` | SPE upload, document record creation, AI analysis trigger |

### Adding Pre-Fill to a New Entity Wizard

1. Create a BFF service (copy `MatterPreFillService` pattern)
2. Register the endpoint (e.g., `POST /api/workspace/{entity}/pre-fill`)
3. Create a playbook with `$choices` for constrained fields
4. Call `useAiPrefill` in the wizard step component
5. Use `AiFieldTag` on fields that were AI-populated

---

## Troubleshooting

### Tool Not Executing

**Symptoms**: Analysis fails with "No handler found" or "Tool not configured"

1. Is **Handler Class** empty or set to a valid handler?
   - Empty → Should use GenericAnalysisHandler (check if registered)
   - Set → Check spelling matches exactly (case-sensitive)
2. Call `GET /api/ai/handlers` to see available handlers
3. Check API logs for: "Available handlers: [...]"

### Configuration JSON Invalid

**Symptoms**: Tool fails with "Invalid configuration format" or "JSON syntax error"

Validate JSON syntax using online validator (jsonlint.com). Common issues:
```json
// BAD (missing comma, trailing comma)
{ "operation": "extract"  "temperature": 0.2, }

// GOOD
{ "operation": "extract", "temperature": 0.2 }
```

### Skill Not Applied

**Symptoms**: Analysis results don't reflect skill instructions

1. Verify N:N relationship: Open playbook → Related → Skills
2. Confirm prompt fragment is populated in the skill record
3. Review combined prompt in logs (should include skill fragment)

### Knowledge Not Used

**Symptoms**: Analysis doesn't reference domain knowledge

1. For **Inline**: Is `sprk_content` field populated?
2. For **RAG**: Is `sprk_deploymentid` set correctly?
3. Check logs for "Loading knowledge {KnowledgeId}"

### AI Assistant Not Responding (Builder)

1. Check your internet connection; wait a few moments and try again
2. If rate limited, wait 30 seconds before retrying
3. Refresh the page if issues persist

### Test Failures

| Error | Solution |
|-------|----------|
| "Document too large" | Use a smaller document (<50MB) |
| "Unsupported format" | Use PDF, DOCX, or TXT files |
| "Rate limit exceeded" | Wait 30 seconds and try again |
| "Analysis failed" | Check document is readable text |

---

## Additional Resources

**Architecture Documents**:
- [AI-ARCHITECTURE.md](../architecture/AI-ARCHITECTURE.md) — Complete AI architecture (four-tier model, execution flow, node types)
- [playbook-architecture.md](../architecture/playbook-architecture.md) — Playbook internals, node executors, execution engine

**Related Guides**:
- [JPS-AUTHORING-GUIDE.md](JPS-AUTHORING-GUIDE.md) — JPS schema reference, prompt authoring, playbook design with Claude Code
- [DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md](DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md) — Dataverse schema updates
- [ai-troubleshooting.md](ai-troubleshooting.md) — AI-specific troubleshooting

**Dataverse Entities Reference**:

| Entity | Logical Name | Purpose |
|--------|-------------|---------|
| Analysis Playbook | `sprk_analysisplaybook` | Playbook definition + canvas JSON |
| Playbook Node | `sprk_playbooknode` | Individual node records (synced from canvas) |
| Analysis Action | `sprk_analysisaction` | System prompt templates |
| Analysis Skill | `sprk_analysisskill` | Prompt fragments (domain expertise) |
| Analysis Knowledge | `sprk_analysisknowledge` | RAG context or inline text |
| Analysis Tool | `sprk_analysistool` | Executable handlers |

**API Reference**:
- `GET /api/ai/scopes/actions` — List available actions
- `GET /api/ai/scopes/skills` — List available skills
- `GET /api/ai/scopes/knowledge` — List available knowledge sources
- `GET /api/ai/scopes/tools` — List available tools
- `GET /api/ai/handlers` — List available tool handlers with metadata
- `POST /api/ai/playbooks/{id}/execute` — Execute a playbook (SSE streaming)
- All require authentication (Entra ID + Dataverse permissions)

---

## Verification

After creating or modifying scopes, verify the setup:

1. **Scope visibility** — call `GET /api/ai/scopes/actions`, `/skills`, `/knowledge`, `/tools` to confirm scopes appear in API responses
2. **Handler registration** — call `GET /api/ai/handlers` to confirm all expected tool handlers are listed (including `GenericAnalysisHandler`)
3. **Tool execution** — create a test playbook with one AI Analysis node, assign an Action + Tool, and execute via `POST /api/ai/playbooks/{id}/execute`
4. **Scope services present** — verify these service files exist in the codebase:
   - `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` (scope resolution orchestration)
   - `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeManagementService.cs` (CRUD operations)
   - `src/server/api/Sprk.Bff.Api/Services/Scopes/ScopeCopyService.cs` (Save As functionality)
   - `src/server/api/Sprk.Bff.Api/Services/Scopes/ScopeInheritanceService.cs` (Extend functionality)
   - `src/server/api/Sprk.Bff.Api/Services/Scopes/OwnershipValidator.cs` (SYS-/CUST- prefix validation)
5. **Pre-fill integration** — upload a document to `POST /api/workspace/matters/pre-fill` and verify structured JSON response with constrained field values

---

*Document Owner: Spaarke Engineering*
*Last Updated: 2026-04-05*
