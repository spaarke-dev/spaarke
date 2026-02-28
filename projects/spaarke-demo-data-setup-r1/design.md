# Spaarke Data CLI — Design Document

> **Author**: Ralph Schroeder
> **Date**: February 27, 2026
> **Status**: Draft
> **Project Folder**: `projects/spaarke-demo-data-setup-r1/` (planning only — implementation in separate repository)
> **Target Repository**: [`spaarke-data-cli`](https://github.com/spaarke-dev/spaarke-data-cli.git) — Local: `C:\code_files\SPAARKE-DATA-CLI`

---

## 1. Executive Summary

Spaarke needs a **reusable data management tool** that serves multiple purposes: populating demo environments with realistic legal-industry data, onboarding customers with their own data, refreshing environments, and generating test datasets. The tool must be a proper CLI application — not a collection of scripts — so that it's maintainable, extensible, and usable by people beyond the core development team (sales engineers, onboarding specialists, QA).

This project defines the **Spaarke Data CLI** (`spaarke-data`), a scenario-driven, AI-assisted data generation and loading tool built as a standalone application in its own repository. It combines high-quality open legal datasets with curated templates and synthetic variations, leveraging multiple AI automation tools — Claude Code for generation, Dataverse MCP for direct record creation, OpenClaw for automated dataset harvesting, PAC CLI/CMT for bulk data transport, and optionally Claude Computer Use for UI-based validation.

### 1.1 Why a Tool, Not Scripts

| Scripts (current design risk) | CLI Tool (target) |
|-------------------------------|-------------------|
| Adding a scenario = editing PowerShell code | Adding a scenario = writing a JSON/YAML config file |
| Knowledge locked in developer heads | Self-documenting commands with `--help` |
| Brittle when schema evolves | Schema-driven — reads live Dataverse schema via MCP |
| One-off, hard to hand off | Installable, versionable, distributable |
| Tightly coupled to product repo | Independent lifecycle, independent CI/CD |

### 1.2 Beyond Demo Data — Customer Onboarding

The same architecture supports four distinct use cases with different data sources but identical loading infrastructure:

| Use Case | Data Source | Who Runs It | Environment |
|----------|-----------|-------------|-------------|
| **Internal demo** | Open datasets + synthetic | Developers, sales engineers | Dev / demo |
| **Customer-tailored demo** | Open data with customer industry branding | Sales engineers | Demo |
| **Customer onboarding** | Customer's actual data (CSV, Excel, existing system exports) | Onboarding team | Production |
| **Environment refresh** | Existing environment snapshot or seed files | DevOps, QA | Any |

The tool separates **data source adapters** (where data comes from) from **data transformers** (how it maps to Spaarke entities) from **data loaders** (how it gets into Dataverse/SPE/Search).

### 1.3 Separate Repository

The Spaarke Data CLI lives in its own repository (`spaarke-data-cli`), not in the product repo (`spaarke`). Rationale:

- **Different lifecycle** — demo data and onboarding tooling evolve independently of product features
- **Different audience** — sales engineers, onboarding specialists, QA — not just developers
- **Large binary assets** — PDFs, DOCX, datasets would bloat the product repo's git history
- **Customer-specific configs** — onboarding scenarios may contain customer-specific (even if anonymized) data
- **Separate CI/CD** — doesn't need to build with every product commit; has its own test/release pipeline
- **Distributable** — can be packaged as an npm global tool or .NET tool and installed on any machine

The planning phase for this project lives in `spaarke/projects/spaarke-demo-data-setup-r1/` (this repo) since that's where project planning artifacts belong. Once the plan is approved, implementation begins in the new `spaarke-data-cli` repository.

---

## 2. Problem Statement

### Current State

- The dev environment has minimal test data, mostly artifacts from individual feature development
- Seed data exists for AI configuration entities (playbooks, actions, skills, tools) in `scripts/seed-data/`
- No coordinated demo dataset that tells coherent stories across entities
- No repeatable process for environment reset and repopulation
- No real or realistic legal documents for document analysis, search, and AI features

### Impact

- Product demos lack realism — "Test Matter 1" and empty document grids undermine credibility
- AI features (document analysis, semantic search, chat, RAG) cannot be demonstrated without rich document content
- Financial features (budget utilization, spend analysis, invoice review) need realistic numerical data
- Event/task management features need a timeline of activities that show workflow progression
- Each demo environment setup is manual, inconsistent, and time-consuming

### Success Criteria

1. **Realistic**: Demo data tells 3-5 coherent legal industry stories with real-seeming names, dates, amounts, and document content
2. **Comprehensive**: All system layers populated — Dataverse records, SPE files, AI enrichment, search indexes, activity records
3. **Repeatable**: Single-command environment reset and repopulation (idempotent scripts)
4. **Legal/IP Clean**: All source data is public domain, open-licensed, or synthetic — no client data
5. **AI-Ready**: Documents have pre-baked AI profiles (summaries, keywords, entities) AND can optionally be re-processed through the live AI pipeline for validation
6. **Extensible**: Easy to add new scenarios or adjust volumes without rewriting the pipeline

---

## 3. Data Landscape

### 3.1 Data Layers

The Spaarke platform has five interconnected data layers that must be populated coherently:

| Layer | Content | Target Volume | Dependencies |
|-------|---------|---------------|--------------|
| **1. Reference/Config** | Event types, playbooks, actions, skills, tools, knowledge sources, grid configs, output types | ~50-75 records | None (foundation layer) |
| **2. Core Business Records** | Matters, Projects, Invoices, Accounts, Contacts, Budgets, Work Assignments | ~200-500 records | Layer 1 for lookups |
| **3. Document Records** | `sprk_document` records with AI profiles (summaries, keywords, entities, classification) | ~500-1000 records | Layer 2 for matter/project/invoice lookups |
| **4. Actual Files** | PDF, DOCX, EML, XLSX files in SharePoint Embedded containers | ~200-500 files | Layer 3 for document-to-file mapping |
| **5. Activity/Transactional** | Events, communications, KPI assessments, analysis sessions, billing events, spend snapshots | ~1000-3000 records | Layers 2-4 for regarding records |

### 3.2 Entity Inventory

#### Core Business Entities

| Entity | Key Fields | Relationships | Notes |
|--------|-----------|---------------|-------|
| `account` | name, address, industry, phone, email | Parent for contacts, matters | Law firms, corporate clients, opposing parties |
| `contact` | fullname, email, jobtitle, phone | Parent account, matters | Attorneys, paralegals, clients, witnesses |
| `sprk_matter` | name, status, type, open date, close date | Account, contacts, documents, events | Central entity for litigation/transaction stories |
| `sprk_project` | name, status, type, start/end dates | Matter, documents, events | Work streams within matters |
| `sprk_invoice` | number, amount, date, status, vendor | Matter, billing events | Outside counsel billing |
| `sprk_budget` | name, amount, period, status | Matter, budget buckets | Financial planning |
| `sprk_workassignment` | name, assignee, status, dates | Matter, project, events | Work allocation |

#### Document Entity (`sprk_document`)

| Field Group | Fields | Generation Strategy |
|-------------|--------|---------------------|
| **Core** | documentname, filename, filesize, mimetype | Derived from source files |
| **Storage** | graphitemid, graphdriveid, containerid, filepath | Populated by SPE upload |
| **Status** | statuscode (Draft→Active), hasfile | Set to Active after upload |
| **AI Profile** | filesummary, filetldr, keywords, documenttype, entities | Pre-baked by Claude during generation |
| **Email Metadata** | emailsubject, emailfrom, emailto, emailcc, emailbody, emaildate | Generated for .eml documents |
| **Record Links** | matter, project, invoice | Linked to scenario business records |
| **Source** | sourcetype (UserUpload, EmailReceived, EmailArchive, etc.) | Varies by document origin story |
| **Search** | searchindexed, searchindexname, searchindexedon | Set after index population |

#### Activity Entities

| Entity | Key Fields | Generation Notes |
|--------|-----------|-----------------|
| `sprk_event` | name, type, priority, duedate, status, regarding* | Realistic task chains with dependencies |
| `sprk_eventlog` | event, action, description, timestamp | Audit trail showing progression |
| `sprk_communication` | subject, body, direction, sentby, regarding* | Email threads tied to matters |
| `sprk_kpiassessment` | matter, performancearea, grade, criteria, notes | Performance scorecards per matter |
| `sprk_analysis` | document, playbook, workingdocument, status | Pre-run analysis sessions |
| `sprk_analysisoutput` | analysis, outputtype, value, sortorder | Analysis results (summaries, entities) |
| Billing events | invoice, amount, date, linesequence | Invoice line items |
| Spend snapshots | budget, period, actual, forecast | Budget utilization over time |

### 3.3 Regarding Record Polymorphism

Events and communications use polymorphic "regarding" lookups:

| Type Code | Entity | Lookup Field |
|-----------|--------|-------------|
| 0 | sprk_project | sprk_regardingproject |
| 1 | sprk_matter | sprk_regardingmatter |
| 2 | sprk_invoice | sprk_regardinginvoice |
| 3 | sprk_analysis | sprk_regardinganalysis |
| 4 | account | sprk_regardingaccount |
| 5 | contact | sprk_regardingcontact |
| 6 | sprk_workassignment | sprk_regardingworkassignment |
| 7 | sprk_budget | sprk_regardingbudget |

---

## 4. Data Sourcing Strategy

### 4.1 Approach: Open Data + Templates + Synthetic Variations

The safest and most effective approach for a demo system combines high-quality open datasets with curated templates and lightly synthetic variations. This ensures realistic content without IP or privacy risk.

### 4.2 Open Legal Dataset Sources

#### Contracts & Clauses (Primary — for document analysis demos)

| Source | Content | Volume | License | Use Case |
|--------|---------|--------|---------|----------|
| **CUAD** (Contract Understanding Atticus Dataset) | 500+ real commercial contracts with 13,000+ expert clause annotations across 41 issue types | ~510 contracts | CC BY 4.0 | Deep analysis demos — clause extraction, risk detection, annotation |
| **ACORD** (Atticus Clause Retrieval Dataset) | Clause retrieval dataset for complex provisions (indemnity, limitation of liability) | Focused clause set | Academic | Semantic search and RAG over clauses |
| **Kaggle Legal-Clause-Dataset** (LawInsider scraped) | 150,000 individual clauses across 350+ clause types | 150K clauses | Public/Kaggle | Dense clause corpus for similarity search, classification |
| **TermScout Verified Contract Database** | Thousands of publicly available commercial contracts as structured data | Thousands | Public | Benchmarking, comparison, "market standard" analysis |

**Selection Plan**: Use CUAD as the primary source (best annotations, clean licensing). Supplement with Kaggle clauses for search index density. Use TermScout for "market standard" comparison scenarios.

#### Contract Templates (for synthetic variations)

| Source | Content | Use Case |
|--------|---------|----------|
| Open legal template collections (GitHub) | Standard NDAs, MSAs, service agreements, startup docs | Base templates for synthetic generation |
| NDA/contract generators and boilerplates | Parameterized templates | Controlled variations with known clause differences |

#### Case Law & Regulatory Text (secondary — for legal research demos)

| Source | Content | Volume | Use Case |
|--------|---------|--------|----------|
| **Caselaw Access Project** (case.law) | U.S. court opinions via API | Millions | Long-form legal text retrieval |
| **CourtListener** (Free Law Project) | Court opinions, oral arguments, PACER data | Large | Multi-source legal research |
| **Federal Register / EU Cellar** | Statutes, regulations, notices | Large | Cross-reference and regulatory compliance scenarios |

**Selection Plan**: Pull a curated subset (~50-100 opinions) relevant to demo scenarios. Use for knowledge base seeding and "find clauses referencing this regulation" demos.

### 4.3 Document Type Distribution

Target mix for a balanced demo:

| Document Type | Source Strategy | Count | Format |
|---------------|----------------|-------|--------|
| **Commercial Contracts** (MSAs, SOWs) | CUAD subset + synthetic variations | 80-120 | PDF, DOCX |
| **NDAs** | Open templates + synthetic party/date variations | 30-50 | PDF, DOCX |
| **Employment Agreements** | Open templates + synthetic | 20-30 | PDF, DOCX |
| **Invoices** | Template XLSX + synthetic data injection | 40-60 | PDF, XLSX |
| **Email Correspondence** | AI-generated .eml files (scenario threads) | 80-150 | EML |
| **Legal Opinions/Memos** | AI-generated from scenario context | 20-30 | PDF, DOCX |
| **Court Filings** | Caselaw Access Project subset | 30-50 | PDF |
| **Regulatory Documents** | Federal Register subset | 15-25 | PDF |
| **Corporate Documents** (bylaws, resolutions) | Open templates + synthetic | 15-20 | PDF, DOCX |
| **Financial Reports** | AI-generated from scenario budgets | 10-15 | XLSX, PDF |

**Total Target: ~400-550 actual files**, each with a corresponding `sprk_document` record.

### 4.4 Licensing & Provenance

Every data source must be documented with:

| Field | Purpose |
|-------|---------|
| Source name | Dataset or template origin |
| License type | CC BY, public domain, synthetic, etc. |
| URL | Link to source |
| Modification notes | What was changed from original |
| Category | Open dataset / template / synthetic / hybrid |

A `DATA-PROVENANCE.md` file will track all sources used in the demo dataset.

---

## 5. Demo Scenarios

### 5.1 Scenario Design Principles

- Each scenario exercises multiple system capabilities
- Scenarios have temporal depth (events spanning weeks/months)
- Scenarios intersect (shared contacts, cross-referenced matters)
- Each scenario has a "demo walkthrough" narrative for presenters

### 5.2 Scenario 1: Active Litigation — "Meridian Corp v. Pinnacle Industries"

**Story**: Patent infringement dispute over proprietary manufacturing process. Active litigation with discovery deadlines, expert depositions, and budget pressure.

**Exercises**: Document management, events/deadlines, KPI assessments, budget tracking, AI document analysis, email correspondence, work assignments

| Entity Type | Records | Details |
|-------------|---------|---------|
| Matter | 1 | Active litigation, opened 6 months ago |
| Projects | 3 | Discovery, Expert Analysis, Trial Prep |
| Accounts | 4 | Meridian (client), Pinnacle (opposing), 2 law firms |
| Contacts | 12 | Attorneys, paralegals, expert witnesses, judges |
| Documents | 80-100 | Contracts (disputed), discovery docs, pleadings, expert reports, correspondence |
| Events | 40-60 | Filing deadlines, deposition dates, discovery cutoffs, review tasks |
| Communications | 30-40 | Attorney-client emails, opposing counsel correspondence |
| Invoices | 8-12 | Monthly outside counsel invoices |
| KPI Assessments | 6 | Guidelines, Budget, Outcomes assessments |
| Budget | 1 | $500K litigation budget with monthly tracking |

**Document Mix**: Contracts from CUAD (annotated), court filings from Caselaw Access, synthetic emails, AI-generated expert reports and memos

### 5.3 Scenario 2: Corporate Transaction — "Atlas Group Acquisition of Horizon Tech"

**Story**: Due diligence for a mid-market technology acquisition. Multiple contract reviews, financial analysis, and regulatory clearance.

**Exercises**: Bulk document analysis, AI chat for due diligence Q&A, semantic search, NDA review, financial document processing

| Entity Type | Records | Details |
|-------------|---------|---------|
| Matter | 1 | M&A transaction, 3 months in |
| Projects | 4 | Due Diligence, Regulatory, Integration Planning, Closing |
| Accounts | 6 | Atlas (acquirer), Horizon (target), advisors, banks |
| Contacts | 15 | Deal team, board members, regulatory contacts |
| Documents | 120-150 | Target company contracts, NDAs, financial statements, regulatory filings, board resolutions |
| Events | 30-40 | Due diligence milestones, regulatory filing dates, closing conditions |
| Communications | 20-30 | Deal team correspondence |
| Invoices | 6-8 | Advisory fees, legal fees |
| Budget | 1 | $2M transaction budget |

**Document Mix**: CUAD contracts (commercial agreements), synthetic NDAs with variations, AI-generated financial summaries, template corporate docs

### 5.4 Scenario 3: Regulatory Compliance — "Q1 2026 Compliance Audit"

**Story**: Quarterly compliance review across multiple business units. Policy documents, audit findings, remediation tracking.

**Exercises**: Knowledge bases, AI semantic search, risk detection, document classification, RAG over policy corpus

| Entity Type | Records | Details |
|-------------|---------|---------|
| Matter | 1 | Compliance audit, recurring quarterly |
| Projects | 3 | Data Privacy, Financial Controls, Employment Compliance |
| Accounts | 3 | Internal business units |
| Contacts | 8 | Compliance officers, auditors, department heads |
| Documents | 60-80 | Policies, audit reports, regulations, remediation plans, checklists |
| Events | 25-35 | Audit milestones, remediation deadlines, review cycles |
| Communications | 15-20 | Audit findings, remediation updates |

**Document Mix**: Federal Register regulations, AI-generated policy documents, synthetic audit reports, template checklists

### 5.5 Scenario 4: Ongoing Matter Management — "Morrison Estate Administration"

**Story**: Estate administration with ongoing task management, beneficiary correspondence, and financial distribution tracking.

**Exercises**: Event/todo management, task chains, email-to-document automation, communication tracking, work assignments

| Entity Type | Records | Details |
|-------------|---------|---------|
| Matter | 1 | Estate administration, 4 months in |
| Projects | 2 | Asset Distribution, Tax Filing |
| Accounts | 2 | Estate, accounting firm |
| Contacts | 10 | Beneficiaries, accountant, court clerk |
| Documents | 40-50 | Will, trust documents, tax filings, correspondence, court orders |
| Events | 35-50 | Filing deadlines, distribution milestones, beneficiary notifications |
| Communications | 25-35 | Beneficiary correspondence, court filings |
| Work Assignments | 8-10 | Paralegal tasks, filing responsibilities |

**Document Mix**: Template estate documents, AI-generated correspondence, synthetic tax forms, court filing templates

### 5.6 Scenario 5: Financial Operations — "Outside Counsel Management Program"

**Story**: Corporate legal department managing multiple outside law firms with invoice review, budget enforcement, and spend analytics.

**Exercises**: Invoice processing, billing events, budget utilization dashboards, spend signals, financial KPIs

| Entity Type | Records | Details |
|-------------|---------|---------|
| Matters | 3 | Cross-referenced from Scenarios 1, 2, 4 |
| Accounts | 5 | Outside law firms |
| Contacts | 8 | Billing partners, AP contacts |
| Invoices | 20-30 | Monthly invoices from multiple firms |
| Billing Events | 100-150 | Line items across all invoices |
| Budgets | 3 | Per-matter budgets |
| Budget Buckets | 12 | Category allocations (discovery, depositions, trial prep, etc.) |
| Spend Snapshots | 18-24 | Monthly snapshots over 6 months |
| Spend Signals | 5-8 | Budget alerts, overage warnings |

**Document Mix**: Template invoices with realistic line items, AI-generated budget reports, synthetic financial summaries

### 5.7 Cross-Scenario Connections

Scenarios are not isolated — they share entities to demonstrate platform breadth:

| Connection | Details |
|------------|---------|
| Scenarios 1 & 5 | Meridian litigation invoices flow into financial operations |
| Scenarios 2 & 5 | Atlas acquisition advisory fees tracked in financial operations |
| Scenarios 1 & 3 | Meridian matter triggers compliance review of discovery handling |
| Scenarios 4 & 5 | Morrison estate legal fees in financial operations |
| All scenarios | Shared contact "Sarah Chen, General Counsel" appears across matters |

---

## 6. AI-Assisted Generation Pipeline

### 6.1 Architecture Overview

```
┌──────────────────────────────────────────────────────────────┐
│                    GENERATION PIPELINE                        │
│                                                              │
│  ┌─────────────┐    ┌──────────────┐    ┌────────────────┐  │
│  │  Scenario    │───>│  Entity      │───>│  Document      │  │
│  │  Narratives  │    │  Generator   │    │  Generator     │  │
│  │  (Claude)    │    │  (Claude +   │    │  (Claude +     │  │
│  │              │    │   Templates) │    │   Open Data +  │  │
│  │              │    │              │    │   Pandoc)      │  │
│  └─────────────┘    └──────┬───────┘    └───────┬────────┘  │
│                            │                     │           │
│                            v                     v           │
│                     ┌──────────────┐    ┌────────────────┐  │
│                     │  Activity    │    │  AI Enrichment │  │
│                     │  Generator   │    │  Generator     │  │
│                     │  (Claude)    │    │  (Claude)      │  │
│                     └──────┬───────┘    └───────┬────────┘  │
│                            │                     │           │
│                            v                     v           │
│                     ┌──────────────────────────────────┐    │
│                     │     JSON Seed Files               │    │
│                     │  (per entity, per scenario)       │    │
│                     └──────────────┬───────────────────┘    │
│                                    │                         │
└────────────────────────────────────┼─────────────────────────┘
                                     │
                                     v
┌──────────────────────────────────────────────────────────────┐
│                     LOADING PIPELINE                          │
│                                                              │
│  ┌────────────────┐  ┌────────────────┐  ┌───────────────┐  │
│  │  Dataverse     │  │  SPE File      │  │  AI Search    │  │
│  │  Web API       │  │  Uploader      │  │  Index        │  │
│  │  Loader        │  │  (via BFF)     │  │  Seeder       │  │
│  └────────────────┘  └────────────────┘  └───────────────┘  │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  Environment Reset Script (idempotent)                │   │
│  └──────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────┘
```

### 6.2 Generation Steps

#### Step 1: Scenario Narrative Expansion (Claude Code)

For each scenario, Claude expands the summary into a detailed narrative with:
- Specific entity names, dates, and amounts
- Timeline of key events
- Document inventory with descriptions
- Relationship map between entities

**Input**: Scenario summary from Section 5
**Output**: `scenarios/{scenario-name}/narrative.json` — structured scenario with all entity references

#### Step 2: Core Business Record Generation (Claude Code + Templates)

Claude generates Dataverse-ready JSON for each entity type:
- Accounts with realistic firm names, addresses, industries
- Contacts with names, titles, email addresses (using `@example.com` per RFC 2606)
- Matters with status progressions, types, dates
- Projects linked to parent matters
- Invoices with realistic amounts and line items
- Budgets with allocations

**Input**: Scenario narrative + entity schema templates
**Output**: `output/dataverse/{entity-name}.json` — Dataverse Web API compatible records

**Key Technique**: Use alternate keys (not GUIDs) for cross-entity references so records are environment-portable. Where Dataverse doesn't support alternate keys for an entity, use a deterministic GUID generation strategy (hash of scenario + entity + sequence).

#### Step 3: Document Content Generation (Claude Code + Open Data + Pandoc)

Three document generation strategies depending on type:

| Strategy | Document Types | Process |
|----------|---------------|---------|
| **Open Data Selection** | Contracts, court filings, regulations | Select from CUAD/Caselaw Access, rename parties to match scenario entities |
| **Template + Variation** | NDAs, employment agreements, corporate docs | Load template, inject scenario-specific names/dates/terms |
| **Full Synthetic** | Emails, memos, expert reports, audit findings | Claude generates full content aligned with scenario narrative |

**Conversion Pipeline**:
```
Claude Markdown → pandoc → DOCX → (optional) LibreOffice → PDF
Claude text → RFC 822 formatter → .eml files
Template XLSX → openpyxl/script → populated XLSX → PDF
```

**Output**: `output/documents/{scenario-name}/{filename}.{ext}` — actual files ready for SPE upload

#### Step 4: AI Enrichment Pre-Baking (Claude Code)

For each generated document, Claude produces the AI profile fields:
- `sprk_filesummary` — 2-4 paragraph summary
- `sprk_filetldr` — 1-2 sentence TL;DR
- `sprk_keywords` — comma-separated keywords
- `sprk_documenttype` — classification (Contract, NDA, Invoice, etc.)
- `sprk_entities` — JSON with organizations, people, dates, amounts, references

**Why pre-bake**: Running the full AI pipeline (Document Intelligence + OpenAI + AI Search indexing) on 500+ documents is expensive and slow. Pre-baking gives instant demo readiness. A subset can optionally be re-processed through the live pipeline for validation.

**Output**: Fields included in the `sprk_document` JSON from Step 2

#### Step 5: Activity Record Generation (Claude Code)

Claude generates temporally coherent activity data:
- Events with realistic progression (Created → Planned → Open → Completed)
- Event logs showing the audit trail
- Communications as email threads (with proper threading via conversationindex)
- KPI assessments with grades and narrative justifications
- Analysis sessions with pre-computed outputs
- Billing events as invoice line items with timekeeper rates and hours

**Output**: `output/dataverse/{activity-entity}.json`

#### Step 6: Search Index Document Generation

Generate Azure AI Search index documents for the knowledge and discovery indexes:
- Extract content chunks from generated documents
- Generate embedding vectors (or placeholder vectors for initial load)
- Create index documents matching the schema in `infrastructure/ai-search/`

**Output**: `output/search-index/{index-name}.json`

### 6.3 AI Generation Prompting Strategy

Each generation step uses a structured prompt pattern:

```
CONTEXT:
- Scenario: {scenario narrative}
- Entity schema: {field definitions with types and constraints}
- Related records: {previously generated records this entity references}
- Conventions: {naming patterns, date ranges, amount ranges}

CONSTRAINTS:
- Use @example.com for all email addresses (RFC 2606)
- Dates within range: 2025-06-01 to 2026-02-28
- Currency amounts in USD, realistic for legal industry
- Status codes must match Dataverse option set values exactly
- Lookup references use alternate keys, not GUIDs

GENERATE:
{count} records for {entity} that tell the story of {scenario summary}

OUTPUT FORMAT:
JSON array matching Dataverse Web API batch format
```

---

## 7. Automation Tooling Strategy

This project leverages five complementary AI/automation tools, each with a distinct role in the pipeline. The goal is to maximize automation while maintaining data quality and repeatability.

### 7.1 Tool Landscape Overview

| Tool | Role in Pipeline | Strengths | Limitations |
|------|-----------------|-----------|-------------|
| **Claude Code** | Content generation, schema authoring, script creation | Deep reasoning, code generation, context-aware content | No direct Dataverse access without MCP |
| **Dataverse MCP Server** | Direct CRUD operations on Dataverse records | Natural language → real records, schema-aware, live validation | Preview status, per-call charges, single-record operations |
| **PAC CLI + CMT** | Bulk data transport (export/import) | Handles relationships, batch operations, proven tooling | Schema.xml setup required, .NET Full Framework dependency |
| **OpenClaw** | Automated dataset harvesting from open sources | Browser automation, form filling, web scraping at scale | Requires self-hosted infrastructure, security considerations |
| **Claude Computer Use** | UI-based validation and edge-case data entry | Sees actual UI, can verify rendering, interact with model-driven apps | 72.5% accuracy on complex tasks, slower than API approaches |

### 7.2 Claude Code — Content Generation Engine

Claude Code is the primary **content generation** tool across all pipeline phases:

**What it does**:
- Generates scenario narratives with internally consistent names, dates, amounts
- Produces Dataverse-ready JSON records conforming to entity schemas
- Creates document content (contracts, memos, emails, reports) aligned with scenario stories
- Pre-bakes AI enrichment fields (summaries, keywords, entities) for each document
- Generates temporally coherent activity data (events, communications, billing)
- Writes and maintains all loader/generator scripts

**How it's used**:
- Interactive sessions for scenario design and review
- Headless mode (`claude -p`) for batch content generation
- Agent teams for parallel scenario generation (one teammate per scenario)
- Subagents for focused tasks (e.g., "generate 20 email threads for Scenario 1")

**Effort guidance**:
| Task | Effort Level |
|------|-------------|
| Scenario narrative expansion | `high` — needs deep reasoning for consistency |
| Entity record generation | `medium` — structured output, constrained by schema |
| Document content generation | `high` — needs legal domain knowledge |
| AI enrichment pre-baking | `medium` — summarization of known content |
| Script creation | `high` — complex PowerShell/API integration |

### 7.3 Dataverse MCP Server — Direct Record Creation

Microsoft's [Dataverse MCP Server](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp) enables Claude Code to directly create and query Dataverse records via natural language, using the Model Context Protocol.

**What it does**:
- CRUD operations on any Dataverse table (`create_record`, `update_record`, `read_query`)
- Schema inspection (list tables, view columns, check option sets)
- Natural language queries converted to FetchXML/OData
- Live validation — immediately see if records are created correctly

**Setup**:
```bash
# Install the Dataverse MCP local proxy
dotnet tool install --global Microsoft.PowerPlatform.Dataverse.MCP

# Configure in .claude/settings.json
{
  "mcpServers": {
    "dataverse": {
      "command": "dataverse-mcp",
      "args": ["--environment", "https://spaarkedev1.crm.dynamics.com"],
      "env": {
        "TENANT_ID": "{tenant-id}"
      }
    }
  }
}
```

**Best use cases in this project**:
- **Interactive record creation** — Claude creates records while maintaining scenario context
- **Validation passes** — after bulk import, query records to verify relationships
- **Small-batch creation** — event types, grid configs, and other reference data (~50 records)
- **Iterative refinement** — create a few records, review in UI, adjust, repeat
- **Schema discovery** — inspect actual table schemas before generating templates

**When NOT to use**:
- Bulk loading 500+ records (too slow for single-record operations; use PAC CLI/CMT)
- Production environments (preview feature, per-call charges apply since Dec 2025)

**Cost note**: Dataverse MCP calls are charged when accessed by non-Copilot Studio agents. Budget for development/testing usage. Use PAC CLI/CMT for repeatable bulk loads.

### 7.4 PAC CLI + Configuration Migration Tool — Bulk Data Transport

The [PAC CLI data commands](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/data) and underlying Configuration Migration Tool (CMT) handle bulk data import/export with relationship resolution.

**What it does**:
- Export data from a source environment as `schema.xml` + `data.xml` (zipped)
- Import data into a target environment with automatic relationship resolution
- Handle lookup references across entities
- Multi-pass import (foundation data first, then dependent data)

**How it fits the pipeline**:

```
┌─────────────────────────────────────────────────────────────┐
│              PAC CLI / CMT Data Flow                         │
│                                                             │
│  1. Claude Code generates JSON seed data                    │
│  2. Transform script converts JSON → CMT schema.xml/data.xml│
│  3. pac data import loads into target Dataverse environment  │
│  4. Dataverse MCP validates records post-import              │
└─────────────────────────────────────────────────────────────┘
```

**schema.xml** defines the entities, fields, and relationships to import:
```xml
<entities>
  <entity name="account" displayname="Account" etc="1"
          primaryidfield="accountid" primarynamefield="name">
    <fields>
      <field displayname="Account Name" name="name" type="string" />
      <field displayname="Industry" name="industrycode" type="optionsetvalue" />
    </fields>
  </entity>
  <entity name="sprk_matter" ...>
    <fields>
      <field displayname="Client" name="sprk_client" type="entityreference"
             lookupType="account" />
    </fields>
  </entity>
</entities>
```

**data.xml** contains the actual record values:
```xml
<entities>
  <entity name="account" displayname="Account">
    <records>
      <record id="{guid}">
        <field name="name" value="Meridian Corporation" />
        <field name="industrycode" value="100000001" />
      </record>
    </records>
  </entity>
</entities>
```

**Key advantages over raw Web API**:
- Automatic relationship resolution (lookups resolved by name/alternate key)
- Multi-pass import handles circular dependencies
- Built-in duplicate detection
- Can export from a "golden" environment and import elsewhere
- Integrates with CI/CD pipelines

**Implementation approach**:
1. Claude Code generates entity JSON from scenario narratives
2. A PowerShell transform script converts JSON → CMT `schema.xml` + `data.xml`
3. `pac data import --data ./output/data.zip` loads records into Dataverse
4. Post-import validation via Dataverse MCP or PowerShell queries

**Limitation**: `pac data` plugin was dropped from Microsoft-hosted agents (2025). For CI/CD automation, call the CMT executables directly via PowerShell, or use the [custom .NET CLI tool](https://github.com/dotnetprog/dataverse-configuration-migration-tool).

### 7.5 OpenClaw — Automated Dataset Harvesting

[OpenClaw](https://openclaw.ai/) is an open-source autonomous AI agent with built-in browser automation that can scrape websites, fill forms, and navigate complex web UIs at machine speed.

**What it does**:
- Automated web scraping with AI-powered page understanding
- Form filling and multi-step navigation
- Data extraction to CSV/JSON from any website
- Runs locally (your infrastructure, your keys, your data)

**How it fits the pipeline — Dataset Harvesting**:

| Source | OpenClaw Task | Output |
|--------|--------------|--------|
| **CUAD Dataset** | Navigate to Atticus Project site, download contract PDFs, extract metadata | `sources/cuad/` directory with organized contracts |
| **Kaggle Legal-Clause-Dataset** | Authenticate, download dataset, extract and categorize 150K clauses | `sources/kaggle-clauses/` with categorized clause CSV |
| **Caselaw Access Project** | Query API for specific case types/jurisdictions, download opinion PDFs | `sources/caselaw/` with curated opinions |
| **TermScout Database** | Navigate contract listings, extract structured clause data points | `sources/termscout/` with benchmark data |
| **Federal Register** | Search for relevant regulations, download as PDF/XML | `sources/regulations/` with regulatory text |
| **Open template repositories** | Crawl GitHub/legal template sites, download NDA/MSA/employment templates | `sources/templates/` with categorized templates |

**Setup**:
```bash
# OpenClaw runs self-hosted (Node.js + browser runtime)
git clone https://github.com/pspdfkit/openclaw.git
cd openclaw && npm install

# Configure with Claude API key (or other LLM)
cp .env.example .env
# Edit .env with ANTHROPIC_API_KEY or OPENAI_API_KEY
```

**Example harvesting task** (CUAD dataset):
```
Download the CUAD v1 contract dataset from the Atticus Project.
For each contract PDF:
1. Save to sources/cuad/contracts/{filename}.pdf
2. Extract metadata (parties, contract type, date) to a CSV row
3. Note which of the 41 CUAD annotation types are present

Output a manifest.csv with columns: filename, parties, contract_type,
date, annotated_clauses, file_size_kb
```

**Security considerations**:
- Run in an isolated environment (VM or container) — OpenClaw has browser access
- Review downloaded content before incorporating into demo data
- Respect robots.txt and rate limits on source websites
- Log all scraping activity for provenance tracking

### 7.6 Claude Computer Use — UI Validation & Edge Cases

[Claude Computer Use](https://platform.claude.com/docs/en/agents-and-tools/tool-use/computer-use-tool) enables Claude to interact with live application UIs — taking screenshots, clicking buttons, filling forms, and navigating pages.

**What it does**:
- Views and interacts with model-driven app forms in a browser
- Can create/edit records through the Dataverse UI (not just the API)
- Validates that loaded data renders correctly in the actual application
- Fills in fields that are only accessible through the UI (e.g., business process flows, custom form scripts)

**Best use cases in this project**:

| Use Case | Why Computer Use | Alternative |
|----------|-----------------|-------------|
| **Post-load validation** | Verify records display correctly in model-driven app forms | Manual inspection |
| **Business process flow advancement** | BPF stage transitions may require UI interaction | Plugin/workflow automation |
| **Form-script-dependent fields** | Some fields are set by form OnLoad/OnChange scripts | Not available via API |
| **Screenshot documentation** | Capture "before/after" screenshots of populated demo environment | Manual screenshots |
| **Edge case data entry** | Records that need specific UI interaction patterns (grids, subgrids, related records) | Complex API scripting |

**Setup** (via Anthropic API):
```python
# Claude Computer Use runs as an API-based agent
# Typically paired with a VNC or browser automation framework
import anthropic

client = anthropic.Anthropic()
response = client.messages.create(
    model="claude-sonnet-4-6",
    max_tokens=4096,
    tools=[{
        "type": "computer_20250124",
        "name": "computer",
        "display_width_px": 1920,
        "display_height_px": 1080
    }],
    messages=[{
        "role": "user",
        "content": "Open the Spaarke dev environment, navigate to the Meridian v. Pinnacle matter, and verify the document grid shows 80+ documents"
    }]
)
```

**Practical approach**: Use Claude Computer Use as a **validation layer**, not the primary data creation method. It's slower than API-based approaches but catches rendering issues and UI-specific problems that API loading can't detect.

**Current capability**: Anthropic's Sonnet models now achieve 72.5% on OSWorld benchmarks (up from <15% in late 2024). Good enough for structured validation tasks, not yet reliable enough for bulk data entry.

### 7.7 Microsoft Power Platform Skills for Claude Code

Microsoft has released an official [Power Platform Skills plugin marketplace](https://github.com/microsoft/power-platform-skills) for Claude Code and GitHub Copilot. This provides:

- Power Pages site creation and deployment
- Model-driven app generative pages
- Dataverse-aware code generation

**Relevance to this project**: May provide additional Dataverse tooling as the marketplace matures. Monitor for data migration or seed data skills. Currently focused on Power Pages SPA development.

### 7.8 Recommended Automation Mix by Pipeline Phase

| Pipeline Phase | Primary Tool | Secondary Tool | Validation Tool |
|----------------|-------------|----------------|-----------------|
| **1. Dataset Harvesting** | OpenClaw (scraping) | Claude Code (API-based downloads) | Manual review |
| **2. Scenario Narrative** | Claude Code | — | Human review |
| **3. Entity Record Generation** | Claude Code (JSON output) | — | Schema validation script |
| **4. Document Content** | Claude Code + Open Data | OpenClaw (template harvesting) | Human spot-check |
| **5. AI Enrichment** | Claude Code (pre-bake) | — | Comparison with live pipeline |
| **6. Activity Data** | Claude Code | — | Temporal consistency check |
| **7. Bulk Data Loading** | PAC CLI / CMT | Dataverse Web API (fallback) | Dataverse MCP (query verification) |
| **8. File Upload** | PowerShell via BFF API | — | SPE container listing |
| **9. Search Index** | PowerShell via AI Search API | — | Search query validation |
| **10. UI Validation** | Claude Computer Use | — | Human walkthrough |
| **11. Environment Reset** | PAC CLI + PowerShell | — | Record count verification |

### 7.9 Tool Integration Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    FULL AUTOMATION PIPELINE                       │
│                                                                  │
│  ┌──────────┐   ┌──────────┐   ┌───────────┐   ┌───────────┐  │
│  │ OpenClaw │   │  Claude   │   │  Claude   │   │  Claude   │  │
│  │ Dataset  │──>│  Code     │──>│  Code     │──>│  Code     │  │
│  │ Harvest  │   │ Generate  │   │ Enrich    │   │ Generate  │  │
│  │          │   │ Records   │   │ Documents │   │ Activity  │  │
│  └──────────┘   └─────┬─────┘   └─────┬─────┘   └─────┬─────┘  │
│                       │               │               │          │
│       ┌───────────────┴───────────────┴───────────────┘          │
│       │                                                          │
│       v                                                          │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │              JSON / XML Seed Files                       │    │
│  │  (entity records, document content, activity data)       │    │
│  └──────────────┬──────────────┬──────────────┬────────────┘    │
│                 │              │              │                   │
│                 v              v              v                   │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────┐         │
│  │  PAC CLI /   │  │  BFF API     │  │  AI Search    │         │
│  │  CMT Import  │  │  SPE Upload  │  │  REST API     │         │
│  │  (Dataverse) │  │  (Files)     │  │  (Indexes)    │         │
│  └──────┬───────┘  └──────┬───────┘  └───────┬───────┘         │
│         │                 │                   │                   │
│         └─────────────────┼───────────────────┘                  │
│                           │                                      │
│                           v                                      │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │         Dataverse MCP + Claude Computer Use              │    │
│  │              (Validation & Verification)                  │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
```

---

## 8. CLI Tool Architecture

### 8.1 Tool Identity

| Property | Value |
|----------|-------|
| **Name** | `spaarke-data` |
| **Type** | CLI application |
| **Runtime** | Node.js (TypeScript) — aligns with existing PCF/React toolchain |
| **Distribution** | npm global package (`npm install -g @spaarke/data-cli`) |
| **Repository** | `spaarke-data-cli` (GitHub, standalone) |
| **Config format** | YAML for scenarios, JSON for entity schemas |

**Why Node.js/TypeScript** (not .NET):
- Sales engineers and onboarding team already have Node.js installed (PCF development)
- TypeScript gives strong typing for entity schemas without .NET SDK dependency
- npm distribution is simpler than dotnet tool packaging
- Can directly leverage Claude API for generation steps
- JSON/YAML handling is more natural in Node.js than .NET

### 8.2 Command Structure

```bash
# ─── GENERATE ────────────────────────────────────────────────────
# Generate seed data from a scenario definition
spaarke-data generate --scenario meridian-v-pinnacle
spaarke-data generate --scenario all
spaarke-data generate --scenario custom --config ./my-scenario.yaml

# Generate with volume control
spaarke-data generate --scenario all --volume light    # ~100 docs
spaarke-data generate --scenario all --volume full     # ~500+ docs

# Generate only specific layers
spaarke-data generate --scenario meridian-v-pinnacle --layer records
spaarke-data generate --scenario meridian-v-pinnacle --layer documents
spaarke-data generate --scenario meridian-v-pinnacle --layer activity

# ─── LOAD ────────────────────────────────────────────────────────
# Load generated data into a target environment
spaarke-data load --target https://spaarkedev1.crm.dynamics.com --data ./output/
spaarke-data load --target dev                          # uses named environment
spaarke-data load --target dev --layer records           # Dataverse records only
spaarke-data load --target dev --layer files              # SPE files only
spaarke-data load --target dev --layer search-index       # AI Search only

# ─── VALIDATE ────────────────────────────────────────────────────
# Validate loaded data against expectations
spaarke-data validate --target dev
spaarke-data validate --target dev --scenario meridian-v-pinnacle
spaarke-data validate --target dev --verbose              # detailed per-entity report

# ─── RESET ───────────────────────────────────────────────────────
# Reset environment (wipe + reload)
spaarke-data reset --target dev --confirm
spaarke-data reset --target dev --scenario meridian-v-pinnacle --confirm

# ─── SCHEMA ──────────────────────────────────────────────────────
# Inspect and export Dataverse schema (via MCP or Web API)
spaarke-data schema export --target dev --output ./schemas/
spaarke-data schema diff --source ./schemas/ --target dev   # detect drift

# ─── HARVEST ─────────────────────────────────────────────────────
# Download open datasets (orchestrates OpenClaw or direct downloads)
spaarke-data harvest cuad --output ./sources/cuad/
spaarke-data harvest caselaw --query "patent infringement" --output ./sources/caselaw/
spaarke-data harvest all --output ./sources/

# ─── ONBOARD ─────────────────────────────────────────────────────
# Customer onboarding mode — import from external data sources
spaarke-data onboard --source ./customer-export.csv --mapping ./mappings/customer-x.yaml --target prod
spaarke-data onboard --source ./customer-matters.xlsx --mapping auto --target staging
```

### 8.3 Plugin Architecture

The tool uses a plugin/adapter pattern to separate concerns:

```
┌──────────────────────────────────────────────────────────────────┐
│                      spaarke-data CLI                             │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │                    Command Layer                             │ │
│  │  generate | load | validate | reset | schema | harvest | onboard │
│  └──────────────────────┬──────────────────────────────────────┘ │
│                         │                                        │
│  ┌──────────────────────┴──────────────────────────────────────┐ │
│  │                  Core Pipeline Engine                        │ │
│  │  Scenario → Source Adapters → Transformers → Loaders         │ │
│  └──────────────────────┬──────────────────────────────────────┘ │
│                         │                                        │
│  ┌──────────┬───────────┼───────────┬──────────────────────────┐ │
│  │          │           │           │                          │ │
│  │  Source   │  Transform │  Loader   │  Validator              │ │
│  │  Adapters │  Plugins   │  Plugins  │  Plugins               │ │
│  │          │           │           │                          │ │
│  │ • claude  │ • entity   │ • cmt     │ • mcp-query            │ │
│  │ • csv     │ • document │ • web-api │ • record-count         │ │
│  │ • excel   │ • activity │ • spe     │ • relationship-check   │ │
│  │ • cuad    │ • enrichment│ • search  │ • computer-use (opt)  │ │
│  │ • caselaw │ • search-idx│          │                        │ │
│  │ • openclaw│           │           │                          │ │
│  └──────────┴───────────┴───────────┴──────────────────────────┘ │
│                                                                  │
│  ┌─────────────────────────────────────────────────────────────┐ │
│  │              Configuration Layer                             │ │
│  │  environments.yaml | scenarios/*.yaml | schemas/*.json       │ │
│  └─────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

#### Source Adapters

Source adapters normalize data from different origins into a common internal format:

| Adapter | Input | Use Case |
|---------|-------|----------|
| `claude` | Scenario YAML + entity schemas → Claude API → JSON | Demo data generation (synthetic) |
| `csv` | CSV files with column mappings | Customer data import |
| `excel` | XLSX files with sheet/column mappings | Customer data import |
| `cuad` | CUAD dataset directory → parsed contracts | Open legal dataset |
| `caselaw` | Caselaw Access API → downloaded opinions | Open legal dataset |
| `openclaw` | OpenClaw browser automation → scraped data | Automated dataset harvesting |
| `cmt-export` | CMT data.zip → parsed entities | Environment snapshot/clone |

#### Transform Plugins

Transform plugins map source data to Spaarke entity schemas:

| Plugin | Function |
|--------|----------|
| `entity` | Maps source fields → Dataverse entity fields (with type coercion, option set resolution) |
| `document` | Generates document records with AI enrichment fields |
| `activity` | Generates temporally coherent events, communications, billing |
| `enrichment` | Pre-bakes AI profiles (summaries, keywords, entities) via Claude API |
| `search-index` | Generates Azure AI Search index documents with embeddings |

#### Loader Plugins

Loader plugins write data to target systems:

| Plugin | Target | Method |
|--------|--------|--------|
| `cmt` | Dataverse | PAC CLI / Configuration Migration Tool (bulk) |
| `web-api` | Dataverse | REST API with upsert (targeted operations) |
| `spe` | SharePoint Embedded | BFF API file upload |
| `search` | Azure AI Search | REST API batch upload |

#### Validator Plugins

| Plugin | Method |
|--------|--------|
| `mcp-query` | Dataverse MCP Server natural language queries |
| `record-count` | Web API count queries per entity |
| `relationship-check` | Verify lookup references resolve correctly |
| `computer-use` | Claude Computer Use UI screenshots (optional) |

### 8.4 Configuration-Driven Scenarios

Scenarios are defined in YAML, not code. Adding a new demo scenario means creating a config file:

```yaml
# scenarios/meridian-v-pinnacle.yaml
name: "Meridian Corp v. Pinnacle Industries"
type: litigation
timeframe:
  start: 2025-08-15
  end: 2026-02-28

entities:
  matters:
    - id: scenario-1-matter-001
      name: "Meridian Corp v. Pinnacle Industries"
      type: litigation
      status: active
      opened: 2025-08-15
      client: scenario-1-account-meridian

  accounts:
    - id: scenario-1-account-meridian
      name: "Meridian Corporation"
      type: corporate-client
      industry: manufacturing
    - id: scenario-1-account-pinnacle
      name: "Pinnacle Industries"
      type: opposing-party
      industry: technology

  contacts:
    - id: scenario-1-contact-chen
      name: "Sarah Chen"
      title: "General Counsel"
      account: scenario-1-account-meridian
      email: sarah.chen@example.com
      shared: true  # appears in other scenarios too

  documents:
    count: 80-100
    mix:
      contracts:
        source: cuad
        count: 30
        filter: { type: ["MSA", "NDA", "License"] }
        rename_parties:
          - from: "{original_party_1}"
            to: "Meridian Corporation"
          - from: "{original_party_2}"
            to: "Pinnacle Industries"
      emails:
        source: claude
        count: 30
        template: litigation-correspondence
      court_filings:
        source: caselaw
        count: 15
        filter: { type: "patent-infringement" }
      memos:
        source: claude
        count: 10
        template: legal-memo

  events:
    count: 40-60
    types: [filing-deadline, deposition, discovery-cutoff, review-task]

  invoices:
    count: 8-12
    firms: [scenario-1-account-lawfirm-a, scenario-1-account-lawfirm-b]
    rate_range: [250, 750]  # hourly rates

  budget:
    total: 500000
    period: monthly
    buckets: [discovery, depositions, expert-analysis, trial-prep]
```

**Customer onboarding** uses a mapping file instead of a scenario:

```yaml
# mappings/customer-acme.yaml
name: "Acme Legal Department Onboarding"
source_type: excel
source_format:
  matters:
    sheet: "Active Matters"
    columns:
      name: "Matter Name"
      type: "Practice Area"
      status: "Status"
      opened: "Open Date"
      client: "Client Name"  # resolved to account lookup

  contacts:
    sheet: "Contacts"
    columns:
      name: "Full Name"
      email: "Email"
      title: "Job Title"
      firm: "Organization"  # resolved to account lookup

transform:
  type_mapping:
    "IP Litigation": litigation
    "Corporate Transaction": transaction
    "Regulatory": compliance
  status_mapping:
    "Open": active
    "Closed": inactive
    "Pending": draft
```

### 8.5 Repository Structure (`spaarke-data-cli`)

```
spaarke-data-cli/
├── README.md
├── package.json
├── tsconfig.json
├── .github/workflows/                # CI/CD
│
├── src/
│   ├── cli/                          # Command handlers
│   │   ├── generate.ts
│   │   ├── load.ts
│   │   ├── validate.ts
│   │   ├── reset.ts
│   │   ├── schema.ts
│   │   ├── harvest.ts
│   │   └── onboard.ts
│   │
│   ├── core/                         # Pipeline engine
│   │   ├── pipeline.ts               # Orchestrates source → transform → load
│   │   ├── scenario-loader.ts        # Parses scenario YAML
│   │   └── schema-registry.ts        # Entity schema management
│   │
│   ├── adapters/                     # Source adapters
│   │   ├── claude-adapter.ts         # AI content generation via Claude API
│   │   ├── csv-adapter.ts            # CSV file import
│   │   ├── excel-adapter.ts          # XLSX file import
│   │   ├── cuad-adapter.ts           # CUAD dataset parser
│   │   ├── caselaw-adapter.ts        # Caselaw Access API client
│   │   ├── openclaw-adapter.ts       # OpenClaw browser automation orchestrator
│   │   └── cmt-export-adapter.ts     # CMT data.zip parser
│   │
│   ├── transforms/                   # Data transformers
│   │   ├── entity-transform.ts       # Generic entity field mapping
│   │   ├── document-transform.ts     # Document records + AI enrichment
│   │   ├── activity-transform.ts     # Events, communications, billing
│   │   ├── enrichment-transform.ts   # AI profile pre-baking
│   │   └── search-index-transform.ts # AI Search document generation
│   │
│   ├── loaders/                      # Target loaders
│   │   ├── cmt-loader.ts             # PAC CLI / CMT import
│   │   ├── webapi-loader.ts          # Dataverse Web API upsert
│   │   ├── spe-loader.ts             # SPE file upload via BFF API
│   │   └── search-loader.ts          # Azure AI Search REST API
│   │
│   ├── validators/                   # Post-load validation
│   │   ├── mcp-validator.ts          # Dataverse MCP queries
│   │   ├── count-validator.ts        # Record count checks
│   │   ├── relationship-validator.ts # Lookup integrity
│   │   └── computer-use-validator.ts # Claude Computer Use (optional)
│   │
│   └── types/                        # Shared TypeScript types
│       ├── scenario.ts               # Scenario YAML schema
│       ├── entity.ts                 # Entity definitions
│       └── config.ts                 # Environment config
│
├── config/
│   ├── environments.yaml             # Named environments (dev, staging, prod)
│   └── defaults.yaml                 # Default generation settings
│
├── scenarios/                        # Built-in demo scenarios
│   ├── meridian-v-pinnacle.yaml
│   ├── atlas-horizon-acquisition.yaml
│   ├── compliance-audit.yaml
│   ├── morrison-estate.yaml
│   ├── outside-counsel-management.yaml
│   └── _shared-entities.yaml         # Cross-scenario shared contacts, firms
│
├── schemas/                          # Dataverse entity schemas (exported or hand-authored)
│   ├── account.schema.json
│   ├── contact.schema.json
│   ├── sprk_matter.schema.json
│   ├── sprk_document.schema.json
│   ├── sprk_event.schema.json
│   └── ...
│
├── templates/                        # Document content templates
│   ├── contracts/
│   ├── ndas/
│   ├── emails/
│   ├── memos/
│   └── invoices/
│
├── mappings/                         # Customer onboarding mapping templates
│   ├── _template.yaml                # Blank mapping template
│   └── examples/
│       ├── csv-matters.yaml
│       └── excel-full-export.yaml
│
├── sources/                          # Downloaded open datasets (gitignored, large)
│   ├── .gitignore
│   ├── cuad/
│   ├── caselaw/
│   └── templates/
│
├── output/                           # Generated data (gitignored)
│   └── .gitignore
│
├── tests/                            # Unit and integration tests
│   ├── adapters/
│   ├── transforms/
│   ├── loaders/
│   └── fixtures/
│
└── docs/
    ├── getting-started.md
    ├── adding-scenarios.md
    ├── customer-onboarding.md
    └── DATA-PROVENANCE.md            # Licensing for all data sources
```

### 8.6 Relationship to Product Repository

```
┌────────────────────────────┐     ┌────────────────────────────┐
│    spaarke (product repo)   │     │  spaarke-data-cli (tool)   │
│                            │     │                            │
│  src/server/api/           │◄────│  loaders/spe-loader.ts     │
│    (BFF API endpoints)     │     │    (calls BFF API for      │
│                            │     │     file upload)           │
│  src/solutions/            │     │                            │
│    (entity definitions)    │────►│  schemas/*.schema.json     │
│                            │     │    (exported/synced)       │
│  scripts/seed-data/        │     │                            │
│    (AI config seed data)   │────►│  built-in reference data   │
│                            │     │                            │
│  infrastructure/ai-search/ │────►│  search-index-transform.ts │
│    (index schemas)         │     │    (index doc generation)  │
│                            │     │                            │
│  projects/spaarke-demo-    │     │                            │
│   data-setup-r1/           │     │                            │
│    (THIS planning project) │────►│  (implementation target)   │
└────────────────────────────┘     └────────────────────────────┘
```

The data CLI **consumes** product repo artifacts (BFF API, entity schemas, index schemas) but does not live inside it. Schema sync can be automated: `spaarke-data schema export --target dev` pulls live schemas from Dataverse.

### 8.7 Progressive Integration Strategy

Product repo artifacts are **not** copied into the data CLI repo upfront. They are added progressively as each component is built, using the live Dataverse environment as the source of truth wherever possible.

| Product Artifact | When to Add | How to Integrate | Source of Truth |
|-----------------|-------------|-----------------|-----------------|
| **Entity schemas** (fields, types, option sets) | Phase 1 — when building `schema export` command | `spaarke-data schema export --target dev` pulls live schemas | **Live Dataverse** (not source code) |
| **AI Search index schemas** | Phase 1 — when building `search-loader` | Copy 5-6 JSON files from `infrastructure/ai-search/` | Product repo (stable, rarely changes) |
| **BFF API endpoint contracts** | Phase 1 — when building `spe-loader` | Define TypeScript interfaces for HTTP contract (URL patterns, request/response) | Product repo API source (derive, don't copy) |
| **Seed data JSON** (playbooks, actions, skills, tools) | Phase 1 — as reference data fixtures | Either call existing deploy scripts or include JSON as built-in fixtures | Product repo `scripts/seed-data/` |
| **Data model docs** | Never copied | Developers read from product repo as reference while building adapters | Product repo `docs/data-model/` |
| **Entity definitions** (solution XML) | Never copied | Schema export from Dataverse is always more current | Live Dataverse |

**Why progressive, not upfront**:
- **Avoids stale copies** — Entity schemas change as the product evolves; exporting from live Dataverse is always current
- **Keeps the repo clean** — Only commit what you actually use; no dead weight
- **Forces the right abstractions** — When you need an entity schema, you build the `schema export` command first; that's the tool's value proposition
- **Design doc has the inventory** — Sections 3.1-3.3 document every entity, field, and relationship; that's the map for building adapters and transforms

**Schema sync workflow** (after initial export):
```bash
# Export current schemas from Dataverse
spaarke-data schema export --target dev --output ./schemas/

# Detect drift (schemas changed since last export?)
spaarke-data schema diff --source ./schemas/ --target dev

# If drift detected, re-export and review changes
spaarke-data schema export --target dev --output ./schemas/
git diff schemas/  # review what changed
git add schemas/ && git commit -m "sync: update entity schemas from dev environment"
```

---

## 9. Data Loading Infrastructure

### 9.1 Primary: PAC CLI / Configuration Migration Tool

The primary loading mechanism uses PAC CLI with the Configuration Migration Tool for bulk Dataverse record import.

**Workflow**:
1. Claude Code generates JSON seed data (per entity, per scenario)
2. `Convert-JsonToCmt.ps1` transforms JSON → CMT `schema.xml` + `data.xml` → `data.zip`
3. `pac data import --data ./output/data.zip --environment https://spaarkedev1.crm.dynamics.com` loads all records
4. CMT handles multi-pass import (foundation entities first, then dependents)

**Dependency order** (handled automatically by CMT, but documented for manual fallback):
  1. Accounts → Contacts → Matters → Projects → Budgets
  2. Documents (Dataverse records only, no files yet)
  3. Invoices → Billing Events → Spend Snapshots
  4. Events → Event Logs → Communications
  5. KPI Assessments → Analysis Sessions → Analysis Outputs
  6. Work Assignments

**Fallback**: If PAC CLI `data` commands are unavailable (deprecated plugin issue), use the CMT executables directly:
```powershell
# Direct CMT invocation
& "$env:LOCALAPPDATA\Microsoft\AppData\DataMigration\DataMigrationUtility.exe" `
    /schemaFile:"output/schema.xml" `
    /dataFile:"output/data.zip" `
    /connectionString:"AuthType=OAuth;Url=https://spaarkedev1.crm.dynamics.com;..."
```

Or use the [custom .NET CLI tool](https://github.com/dotnetprog/dataverse-configuration-migration-tool):
```bash
dotnet tool install --global Dataverse.ConfigurationMigration.Tool
dv-config-migration import --data output/data.xml --schema output/schema.xml
```

### 9.2 Secondary: Dataverse Web API (Targeted Operations)

For operations where CMT is overkill or doesn't fit (single-record upserts, post-import patches):

- **Authentication**: Use existing PAC CLI auth token or Azure AD app registration
- **Operation**: Upsert using alternate keys where available
- **Batch**: Dataverse batch API (`$batch`) for up to 1000 operations per batch
- **Idempotent**: Upsert ensures re-running doesn't create duplicates

### 9.3 Validation: Dataverse MCP Server

After bulk import, use the Dataverse MCP Server to validate:
```
# Example validation queries via Claude Code + MCP
"List all sprk_matter records and verify 5 matters exist across all scenarios"
"Check that all sprk_document records for matter 'Meridian v. Pinnacle' have non-null sprk_filesummary"
"Verify sprk_event records link to the correct regarding records"
```

### 9.4 SPE Document Uploader

PowerShell script using BFF API endpoints:

1. Authenticate to BFF API
2. Get or create SPE container for each matter
3. Upload files via `PUT /api/drives/{driveId}/upload`
4. Capture returned `graphitemid` and `graphdriveid`
5. Update corresponding `sprk_document` records with storage references (via Web API patch)
6. Set document status to Active

### 9.5 AI Search Index Seeder

PowerShell or Python script using Azure AI Search REST API:

1. Clear existing index documents (optional, for reset)
2. Upload index documents in batches of 1000
3. Verify document count matches expectations

### 9.6 Environment Reset Script

`Reset-DemoEnvironment.ps1` — single command to wipe and repopulate:

```powershell
# Phase 1: Delete (reverse dependency order)
# - Analysis outputs → Analysis sessions
# - Event logs → Events → Communications
# - Billing events → Invoices → Spend snapshots
# - Documents (Dataverse records)
# - SPE container contents
# - Projects → Matters → Budgets
# - Contacts → Accounts (preserve system accounts)
# - AI Search index documents

# Phase 2: Reload (forward dependency order)
# - PAC CLI / CMT import for all Dataverse records (Section 9.1)
# - BFF API file upload (Section 9.4)
# - AI Search index seeding (Section 9.5)

# Phase 3: Validate
# - Dataverse MCP queries to verify record counts and relationships
# - Optional: Claude Computer Use to spot-check UI rendering
```

**Safety**: Requires explicit `--confirm` flag. Validates target environment URL before proceeding. Never runs against production.

---

## 10. Project Artifacts — What Lives Where

Two repositories are involved in this project:

| Artifact | Location | Purpose |
|----------|----------|---------|
| **This design document** | `spaarke/projects/spaarke-demo-data-setup-r1/design.md` | Planning phase |
| **spec.md** | `spaarke/projects/spaarke-demo-data-setup-r1/spec.md` | AI-optimized specification |
| **plan.md, tasks/** | `spaarke/projects/spaarke-demo-data-setup-r1/` | Task decomposition and tracking |
| **CLI tool source code** | `spaarke-data-cli/src/` | Implementation (new repo) |
| **Scenario definitions** | `spaarke-data-cli/scenarios/` | YAML scenario configs |
| **Entity schemas** | `spaarke-data-cli/schemas/` | Exported/synced from Dataverse |
| **Document templates** | `spaarke-data-cli/templates/` | Content templates for generation |
| **Customer mappings** | `spaarke-data-cli/mappings/` | Onboarding data mapping configs |
| **Open datasets** | `spaarke-data-cli/sources/` (gitignored) | Downloaded CUAD, Caselaw, etc. |
| **Generated output** | `spaarke-data-cli/output/` (gitignored) | Seed data ready for loading |

See Section 8.5 for the full `spaarke-data-cli` repository structure.

---

## 11. Technical Decisions

### 11.1 Dataverse Loading: PAC CLI / CMT as Primary, Web API as Fallback

**Decision**: Use PAC CLI with Configuration Migration Tool for bulk data import. Use Dataverse Web API for targeted operations and post-import patches. Use Dataverse MCP Server for validation.

**Rationale**:
- CMT handles relationship resolution automatically (multi-pass import)
- Scriptable and repeatable via PowerShell
- Built-in duplicate detection
- `schema.xml` is version-controllable and environment-portable
- Fallback to Web API for operations CMT can't handle (e.g., SPE document record patching after file upload)
- MCP Server provides natural language validation without writing FetchXML queries
- Existing patterns in `scripts/` (e.g., `test-dataverse-connection.cs`, seed data deployment scripts)

**Alternatives considered**:
- Solution with data — rejected because GUIDs are brittle across environments
- Dataverse MCP only — too slow for 500+ records (single-record operations)
- Web API only — requires manual relationship ordering that CMT handles automatically

### 11.2 Document Generation: Open Data + Templates + Synthetic

**Decision**: Three-tier approach combining real open data, parameterized templates, and AI-generated synthetic content.

**Rationale**:
- CUAD contracts provide real-world complexity and clause annotations that no synthetic data can match
- Templates ensure controlled variations for specific demo points (e.g., "show two NDAs with different liability caps")
- Synthetic content fills gaps (emails, memos, reports) where no open dataset exists
- All three tiers are IP-clean

### 11.3 AI Enrichment: Pre-Bake with Optional Re-Processing

**Decision**: Generate AI profile fields (summaries, keywords, entities) during data generation, not by running the live AI pipeline.

**Rationale**:
- Running Document Intelligence + OpenAI on 500+ documents would cost ~$50-100+ in API calls per environment reset
- Pre-baked data loads in seconds, not hours
- Summaries are more consistent when generated alongside the document content
- A subset (10-20 documents) can be left un-enriched to demonstrate the live pipeline in demos

### 11.4 File Format Conversion

**Decision**: Use pandoc for Markdown→DOCX, LibreOffice CLI for DOCX→PDF, direct text generation for .eml.

**Rationale**:
- Pandoc is widely available, fast, and produces clean DOCX
- LibreOffice CLI (`soffice --headless --convert-to pdf`) handles DOCX→PDF reliably
- .eml files are plain text (RFC 822) — no conversion tool needed
- XLSX generation via template copy + cell value injection (openpyxl or PowerShell COM)

### 11.5 Environment Portability

**Decision**: Use alternate keys and deterministic identifiers instead of hardcoded GUIDs.

**Rationale**:
- Demo data must work across Dev, Staging, and customer demo environments
- Alternate keys (e.g., `scenario-1-matter-001`) resolve to environment-specific GUIDs at load time
- Document-to-file linkage uses file paths, resolved during upload

---

### 11.6 Dataset Harvesting: OpenClaw for Automation, Manual for Fallback

**Decision**: Use OpenClaw for automated harvesting of open legal datasets. Fall back to manual download for sources that require authentication or have anti-scraping measures.

**Rationale**:
- CUAD, Kaggle, and Caselaw Access all have publicly accessible download endpoints
- OpenClaw's browser automation handles multi-step downloads (navigate → filter → download → organize)
- Automated harvesting is repeatable — re-run when sources update
- Manual fallback ensures no blocking dependency on OpenClaw availability

**Security**: Run OpenClaw in an isolated container. Review all harvested content before incorporation.

### 11.7 UI Validation: Claude Computer Use for Post-Load Checks

**Decision**: Use Claude Computer Use as an optional validation layer after data loading. Not required for MVP.

**Rationale**:
- 72.5% accuracy on OSWorld is sufficient for structured validation tasks (check presence, count records, verify rendering)
- Catches issues that API-level validation misses (form script errors, subgrid rendering, BPF stage display)
- Not reliable enough for primary data entry (too slow, occasional misclicks)
- Screenshots captured during validation serve as demo documentation

---

## 12. Scope & Phasing

### Phasing Philosophy: Data-First, Then Tooling

The phasing prioritizes **getting demo data into Dataverse fast** over building a polished CLI tool. The rationale:

- Building CLI infrastructure (pipeline engine, adapters, transforms, loaders) before loading any data risks spending weeks before discovering the scenario designs don't work in the UI, entity relationships need adjusting, or loading order is wrong
- Existing PowerShell patterns in the product repo (`Invoke-DataverseApi.ps1`, bearer token auth, Web API upsert) already solve the loading problem for a single scenario
- Phase 0 scripts become test fixtures and reference implementations for the CLI commands built in Phase 1

Each phase is a separate project in `spaarke-data-cli/projects/` with its own `spec.md`, plan, and tasks.

### Phase 0: "Data in Dataverse" Sprint (3-5 days)

**Goal**: One complete demo scenario (Meridian v. Pinnacle) loaded into dev — no CLI infrastructure needed.

**Approach**: Use Claude Code interactively to generate structured JSON data files, then PowerShell scripts (reusing existing product repo patterns) to load via Dataverse Web API and BFF API.

| Deliverable | Details | Primary Tool |
|-------------|---------|-------------|
| Scenario 1 data design | Detailed JSON for all entities in Meridian v. Pinnacle | Claude Code (interactive) |
| Layer 1-2: Core business records | Accounts → Contacts → Matters → Projects → Budgets → Invoices loaded via Web API | PowerShell + Web API |
| Layer 3: Document records | `sprk_document` records with pre-baked AI enrichment fields | PowerShell + Web API |
| Layer 4: Actual files | 20-30 PDFs/DOCX uploaded to SPE via BFF API, linked to document records | PowerShell + BFF API |
| Layer 5: Activity records | Events, communications, billing events, KPI assessments | PowerShell + Web API |
| Loading scripts | Idempotent PowerShell scripts in `scripts/phase0/` | PowerShell |
| Validation | Manual spot-check + basic FetchXML count queries | Dataverse UI + PAC CLI |
| DATA-PROVENANCE.md | Licensing documentation for CUAD and any open data used | Markdown |

**Entity volumes for Scenario 1**:
- 1 matter, 3 projects, 4 accounts, 12 contacts
- 50-80 document records, 20-30 actual files
- 40-60 events, 30-40 communications, 8-12 invoices, 6 KPI assessments
- 1 budget with monthly spend snapshots

**Key constraint**: All loading scripts must be idempotent (re-runnable without creating duplicates) using alternate keys or deterministic identifiers.

**What we learn**: Whether the scenario design works in the UI, which entity relationships are tricky, what loading order is actually needed, what data volumes feel right on forms and grids.

### Phase 1: CLI Core + Repeatable Loading (1-2 weeks)

**Goal**: Wrap the Phase 0 loading process into `spaarke-data generate` and `spaarke-data load` commands so anyone on the team can reset and repopulate the dev environment.

| Deliverable | Details | Primary Tool |
|-------------|---------|-------------|
| `generate` command | Calls Claude API to produce scenario JSON from YAML definitions | TypeScript + Claude API |
| `load` command | Orchestrates Web API upserts in dependency order | TypeScript + Web API |
| `reset` command | Wipe (reverse-order delete) + reload | TypeScript + Web API |
| Scenario 1 YAML config | `meridian-v-pinnacle.yaml` — formalized from Phase 0's data | YAML |
| Entity schemas exported | JSON schema files from live Dataverse via Web API metadata endpoint | TypeScript |
| SPE file loader | BFF API upload integrated into `load --layer files` | TypeScript |
| `config/environments.yaml` | Named environments (`--target dev` works) | YAML |
| Basic validation | Post-load record count + lookup integrity checks | TypeScript |
| Scenario 1 fully automated | `spaarke-data generate --scenario meridian-v-pinnacle && spaarke-data load --target dev` | CLI |

**Key decision**: Start with Web API loader (already proven in Phase 0), not CMT. CMT loader added in Phase 2 when bulk performance matters.

**Dependencies on Phase 0**: Phase 0's JSON data files, loading order, and entity relationship patterns directly inform the CLI command implementations. Phase 0 scripts become integration test baselines.

### Phase 2: All Scenarios + Real Content (2-3 weeks)

**Goal**: All 5 demo scenarios loaded, open datasets integrated, AI enrichment complete, search indexes populated.

| Deliverable | Details | Primary Tool |
|-------------|---------|-------------|
| CUAD source adapter | Parses CUAD contracts, renames parties per scenario config | TypeScript |
| Caselaw source adapter | Caselaw Access API client for court opinions | TypeScript |
| Scenarios 2-5 YAML configs | Atlas/Horizon Acquisition, Compliance Audit, Morrison Estate, Outside Counsel Management | YAML |
| Cross-scenario shared entities | `_shared-entities.yaml` for contacts/firms that span scenarios | YAML |
| AI enrichment pre-baking | Claude generates summary, keywords, entities, classification for all documents | TypeScript + Claude API |
| AI Search index seeder | `load --layer search-index` populates Azure AI Search | TypeScript + AI Search REST |
| CMT loader (optional) | PAC CLI / CMT bulk import as alternative to Web API for full reloads | TypeScript + PAC CLI |
| `validate` command | Full validation suite (record counts + relationships + field completeness) | TypeScript |
| All 5 scenarios loaded | 400-550 document files, all entity types, all relationships | CLI |
| DATA-PROVENANCE.md | Complete licensing documentation for all data sources | Markdown |
| Demo walkthrough guides | Per-scenario presenter scripts with demo narrative | Markdown |

### Phase 3: Onboarding + Distribution (as needed)

**Goal**: Customer-facing tool for onboarding and environment management.

| Deliverable | Details | Primary Tool |
|-------------|---------|-------------|
| CSV source adapter | Customer data import from CSV files | TypeScript |
| Excel source adapter | Customer data import from XLSX files | TypeScript |
| `onboard` command | Mapping-driven import workflow with column→field mapping | TypeScript |
| Sample onboarding mappings | `mappings/examples/` with CSV and Excel examples | YAML |
| `schema diff` command | Detect drift between local schemas and live environment | TypeScript |
| OpenClaw adapter | Browser automation for open dataset harvesting | TypeScript + OpenClaw |
| Claude Computer Use validator | Optional UI rendering checks with screenshots | TypeScript + Computer Use API |
| Volume scaling | `--volume light` (100 docs) / `--volume full` (500+) flags | TypeScript |
| npm package published | `npm install -g @spaarke/data-cli` | npm |
| CI/CD pipeline | GitHub Actions for build, test, publish | GitHub Actions |
| Onboarding documentation | Getting started, adding scenarios, customer onboarding guides | Markdown |

### Phase Dependency Map

```
Phase 0 ──→ Phase 1 ──→ Phase 2 ──→ Phase 3
(data)      (CLI)        (scale)      (distribute)

Phase 0 outputs:
  ├── JSON data files      → Phase 1 formalizes into YAML scenarios
  ├── Loading scripts      → Phase 1 reimplements in TypeScript
  ├── Entity load order    → Phase 1 codifies in pipeline engine
  └── Lessons learned      → Phase 1 avoids discovered pitfalls

Phase 1 outputs:
  ├── generate command     → Phase 2 adds adapters (CUAD, Caselaw)
  ├── load command         → Phase 2 adds CMT + Search loaders
  ├── Entity schemas       → Phase 2 uses for all scenarios
  └── Scenario 1 YAML      → Phase 2 follows same pattern for 2-5

Phase 2 outputs:
  ├── All scenarios        → Phase 3 uses as onboarding reference
  ├── Adapter pattern      → Phase 3 adds CSV/Excel adapters
  └── Validation suite     → Phase 3 extends with Computer Use
```

---

## 13. Risk & Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| CUAD dataset contains sensitive party names | Demo shows real company names in contracts | Find-and-replace party names with fictional equivalents during import |
| SPE container provisioning requires Graph permissions | Can't upload files without proper auth | Use existing BFF API which already handles Graph auth |
| Dataverse API rate limits during bulk load | Slow or failed loads | Use CMT batch import; add retry logic with exponential backoff |
| AI-generated content is obviously fake | Demo credibility suffers | Use open dataset content as primary, synthetic only for gaps |
| Search index vectors require embedding API calls | Expensive to generate at scale | Pre-compute embeddings during generation, store in seed files |
| Document conversion tools not available on all machines | Build breaks on dev machines without pandoc/LibreOffice | Docker container with all tools, or pre-convert and commit output files |
| PAC CLI `pac data` plugin deprecated | Cannot use `pac data import` directly | Use CMT executables directly or custom .NET CLI tool as fallback |
| Dataverse MCP Server is preview, per-call charges | Unexpected costs, feature instability | Use MCP for validation only (low volume), not bulk operations; budget for dev usage |
| OpenClaw scraping blocked by source websites | Cannot harvest open datasets automatically | Manual download fallback; respect robots.txt; use API endpoints where available |
| OpenClaw security surface | Browser agent running with local access | Run in isolated container/VM; review all harvested content; log all activity |
| Claude Computer Use accuracy (<100%) | Validation may miss issues or false-flag | Use as supplementary check, not sole validation; human spot-check on critical scenarios |
| CMT schema.xml drift from actual Dataverse schema | Import failures if schema doesn't match environment | Generate schema.xml from live environment export; validate before each import |

---

## 14. Content Focus Decision

Based on the Spaarke platform's strengths and typical demo audience (legal operations, corporate legal departments), the recommended content focus priority:

1. **Commercial Contracts** (NDAs, MSAs, SOWs, DPAs) — primary focus
   - Best showcase for document analysis, clause extraction, AI chat
   - CUAD provides excellent annotated source material
   - Templates enable controlled variation demos

2. **Litigation/Case Materials** — secondary focus
   - Demonstrates matter management, event tracking, document discovery
   - Caselaw Access Project provides authentic court documents
   - Essential for the Meridian v. Pinnacle scenario

3. **Financial/Invoice Documents** — tertiary focus
   - Demonstrates financial intelligence features
   - Template-based generation works well for structured data

4. **Employment Documents** — supporting
   - Adds variety to document classification demos
   - Template-based with synthetic variations

---

## 15. Next Steps

1. ✅ **Review this design** — tool architecture, scenarios, onboarding vision, separate repo confirmed
2. ✅ **Create `spaarke-data-cli` GitHub repository** — TypeScript project scaffolded and pushed
3. ✅ **Create phase-specific project specs** — spec.md for each phase in `spaarke-data-cli/projects/`
4. **Begin Phase 0** — "Data in Dataverse" sprint:
   a. Run `/project-pipeline` on `projects/phase-0-data-in-dataverse/` to generate plan + tasks
   b. Execute Phase 0 tasks — generate JSON data, create loading scripts, load Scenario 1
   c. Validate data in Dataverse UI
5. **Begin Phase 1** — CLI Core + Repeatable Loading (after Phase 0 validates the data design)
6. **Begin Phase 2** — All Scenarios + Real Content (after Phase 1 CLI is working)
7. **Phase 3** — Onboarding + Distribution (as needed, future)

---

## 16. References & Sources

### Open Legal Datasets
- [CUAD — Contract Understanding Atticus Dataset](https://www.atticusprojectai.org/cuad) — 500+ annotated commercial contracts (CC BY 4.0)
- [ACORD — Atticus Clause Retrieval Dataset](https://aclanthology.org/2025.acl-long.1206.pdf) — Clause retrieval for complex provisions
- [Kaggle Legal-Clause-Dataset](https://www.kaggle.com/datasets/bahushruth/legalclausedataset) — 150K clauses from LawInsider
- [TermScout Verified Contract Database](https://blog.termscout.com/what-is-the-public-contract-database) — Public commercial contracts as structured data
- [Awesome Legal Data](https://github.com/openlegaldata/awesome-legal-data) — Curated list of open legal datasets
- [Caselaw Access Project](https://case.law) — Millions of U.S. court opinions
- [Free Law Project / CourtListener](https://free.law/open-source-tools/) — Court opinions, PACER data
- [Open Law Lab — Legal Templates](https://www.openlawlab.com/2014/08/05/githubbing-law/) — Open source legal document repositories

### Automation Tools
- [Dataverse MCP Server](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp) — Microsoft's official MCP server for Dataverse CRUD
- [Dataverse MCP Blog Post](https://www.microsoft.com/en-us/power-platform/blog/2025/07/07/dataverse-mcp/) — Overview and capabilities
- [PAC CLI Data Commands](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/data) — Configuration Migration Tool via CLI
- [Configuration Migration Tool Guide](https://learn.microsoft.com/en-us/power-platform/admin/manage-configuration-data) — schema.xml/data.xml format
- [Custom .NET CMT CLI](https://github.com/dotnetprog/dataverse-configuration-migration-tool) — Alternative CMT implementation
- [OpenClaw](https://openclaw.ai/) — Open-source autonomous AI agent with browser automation
- [Claude Computer Use](https://platform.claude.com/docs/en/agents-and-tools/tool-use/computer-use-tool) — Anthropic's computer control API
- [Microsoft Power Platform Skills](https://github.com/microsoft/power-platform-skills) — Claude Code plugin marketplace for Power Platform
- [Claude Code MCP Integration](https://code.claude.com/docs/en/mcp) — How Claude Code connects to MCP servers
