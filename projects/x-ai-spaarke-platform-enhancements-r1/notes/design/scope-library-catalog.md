# Scope Library Catalog

> **Project**: ai-spaarke-platform-enhancements-r1
> **Phase**: 3 — Workstream B: Scope Library & Seed Data
> **Last Updated**: 2026-02-23 (AIPL-034: Playbooks section added)

This document catalogs all system-defined scope library records seeded into the `spaarkedev1` Dataverse environment as part of Phase 3. System records are distinguished by a `SYS-` name prefix convention in `ScopeResolverService` and are non-mutable by end users.

---

## Actions (sprk_analysisaction)

Actions define the base system prompt that establishes the LLM's role, task, and output format for a document analysis scenario. Each action is specialized for a specific document category.

- **sprk_actioncode**: Alternate key used by `ActionLookupService.GetByCodeAsync()` for deterministic lookup
- **sprk_systemprompt**: The full system prompt text (200+ words) that defines the LLM's persona and analysis requirements
- **sprk_description**: Human-readable description of the action's purpose

**Entity**: `sprk_analysisaction` | **Collection**: `sprk_analysisactions`
**Seeded By**: AIPL-030 | **Environment**: spaarkedev1.crm.dynamics.com

### Action Catalog

| Code | Name | Document Type | Key Extractions | Dataverse ID |
|------|------|---------------|----------------|--------------|
| ACT-001 | Contract Review | Commercial contracts, MSA, PSA, vendor agreements | Parties, obligations, dates, payment terms, termination rights, IP, liability | 41e3beef-cb10-f111-8342-7c1e520aa4df |
| ACT-002 | NDA Analysis | Non-disclosure agreements, confidentiality agreements, CDAs | Confidential info definition, obligations, exclusions, duration, remedies | 34c9ecf2-cb10-f111-8342-7ced8d1dc988 |
| ACT-003 | Lease Agreement Review | Commercial/residential leases, NNN, office, retail | Rent, escalation, renewal options, permitted use, landlord/tenant obligations | fb19c1f5-cb10-f111-8342-7c1e520aa4df |
| ACT-004 | Invoice Processing | Vendor invoices, utility bills, subscription invoices, AP processing | Vendor info, line items, financial totals, payment terms, validation checks | 38c9ecf2-cb10-f111-8342-7ced8d1dc988 |
| ACT-005 | SLA Analysis | Service level agreements, managed service SLAs, cloud SLAs | SLOs, measurement methodology, credits, escalation, maintenance exclusions | fe19c1f5-cb10-f111-8342-7c1e520aa4df |
| ACT-006 | Employment Agreement Review | Offer letters, employment contracts, contractor agreements | Compensation, equity, IP assignment, non-compete, non-solicit, severance | 3cc9ecf2-cb10-f111-8342-7ced8d1dc988 |
| ACT-007 | Statement of Work Analysis | SOWs, work orders, task orders, project orders | Deliverables, milestones, acceptance criteria, fees, change orders | 3fc9ecf2-cb10-f111-8342-7ced8d1dc988 |
| ACT-008 | General Legal Document Review | Any unclassified legal document | Document classification, parties, dates, obligations, risk summary | 41c9ecf2-cb10-f111-8342-7ced8d1dc988 |

### Action Alternate Key Lookup

```
GET /api/data/v9.2/sprk_analysisactions(sprk_actioncode='ACT-001')
    ?$select=sprk_name,sprk_actioncode,sprk_systemprompt,sprk_description
```

This is how `ActionLookupService.GetByCodeAsync("ACT-001")` operates internally.

### Prompt File Locations

Full system prompt text for each action is version-controlled in:

| Code | Prompt File |
|------|------------|
| ACT-001 | `notes/design/action-prompts/ACT-001-contract-review.md` |
| ACT-002 | `notes/design/action-prompts/ACT-002-nda-analysis.md` |
| ACT-003 | `notes/design/action-prompts/ACT-003-lease-agreement-review.md` |
| ACT-004 | `notes/design/action-prompts/ACT-004-invoice-processing.md` |
| ACT-005 | `notes/design/action-prompts/ACT-005-sla-analysis.md` |
| ACT-006 | `notes/design/action-prompts/ACT-006-employment-agreement-review.md` |
| ACT-007 | `notes/design/action-prompts/ACT-007-statement-of-work-analysis.md` |
| ACT-008 | `notes/design/action-prompts/ACT-008-general-legal-document-review.md` |

