# How to Create AI Playbooks and Scopes

> **Version**: 2.0
> **Date**: March 1, 2026
> **Audience**: Dataverse Administrators, Power Users, Engineers
> **Prerequisites**: Access to Dataverse environment with Spaarke AI solution installed

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

### How They Work Together

```
Playbook Execution Flow:
1. Action provides base system prompt
2. Skills add specialized instructions
3. Knowledge provides domain context
4. Tool executes with combined prompt and returns results
```

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
2. Configure:
   - **Name**: Display label
   - **Output Variable**: Variable name other nodes can reference (e.g., `summary_result`)
   - **AI Model**: Select model deployment (AI nodes only)
   - **Skills**: Select prompt fragments that add domain expertise
   - **Knowledge**: Select context sources (inline or RAG)
   - **Tools**: Select executable handler
   - **Configuration**: Type-specific settings (template, email recipients, etc.)
   - **Runtime**: Timeout (seconds), retry count

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

Nodes can reference outputs from previously completed nodes using **template variables**:

```
{{summary_result.TextContent}}      — raw text from an upstream node
{{entities.StructuredData}}          — JSON data from an upstream node
{{classify_result.Confidence}}       — confidence score
```

Use these in:
- AI Completion `userPromptTemplate` field
- Deliver Output `template` field
- Send Email `emailBody` field

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
{{summary.TextContent}}

## Document Classification
**Type**: {{classification.StructuredData.documentType}}
**Confidence**: {{classification.Confidence}}

## Key Entities
{{entities.TextContent}}

## Matter Match
{{match_result.TextContent}}
```

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

**Last Updated**: March 1, 2026
