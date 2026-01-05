# AI Analysis Playbook & Scope Design

> **Version**: 1.1
> **Date**: January 4, 2026
> **Status**: Draft
> **Purpose**: Define the complete "recipes" for each Playbook including Actions, Skills, Knowledge, and Tools
> **Related**: [AI-ANALYSIS-IMPLEMENTATION-DESIGN.md](AI-ANALYSIS-IMPLEMENTATION-DESIGN.md) - How to implement this design

---

## Table of Contents

1. [Overview](#overview)
2. [Dataverse Entity Model](#dataverse-entity-model)
3. [Document Type Taxonomy](#document-type-taxonomy)
4. [Matter Type Taxonomy](#matter-type-taxonomy)
5. [Playbook Definitions](#playbook-definitions)
6. [Scope Definitions](#scope-definitions)
7. [Playbook-Scope Matrix](#playbook-scope-matrix)
8. [Document Type → Suggested Playbooks](#document-type--suggested-playbooks)
9. [Matter Type → Default Playbook](#matter-type--default-playbook)

---

## Overview

The Playbook system enables no-code AI workflow composition. Each Playbook is a "recipe" that combines:

| Scope Type | Purpose | Example |
|------------|---------|---------|
| **Actions** | Individual AI operations | "Extract Entities", "Detect Risks" |
| **Skills** | Reusable analysis bundles | "Contract Analysis", "NDA Review" |
| **Knowledge** | RAG context sources | "Standard Contract Terms", "Risk Taxonomy" |
| **Tools** | Handler implementations | EntityExtractorHandler, ClauseAnalyzerHandler |

```
┌─────────────────────────────────────────────────────────────────┐
│                         PLAYBOOK                                 │
│                    "Full NDA Analysis"                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   SKILLS (What analysis workflows to run):                       │
│   ┌─────────────┐  ┌─────────────┐  ┌─────────────┐            │
│   │ NDA Review  │  │ Risk        │  │ Clause      │            │
│   │             │  │ Assessment  │  │ Comparison  │            │
│   └──────┬──────┘  └──────┬──────┘  └──────┬──────┘            │
│          │                │                │                    │
│   ACTIONS (What operations each skill performs):                 │
│   ┌──────▼──────┐  ┌──────▼──────┐  ┌──────▼──────┐            │
│   │ Extract     │  │ Detect      │  │ Analyze     │            │
│   │ Entities    │  │ Risks       │  │ Clauses     │            │
│   └─────────────┘  └─────────────┘  └─────────────┘            │
│                                                                  │
│   KNOWLEDGE (What context to retrieve via RAG):                  │
│   ┌─────────────┐  ┌─────────────┐  ┌─────────────┐            │
│   │ Standard    │  │ Risk        │  │ NDA Best    │            │
│   │ NDA Terms   │  │ Categories  │  │ Practices   │            │
│   └─────────────┘  └─────────────┘  └─────────────┘            │
│                                                                  │
│   TOOLS (What handlers execute the actions):                     │
│   ┌─────────────┐  ┌─────────────┐  ┌─────────────┐            │
│   │ Entity      │  │ Risk        │  │ Clause      │            │
│   │ Extractor   │  │ Detector    │  │ Analyzer    │            │
│   └─────────────┘  └─────────────┘  └─────────────┘            │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Dataverse Entity Model

Understanding the Dataverse entities that store Playbooks and Scopes is essential for implementation.

### Entity Relationship Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         DATAVERSE ENTITY MODEL                               │
└─────────────────────────────────────────────────────────────────────────────┘

                              TYPE LOOKUP TABLES
                              (Categorization Only)
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│ sprk_analysis   │  │ sprk_aiskill    │  │ sprk_ai         │  │ sprk_ai         │
│   actiontype    │  │     type        │  │  knowledgetype  │  │    tooltype     │
│ ───────────────│  │ ────────────── │  │ ────────────── │  │ ────────────── │
│ "Extraction"    │  │ "Document       │  │ "RAG Index"     │  │ "Entity         │
│ "Classification"│  │  Analysis"      │  │ "Inline Text"   │  │  Extraction"    │
│ "Summarization" │  │ "Compliance"    │  │ "Document Ref"  │  │ "Classification"|
└────────┬────────┘  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘
         │                    │                    │                    │
         │ Lookup             │ Lookup             │ Lookup             │ Lookup
         │ (categorizes)      │ (categorizes)      │ (categorizes)      │ (categorizes)
         ▼                    ▼                    ▼                    ▼
┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐
│ sprk_analysis   │  │ sprk_analysis   │  │ sprk_analysis   │  │ sprk_analysis   │
│     action      │  │     skill       │  │    knowledge    │  │      tool       │
│ ═══════════════ │  │ ═══════════════ │  │ ═══════════════ │  │ ═══════════════ │
│ MASTER LIST     │  │ MASTER LIST     │  │ MASTER LIST     │  │ MASTER LIST     │
│ of Actions      │  │ of Skills       │  │ of Knowledge    │  │ of Tools        │
│ ───────────────│  │ ────────────── │  │ ────────────── │  │ ────────────── │
│ ACT-001         │  │ SKL-001         │  │ KNW-001         │  │ TL-001          │
│ ACT-002         │  │ SKL-002         │  │ KNW-002         │  │ TL-002          │
│ ACT-003...      │  │ SKL-003...      │  │ KNW-003...      │  │ TL-003...       │
└────────┬────────┘  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘
         │                    │                    │                    │
         │ N:N                │ N:N                │ N:N                │ N:N
         │                    │                    │                    │
         └────────────────────┼────────────────────┼────────────────────┘
                              │                    │
                              ▼                    ▼
                    ┌─────────────────────────────────────────┐
                    │       sprk_analysisplaybook             │
                    │ ═══════════════════════════════════════ │
                    │ PB-001 "Quick Document Review"          │
                    │ PB-002 "Full Contract Analysis"         │
                    │ PB-003 "NDA Review"                     │
                    │ ...                                     │
                    │                                         │
                    │ Fields:                                 │
                    │ - sprk_name                             │
                    │ - sprk_description                      │
                    │ - sprk_outputtypeid (lookup)            │
                    │ - sprk_ispublic                         │
                    │ - ownerid                               │
                    └─────────────────────────────────────────┘
```

### Key Concept: Master List vs Category Lookup

**Important Distinction:**

| Entity | Purpose | Contains |
|--------|---------|----------|
| `sprk_analysisskill` | **Master List** | Actual skill definitions (ID, Name, PromptFragment) |
| `sprk_aiskilltype` | **Category Lookup** | Categories to organize skills ("Document Analysis", "Compliance") |

**The same pattern applies to all scope types:**

- **Actions**: `sprk_analysisaction` (records) → `sprk_analysisactiontype` (categories)
- **Skills**: `sprk_analysisskill` (records) → `sprk_aiskilltype` (categories)
- **Knowledge**: `sprk_analysisknowledge` (records) → `sprk_aiknowledgetype` (categories)
- **Tools**: `sprk_analysistool` (records) → `sprk_aitooltype` (categories)

### N:N Relationship Tables

Playbooks link to scope items via many-to-many relationship tables:

| Relationship Table | Connects | Purpose |
|--------------------|----------|---------|
| `sprk_analysisplaybook_action` | Playbook ↔ Action | Actions in this playbook |
| `sprk_playbook_skill` | Playbook ↔ Skill | Skills in this playbook |
| `sprk_playbook_knowledge` | Playbook ↔ Knowledge | Knowledge sources for this playbook |
| `sprk_playbook_tool` | Playbook ↔ Tool | Tools available to this playbook |

### How Selection Works

When a user creates a Playbook:

```
1. User creates Playbook record in sprk_analysisplaybook
   → Name: "Contract Review"

2. User SELECTS Skills from sprk_analysisskill (the master list)
   → Pick: "Contract Date Extraction", "Party Identification", "Risk Assessment"
   → These are actual skill RECORDS, not types

3. The N:N table (sprk_playbook_skill) stores the links:
   ┌─────────────────────────────────────────┐
   │        sprk_playbook_skill              │
   ├─────────────────────────────────────────┤
   │ PlaybookId          │ SkillId           │
   │ {Contract Review}   │ {SKL-001}         │
   │ {Contract Review}   │ {SKL-002}         │
   │ {Contract Review}   │ {SKL-005}         │
   └─────────────────────────────────────────┘

4. Each Skill MAY have a Type lookup for categorization:
   SKL-001 "Contract Date Extraction" → Type: "Document Analysis"
   SKL-002 "Party Identification"     → Type: "Document Analysis"
   SKL-005 "Risk Assessment"          → Type: "Risk Analysis"

   The Type is metadata for filtering/grouping, NOT the selection source.
```

### Entity Field Definitions

#### sprk_analysisskill

| Field | Type | Description |
|-------|------|-------------|
| `sprk_analysisskillid` | GUID | Primary key |
| `sprk_name` | String | Skill name (e.g., "Contract Analysis") |
| `sprk_description` | Memo | Description of what the skill does |
| `sprk_promptfragment` | Memo | System prompt content for this skill |
| `sprk_skilltypeid` | Lookup | Reference to `sprk_aiskilltype` (optional category) |
| `sprk_category` | String | Additional categorization field |
| `statecode` | State | Active/Inactive |

#### sprk_analysisknowledge

| Field | Type | Description |
|-------|------|-------------|
| `sprk_analysisknowledgeid` | GUID | Primary key |
| `sprk_name` | String | Knowledge source name |
| `sprk_description` | Memo | Description |
| `sprk_knowledgetypeid` | Lookup | Reference to `sprk_aiknowledgetype` |
| `sprk_type` | OptionSet | Inline (0), Document (1), RagIndex (2) |
| `sprk_content` | Memo | For inline text content |
| `sprk_documentid` | GUID | Reference to SPE document |
| `sprk_deploymentid` | GUID | Reference to RAG deployment config |

#### sprk_analysistool

| Field | Type | Description |
|-------|------|-------------|
| `sprk_analysistoolid` | GUID | Primary key |
| `sprk_name` | String | Tool name |
| `sprk_description` | Memo | Description |
| `sprk_tooltypeid` | Lookup | Reference to `sprk_aitooltype` |
| `sprk_type` | OptionSet | EntityExtractor (0), ClauseAnalyzer (1), etc. |
| `sprk_handlerclass` | String | C# handler class name |
| `sprk_configuration` | Memo | JSON configuration |

---

## Document Type Taxonomy

Before defining Playbooks, we need a consistent document type taxonomy:

| Document Type | Code | Examples |
|---------------|------|----------|
| **Contract** | `CONTRACT` | Service agreements, purchase orders |
| **NDA** | `NDA` | Non-disclosure, confidentiality agreements |
| **Lease** | `LEASE` | Commercial, residential, equipment leases |
| **Employment** | `EMPLOYMENT` | Offer letters, employment contracts |
| **SLA** | `SLA` | Service level agreements |
| **Invoice** | `INVOICE` | Invoices, statements |
| **Amendment** | `AMENDMENT` | Contract amendments, addenda |
| **Letter** | `LETTER` | Formal correspondence |
| **Policy** | `POLICY` | Insurance policies, company policies |
| **Report** | `REPORT` | Analysis reports, assessments |
| **Any** | `ANY` | Applies to all document types |

---

## Matter Type Taxonomy

Matters (legal cases/projects) have types that influence which Playbooks apply:

| Matter Type | Code | Typical Documents |
|-------------|------|-------------------|
| **Commercial** | `COMMERCIAL` | Contracts, NDAs, SLAs |
| **Real Estate** | `REAL_ESTATE` | Leases, purchase agreements |
| **Employment** | `EMPLOYMENT` | Employment contracts, policies |
| **Litigation** | `LITIGATION` | Correspondence, evidence documents |
| **M&A** | `MERGERS_ACQUISITIONS` | Due diligence, contracts |
| **Compliance** | `COMPLIANCE` | Policies, regulatory documents |
| **General** | `GENERAL` | Any matter type |

---

## Playbook Definitions

### PB-001: Quick Document Review

**Purpose**: Fast, high-level overview of any document. Good starting point for triage.

| Attribute | Value |
|-----------|-------|
| **ID** | `PB-001` |
| **Name** | Quick Document Review |
| **Description** | Rapid document triage with key information extraction |
| **Document Types** | `ANY` |
| **Matter Types** | `GENERAL` |
| **Estimated Time** | 30-60 seconds |
| **Complexity** | Low |

**Skills**:
| Skill | Purpose | Sequence |
|-------|---------|----------|
| Executive Summary | Generate 3-5 sentence overview | 1 |
| Entity Extraction | Pull key parties, dates, amounts | 2 |

**Actions**:
| Action | Tool Handler | Parameters |
|--------|--------------|------------|
| Summarize Content | SummaryHandler | `length: "brief"`, `format: "paragraph"` |
| Extract Entities | EntityExtractorHandler | `entity_types: ["party", "date", "amount"]` |
| Classify Document | DocumentClassifierHandler | `taxonomy: "standard"` |

**Knowledge Sources**:
| Source | Purpose |
|--------|---------|
| Document Type Definitions | Help classify document accurately |

**Output Structure**:
```json
{
  "summary": "Brief 3-5 sentence summary",
  "documentType": "CONTRACT",
  "confidence": 0.92,
  "keyEntities": {
    "parties": ["Acme Corp", "Widget Inc"],
    "effectiveDate": "2026-01-15",
    "expirationDate": "2027-01-14",
    "totalValue": "$150,000"
  }
}
```

---

### PB-002: Full Contract Analysis

**Purpose**: Comprehensive contract review covering all aspects.

| Attribute | Value |
|-----------|-------|
| **ID** | `PB-002` |
| **Name** | Full Contract Analysis |
| **Description** | Complete contract review with risk identification, clause analysis, and recommendations |
| **Document Types** | `CONTRACT`, `AMENDMENT` |
| **Matter Types** | `COMMERCIAL`, `MERGERS_ACQUISITIONS` |
| **Estimated Time** | 3-5 minutes |
| **Complexity** | High |

**Skills**:
| Skill | Purpose | Sequence |
|-------|---------|----------|
| Contract Analysis | Full contract review workflow | 1 |
| Risk Assessment | Identify risks and compliance issues | 2 |
| Clause Comparison | Compare to standard terms | 3 |

**Actions**:
| Action | Tool Handler | Parameters |
|--------|--------------|------------|
| Summarize Content | SummaryHandler | `length: "detailed"`, `sections: true` |
| Extract Entities | EntityExtractorHandler | `entity_types: ["party", "date", "amount", "obligation", "deliverable"]` |
| Analyze Clauses | ClauseAnalyzerHandler | `clause_categories: ["payment", "termination", "liability", "IP", "confidentiality"]` |
| Detect Risks | RiskDetectorHandler | `risk_threshold: 0.6`, `categories: ["financial", "legal", "operational"]` |
| Compare Clauses | ClauseComparisonHandler | `comparison_source: "standard_terms"` |

**Knowledge Sources**:
| Source | Purpose |
|--------|---------|
| Standard Contract Terms | Compare clauses to organization standards |
| Risk Categories | Risk classification taxonomy |
| Best Practices | Legal review best practices |

**Output Structure**:
```json
{
  "summary": {
    "executive": "High-level summary",
    "sections": [
      { "title": "Parties", "content": "..." },
      { "title": "Term", "content": "..." },
      { "title": "Payment", "content": "..." }
    ]
  },
  "keyTerms": {
    "effectiveDate": "2026-01-15",
    "expirationDate": "2027-01-14",
    "renewalType": "Auto-renewal",
    "noticePeriod": "30 days",
    "totalValue": "$150,000",
    "paymentTerms": "Net 30"
  },
  "clauses": [
    {
      "type": "termination",
      "text": "Either party may terminate...",
      "location": "Section 8.2",
      "comparison": {
        "matchesStandard": false,
        "deviation": "Notice period is 30 days vs standard 60 days",
        "risk": "medium"
      }
    }
  ],
  "risks": [
    {
      "category": "financial",
      "severity": "high",
      "description": "Unlimited liability clause in Section 9.1",
      "recommendation": "Negotiate liability cap"
    }
  ],
  "recommendations": [
    "Negotiate 60-day notice period",
    "Add liability cap provision",
    "Clarify IP ownership for deliverables"
  ]
}
```

---

### PB-003: NDA Review

**Purpose**: Specialized analysis for non-disclosure agreements.

| Attribute | Value |
|-----------|-------|
| **ID** | `PB-003` |
| **Name** | NDA Review |
| **Description** | Comprehensive NDA analysis with confidentiality scope and term evaluation |
| **Document Types** | `NDA` |
| **Matter Types** | `COMMERCIAL`, `MERGERS_ACQUISITIONS`, `EMPLOYMENT` |
| **Estimated Time** | 2-3 minutes |
| **Complexity** | Medium |

**Skills**:
| Skill | Purpose | Sequence |
|-------|---------|----------|
| NDA Review | NDA-specific analysis | 1 |
| Risk Assessment | Identify confidentiality risks | 2 |

**Actions**:
| Action | Tool Handler | Parameters |
|--------|--------------|------------|
| Summarize Content | SummaryHandler | `length: "standard"`, `focus: "nda"` |
| Extract Entities | EntityExtractorHandler | `entity_types: ["party", "date", "confidential_info_definition"]` |
| Analyze Clauses | ClauseAnalyzerHandler | `clause_categories: ["definition_of_confidential", "exclusions", "term", "return_of_materials", "injunctive_relief"]` |
| Detect Risks | RiskDetectorHandler | `risk_categories: ["scope_too_broad", "term_too_long", "missing_exclusions", "unilateral_vs_mutual"]` |

**Knowledge Sources**:
| Source | Purpose |
|--------|---------|
| Standard NDA Terms | Organization's preferred NDA language |
| NDA Best Practices | Industry standards for NDAs |

**NDA-Specific Extractions**:
| Field | Description | Example |
|-------|-------------|---------|
| `ndaType` | Mutual or Unilateral | "Mutual" |
| `confidentialInfoDefinition` | How CI is defined | "Broad - includes all business info" |
| `exclusions` | Standard carve-outs present | ["public domain", "prior knowledge", "independently developed"] |
| `term` | Duration of obligations | "3 years from disclosure" |
| `survivalPeriod` | Post-termination obligations | "2 years after termination" |
| `governingLaw` | Jurisdiction | "Delaware" |
| `returnOfMaterials` | Return/destruction requirements | "Within 30 days of request" |

**Output Structure**:
```json
{
  "ndaType": "Mutual",
  "parties": {
    "disclosingParty": "Acme Corp",
    "receivingParty": "Widget Inc",
    "effectiveDate": "2026-01-15"
  },
  "scope": {
    "definition": "Broad definition including all business information",
    "exclusions": ["public domain", "prior knowledge"],
    "missingExclusions": ["independently developed", "required by law"],
    "riskLevel": "medium"
  },
  "term": {
    "duration": "3 years from effective date",
    "survivalPeriod": "2 years after termination",
    "assessment": "Standard duration"
  },
  "risks": [
    {
      "issue": "Missing 'independently developed' exclusion",
      "severity": "medium",
      "recommendation": "Add standard exclusion for independently developed information"
    }
  ],
  "complianceChecklist": {
    "hasDefinitionOfCI": true,
    "hasStandardExclusions": false,
    "hasReturnProvision": true,
    "hasInjunctiveRelief": true,
    "isMarkedMutual": true
  }
}
```

---

### PB-004: Lease Review

**Purpose**: Specialized analysis for commercial and residential leases.

| Attribute | Value |
|-----------|-------|
| **ID** | `PB-004` |
| **Name** | Lease Review |
| **Description** | Complete lease analysis including rent, term, and tenant obligations |
| **Document Types** | `LEASE` |
| **Matter Types** | `REAL_ESTATE`, `COMMERCIAL` |
| **Estimated Time** | 3-4 minutes |
| **Complexity** | High |

**Skills**:
| Skill | Purpose | Sequence |
|-------|---------|----------|
| Lease Review | Lease-specific analysis | 1 |
| Risk Assessment | Property and financial risks | 2 |

**Actions**:
| Action | Tool Handler | Parameters |
|--------|--------------|------------|
| Summarize Content | SummaryHandler | `length: "detailed"`, `focus: "lease"` |
| Extract Entities | EntityExtractorHandler | `entity_types: ["landlord", "tenant", "property_address", "rent_amount", "dates"]` |
| Analyze Clauses | ClauseAnalyzerHandler | `clause_categories: ["rent", "term", "renewal", "maintenance", "insurance", "assignment", "default"]` |
| Detect Risks | RiskDetectorHandler | `risk_categories: ["rent_escalation", "CAM_charges", "personal_guarantee", "early_termination"]` |

**Knowledge Sources**:
| Source | Purpose |
|--------|---------|
| Standard Lease Terms | Market-standard lease provisions |
| Local Regulations | Jurisdiction-specific requirements |

**Lease-Specific Extractions**:
| Field | Description | Example |
|-------|-------------|---------|
| `leaseType` | Commercial/Residential/Equipment | "Commercial" |
| `baseRent` | Monthly rent amount | "$5,000/month" |
| `rentEscalation` | Annual increase terms | "3% annually" |
| `camCharges` | Common area maintenance | "$2.50/sq ft estimated" |
| `securityDeposit` | Deposit amount | "$10,000" |
| `termStart` | Lease commencement | "2026-02-01" |
| `termEnd` | Lease expiration | "2031-01-31" |
| `renewalOptions` | Extension terms | "Two 5-year options" |
| `personalGuarantee` | Guarantor requirements | "Required for first 2 years" |

**Output Structure**:
```json
{
  "leaseType": "Commercial",
  "parties": {
    "landlord": "Property Holdings LLC",
    "tenant": "Widget Inc",
    "guarantor": "John Smith (personal)"
  },
  "property": {
    "address": "123 Business Park, Suite 400",
    "squareFootage": 5000,
    "permittedUse": "General office"
  },
  "financialTerms": {
    "baseRent": "$5,000/month",
    "rentEscalation": "3% annually",
    "camCharges": "$2.50/sq ft (estimated $12,500/year)",
    "securityDeposit": "$10,000",
    "totalFirstYearCost": "$72,500"
  },
  "term": {
    "startDate": "2026-02-01",
    "endDate": "2031-01-31",
    "initialTerm": "5 years",
    "renewalOptions": "Two 5-year options at market rate"
  },
  "keyDates": [
    { "event": "Lease Commencement", "date": "2026-02-01" },
    { "event": "First Rent Escalation", "date": "2027-02-01" },
    { "event": "Renewal Notice Deadline", "date": "2030-08-01" },
    { "event": "Lease Expiration", "date": "2031-01-31" }
  ],
  "risks": [
    {
      "category": "financial",
      "severity": "high",
      "issue": "Personal guarantee required",
      "recommendation": "Negotiate removal after 24 months of timely payments"
    },
    {
      "category": "operational",
      "severity": "medium",
      "issue": "CAM charges are estimates with no cap",
      "recommendation": "Negotiate CAM cap at 105% of estimate"
    }
  ]
}
```

---

### PB-005: Employment Contract Review

**Purpose**: Analyze employment agreements and offer letters.

| Attribute | Value |
|-----------|-------|
| **ID** | `PB-005` |
| **Name** | Employment Contract Review |
| **Description** | Employment agreement analysis including compensation, benefits, and restrictive covenants |
| **Document Types** | `EMPLOYMENT` |
| **Matter Types** | `EMPLOYMENT` |
| **Estimated Time** | 2-3 minutes |
| **Complexity** | Medium |

**Skills**:
| Skill | Purpose | Sequence |
|-------|---------|----------|
| Employment Contract | Employment-specific analysis | 1 |
| Compliance Check | Employment law compliance | 2 |

**Actions**:
| Action | Tool Handler | Parameters |
|--------|--------------|------------|
| Summarize Content | SummaryHandler | `length: "standard"`, `focus: "employment"` |
| Extract Entities | EntityExtractorHandler | `entity_types: ["employer", "employee", "position", "compensation", "dates"]` |
| Analyze Clauses | ClauseAnalyzerHandler | `clause_categories: ["compensation", "benefits", "termination", "non_compete", "non_solicitation", "IP_assignment", "confidentiality"]` |
| Detect Risks | RiskDetectorHandler | `risk_categories: ["restrictive_covenants", "at_will_status", "arbitration_clause"]` |

**Knowledge Sources**:
| Source | Purpose |
|--------|---------|
| Employment Standards | Organization's standard employment terms |
| Regulatory Guidelines | Employment law requirements by jurisdiction |

**Employment-Specific Extractions**:
| Field | Description | Example |
|-------|-------------|---------|
| `position` | Job title | "Senior Software Engineer" |
| `employmentType` | Full-time/Part-time/Contract | "Full-time" |
| `baseSalary` | Annual compensation | "$150,000" |
| `bonus` | Bonus structure | "Up to 15% annual bonus" |
| `equity` | Stock/options | "10,000 stock options, 4-year vest" |
| `startDate` | Employment start | "2026-02-15" |
| `nonCompetePeriod` | Post-employment restriction | "12 months" |
| `nonCompeteScope` | Geographic/industry scope | "Same industry, 50-mile radius" |

**Output Structure**:
```json
{
  "parties": {
    "employer": "Tech Corp Inc",
    "employee": "Jane Doe",
    "position": "Senior Software Engineer",
    "department": "Engineering",
    "reportsTo": "VP Engineering"
  },
  "compensation": {
    "baseSalary": "$150,000/year",
    "payFrequency": "Bi-weekly",
    "bonus": {
      "target": "15% of base",
      "criteria": "Company and individual performance"
    },
    "equity": {
      "type": "Stock options",
      "amount": "10,000 shares",
      "vestingSchedule": "4-year with 1-year cliff",
      "exercisePrice": "$10/share"
    }
  },
  "benefits": {
    "healthInsurance": "Company-paid for employee",
    "pto": "20 days/year",
    "401k": "4% match"
  },
  "restrictiveCovenants": {
    "nonCompete": {
      "duration": "12 months",
      "scope": "Same industry",
      "geographic": "50-mile radius",
      "enforceability": "May be unenforceable in California"
    },
    "nonSolicitation": {
      "duration": "24 months",
      "scope": "Employees and customers"
    },
    "confidentiality": "Perpetual"
  },
  "termination": {
    "atWillStatus": true,
    "severance": "None specified",
    "noticePeriod": "2 weeks requested, not required"
  },
  "risks": [
    {
      "issue": "Broad non-compete may be unenforceable",
      "severity": "medium",
      "recommendation": "Narrow scope or remove if employee in CA"
    }
  ]
}
```

---

### PB-006: Invoice Validation

**Purpose**: Extract and validate invoice data for accounts payable processing.

| Attribute | Value |
|-----------|-------|
| **ID** | `PB-006` |
| **Name** | Invoice Validation |
| **Description** | Extract invoice details and validate against expected data |
| **Document Types** | `INVOICE` |
| **Matter Types** | `GENERAL`, `COMMERCIAL` |
| **Estimated Time** | 15-30 seconds |
| **Complexity** | Low |

**Skills**:
| Skill | Purpose | Sequence |
|-------|---------|----------|
| Invoice Processing | Invoice data extraction | 1 |

**Actions**:
| Action | Tool Handler | Parameters |
|--------|--------------|------------|
| Extract Entities | EntityExtractorHandler | `entity_types: ["vendor", "invoice_number", "date", "line_items", "amounts", "payment_terms"]` |
| Classify Document | DocumentClassifierHandler | `taxonomy: "invoice_type"` |

**Knowledge Sources**:
| Source | Purpose |
|--------|---------|
| Vendor Master | Validate vendor information |
| PO Database | Match to purchase orders |

**Invoice-Specific Extractions**:
| Field | Description | Example |
|-------|-------------|---------|
| `invoiceNumber` | Unique invoice ID | "INV-2026-001234" |
| `invoiceDate` | Date issued | "2026-01-15" |
| `dueDate` | Payment due date | "2026-02-14" |
| `vendorName` | Supplier name | "Office Supplies Inc" |
| `vendorAddress` | Supplier address | "123 Vendor St..." |
| `subtotal` | Pre-tax amount | "$1,500.00" |
| `taxAmount` | Tax amount | "$127.50" |
| `totalAmount` | Total due | "$1,627.50" |
| `paymentTerms` | Terms | "Net 30" |
| `poNumber` | Related PO | "PO-2025-5678" |

**Output Structure**:
```json
{
  "invoiceDetails": {
    "invoiceNumber": "INV-2026-001234",
    "invoiceDate": "2026-01-15",
    "dueDate": "2026-02-14",
    "paymentTerms": "Net 30"
  },
  "vendor": {
    "name": "Office Supplies Inc",
    "address": "123 Vendor St, City, ST 12345",
    "taxId": "12-3456789"
  },
  "lineItems": [
    {
      "description": "Office chairs (x10)",
      "quantity": 10,
      "unitPrice": 100.00,
      "amount": 1000.00
    },
    {
      "description": "Standing desks (x5)",
      "quantity": 5,
      "unitPrice": 100.00,
      "amount": 500.00
    }
  ],
  "totals": {
    "subtotal": 1500.00,
    "tax": 127.50,
    "shipping": 0.00,
    "total": 1627.50
  },
  "validation": {
    "poMatch": {
      "poNumber": "PO-2025-5678",
      "matched": true,
      "variance": 0.00
    },
    "vendorVerified": true,
    "duplicateCheck": "No duplicates found"
  }
}
```

---

### PB-007: SLA Analysis

**Purpose**: Analyze service level agreements for obligations and metrics.

| Attribute | Value |
|-----------|-------|
| **ID** | `PB-007` |
| **Name** | SLA Analysis |
| **Description** | Extract and evaluate SLA commitments, metrics, and penalties |
| **Document Types** | `SLA`, `CONTRACT` |
| **Matter Types** | `COMMERCIAL` |
| **Estimated Time** | 2-3 minutes |
| **Complexity** | Medium |

**Skills**:
| Skill | Purpose | Sequence |
|-------|---------|----------|
| SLA Analysis | SLA-specific extraction | 1 |
| Compliance Check | Verify SLA adequacy | 2 |

**Actions**:
| Action | Tool Handler | Parameters |
|--------|--------------|------------|
| Summarize Content | SummaryHandler | `length: "standard"`, `focus: "sla"` |
| Extract Entities | EntityExtractorHandler | `entity_types: ["service_levels", "metrics", "penalties", "exclusions"]` |
| Analyze Clauses | ClauseAnalyzerHandler | `clause_categories: ["uptime", "response_time", "resolution_time", "credits", "exclusions", "reporting"]` |

**Knowledge Sources**:
| Source | Purpose |
|--------|---------|
| Industry SLA Benchmarks | Compare to industry standards |
| Internal SLA Standards | Organization's minimum requirements |

**SLA-Specific Extractions**:
| Field | Description | Example |
|-------|-------------|---------|
| `serviceDescription` | What's covered | "Cloud hosting services" |
| `uptimeCommitment` | Availability SLA | "99.9%" |
| `responseTimeP1` | Critical response | "15 minutes" |
| `resolutionTimeP1` | Critical resolution | "4 hours" |
| `creditStructure` | Penalty credits | "5% per 0.1% below SLA" |
| `exclusions` | What doesn't count | "Scheduled maintenance" |
| `reportingFrequency` | SLA reports | "Monthly" |

**Output Structure**:
```json
{
  "serviceOverview": {
    "provider": "Cloud Services Inc",
    "customer": "Widget Corp",
    "serviceName": "Enterprise Cloud Hosting",
    "effectiveDate": "2026-01-01",
    "term": "3 years"
  },
  "serviceLevels": {
    "availability": {
      "commitment": "99.9%",
      "measurementPeriod": "Monthly",
      "calculationMethod": "(Total minutes - Downtime) / Total minutes",
      "industryBenchmark": "99.95%",
      "assessment": "Below industry standard"
    },
    "responseTime": {
      "priority1": { "target": "15 minutes", "assessment": "Good" },
      "priority2": { "target": "1 hour", "assessment": "Acceptable" },
      "priority3": { "target": "4 hours", "assessment": "Acceptable" }
    },
    "resolutionTime": {
      "priority1": { "target": "4 hours", "assessment": "Good" },
      "priority2": { "target": "24 hours", "assessment": "Acceptable" },
      "priority3": { "target": "72 hours", "assessment": "Slow" }
    }
  },
  "remedies": {
    "creditStructure": [
      { "slaRange": "99.0% - 99.9%", "credit": "5% of monthly fee" },
      { "slaRange": "95.0% - 99.0%", "credit": "10% of monthly fee" },
      { "slaRange": "Below 95.0%", "credit": "25% of monthly fee" }
    ],
    "creditCap": "25% of monthly fee",
    "exclusiveRemedy": true
  },
  "exclusions": [
    "Scheduled maintenance (with 48-hour notice)",
    "Customer-caused outages",
    "Force majeure events",
    "Third-party service failures"
  ],
  "risks": [
    {
      "issue": "Uptime commitment below industry standard",
      "severity": "medium",
      "recommendation": "Negotiate 99.95% or higher"
    },
    {
      "issue": "Credits are exclusive remedy",
      "severity": "high",
      "recommendation": "Add termination right for repeated failures"
    }
  ]
}
```

---

### PB-008: Due Diligence Document Review

**Purpose**: Bulk document review for M&A due diligence.

| Attribute | Value |
|-----------|-------|
| **ID** | `PB-008` |
| **Name** | Due Diligence Review |
| **Description** | Rapid assessment of documents for M&A or investment due diligence |
| **Document Types** | `ANY` |
| **Matter Types** | `MERGERS_ACQUISITIONS` |
| **Estimated Time** | 1-2 minutes per document |
| **Complexity** | Medium |

**Skills**:
| Skill | Purpose | Sequence |
|-------|---------|----------|
| Executive Summary | Quick overview | 1 |
| Risk Assessment | Flag deal-breakers | 2 |

**Actions**:
| Action | Tool Handler | Parameters |
|--------|--------------|------------|
| Summarize Content | SummaryHandler | `length: "brief"`, `focus: "due_diligence"` |
| Classify Document | DocumentClassifierHandler | `taxonomy: "dd_category"` |
| Extract Entities | EntityExtractorHandler | `entity_types: ["party", "date", "amount", "obligation", "change_of_control"]` |
| Detect Risks | RiskDetectorHandler | `risk_categories: ["change_of_control", "consent_required", "assignment_restriction", "material_obligation"]` |

**Knowledge Sources**:
| Source | Purpose |
|--------|---------|
| Due Diligence Checklist | Standard DD categories |
| Deal-breaker Patterns | Known risk patterns |

**DD-Specific Extractions**:
| Field | Description | Example |
|-------|-------------|---------|
| `ddCategory` | DD checklist category | "Material Contracts" |
| `materialityFlag` | Is it material? | "High" |
| `changeOfControlClause` | CoC provisions | "Requires consent" |
| `assignability` | Can it be assigned? | "Non-assignable" |
| `consentRequired` | Consents needed | "Counterparty consent required" |
| `keyDates` | Important dates | Expiration, renewal deadlines |

**Output Structure**:
```json
{
  "documentOverview": {
    "title": "Master Services Agreement - Tech Vendor",
    "ddCategory": "Material Contracts",
    "materialityAssessment": "High - >$500K annual value",
    "documentType": "CONTRACT"
  },
  "keyInformation": {
    "parties": ["Target Corp", "Tech Vendor Inc"],
    "effectiveDate": "2024-01-01",
    "expirationDate": "2027-12-31",
    "annualValue": "$750,000"
  },
  "dealImpact": {
    "changeOfControl": {
      "clausePresent": true,
      "requirement": "Written consent required within 30 days of closing",
      "terminationRight": "Counterparty may terminate if consent not obtained",
      "riskLevel": "high"
    },
    "assignability": {
      "restriction": "Non-assignable without consent",
      "consentStandard": "Consent not to be unreasonably withheld",
      "riskLevel": "medium"
    }
  },
  "actionItems": [
    {
      "priority": "high",
      "action": "Obtain vendor consent pre-closing",
      "deadline": "Before closing",
      "owner": "Legal"
    }
  ],
  "flags": [
    {
      "type": "consent_required",
      "severity": "high",
      "description": "Change of control consent required from Tech Vendor"
    }
  ]
}
```

---

### PB-009: Compliance Document Review

**Purpose**: Review documents for regulatory compliance requirements.

| Attribute | Value |
|-----------|-------|
| **ID** | `PB-009` |
| **Name** | Compliance Review |
| **Description** | Identify compliance requirements and verify adherence |
| **Document Types** | `POLICY`, `CONTRACT`, `ANY` |
| **Matter Types** | `COMPLIANCE` |
| **Estimated Time** | 2-3 minutes |
| **Complexity** | Medium |

**Skills**:
| Skill | Purpose | Sequence |
|-------|---------|----------|
| Compliance Check | Regulatory requirement identification | 1 |
| Risk Assessment | Non-compliance risk | 2 |

**Actions**:
| Action | Tool Handler | Parameters |
|--------|--------------|------------|
| Summarize Content | SummaryHandler | `length: "standard"`, `focus: "compliance"` |
| Extract Entities | EntityExtractorHandler | `entity_types: ["regulation_reference", "compliance_requirement", "deadline", "responsible_party"]` |
| Analyze Clauses | ClauseAnalyzerHandler | `clause_categories: ["data_protection", "audit_rights", "reporting", "certifications", "breach_notification"]` |
| Detect Risks | RiskDetectorHandler | `risk_categories: ["gdpr", "ccpa", "sox", "hipaa", "industry_specific"]` |

**Knowledge Sources**:
| Source | Purpose |
|--------|---------|
| Regulatory Guidelines | Current regulatory requirements |
| Compliance Checklists | Standard compliance frameworks |

**Output Structure**:
```json
{
  "documentOverview": {
    "title": "Data Processing Agreement",
    "complianceFrameworks": ["GDPR", "CCPA"],
    "assessmentDate": "2026-01-15"
  },
  "complianceRequirements": [
    {
      "framework": "GDPR",
      "requirement": "Data processing agreement",
      "status": "Present",
      "articleReference": "Article 28"
    },
    {
      "framework": "GDPR",
      "requirement": "Sub-processor notification",
      "status": "Missing",
      "articleReference": "Article 28(2)",
      "recommendation": "Add sub-processor notification clause"
    }
  ],
  "gaps": [
    {
      "requirement": "Sub-processor notification mechanism",
      "severity": "high",
      "recommendation": "Add clause requiring 30-day notice of new sub-processors"
    },
    {
      "requirement": "Data breach notification timeline",
      "severity": "medium",
      "recommendation": "Specify 72-hour notification requirement per GDPR"
    }
  ],
  "remediationPlan": [
    {
      "gap": "Sub-processor notification",
      "action": "Amend DPA to include notification mechanism",
      "priority": "high",
      "deadline": "2026-02-15"
    }
  ]
}
```

---

### PB-010: Risk-Focused Contract Scan

**Purpose**: Quick scan focused only on risk identification.

| Attribute | Value |
|-----------|-------|
| **ID** | `PB-010` |
| **Name** | Risk-Focused Scan |
| **Description** | Rapid risk identification without full analysis |
| **Document Types** | `CONTRACT`, `NDA`, `LEASE`, `EMPLOYMENT` |
| **Matter Types** | `GENERAL` |
| **Estimated Time** | 30-60 seconds |
| **Complexity** | Low |

**Skills**:
| Skill | Purpose | Sequence |
|-------|---------|----------|
| Risk Assessment | Focused risk identification | 1 |

**Actions**:
| Action | Tool Handler | Parameters |
|--------|--------------|------------|
| Detect Risks | RiskDetectorHandler | `risk_threshold: 0.5`, `categories: "all"`, `output: "risks_only"` |
| Extract Entities | EntityExtractorHandler | `entity_types: ["party", "date"]`, `minimal: true` |

**Knowledge Sources**:
| Source | Purpose |
|--------|---------|
| Risk Categories | Risk classification taxonomy |

**Output Structure**:
```json
{
  "scanSummary": {
    "documentName": "Services Agreement - Vendor X",
    "scanDate": "2026-01-15",
    "totalRisks": 4,
    "highRisks": 1,
    "mediumRisks": 2,
    "lowRisks": 1
  },
  "risks": [
    {
      "severity": "high",
      "category": "liability",
      "location": "Section 9.1",
      "issue": "Unlimited liability clause",
      "text": "Vendor shall be liable for all damages...",
      "recommendation": "Negotiate liability cap"
    },
    {
      "severity": "medium",
      "category": "termination",
      "location": "Section 12.2",
      "issue": "Short termination notice period",
      "text": "Either party may terminate with 15 days notice",
      "recommendation": "Extend to 30-60 days"
    }
  ],
  "quickDecision": {
    "recommendation": "Review Required",
    "reason": "High-severity risk identified",
    "priority": "High"
  }
}
```

---

## Scope Definitions

### Actions (Individual AI Operations)

| ID | Name | Description | Handler |
|----|------|-------------|---------|
| `ACT-001` | Extract Entities | Extract key entities (parties, dates, amounts) | `EntityExtractorHandler` |
| `ACT-002` | Analyze Clauses | Identify and analyze contract clauses | `ClauseAnalyzerHandler` |
| `ACT-003` | Classify Document | Determine document type and category | `DocumentClassifierHandler` |
| `ACT-004` | Summarize Content | Generate executive summary | `SummaryHandler` |
| `ACT-005` | Detect Risks | Identify potential risks and issues | `RiskDetectorHandler` |
| `ACT-006` | Compare Clauses | Compare clauses to standard terms | `ClauseComparisonHandler` |
| `ACT-007` | Extract Dates | Extract and normalize all dates | `DateExtractorHandler` |
| `ACT-008` | Calculate Values | Sum and validate monetary amounts | `FinancialCalculatorHandler` |

### Skills (Reusable Analysis Bundles)

| ID | Name | Included Actions | Applicable Document Types |
|----|------|------------------|---------------------------|
| `SKL-001` | Contract Analysis | ACT-001, ACT-002, ACT-004, ACT-005 | CONTRACT, AMENDMENT |
| `SKL-002` | Invoice Processing | ACT-001, ACT-003, ACT-008 | INVOICE |
| `SKL-003` | NDA Review | ACT-001, ACT-002, ACT-004, ACT-005 | NDA |
| `SKL-004` | Lease Review | ACT-001, ACT-002, ACT-004, ACT-005, ACT-007 | LEASE |
| `SKL-005` | Employment Contract | ACT-001, ACT-002, ACT-004, ACT-005 | EMPLOYMENT |
| `SKL-006` | SLA Analysis | ACT-001, ACT-002, ACT-004 | SLA |
| `SKL-007` | Compliance Check | ACT-002, ACT-005 | ANY |
| `SKL-008` | Executive Summary | ACT-003, ACT-004 | ANY |
| `SKL-009` | Risk Assessment | ACT-005, ACT-006 | CONTRACT, NDA, LEASE |
| `SKL-010` | Clause Comparison | ACT-002, ACT-006 | CONTRACT, NDA |

### Knowledge Sources (RAG Context)

| ID | Name | Content Type | Applicable To |
|----|------|--------------|---------------|
| `KNW-001` | Standard Contract Terms | Organization's standard clauses | Contracts, NDAs |
| `KNW-002` | Regulatory Guidelines | Industry compliance requirements | All documents |
| `KNW-003` | Best Practices | Legal review best practices | All analysis |
| `KNW-004` | Risk Categories | Risk classification taxonomy | Risk detection |
| `KNW-005` | Defined Terms | Standard definitions and acronyms | All documents |
| `KNW-006` | NDA Standards | Standard NDA provisions | NDAs |
| `KNW-007` | Lease Standards | Commercial lease benchmarks | Leases |
| `KNW-008` | Employment Standards | Employment law requirements | Employment |
| `KNW-009` | SLA Benchmarks | Industry SLA standards | SLAs |
| `KNW-010` | Due Diligence Checklist | M&A DD requirements | Due diligence |

### Tools (Handler Implementations)

| ID | Handler Class | Purpose | Parameters |
|----|---------------|---------|------------|
| `TL-001` | `EntityExtractorHandler` | Extract named entities | `entity_types[]`, `confidence_threshold` |
| `TL-002` | `ClauseAnalyzerHandler` | Analyze document clauses | `clause_categories[]`, `risk_threshold` |
| `TL-003` | `DocumentClassifierHandler` | Classify document type | `taxonomy`, `confidence_threshold` |
| `TL-004` | `SummaryHandler` | Generate summaries | `length`, `format`, `focus` |
| `TL-005` | `RiskDetectorHandler` | Detect risks | `risk_categories[]`, `risk_threshold` |
| `TL-006` | `ClauseComparisonHandler` | Compare to standards | `comparison_source`, `similarity_threshold` |
| `TL-007` | `DateExtractorHandler` | Extract and normalize dates | `date_format`, `include_relative` |
| `TL-008` | `FinancialCalculatorHandler` | Financial calculations | `operations[]`, `currency` |

---

## Playbook-Scope Matrix

Quick reference showing which scopes are used by each Playbook:

| Playbook | Skills | Actions | Knowledge | Tools |
|----------|--------|---------|-----------|-------|
| PB-001 Quick Review | SKL-008 | ACT-001, ACT-003, ACT-004 | KNW-005 | TL-001, TL-003, TL-004 |
| PB-002 Full Contract | SKL-001, SKL-009, SKL-010 | ACT-001-006 | KNW-001, KNW-003, KNW-004 | TL-001-006 |
| PB-003 NDA Review | SKL-003, SKL-009 | ACT-001, ACT-002, ACT-004, ACT-005 | KNW-001, KNW-003, KNW-006 | TL-001, TL-002, TL-004, TL-005 |
| PB-004 Lease Review | SKL-004, SKL-009 | ACT-001, ACT-002, ACT-004, ACT-005, ACT-007 | KNW-003, KNW-004, KNW-007 | TL-001, TL-002, TL-004, TL-005, TL-007 |
| PB-005 Employment | SKL-005, SKL-007 | ACT-001, ACT-002, ACT-004, ACT-005 | KNW-002, KNW-008 | TL-001, TL-002, TL-004, TL-005 |
| PB-006 Invoice | SKL-002 | ACT-001, ACT-003, ACT-008 | KNW-005 | TL-001, TL-003, TL-008 |
| PB-007 SLA | SKL-006, SKL-007 | ACT-001, ACT-002, ACT-004 | KNW-002, KNW-009 | TL-001, TL-002, TL-004 |
| PB-008 Due Diligence | SKL-008, SKL-009 | ACT-001, ACT-003, ACT-004, ACT-005 | KNW-004, KNW-010 | TL-001, TL-003, TL-004, TL-005 |
| PB-009 Compliance | SKL-007, SKL-009 | ACT-002, ACT-004, ACT-005 | KNW-002, KNW-003 | TL-002, TL-004, TL-005 |
| PB-010 Risk Scan | SKL-009 | ACT-001, ACT-005 | KNW-004 | TL-001, TL-005 |

---

## Document Type → Suggested Playbooks

When a document is uploaded, suggest playbooks based on classification:

| Document Type | Primary Playbook | Alternative Playbooks |
|---------------|------------------|----------------------|
| `CONTRACT` | PB-002 Full Contract | PB-001 Quick, PB-010 Risk Scan |
| `NDA` | PB-003 NDA Review | PB-001 Quick, PB-010 Risk Scan |
| `LEASE` | PB-004 Lease Review | PB-001 Quick, PB-002 Full Contract |
| `EMPLOYMENT` | PB-005 Employment | PB-001 Quick |
| `SLA` | PB-007 SLA Analysis | PB-002 Full Contract |
| `INVOICE` | PB-006 Invoice | PB-001 Quick |
| `AMENDMENT` | PB-002 Full Contract | PB-001 Quick |
| `POLICY` | PB-009 Compliance | PB-001 Quick |
| Unknown | PB-001 Quick Review | PB-010 Risk Scan |

---

## Matter Type → Default Playbook

For new matters, suggest a default playbook:

| Matter Type | Default Playbook | Reasoning |
|-------------|------------------|-----------|
| `COMMERCIAL` | PB-002 Full Contract | Most documents are contracts |
| `REAL_ESTATE` | PB-004 Lease Review | Primary document type is leases |
| `EMPLOYMENT` | PB-005 Employment | Employment agreements are core |
| `MERGERS_ACQUISITIONS` | PB-008 Due Diligence | DD review is primary use case |
| `COMPLIANCE` | PB-009 Compliance | Compliance review focus |
| `LITIGATION` | PB-001 Quick Review | Documents vary widely |
| `GENERAL` | PB-001 Quick Review | No specific focus |

---

## Changelog

| Date | Change |
|------|--------|
| 2026-01-04 | Initial design document created |