Creation script: `scripts/Create-ActionSeedRecords.ps1`

---

## Tools (sprk_analysistool)

Tools define callable functions that the AI agent can invoke during analysis. Each tool specifies:
- **sprk_toolcode**: Alternate key used by `ToolLookupService.GetByCodeAsync()` for deterministic lookup
- **sprk_handlerclass**: C# handler class name resolved by `ScopeResolverService.MapHandlerClassToToolType()`
- **sprk_configuration**: JSON Schema string defining the tool's accepted input parameters

**Entity**: `sprk_analysistool` | **Collection**: `sprk_analysistools`
**Seeded By**: AIPL-033 | **Environment**: spaarkedev1.crm.dynamics.com

### Tool Catalog

| Code | Name | Handler Class | Resolved ToolType | Description |
|------|------|---------------|-------------------|-------------|
| TL-001 | DocumentSearch | `DocumentSearchHandler` | `Custom` | Search knowledge base and document index for relevant content |
| TL-002 | AnalysisRetrieval | `AnalysisQueryHandler` | `Custom` | Retrieve previously computed analysis results for a document |
| TL-003 | KnowledgeRetrieval | `KnowledgeRetrievalHandler` | `Custom` | Retrieve specific knowledge source content by identifier |
| TL-004 | TextRefinement | `TextRefinementHandler` | `Custom` | Refine or reformat a text section using AI-assisted editing |
| TL-005 | CitationExtractor | `CitationExtractorHandler` | `EntityExtractor` | Extract and normalize citation references from document text |
| TL-006 | SummaryGenerator | `SummaryGeneratorHandler` | `Summary` | Generate structured summaries of document sections |
| TL-007 | RedFlagDetector | `RedFlagDetectorHandler` | `RiskDetector` | Detect risk indicators and compliance issues in document text |
| TL-008 | PartyExtractor | `PartyExtractorHandler` | `EntityExtractor` | Extract and normalize party information from document text |

**ToolType mapping note**: `ScopeResolverService.MapHandlerClassToToolType()` uses substring matching on the handler class name:
- Contains `"Summary"` → `ToolType.Summary` (TL-006)
- Contains `"RiskDetector"` → `ToolType.RiskDetector` (TL-007)
- Contains `"EntityExtractor"` → `ToolType.EntityExtractor` (TL-005, TL-008)
- No match → `ToolType.Custom` (TL-001, TL-002, TL-003, TL-004)

---

### TL-001: DocumentSearch

**Handler**: `DocumentSearchHandler`
**ToolType**: `Custom`
**Description**: Search the knowledge base and document index for relevant content matching a query.

**JSON Schema** (`sprk_configuration`):
```json
{
  "type": "object",
  "properties": {
    "query": {
      "type": "string",
      "description": "Search query text"
    },
    "topK": {
      "type": "integer",
      "description": "Maximum number of results to return",
      "default": 5
    },
    "indexName": {
      "type": "string",
      "description": "Optional: target a specific AI Search index"
    }
  },
  "required": ["query"]
}
```

---

### TL-002: AnalysisRetrieval

**Handler**: `AnalysisQueryHandler`
**ToolType**: `Custom`
**Description**: Retrieve previously computed analysis results for a specific document or analysis session.

**JSON Schema** (`sprk_configuration`):
```json
{
  "type": "object",
  "properties": {
    "documentId": {
      "type": "string",
      "description": "Document identifier to retrieve analysis for"
    },
    "analysisType": {
      "type": "string",
      "description": "Optional: filter by analysis type (e.g., summary, risk)"
    }
  },
  "required": ["documentId"]
}
```

---

### TL-003: KnowledgeRetrieval

**Handler**: `KnowledgeRetrievalHandler`
**ToolType**: `Custom`
**Description**: Retrieve specific knowledge source content by identifier or type from the knowledge store.

**JSON Schema** (`sprk_configuration`):
```json
{
  "type": "object",
  "properties": {
    "knowledgeId": {
      "type": "string",
      "description": "Knowledge source identifier"
    },
    "contentType": {
      "type": "string",
      "description": "Type of knowledge content to retrieve",
      "enum": ["inline", "rag", "document"]
    }
  },
  "required": ["knowledgeId"]
}
```

---

### TL-004: TextRefinement

