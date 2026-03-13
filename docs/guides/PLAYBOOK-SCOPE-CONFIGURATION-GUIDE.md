# Playbook Scope Configuration Guide

> **Version**: 3.2
> **Date**: March 13, 2026
> **Audience**: Dataverse Administrators, Power Users, Engineers
> **Prerequisites**: Access to Dataverse environment with Spaarke AI solution installed
> **Related**: For output node types (DeliverOutput, DeliverToIndex, SendEmail, CreateTask), see [playbook-architecture.md](../architecture/playbook-architecture.md)

---

## Table of Contents

1. [Overview](#overview)
2. [Understanding Scopes](#understanding-scopes)
3. [Creating Tools](#creating-tools)
4. [Creating Skills](#creating-skills)
5. [Creating Knowledge Sources](#creating-knowledge-sources)
6. [Creating Actions](#creating-actions)
7. [Building Playbooks in the Visual Canvas](#building-playbooks-in-the-visual-canvas)
8. [Multi-Node Playbook Design](#multi-node-playbook-design)
9. [Performance Optimization](#performance-optimization)
10. [Troubleshooting](#troubleshooting)

---

## Overview

**Playbook Scopes** are reusable building blocks for AI analysis workflows. This guide shows you how to create and configure each type of scope in Dataverse.

### What You'll Learn

- How to create configuration-driven tools that work without code deployment
- How to write effective prompt fragments for skills
- How to configure RAG knowledge sources
- How to create system prompt templates for actions
- Best practices for each scope type

### Key Benefit

**Configuration-Driven**: Once you create a scope in Dataverse, it works immediately in playbooks - no code deployment required!

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

When a node executes, the server assembles all scopes into a single prompt sent to Azure OpenAI. Here is the exact assembly sequence:

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
│       The Action's SystemPrompt is the primary instruction. │
│       If no Action is set, the Tool's prompt template is    │
│       used. If neither exists, a built-in default runs.     │
│                          ↓                                  │
│  ┌─ 2. PLACEHOLDER SUBSTITUTION ────────────────────────┐  │
│  │   {document}    → full extracted document text         │  │
│  │   {parameters}  → Tool Configuration.Parameters JSON   │  │
│  │   {tool_name}   → Tool record name                     │  │
│  │   {tool_description} → Tool record description          │  │
│  └───────────────────────────────────────────────────────┘  │
│       If the prompt contains {document}, the document text  │
│       is inserted there. These are literal string replaces. │
│                          ↓                                  │
│  ┌─ 3. SKILLS (appended to prompt) ─────────────────────┐  │
│  │   ## Additional Analysis Instructions                 │  │
│  │                                                       │  │
│  │   [Contract Analysis]                                 │  │
│  │   Focus on liability clauses, indemnification...      │  │
│  │                                                       │  │
│  │   [Financial Terminology]                             │  │
│  │   Use standard financial definitions per GAAP...      │  │
│  └───────────────────────────────────────────────────────┘  │
│       Each selected Skill's PromptFragment is concatenated  │
│       and appended. Skills are additive modifiers — they    │
│       refine the analysis without replacing the Action.     │
│                          ↓                                  │
│  ┌─ 4. KNOWLEDGE (appended to prompt) ──────────────────┐  │
│  │   ## Reference Knowledge                              │  │
│  │                                                       │  │
│  │   [Standard Contract Clauses]                         │  │
│  │   Force Majeure: Unforeseeable circumstances...       │  │
│  │                                                       │  │
│  │   [Company Policies]                                  │  │
│  │   Section 4.2: All contracts must include...          │  │
│  └───────────────────────────────────────────────────────┘  │
│       Each selected Knowledge source's content is appended. │
│       Currently supports Inline text. Document and RAG      │
│       types are defined but not yet implemented.            │
│                          ↓                                  │
│  ┌─ 5. DOCUMENT (auto-appended if needed) ──────────────┐  │
│  │   ## Document                                         │  │
│  │                                                       │  │
│  │   [full extracted document text]                       │  │
│  └───────────────────────────────────────────────────────┘  │
│       If the prompt already used {document} placeholder,    │
│       the text was inserted there (step 2). If NOT, the     │
│       full document is auto-appended here so the LLM        │
│       always sees the document content.                     │
│                          ↓                                  │
│  ┌─ 6. OUTPUT SCHEMA (from Tool Configuration) ─────────┐  │
│  │   Return your response as valid JSON matching:        │  │
│  │   { "type": "object", "properties": { ... } }        │  │
│  └───────────────────────────────────────────────────────┘  │
│       The Tool's output_schema (JSON Schema) tells the LLM  │
│       what structure to return. If no schema is defined,     │
│       defaults to: { "result": ..., "confidence": 0.0-1.0 } │
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

The **Action** record's SystemPrompt is the primary AI instruction. When an Action is assigned to a node, its prompt takes precedence over the Tool's built-in prompt template. The Tool provides the execution mechanism (which handler class to use) and the output format (JSON Schema), but the Action controls what the LLM actually does.

**Without Action**: Tool's prompt template or built-in default runs → output matches tool's default behavior.

**With Action**: Action's SystemPrompt runs → Tool provides handler and output format → output matches Action's instructions.

---

## Creating Tools

Tools are the executable components that call Azure OpenAI and process responses.

### Method 1: Configuration-Driven Tool (Recommended for Most Cases)

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

**Configuration Fields Explained**:

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

#### Step 1: Open Analysis Tool Entity

Same as Method 1.

#### Step 2: Fill Basic Information with Handler Class

| Field | What to Enter | Example |
|-------|---------------|---------|
| **Name** | Descriptive tool name | "Entity Extractor" |
| **Description** | What the tool does | "Extracts structured entities (Person, Org, Date) from documents" |
| **Tool Type** | Select category | "01 - Entity Extraction" |
| **Handler Class** | **Exact handler name** | "EntityExtractorHandler" |

**Available Handlers** (as of January 2026):

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

**How to Find Available Handlers**:
- Call API: `GET /api/ai/handlers` (requires authentication)
- Lists all registered handlers with metadata
- Shows supported parameters and configuration schemas

#### Step 3: Configure Handler-Specific Settings

Each handler has its own configuration schema. Example for EntityExtractorHandler:

```json
{
  "entityTypes": ["Person", "Organization", "Date", "MonetaryValue", "Location"],
  "confidenceThreshold": 0.7,
  "includeContext": true
}
```

**Note**: Invalid handler names fall back to GenericAnalysisHandler with a warning in logs.

---

### Tool Configuration JSON Reference

The `sprk_configuration` field on every tool record is a JSON string. Its structure depends on the handler class.

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
| `operation` | **Yes** | Operation type: `extract`, `classify`, `validate`, `generate`, `transform`, `analyze`. Determines the built-in default prompt if no Action or prompt_template is set. |
| `prompt_template` | No | Custom prompt with `{document}` and `{parameters}` placeholders. Only used when no Action SystemPrompt is assigned to the node (Priority B). |
| `output_schema` | No | JSON Schema (Draft 07) defining the expected output structure. Appended to prompt as: "Return your response as valid JSON matching this schema: [schema]". If omitted, defaults to: `{ "result": ..., "confidence": 0.0-1.0 }`. |
| `parameters` | No | Arbitrary JSON injected into `{parameters}` placeholder. Use for runtime-configurable values like entity types, categories, thresholds. |
| `temperature` | No | AI creativity setting (0.0=deterministic, 1.0=creative). Default: 0.3. Use 0.1-0.3 for extraction/classification, 0.5-0.8 for generation. |
| `max_tokens` | No | Maximum response tokens. Default: 2000. Range: 100-8000. |

#### Understanding output_schema

The `output_schema` is a standard **JSON Schema** that controls what the LLM returns. It is fully customizable per tool:

- **Add any fields** you need (strings, numbers, booleans, nested objects, arrays)
- **Use enums** to constrain values: `"enum": ["High", "Medium", "Low"]`
- **Require fields**: `"required": ["summary", "confidence"]`
- **Nest deeply**: objects within objects, arrays of objects, etc.

The schema is appended verbatim to the end of the prompt with the instruction "Return your response as valid JSON matching this schema". The LLM follows it to structure its response.

**Example schemas for common operations:**

Classification:
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

Multi-entity extraction:
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

This means:
- Assigning an Action to a node **replaces** the tool's prompt template with the Action's SystemPrompt
- The Tool still provides: handler class, output_schema, parameters, temperature, max_tokens
- Skills and Knowledge are appended regardless of which prompt source is used

---

## Creating Skills

Skills are prompt fragments that add specialized instructions to analysis workflows.

### When to Use Skills

- Add domain expertise (legal, financial, technical)
- Refine behavior for specific document types
- Provide formatting instructions
- Add quality checks or validation rules

### Step-by-Step: Create a Skill

#### Step 1: Open Analysis Skill Entity

1. Navigate to **Advanced Find** in Dataverse (or open the **Analysis Skills** view)
2. Look for: **Analysis Skills** (table: `sprk_analysisskill`)
3. Click **New**

#### Step 2: Fill Basic Information

| Field | What to Enter | Example |
|-------|---------------|---------|
| **Name** | Descriptive skill name | "Contract Analysis" |
| **Description** | What the skill adds | "Adds specialized instructions for analyzing legal contracts" |
| **Skill Type** | Select category | "01 - Document Analysis" |

#### Step 3: Write the Prompt Fragment

Click into the **Prompt Fragment** field and write instructions:

```markdown
## Contract Analysis Instructions

When analyzing contracts, focus on:

1. **Parties Involved**
   - Identify all parties to the agreement
   - Note their roles and obligations

2. **Key Terms**
   - Effective date and termination conditions
   - Payment terms and schedules
   - Renewal and cancellation clauses

3. **Obligations and Responsibilities**
   - List obligations for each party
   - Identify deliverables and deadlines

4. **Risk Factors**
   - Highlight liability limitations
   - Note indemnification clauses
   - Flag unusual or high-risk terms

5. **Compliance Requirements**
   - Identify regulatory or legal compliance obligations
   - Note any jurisdictional considerations

Format your analysis with clear headings and bullet points for easy scanning.
```

### Best Practices for Skills

✅ **DO**:
- Use clear, structured formatting (headings, numbered lists)
- Be specific about what to look for
- Provide examples when helpful
- Keep focused on one domain or aspect

❌ **DON'T**:
- Make them too generic ("analyze the document carefully")
- Duplicate instructions already in the Action
- Include tool-specific execution logic
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

For high-severity risks:
- Describe the risk clearly
- Explain potential consequences
- Suggest mitigation strategies
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

#### Step 1: Open Analysis Knowledge Entity

1. Navigate to **Advanced Find** in Dataverse (or open the **Analysis Knowledge** view)
2. Look for: **Analysis Knowledge** (table: `sprk_analysisknowledge`)
3. Click **New**

#### Step 2: Fill Basic Information

| Field | What to Enter | Example |
|-------|---------------|---------|
| **Name** | Descriptive name | "Standard Contract Clauses Reference" |
| **Description** | What it contains | "Common boilerplate clauses and their standard meanings" |
| **Knowledge Type** | Select "Inline" or equivalent | "01 - Standards" |

#### Step 3: Add Content

Click into the **Content** field and paste your reference text:

```markdown
# Standard Contract Clauses

## Force Majeure
Definition: Unforeseeable circumstances that prevent fulfillment of contract
Standard language: "Neither party shall be liable for failure to perform due to causes beyond reasonable control, including acts of God, war, strikes, or government actions."

## Indemnification
Definition: Agreement to compensate for loss or damage
Standard language: "Each party agrees to indemnify and hold harmless the other from claims arising from their negligence or breach of this agreement."

## Termination for Convenience
Definition: Right to end contract without cause
Standard language: "Either party may terminate this agreement with 30 days written notice."

## Confidentiality
Definition: Protection of proprietary information
Standard language: "Both parties agree to maintain confidentiality of proprietary information disclosed during the term of this agreement and for 2 years thereafter."
```

**Formatting Tips**:
- Use Markdown formatting (headings, lists)
- Keep it organized and scannable
- Include definitions and context
- Limit to 10,000 words for inline content

---

### Method 2: RAG Knowledge (For Large Collections)

**Note**: RAG deployments are typically configured by admins/engineers. Contact your Spaarke administrator to set up RAG indices.

#### Step 1: Create RAG Deployment (Admin Task)

Prerequisites:
- Azure AI Search resource configured
- Documents indexed using RAG indexing jobs
- Deployment record created in Dataverse

#### Step 2: Reference RAG Deployment

| Field | What to Enter | Example |
|-------|---------------|---------|
| **Name** | Descriptive name | "Company Knowledge Base" |
| **Description** | What documents are included | "All company policies, procedures, and training materials" |
| **Knowledge Type** | Select "RAG" or equivalent | "02 - Regulations" |
| **Deployment ID** | Select deployment | "Company-Policies-RAG-2026" |
| **Content** | Optional JSON config | `{"topK": 5, "similarityThreshold": 0.7}` |

**RAG Configuration Options** (optional JSON in Content field):
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
- Establish quality standards

### Step-by-Step: Create an Action

#### Step 1: Open Analysis Action Entity

1. Navigate to **Advanced Find** in Dataverse (or open the **Analysis Actions** view)
2. Look for: **Analysis Actions** (table: `sprk_analysisaction`)
3. Click **New**

#### Step 2: Fill Basic Information

| Field | What to Enter | Example |
|-------|---------------|---------|
| **Name** | Action name (verb-based) | "Summarize Content" |
| **Description** | What behavior it defines | "Generate concise summaries with key points and takeaways" |
| **Action Type** | Select category | "03 - Summarization" |

#### Step 3: Write the System Prompt

Click into the **System Prompt** field and write a comprehensive prompt:

```markdown
# Role
You are a professional document summarization specialist with expertise in distilling complex information into clear, actionable summaries.

# Task
Generate a comprehensive yet concise summary of the provided document that captures:
- Main purpose and key messages
- Critical information and decisions
- Important dates, numbers, and facts
- Action items or next steps (if applicable)

# Guidelines

## Structure
Your summary must include:
1. **TL;DR** (1-2 sentences): The absolute essence in plain language
2. **Overview** (1 paragraph): Context and main purpose
3. **Key Points** (bulleted list): 3-7 most important takeaways
4. **Details** (if needed): Additional context organized by topic

## Writing Style
- Use clear, professional language
- Avoid jargon unless industry-standard
- Write in active voice
- Be objective and factual
- Use present tense for facts, past tense for completed actions

## Quality Standards
- Accuracy: Only include information from the source document
- Completeness: Don't omit critical information
- Conciseness: Aim for 20-30% of original length
- Clarity: Someone unfamiliar with the topic should understand

## What to Emphasize
- Decisions and conclusions
- Numerical data and metrics
- Deadlines and time-sensitive information
- Changes or updates from previous versions

## What to De-emphasize
- Procedural details
- Boilerplate language
- Redundant information

# Output Format
Return your response as structured JSON:
```json
{
  "tldr": "One-sentence summary",
  "overview": "Paragraph overview",
  "keyPoints": ["Point 1", "Point 2", "Point 3"],
  "details": {
    "section1": "Content",
    "section2": "Content"
  },
  "confidence": 0.95
}
```

# Document
{document}

Begin your analysis.
```

### Best Practices for Actions

✅ **DO**:
- Define the LLM's role and expertise clearly
- Specify exact output format (JSON schema)
- Include quality standards and examples
- Use placeholders like `{document}` for dynamic content
- Structure with clear sections (Role, Task, Guidelines, Format)

❌ **DON'T**:
- Make assumptions about document type (that's what Skills do)
- Include tool-specific execution logic
- Write generic prompts ("analyze the document")
- Forget to specify output format

### Action Template Structure

**Recommended sections**:
1. **# Role** - Who is the LLM in this scenario?
2. **# Task** - What should it do?
3. **# Guidelines** - How should it do it?
   - Structure requirements
   - Writing style
   - Quality standards
   - Emphasis guidance
4. **# Output Format** - Exact JSON or text format
5. **# Document** - Placeholder: `{document}`

---

## Building Playbooks in the Visual Canvas

The **Playbook Builder** is a visual node-based editor for creating AI analysis workflows. Each playbook is a directed graph of nodes connected by edges.

### Opening the Playbook Builder

1. Navigate to **Analysis Playbooks** in Dataverse
2. Open an existing playbook record (or create a new one)
3. The Playbook Builder canvas loads automatically on the form

### Node Types (Palette)

Drag node types from the left sidebar palette onto the canvas:

| Node Type | Category | Purpose | Requires Scopes? |
|-----------|----------|---------|-------------------|
| **AI Analysis** | AI | LLM-powered analysis using action + skills + knowledge + tool | Yes (full scopes) |
| **AI Completion** | AI | Raw LLM completion with system prompt and user template | Yes (full scopes) |
| **Condition** | Logic | Branch execution based on expression (true/false paths) | No |
| **Wait** | Logic | Pause for duration, until datetime, or condition met | No |
| **Deliver Output** | Output | Format and assemble results from upstream nodes | No |
| **Create Task** | Action | Create a Dataverse task record | No |
| **Send Email** | Action | Send email via Microsoft Graph | No |

### Configuring a Node

1. **Click a node** on the canvas to open the Properties Panel (right sidebar)
2. The panel shows sections based on node type:

**For AI Analysis / AI Completion nodes:**

| Section | Control | Purpose |
|---------|---------|---------|
| **Basic** | Name, Output Variable | Display label and variable name for downstream references |
| **Action** | Dropdown (single-select) | Select the analysis action — defines the primary AI instruction (SystemPrompt) |
| **AI Model** | Dropdown | Select Azure OpenAI model deployment |
| **Skills** | Checkboxes (multi-select) | Add prompt fragment modifiers for domain expertise |
| **Knowledge** | Checkboxes (multi-select) | Add reference context (inline text, RAG) |
| **Tool** | Radio buttons (single-select) | Select one execution handler — defines how the AI processes and formats output |
| **Runtime** | Timeout, Retry Count | Execution constraints |

**For Deliver Output nodes:**

| Section | Control | Purpose |
|---------|---------|---------|
| **Basic** | Name, Output Variable | Display label and variable name |
| **Configuration** | Delivery Format, Template, Metadata toggles, Max Length | How to format and assemble upstream node outputs |

**For other node types** (Condition, Wait, Create Task, Send Email):

| Section | Control | Purpose |
|---------|---------|---------|
| **Basic** | Name, Output Variable | Display label and variable name |
| **Configuration** | Type-specific fields | Condition expression, wait duration, email recipients, etc. |
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

Nodes can reference outputs from previously completed nodes using **Handlebars template variables**. The variable name is the **Output Variable** you set on the upstream node.

**Available template paths per node output:**

| Path | Type | Description |
|------|------|-------------|
| `{{varName.text}}` | string | The raw text output from the node |
| `{{varName.output.fieldName}}` | any | A specific field from the structured JSON output |
| `{{varName.success}}` | boolean | Whether the node executed successfully |

**Built-in context variables** (available in all nodes, no upstream node required):

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
{{summary.success}}                     — true/false execution status
{{document.id}}                         — Dataverse document GUID (for Update Record recordId)
{{document.name}}                       — document display name
```

Use these in:
- AI Completion `userPromptTemplate` field
- Deliver Output `template` field
- Update Record `fields` values and `recordId`
- Send Email `emailBody` field
- Create Task `description` and `regarding` fields

> **Tip**: The `.text` path returns the full text content. The `.output.fieldName` path navigates into structured JSON when the AI tool returns parsed data (e.g., from an output schema). The `document.*` and `run.*` variables are always available — they come from the execution context, not from upstream nodes.

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

### Deliver Output Template

The Deliver Output node uses a **Handlebars template** to assemble all upstream results:

```handlebars
# Contract Review Report

## Summary
{{summary.text}}

## Document Classification
**Type**: {{classification.output.documentType}}

## Key Entities
{{entities.text}}

## Matter Match
{{match_result.text}}
```

> See [Configuring Deliver Output Nodes](#configuring-deliver-output-nodes) below for complete configuration details.

---

## Configuring Deliver Output Nodes

The **Deliver Output** node is typically the final node in a playbook. It assembles results from all upstream AI nodes into a single formatted deliverable shown to the user in the Analysis Workspace.

### Two Modes of Operation

| Mode | When Used | Behavior |
|------|-----------|----------|
| **Auto-Assembly** | Template field is **empty** | Concatenates all upstream node outputs as-is (raw text and/or JSON). Fast to set up but produces unformatted output. |
| **Template** | Template field has content | Renders a Handlebars template with variable references to upstream node outputs. Produces clean, structured output. |

**Recommendation**: Always use a template for production playbooks. Auto-assembly is useful only for debugging.

### Delivery Format

The **Delivery Format** dropdown controls the output content type:

| Format | Use Case | Rendering |
|--------|----------|-----------|
| **Markdown** (default) | Human-readable reports, summaries | Rendered as styled Markdown in the Analysis Workspace |
| **HTML** | Rich formatting, tables, embedded links | Rendered as HTML |
| **Plain Text** | Simple text output, logs | Rendered as monospace text |
| **JSON** | Machine-readable structured data, API consumption | Rendered as formatted JSON with all node outputs assembled |

> **Markdown is recommended** for most playbooks. It renders well in the Analysis Workspace and supports headings, lists, tables, bold, italic, and code blocks.

### Writing Templates

Templates use **Handlebars syntax** to reference outputs from upstream nodes. The variable name must match the **Output Variable** set on the upstream node.

#### Available Template Variables

For each upstream node with output variable `varName`:

| Expression | Returns |
|-----------|---------|
| `{{varName.text}}` | The full text content from that node |
| `{{varName.output.fieldName}}` | A specific field from structured JSON output |
| `{{varName.success}}` | `true` or `false` — whether the node succeeded |

#### Template Example: Document Summary Playbook

Given a playbook with these nodes:

| Node | Output Variable | Purpose |
|------|----------------|---------|
| Profile Document | `profile` | Extracts document metadata |
| Summarize | `summary` | Generates executive summary |

Template:

```handlebars
# Document Summary

## Profile
{{profile.text}}

## Executive Summary
{{summary.text}}
```

#### Template Example: Contract Review Playbook

Given a playbook with four AI nodes:

| Node | Output Variable | Purpose |
|------|----------------|---------|
| Summarize Content | `summary` | Executive summary |
| Classify Document | `classify` | Document type classification |
| Extract Entities | `entities` | Parties, dates, terms |
| Risk Assessment | `risk` | Key risks identified |

Template:

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

#### Template Example: Accessing Structured Output Fields

When a tool uses an **Output Schema** (configured in the tool's `sprk_configuration` JSON), the AI returns structured JSON. You can access individual fields:

```handlebars
## Document Profile

| Field | Value |
|-------|-------|
| **Type** | {{profile.output.documentType}} |
| **Date** | {{profile.output.documentDate}} |
| **Parties** | {{profile.output.parties}} |

## Summary
{{summary.text}}
```

> **Note**: The `.output.fieldName` syntax only works when the upstream tool produces structured JSON (via an Output Schema). If the tool returns plain text, use `.text` instead.

### Output Format Options

These settings control post-processing of the rendered output:

| Option | Default | Purpose |
|--------|---------|---------|
| **Include Metadata** | Off | Appends execution metadata (run ID, timestamps, node count, confidence scores) to the output |
| **Include Source Citations** | Off | Appends source citation references to the output |
| **Max Output Length** | 0 (unlimited) | Maximum characters in output. Content beyond this limit is truncated with "...(truncated)" |

- **Include Metadata** is useful for audit trails and debugging
- **Include Source Citations** is useful when Knowledge sources (RAG) contribute to the output
- **Max Output Length** prevents runaway outputs from consuming excessive storage

### JSON Delivery Format (Special Behavior)

When **Delivery Format = JSON**, the node assembles all upstream outputs into a structured JSON object:

```json
{
  "_metadata": {
    "playbookId": "...",
    "runId": "...",
    "generatedAt": "2026-03-02T...",
    "nodeCount": 3,
    "overallConfidence": 0.92
  },
  "summary": { ... },
  "entities": { ... },
  "classification": { ... }
}
```

- The `_metadata` block is only included when **Include Metadata** is enabled
- Each upstream node's structured output is included under its output variable name
- If a template is also provided, the engine attempts to parse the rendered template as JSON

### Configuring in the Playbook Builder

1. **Drag** a "Deliver Output" node from the palette onto the canvas
2. **Connect** all upstream AI nodes to it (draw edges from each AI node → Deliver Output)
3. **Click** the Deliver Output node to open the Properties Panel
4. **Set** the Output Variable name (e.g., `final_output`)
5. **Choose** a Delivery Format (Markdown recommended)
6. **Write** a template using `{{outputVariable.text}}` or `{{outputVariable.output.field}}` syntax
7. **Toggle** metadata and citation options as needed
8. **Set** max output length if needed (0 = unlimited)
9. **Save** the playbook (Ctrl+S)

### Common Mistakes

| Mistake | Symptom | Fix |
|---------|---------|-----|
| Empty template | Output shows raw JSON dump of all node data | Write a template with `{{varName.text}}` references |
| Wrong variable name | `{{summary.text}}` shows blank | Check the upstream node's **Output Variable** field matches exactly |
| Using `.TextContent` instead of `.text` | Template renders literally | Use `.text` (lowercase, no "Content" suffix) |
| Using `.StructuredData` instead of `.output` | Template renders literally | Use `.output.fieldName` to access structured data |
| No edges to Deliver Output | Output is empty | Connect upstream nodes to the Deliver Output node |
| Delivery Format mismatch | Markdown renders as code block | Match the format to your template content type |

---

## Configuring Update Record Nodes

The **Update Record** node writes AI analysis results back to Dataverse entity records. It is fully configuration-driven — no custom code is needed for any entity or field combination.

### When to Use

Use Update Record nodes to:
- Write AI-extracted fields back to the source document record (`sprk_document`)
- Update status/choice fields after analysis completes (e.g., document type, summary status)
- Set boolean flags based on AI classification (e.g., is confidential, requires review)
- Set lookup fields linking documents to matched entities (e.g., link document to a matter)
- Write computed values to any Dataverse entity

### Typed Field Mappings (Recommended)

The recommended way to configure an Update Record node is with **typed field mappings**. Each field mapping declares the Dataverse field type so the executor can correctly coerce AI string output into the right Dataverse value.

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
      "field": "sprk_filesummarystatus",
      "type": "choice",
      "value": "{{aiAnalysis.output.status}}",
      "options": {
        "pending": 100000000,
        "in progress": 100000001,
        "complete": 100000002
      }
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
        "letter": 100000004,
        "memo": 100000005,
        "email": 100000006,
        "agreement": 100000007,
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

Each field mapping has a `type` that determines how the AI's string output is coerced into the correct Dataverse value:

| Type | Dataverse Column Type | Coercion Behavior | Example |
|------|----------------------|-------------------|---------|
| **string** | Single/Multi-line Text | Pass through as-is | AI outputs `"A detailed summary..."` → stored as string |
| **choice** | Choice (OptionSet) | Case-insensitive label lookup in `options` map → integer value | AI outputs `"Complete"` → matched to `"complete": 100000002` → stored as `100000002` |
| **boolean** | Yes/No (Two Option) | Parses common truthy/falsy strings | AI outputs `"yes"` → stored as `true` |
| **number** | Whole Number / Decimal | Parses as integer, then decimal | AI outputs `"42"` → stored as `42` |

### Choice Field Coercion (Detail)

Choice (OptionSet) fields require mapping AI text labels to Dataverse integer option values. The `options` property defines this mapping:

```json
{
  "field": "sprk_documenttype",
  "type": "choice",
  "value": "{{aiAnalysis.output.documentType}}",
  "options": {
    "contract": 100000000,
    "invoice": 100000001,
    "proposal": 100000002
  }
}
```

**How coercion works:**

1. AI outputs a string value (e.g., `"Contract"`)
2. Executor trims and lowercases: `"contract"`
3. Looks up in `options` map (case-insensitive): `"contract" → 100000000`
4. Sends `100000000` to Dataverse via OData PATCH

**Fallback behavior:**
- If the AI output doesn't match any label, the executor tries parsing it as a raw integer (in case the AI returns the numeric value directly)
- If neither matches, the field is skipped with a warning in the API logs

**Matching is case-insensitive**: `"Contract"`, `"contract"`, `"CONTRACT"` all match the same option.

### Boolean Field Coercion (Detail)

Boolean fields map common AI text responses to `true`/`false`:

| AI Output → `true` | AI Output → `false` |
|---------------------|----------------------|
| `"true"`, `"yes"`, `"1"`, `"on"` | `"false"`, `"no"`, `"0"`, `"off"` |

```json
{
  "field": "sprk_isconfidential",
  "type": "boolean",
  "value": "{{aiAnalysis.output.isConfidential}}"
}
```

Matching is case-insensitive: `"Yes"`, `"YES"`, `"yes"` all coerce to `true`.

### Choice Options and AI Prompt Alignment

The `options` map in a Choice field mapping serves **dual purpose**:

1. **Downstream coercion**: Converts AI string output → Dataverse integer option value
2. **Upstream prompt guidance**: The same labels should appear in your AI Action prompt so the AI knows the valid values to choose from

**Example — keeping prompts and field mappings in sync:**

In the **AI Action prompt** (SystemPrompt), include the valid values:

```
Classify the document type. Choose exactly one:
- contract
- invoice
- proposal
- report
- letter
- memo
- email
- agreement
- other
```

In the **Update Record node**, use the same labels in the `options` map:

```json
{
  "field": "sprk_documenttype",
  "type": "choice",
  "value": "{{aiAnalysis.output.documentType}}",
  "options": {
    "contract": 100000000,
    "invoice": 100000001,
    "proposal": 100000002,
    "report": 100000003,
    "letter": 100000004,
    "memo": 100000005,
    "email": 100000006,
    "agreement": 100000007,
    "other": 100000012
  }
}
```

This ensures the AI picks from labels that the downstream executor can reliably map to Dataverse values.

> **Tip**: Define the valid values **once** in the UpdateRecord field mapping, then copy the same labels into your Action prompt. This prevents mismatches where the AI outputs a label that doesn't appear in the options map.

### Configuring in the Playbook Builder UI

The Playbook Builder provides a visual form for configuring typed field mappings:

1. **Click** an Update Record node on the canvas to open the Properties Panel
2. **Set** the Entity Logical Name (e.g., `sprk_document`)
3. **Set** the Record ID (e.g., `{{document.id}}`)
4. **Click "Add Field"** to add a field mapping
5. For each mapping:
   - Enter the **field logical name** (e.g., `sprk_documenttype`)
   - Select the **type** from the dropdown: String, Choice, Boolean, or Number
   - Enter the **value template** (e.g., `{{aiAnalysis.output.documentType}}`)
   - For **Choice** fields: an options editor appears where you add label → value pairs
6. **Save** the playbook (Ctrl+S)

**Choice Options Editor:**
- Click **"Add Option"** to add a new label → value pair
- Enter the **label** (text the AI outputs, e.g., "Complete")
- Set the **value** (Dataverse OptionSet integer, e.g., `100000002`)
- Click the delete button to remove an option

### Template Variables in Field Values

All field values support Handlebars template variables referencing upstream node outputs:

```json
{
  "fieldMappings": [
    {
      "field": "sprk_filesummary",
      "type": "string",
      "value": "{{summary.text}}"
    },
    {
      "field": "sprk_analysisscore",
      "type": "number",
      "value": "{{risk.output.overallScore}}"
    }
  ]
}
```

**Built-in context variables** are also available:

| Variable | Description |
|----------|-------------|
| `{{document.id}}` | GUID of the document being analyzed (use for `recordId`) |
| `{{document.name}}` | Document display name |
| `{{document.fileName}}` | Original file name with extension |
| `{{run.id}}` | Current playbook run ID |

### Lookup Fields

Lookup fields use the `@odata.bind` syntax internally. Configure them in the `lookups` section (alongside `fieldMappings`):

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

This generates: `"sprk_relatedmatter@odata.bind": "/sprk_matters(guid)"` in the Dataverse PATCH request.

### Example: Document Summary Write-Back

After AI nodes produce a summary, document type classification, and status:

```json
{
  "entityLogicalName": "sprk_document",
  "recordId": "{{document.id}}",
  "fieldMappings": [
    {
      "field": "sprk_filesummary",
      "type": "string",
      "value": "{{summary.text}}"
    },
    {
      "field": "sprk_filetldr",
      "type": "string",
      "value": "{{summary.output.tldr}}"
    },
    {
      "field": "sprk_documenttype",
      "type": "choice",
      "value": "{{summary.output.documentType}}",
      "options": {
        "contract": 100000000,
        "invoice": 100000001,
        "proposal": 100000002,
        "report": 100000003,
        "letter": 100000004,
        "other": 100000012
      }
    },
    {
      "field": "sprk_filesummarystatus",
      "type": "choice",
      "value": "{{summary.output.status}}",
      "options": {
        "pending": 100000000,
        "in progress": 100000001,
        "complete": 100000002
      }
    },
    {
      "field": "sprk_isconfidential",
      "type": "boolean",
      "value": "{{summary.output.isConfidential}}"
    }
  ]
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

With the legacy format, type coercion is heuristic (automatic best-guess):
- String values that parse as integers → stored as `int`
- String values that parse as decimals → stored as `decimal`
- String values that parse as booleans → stored as `bool`
- All other values → stored as strings

> **Recommendation**: Migrate existing playbooks to `fieldMappings` for explicit type control. The Playbook Builder UI automatically migrates legacy `fields` to `fieldMappings` (all typed as `string`) when you open and save a node.

### How the Executor Chooses Format

The server detects which format to use:
- If `fieldMappings` array is present and non-empty → **typed coercion** (new path)
- If only `fields` dictionary is present → **heuristic coercion** (legacy path)
- Both can coexist in the same playbook (different nodes can use different formats)

---

## Performance Optimization

### How Parallel Execution Works

The execution engine groups nodes into **batches** based on dependencies:
- Nodes in the **same batch** have no dependencies on each other and run **in parallel**
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
  Batch 1    Batch 2  │   Batch 3
                      └
Total time = max(Summarize, Classify, Extract) + Match + Output
```

### The Optimization Rule

> **Only add edges where data actually flows.** If a node does not reference `{{upstream.output}}` in its prompt, it should NOT have a dependency edge.

In the contract review example: Summarize, Classify, Extract, and Match all analyze the **source document** — they don't need each other's output. Connect them all directly to the Deliver Output node for maximum parallelism (Pattern B).

### Estimating Execution Time

Each AI Analysis node's duration is roughly:
- Network overhead: ~200-500ms
- Token processing: (input_tokens + output_tokens) / tokens_per_second
- For GPT-4o class models: ~50-80 tokens/sec output
- A node producing 500 output tokens ≈ 7-10 seconds

Four AI nodes in parallel ≈ 7-10 seconds total. Four sequential ≈ 28-40 seconds.

---

## Troubleshooting

### Issue: Tool Not Executing

**Symptoms**: Analysis fails with "No handler found" or "Tool not configured"

**Check**:
1. Is **Handler Class** empty or set to a valid handler?
   - Empty → Should use GenericAnalysisHandler (check if registered)
   - Set → Check spelling matches exactly (case-sensitive)
2. Call `GET /api/ai/handlers` to see available handlers
3. Check API logs for: "Available handlers: [...]"

**Solution**:
- If handler not found, it falls back to GenericAnalysisHandler
- If still fails, verify GenericAnalysisHandler is registered in DI
- Check `sprk_configuration` field has valid JSON

---

### Issue: Configuration JSON Invalid

**Symptoms**: Tool fails with "Invalid configuration format" or "JSON syntax error"

**Check**:
1. Validate JSON syntax using online validator (jsonlint.com)
2. Check for:
   - Missing commas
   - Unmatched braces/brackets
   - Invalid escape sequences

**Solution**:
```json
// ❌ BAD (missing comma, trailing comma)
{
  "operation": "extract"
  "temperature": 0.2,
}

// ✅ GOOD
{
  "operation": "extract",
  "temperature": 0.2
}
```

---

### Issue: Skill Not Applied

**Symptoms**: Analysis results don't reflect skill instructions

**Check**:
1. Is skill added to playbook via N:N relationship?
2. Is prompt fragment populated in the skill record?
3. Check Analysis logs for combined prompt

**Solution**:
- Verify N:N relationship: Open playbook → Related → Skills
- Test with a simple, obvious skill instruction
- Review combined prompt in logs (should include skill fragment)

---

### Issue: Knowledge Not Used

**Symptoms**: Analysis doesn't reference domain knowledge

**Check**:
1. For **Inline**: Is `sprk_content` field populated?
2. For **RAG**: Is `sprk_deploymentid` set correctly?
3. Is knowledge added to playbook via N:N relationship?

**Solution**:
- Inline: Ensure content is formatted as Markdown or plain text
- RAG: Verify deployment exists and documents are indexed
- Check logs for "Loading knowledge {KnowledgeId}"

---

### Issue: Action Prompt Not Effective

**Symptoms**: Results don't match expected behavior or format

**Check**:
1. Is output format specified clearly (JSON schema)?
2. Are guidelines specific enough?
3. Is temperature set appropriately (lower=deterministic)?

**Solution**:
- Add explicit JSON schema with required fields
- Provide examples of good vs bad output
- Lower temperature (0.2-0.3) for structured extraction
- Higher temperature (0.7-0.9) for creative generation

---

## Best Practices Summary

### Configuration-Driven First
- Start with GenericAnalysisHandler (no code deployment)
- Only use custom handlers for complex scenarios
- Test configurations in non-production first

### Prompt Engineering
- Be specific and structured
- Use examples and formatting guidelines
- Specify exact output format (JSON schema)
- Test with various document types

### Naming Conventions
- **Tools**: Noun or verb-noun ("Entity Extractor", "Summarize Document")
- **Skills**: Domain-noun ("Contract Analysis", "Financial Review")
- **Knowledge**: Descriptive name ("Standard Clauses", "Company Policies")
- **Actions**: Verb phrase ("Extract Entities", "Summarize Content")

### Testing Strategy
1. Create scope in dev environment
2. Add to test playbook
3. Execute with sample documents
4. Review Analysis Output records
5. Refine configuration based on results
6. Deploy to production when validated

---

## Additional Resources

**Architecture Documents**:
- [AI-ARCHITECTURE.md](../architecture/AI-ARCHITECTURE.md) - Complete AI architecture (four-tier model, execution flow, node types)

**Related Guides**:
- [HOW-TO-CREATE-UPDATE-SCHEMA.md](DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md) - Dataverse schema updates
- [AI-TROUBLESHOOTING.md](ai-troubleshooting.md) - AI-specific troubleshooting

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
- `GET /api/ai/scopes/actions` - List available actions
- `GET /api/ai/scopes/skills` - List available skills
- `GET /api/ai/scopes/knowledge` - List available knowledge sources
- `GET /api/ai/scopes/tools` - List available tools
- `GET /api/ai/handlers` - List available tool handlers with metadata
- `POST /api/ai/playbooks/{id}/execute` - Execute a playbook (SSE streaming)
- All require authentication (Entra ID + Dataverse permissions)

---

**Last Updated**: March 3, 2026