**Handler**: `TextRefinementHandler`
**ToolType**: `Custom`
**Description**: Refine, reformat, or restructure a text section using AI-assisted editing.

**JSON Schema** (`sprk_configuration`):
```json
{
  "type": "object",
  "properties": {
    "text": {
      "type": "string",
      "description": "Input text to refine"
    },
    "instruction": {
      "type": "string",
      "description": "Refinement instruction (e.g., summarize, rewrite formally, bullet points)"
    },
    "maxLength": {
      "type": "integer",
      "description": "Maximum output length in characters"
    }
  },
  "required": ["text", "instruction"]
}
```

---

### TL-005: CitationExtractor

**Handler**: `CitationExtractorHandler`
**ToolType**: `EntityExtractor` (handler contains "EntityExtractor")
**Description**: Extract and normalize citation references from analysis results and document text.

**JSON Schema** (`sprk_configuration`):
```json
{
  "type": "object",
  "properties": {
    "text": {
      "type": "string",
      "description": "Text to extract citations from"
    },
    "format": {
      "type": "string",
      "description": "Citation format standard",
      "enum": ["bluebook", "apa", "mla", "auto"],
      "default": "auto"
    },
    "includeContext": {
      "type": "boolean",
      "description": "Include surrounding context for each citation",
      "default": false
    }
  },
  "required": ["text"]
}
```

---

### TL-006: SummaryGenerator

**Handler**: `SummaryGeneratorHandler`
**ToolType**: `Summary` (handler contains "Summary")
**Description**: Generate structured summaries of document sections or complete documents.

**JSON Schema** (`sprk_configuration`):
```json
{
  "type": "object",
  "properties": {
    "text": {
      "type": "string",
      "description": "Text to summarize"
    },
    "summaryType": {
      "type": "string",
      "description": "Type of summary to generate",
      "enum": ["executive", "detailed", "bullet", "one-sentence"],
      "default": "executive"
    },
    "maxWords": {
      "type": "integer",
      "description": "Maximum word count for the summary",
      "default": 200
    },
    "focusAreas": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Optional: specific topics to emphasize in the summary"
    }
  },
  "required": ["text"]
}
```

---

### TL-007: RedFlagDetector

**Handler**: `RedFlagDetectorHandler`
**ToolType**: `RiskDetector` (handler contains "RiskDetector")
**Description**: Detect risk indicators, problematic clauses, and compliance issues in document sections.

**JSON Schema** (`sprk_configuration`):
```json
{
  "type": "object",
  "properties": {
    "text": {
      "type": "string",
      "description": "Document text to analyze for risk indicators"
    },
    "riskCategories": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Risk categories to check (e.g., liability, compliance, financial)"
    },
    "severity": {
      "type": "string",
      "description": "Minimum severity threshold",
      "enum": ["low", "medium", "high", "critical"],
      "default": "medium"
    }
  },
  "required": ["text"]
}
```

---

### TL-008: PartyExtractor

**Handler**: `PartyExtractorHandler`
**ToolType**: `EntityExtractor` (handler contains "EntityExtractor")
**Description**: Extract and normalize party information (people, organizations, roles) from document text.

**JSON Schema** (`sprk_configuration`):
```json
{
  "type": "object",
  "properties": {
    "text": {
      "type": "string",
      "description": "Document text to extract parties from"
    },
    "partyTypes": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Party types to extract",
      "enum": ["person", "organization", "role", "all"],
      "default": ["all"]
    },
    "includeAliases": {
      "type": "boolean",
      "description": "Include alternate names or aliases",
      "default": true
    }
  },
  "required": ["text"]
}
```

---

## Skills (sprk_analysisskill)

Skills are composable prompt fragments that inject additional capability context into an Action's system prompt. Each skill:
- **sprk_skillcode**: Alternate key used by `SkillLookupService.GetByCodeAsync()` for deterministic lookup
- **sprk_promptfragment**: The prompt fragment text injected into the parent Action's system prompt

**Entity**: `sprk_analysisskill` | **Collection**: `sprk_analysisskills`
**Seeded By**: AIPL-031 | **Environment**: spaarkedev1.crm.dynamics.com

### Skill Catalog

| Code | Name | Description |
|------|------|-------------|
| SKL-001 | Citation Extraction | Cite every claim with [Section: X, Page Y] format |
| SKL-002 | Risk Flagging | Highlight clauses requiring legal review with [RISK: HIGH/MEDIUM/LOW] |
| SKL-003 | Summary Generation | Generate executive summary in 3-5 bullet points at start of response |
| SKL-004 | Date Extraction | Extract and normalize all dates to ISO 8601 in a Key Dates table |
| SKL-005 | Party Identification | Identify all parties with full legal names, roles, and contact details |
| SKL-006 | Obligation Mapping | Map mutual obligations into a structured Party/Obligation/Condition/Deadline table |
| SKL-007 | Defined Terms | Extract all defined terms into an alphabetical Defined Terms Glossary |
| SKL-008 | Financial Terms | Extract monetary amounts, payment schedules, rates, and financial obligations |
| SKL-009 | Termination Analysis | Analyze termination triggers, notice periods, cure periods, and consequences |
| SKL-010 | Jurisdiction and Governing Law | Identify applicable law, jurisdiction, and dispute resolution mechanism |

---

## Knowledge Sources (sprk_content)

Knowledge Sources are authoritative reference documents indexed into the knowledge-index in Azure AI Search. The RAG system retrieves relevant chunks from these sources when answering user queries. System knowledge sources are shared across all tenants (tenantId="system", ADR-014) and are non-editable by end users.

- **sprk_externalid**: Alternate key (KNW-001 through KNW-010) used by `KnowledgeRetrievalHandler` for direct lookup
- **sprk_contenttext**: Full markdown content of the knowledge source (indexed into knowledge-index at 512-token chunks)
- **sprk_contenttype**: "Reference" for all system knowledge sources
- **sprk_isactive**: true — included in RAG retrieval
- **sprk_issystem**: true — non-editable by end users
- **sprk_tenantid**: "system" — shared across all tenants (ADR-014)

**Entity**: `sprk_content` | **Collection**: `sprk_contents`
**Seeded By**: AIPL-032 | **Environment**: spaarkedev1.crm.dynamics.com

**AI Search indexing**: Performed post-AIPL-018 deployment via `POST /api/ai/knowledge/index/batch` with `tenantId="system"`.
**Index**: `knowledge-index` (spaarke-search-dev.search.windows.net)
**Semantic config**: `knowledge-semantic-config`

### Knowledge Source Catalog

| External ID | Name | Content Summary |
|-------------|------|----------------|
| KNW-001 | Common Contract Terms Glossary | Definitions for 50+ standard contract terms (A–W): acceptance, amendment, arbitration, assignment, at-will, breach, cap on liability, change order, confidential information, consequential damages, counterparts, cure period, default, deliverable, dispute resolution, effective date, entire agreement, escrow, exclusivity, force majeure, governing law, good faith, indemnification, IP, limitation of liability, liquidated damages, MAC, milestone, non-solicitation, notice, representations and warranties, renewal, severability, specific performance, subcontractor, term, termination for cause, termination for convenience, warranty, waiver |
| KNW-002 | NDA Review Checklist | 20-item checklist for NDA review: party identification, purpose, confidential information definition and scope, standard exclusions, standard of care, permitted use, permitted disclosure to representatives, required disclosures (legal process), agreement term, survival of obligations, return/destruction of information, injunctive relief, damages limitations, no license provision, residuals clause, mutual vs. one-way structure, non-solicitation, relationship disclaimer, governing law, and general boilerplate provisions |
| KNW-003 | Lease Agreement Standards | Commercial lease standards: base rent and escalation (fixed, CPI, fair market), operating expense structures (gross, NN, NNN, modified gross, full-service gross), controllable vs. uncontrollable expense caps, premises definition, permitted use, exclusive use rights, lease term, renewal/expansion/termination options, tenant improvement allowance, alterations, assignment and subletting, insurance requirements (CGL, property, workers' comp, umbrella), mutual waiver of subrogation, indemnification, events of default, landlord remedies, self-help rights, holdover provisions |
| KNW-004 | Invoice Processing Guide | AP invoice processing: invoice types (standard, recurring, progress billing, retainage, credit memo, pro forma, self-billing), required fields (vendor ID, date fields, buyer fields, PO number, line items, financial summary), three-way and two-way matching, matching tolerances (price ±1%, quantity ±2%, total ±0.5%), duplicate detection, vendor master file validation, PO validation, contract compliance, tax validation, exception handling (10 exception codes with resolution), escalation matrix, vendor credit memo process, payment terms (Net 30/60/90, 2/10 Net 30, EOM, CIA, COD), early payment discount capture, approval workflow with delegation rules |
| KNW-005 | SLA Metrics Reference | SLA/SLO/SLI definitions, availability tiers (99.0% through 99.999% with annual/monthly downtime), scheduled maintenance exclusions, force majeure exclusions, response time/latency metrics (p50/p95/p99/p99.9 by service type), throughput (RPS/TPS/MPS), error rate targets, incident severity levels (P1–P4) with response time and resolution SLAs, time to restore service, data durability (11 nines for object storage), RPO/RTO tiers, tiered service credit schedules, credit caps and exclusive remedy provisions, credit claim procedure, measurement methodology (synthetic monitoring, RUM), monthly availability reporting |
| KNW-006 | Employment Law Quick Reference | US employment law fundamentals: at-will employment and exceptions (implied contracts, anti-discrimination, public policy, good faith), employee vs. independent contractor classification (behavioral/financial/type of relationship tests, California ABC test), FLSA minimum wage and overtime (white-collar exemptions, salary basis test), pay frequency and final pay, non-compete agreements (state-by-state enforceability, reasonableness factors, FTC rule status), non-solicitation agreements, employee confidentiality obligations, DTSA immunity notice requirement, work made for hire, invention assignment and moonlighting protection statutes (CA, DE, IL, MN, WA), prior IP exclusions, FMLA, ADA reasonable accommodation, USERRA, state paid leave programs, WARN Act, separation agreements and OWBPA requirements for ADEA releases |
| KNW-007 | IP Assignment Clause Library | Annotated IP assignment clauses: broad work product assignment (employer-favorable, three-part scope), work-performed-for assignment (narrower/balanced), independent contractor work-for-hire and assignment (dual structure: WFH + fallback), background IP definition and license-back (limited to incorporated use), prior inventions exclusion schedule with automatic license-back, AI/ML-specific model and training data assignment, open source compliance clause (copyleft restrictions), moral rights waiver, future patents assignment with power of attorney, moonlighting protection statute compliance |
| KNW-008 | Termination and Remedy Provisions | Comprehensive termination and remedy reference: termination for cause triggers (material breach, insolvency/bankruptcy, breach of R&Ws, regulatory events), notice and cure periods by default type, automatic vs. right-to-terminate, termination for convenience (T4C) with standard notice periods, termination fees and break fees, post-termination transition assistance (90–180 days), return and destruction of data, outstanding payment reconciliation, survival of provisions (confidentiality, IP, indemnification, limitation of liability, governing law), money damages types (direct, consequential, incidental, reliance, restitution), mutual waiver of consequential damages with carve-outs, liquidated damages enforceability, injunctive and equitable relief, exclusive remedy provisions, set-off rights |
| KNW-009 | Governing Law and Jurisdiction Guide | Governing law and dispute resolution reference: what governing law determines (interpretation, damages, statute of limitations, UCC), common US law choices (Delaware, New York, California, Texas, Illinois) with key features, international choices (English law, New York, CISG exclusion), conflict-of-law exclusions, exclusive vs. non-exclusive jurisdiction, federal vs. state court selection, forum non conveniens waiver, service of process, arbitration (when to use, major bodies — AAA/JAMS/ICC/LCIA/SIAC/ICDR), seat vs. venue of arbitration, essential arbitration clause elements, carve-outs from arbitration (injunctive relief, IP, small claims), tiered dispute resolution (negotiation → mediation → arbitration/litigation), interim relief pending arbitration, New York Convention (170+ countries), court judgment enforcement internationally |
| KNW-010 | Legal Document Red Flags Catalog | 32 red flags across 10 categories with severity ratings: liability/indemnification (uncapped indemnification [CRITICAL], asymmetric indemnification, unlimited IP indemnification, consequential damages waiver without carve-outs), payment/financial (automatic escalation without cap, unilateral price change right [CRITICAL], payment before delivery, no cure period), termination (no T4C right, immediate acceleration upon termination [CRITICAL], vendor T4C on short notice), data/privacy (vendor data ownership claim [CRITICAL], broad data use for third parties, no data return/deletion obligation), IP (broad work product ownership, no IP indemnification, overbroad license to customer IP), contract structure (automatic renewal without notification window, unilateral modification right [CRITICAL], incorporation by URL reference "as amended"), dispute resolution (one-sided forum selection, class action waiver, vendor-preferred arbitration), confidentiality (residuals clause, short survival for trade secrets), employment/personnel (one-sided non-solicitation, inadequate vendor personnel security), miscellaneous (broad data legality representation, no audit right, missing force majeure, no consent right on vendor change of control, survival clause omitting key obligations) |

### Alternate Key Lookup

```
GET /api/data/v9.2/sprk_contents(sprk_externalid='KNW-001')
    ?$select=sprk_name,sprk_externalid,sprk_contenttext,sprk_contenttype,sprk_tenantid
```

### Content File Locations

Full content for each knowledge source is version-controlled in:

| External ID | Content File |
|-------------|-------------|
| KNW-001 | `notes/design/knowledge-sources/KNW-001-contract-terms-glossary.md` |
| KNW-002 | `notes/design/knowledge-sources/KNW-002-nda-checklist.md` |
| KNW-003 | `notes/design/knowledge-sources/KNW-003-lease-agreement-standards.md` |
| KNW-004 | `notes/design/knowledge-sources/KNW-004-invoice-processing-guide.md` |
| KNW-005 | `notes/design/knowledge-sources/KNW-005-sla-metrics-reference.md` |
| KNW-006 | `notes/design/knowledge-sources/KNW-006-employment-law-quick-reference.md` |
| KNW-007 | `notes/design/knowledge-sources/KNW-007-ip-assignment-clause-library.md` |
| KNW-008 | `notes/design/knowledge-sources/KNW-008-termination-and-remedy-provisions.md` |
| KNW-009 | `notes/design/knowledge-sources/KNW-009-governing-law-and-jurisdiction-guide.md` |
| KNW-010 | `notes/design/knowledge-sources/KNW-010-legal-red-flags-catalog.md` |

Creation script: `scripts/Create-KnowledgeSourceRecords.ps1`

---

## Usage Notes

### Alternate Key Lookup

Tools are resolved by code via the Dataverse alternate key on `sprk_toolcode`. Example OData query:

```
GET /api/data/v9.2/sprk_analysistools(sprk_toolcode='TL-001')
    ?$select=sprk_name,sprk_toolcode,sprk_handlerclass,sprk_configuration
```

This is how `ToolLookupService.GetByCodeAsync("TL-001")` operates internally.

### Handler Class Constraints

Handler class names MUST remain stable. Changing `sprk_handlerclass` on a tool record will break `MapHandlerClassToToolType()` resolution in `ScopeResolverService` unless the mapping switch expression is also updated. The C# handler implementations (Workstream C) must use these exact class names.

---

---

## Playbooks (sprk_aiplaybook)

Playbooks are the top-level orchestration definitions that compose Actions, Skills, Knowledge Sources, and Tools into a complete document analysis workflow. Each playbook stores its scope composition in a `sprk_canvasjson` field as valid JSON with external code references (ACT-*, SKL-*, KNW-*, TL-*) resolved at runtime by `PlaybookScopeResolver`.

- **sprk_externalid**: Alternate key (PB-001 through PB-010) used for deterministic lookup
- **sprk_canvasjson**: Valid JSON encoding the scope composition — `actionId`, `skillIds`, `knowledgeSourceIds`, `toolIds`, and `configuration` (temperature, maxTokens, streaming)
- **sprk_targetdocumenttype**: The primary document category this playbook is designed for
- **sprk_isactive**: true — selectable in AnalysisWorkspace playbook dropdown
- **sprk_issystem**: true — non-editable by end users

**Entity**: `sprk_aiplaybook` | **Collection**: `sprk_aiplaybooks`
**Seeded By**: AIPL-034 | **Environment**: spaarkedev1.crm.dynamics.com

**Canvas JSON schema** (stored in `sprk_canvasjson`):
```json
{
  "actionId": "ACT-001",
  "skillIds": ["SKL-001", "SKL-002"],
  "knowledgeSourceIds": ["KNW-001"],
  "toolIds": ["TL-001", "TL-002"],
  "configuration": {
    "temperature": 0.3,
    "maxTokens": 2000,
    "streaming": true
  }
}
```

**Runtime resolution**: `PlaybookScopeResolver` resolves each code reference (e.g., `"ACT-001"`) to the corresponding Dataverse record ID via alternate key lookup before execution. Canvas JSON is environment-portable.

### Playbook Catalog

| External ID | Name | Target Document Type | Description |
|-------------|------|---------------------|-------------|
| PB-001 | Standard Contract Review | Contract | Comprehensive review of commercial contracts, MSAs, and vendor agreements. Extracts parties, obligations, key dates, and identifies risk clauses. Generates executive summary with citation support. |
| PB-002 | NDA Deep Review | NDA | Deep review of non-disclosure and confidentiality agreements. Identifies all parties, extracts key dates, flags unbalanced obligations and unfavorable residuals clauses. Includes red-flag risk detection. |
| PB-003 | Commercial Lease Analysis | Lease | Analysis of commercial and residential lease agreements. Generates executive summary, maps landlord and tenant obligations, and identifies lease terms, renewal options, and maintenance responsibilities. |
| PB-004 | Invoice Validation | Invoice | Validation of vendor invoices and AP documents. Extracts key dates and payment terms, identifies financial amounts and schedules, and cross-references against company knowledge sources for compliance. |
| PB-005 | SLA Compliance Review | SLA | Review of service level agreements and managed service contracts. Flags risk clauses around SLO commitments and credit provisions, maps vendor and client obligations, and retrieves relevant SLA benchmarks. |
| PB-006 | Employment Agreement Review | EmploymentAgreement | Review of employment contracts, offer letters, and contractor agreements. Identifies all parties, extracts defined terms, analyzes termination provisions, and maps obligations for both employer and employee. |
| PB-007 | Statement of Work Analysis | StatementOfWork | Analysis of statements of work, task orders, and project agreements. Generates executive summary of deliverables and milestones, maps mutual obligations, and retrieves relevant SLA and performance benchmarks. |
| PB-008 | IP Assignment Review | Contract | Review of intellectual property assignment clauses in contracts and employment agreements. Extracts and glossarizes all defined IP terms, analyzes termination and reversion rights, and detects overbroad assignment language. |
| PB-009 | Termination Risk Assessment | Contract | Focused assessment of termination risk in contracts. Flags high-severity risk clauses, provides full termination analysis of triggers and cure periods, and retrieves reference provisions on termination and remedy standards. |
| PB-010 | Quick Legal Scan | LegalDocument | Fast risk-focused scan of any legal document. Flags high-priority risk clauses and identifies applicable law and jurisdiction. Optimized for speed — use when a full review is not required but red flags must be surfaced quickly. |

### Scope Composition Detail

| External ID | Action | Skills | Knowledge Sources | Tools |
|-------------|--------|--------|-------------------|-------|
| PB-001 | ACT-001 | SKL-001, SKL-002, SKL-003 | KNW-001 | TL-001, TL-002 |
| PB-002 | ACT-002 | SKL-001, SKL-004, SKL-005 | KNW-002 | TL-001, TL-002, TL-007 |
| PB-003 | ACT-003 | SKL-003, SKL-006 | KNW-003 | TL-001, TL-002 |
| PB-004 | ACT-004 | SKL-004, SKL-008 | KNW-004 | TL-001, TL-003 |
| PB-005 | ACT-005 | SKL-002, SKL-006 | KNW-005 | TL-001, TL-002, TL-007 |
| PB-006 | ACT-006 | SKL-005, SKL-007, SKL-009 | KNW-006, KNW-007 | TL-001, TL-002 |
| PB-007 | ACT-007 | SKL-003, SKL-006 | KNW-005 | TL-001, TL-002 |
| PB-008 | ACT-001 | SKL-007, SKL-009 | KNW-007 | TL-001, TL-002, TL-008 |
| PB-009 | ACT-001 | SKL-002, SKL-009 | KNW-008 | TL-001, TL-007 |
| PB-010 | ACT-008 | SKL-002, SKL-010 | KNW-010 | TL-001, TL-007 |

### Alternate Key Lookup

```
GET /api/data/v9.2/sprk_aiplaybooks(sprk_externalid='PB-001')
    ?$select=sprk_name,sprk_externalid,sprk_canvasjson,sprk_targetdocumenttype,sprk_isactive,sprk_issystem
```

Creation script: `scripts/Create-PlaybookSeedRecords.ps1`

---

*Catalog maintained by: AIPL-030 (Actions), AIPL-031 (Skills), AIPL-032 (Knowledge Sources), AIPL-033 (Tools), AIPL-034 (Playbooks) seed data tasks*
