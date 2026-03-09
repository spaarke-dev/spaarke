# AI Document Analysis Enhancements

> **Created**: January 4, 2026
> **Status**: Collecting Requirements
> **Related Project**: AI Document Intelligence R3

---

## Overview

This project captures enhancement requests for the AI Document Analysis features, including the AnalysisWorkspace PCF control and related API endpoints. Enhancements are collected here for prioritization and future implementation.

### Enhancement Summary

| ID | Title | Priority | Component |
|----|-------|----------|-----------|
| ENH-001 | Chat Context Switching | High | PCF |
| ENH-002 | Predefined Prompts (Copilot-Style) | High | PCF |
| ENH-003 | Prompt Library Management | Medium | PCF, API |
| ENH-004 | AI Wording Refinement | High | PCF, API |
| ENH-005 | Deviation Detection & Scoring | Medium | API |
| ENH-006 | Ambiguity & Error Detection | Medium | API |
| ENH-007 | Multi-User Annotations | Low | PCF, Dataverse |
| ENH-008 | Document Version Comparison | Low | PCF, API |
| ENH-009 | Seed Data: Actions & Skills | High | Dataverse |
| ENH-010 | Seed Data: Knowledge Sources | High | Dataverse, API |
| ENH-011 | Seed Data: Sample Playbooks | High | Dataverse |
| ENH-012 | End-to-End Playbook Testing | High | All |

---

## Enhancement Backlog

### ENH-001: Chat Context Switching

**Priority**: High
**Component**: AnalysisWorkspace PCF
**Status**: Proposed

**Current Behavior**:
- Chat functionality only interacts with the document preview (raw file content)
- Users cannot ask questions about the analysis output

**Desired Behavior**:
- Users can select chat context: "Document" or "Analysis"
- When "Document" is selected, AI responds based on file content
- When "Analysis" is selected, AI responds based on the generated analysis output
- Visual indicator shows which context is active

**User Story**:
> As a user, I want to chat with either my document or the analysis results so that I can get clarification on specific findings or dig deeper into source content.

**Technical Notes**:
- May require separate system prompts per context
- Analysis context needs access to structured analysis output (JSON or formatted text)
- Consider caching analysis output for chat context

---

### ENH-002: Predefined Prompts (Copilot-Style)

**Priority**: High
**Component**: AnalysisWorkspace PCF
**Status**: Proposed

**Current Behavior**:
- Chat input is free-form text only
- Users must type all questions manually

**Desired Behavior**:
- Provide predefined prompt suggestions (Copilot-style chips/buttons)
- Suggestions context-aware based on:
  - Document type (contract, invoice, report)
  - Analysis playbook used
  - Current chat context (document vs. analysis)
- Examples:
  - "Summarize key findings"
  - "What are the risk items?"
  - "Explain this clause"
  - "Compare to standard terms"

**User Story**:
> As a user, I want quick-access prompts so that I can efficiently explore my document and analysis without typing common questions.

**Technical Notes**:
- Prompt suggestions could be:
  - Static per playbook
  - Dynamic based on analysis results
  - User-customizable favorites
- UI pattern: Chips above input, expandable "/prompts" menu (per screenshot)

---

### ENH-003: Prompt Library Management

**Priority**: Medium
**Component**: AnalysisWorkspace PCF, API
**Status**: Proposed

**Current Behavior**:
- No saved prompts functionality

**Desired Behavior**:
- Users can save favorite prompts
- Organization-level prompt templates
- Prompts categorized by use case
- "/prompts" command to access library

**User Story**:
> As a user, I want to save and reuse effective prompts so that I don't have to remember or retype them.

**Technical Notes**:
- Storage: Dataverse entity (sprk_aiprompttemplate)
- Scope: User-level and Org-level
- Consider integration with playbook system

---

---

### ENH-004: AI Wording Refinement

**Priority**: High
**Component**: AnalysisWorkspace PCF, API
**Status**: Proposed

**Current Behavior**:
- Analysis output is read-only
- Users cannot request alternative wording for flagged clauses

**Desired Behavior**:
- Users can select a clause or finding and request AI wording suggestions
- AI proposes alternative language based on:
  - Playbook standards
  - Risk mitigation goals
  - Negotiation fallback positions
- Users can copy suggested text or export to Word

**User Story**:
> As a legal professional, I want AI to suggest improved wording for risky clauses so that I can quickly draft counterproposals.

**Technical Notes**:
- Requires context: original clause + playbook standards + risk level
- May need "improvement mode" prompts (e.g., "make more favorable to us")
- Consider integration with export formats (DOCX redlines)

**Industry Reference**: Spellbook, LegalOn AI Revise, Juro Redlining Agent

---

### ENH-005: Deviation Detection & Scoring

**Priority**: Medium
**Component**: API (Analysis Orchestration)
**Status**: Proposed

**Current Behavior**:
- ClauseAnalyzer identifies clauses and risks
- No comparison against organizational standards

**Desired Behavior**:
- Compare extracted clauses against playbook "standard positions"
- Flag deviations with severity rating
- Generate compliance score (e.g., "78% aligned with standard terms")
- Highlight specific differences from expected language

**User Story**:
> As a contract reviewer, I want to see how this contract compares to our standard terms so that I can focus on the deviations that matter.

**Technical Notes**:
- Requires playbook entity to store "standard clause" templates
- Semantic similarity comparison (not just keyword matching)
- Consider RAG retrieval of similar past contracts

**Industry Reference**: Ironclad clause flagging, LEGALFLY deviation detection

---

### ENH-006: Ambiguity & Error Detection

**Priority**: Medium
**Component**: API (New Tool Handler)
**Status**: Proposed

**Current Behavior**:
- Analysis focuses on clause identification and risk
- No specific checks for linguistic issues

**Desired Behavior**:
- Detect ambiguous language ("reasonable efforts", undefined terms)
- Identify potential errors (date inconsistencies, calculation mistakes)
- Flag internal contradictions within the document
- Highlight undefined acronyms or terms

**User Story**:
> As a legal reviewer, I want AI to catch linguistic issues and potential errors so that I don't miss problems during review.

**Technical Notes**:
- New tool handler: `AmbiguityDetectorHandler`
- Cross-reference defined terms section with usage
- Date/number consistency checks

**Industry Reference**: Spellbook error detection, Harvey AI analysis

---

### ENH-007: Multi-User Annotations

**Priority**: Low
**Component**: AnalysisWorkspace PCF, Dataverse
**Status**: Proposed

**Current Behavior**:
- Analysis results are single-user view
- No way to add team comments or annotations

**Desired Behavior**:
- Team members can add comments to specific findings
- Comments persist with the analysis record
- @mention colleagues for review
- Track annotation history

**User Story**:
> As a team lead, I want to annotate analysis findings and assign follow-ups so that my team can collaborate on complex reviews.

**Technical Notes**:
- New Dataverse entity: `sprk_analysisannotation`
- Relationship to analysis record + specific finding (by index/ID)
- Real-time updates via SignalR or polling

---

### ENH-008: Document Version Comparison

**Priority**: Low
**Component**: API, AnalysisWorkspace PCF
**Status**: Proposed

**Current Behavior**:
- Each analysis is independent
- No comparison between document versions

**Desired Behavior**:
- Compare analysis results between two versions of the same document
- Highlight what changed (new clauses, removed risks, modified terms)
- Track negotiation progress across rounds

**User Story**:
> As a negotiator, I want to see what changed between contract versions so that I can track negotiation progress.

**Technical Notes**:
- Requires version linkage (parent document ID)
- Diff algorithm for structured analysis output
- Consider storing analysis snapshots

---

### ENH-009: Seed Data: Actions & Skills

**Priority**: High
**Component**: Dataverse
**Status**: Proposed

**Background**:
R3 spec (Phase 3) specified "Seed data: 5 Actions, 10 Skills, 5 Knowledge sources" but only the infrastructure was built. No actual seed data records were created.

**Required Actions (5)**:
| Action Name | Description |
|-------------|-------------|
| Extract Entities | Extract key entities (parties, dates, amounts) from document |
| Analyze Clauses | Identify and analyze contract clauses |
| Classify Document | Determine document type and category |
| Summarize Content | Generate executive summary of document |
| Detect Risks | Identify potential risks and compliance issues |

**Required Skills (10)**:
| Skill Name | Description |
|------------|-------------|
| Contract Analysis | Full contract review workflow |
| Invoice Processing | Invoice data extraction and validation |
| NDA Review | Non-disclosure agreement analysis |
| Lease Review | Commercial/residential lease analysis |
| Employment Contract | Employment agreement review |
| SLA Analysis | Service level agreement evaluation |
| Compliance Check | Regulatory compliance verification |
| Executive Summary | High-level document summarization |
| Risk Assessment | Comprehensive risk identification |
| Clause Comparison | Compare clauses to standards |

**Acceptance Criteria**:
- [ ] 5 Action records created in Dataverse (sprk_aiaction)
- [ ] 10 Skill records created in Dataverse (sprk_aiskill)
- [ ] Actions linked to appropriate Tool handlers
- [ ] Skills linked to relevant Actions
- [ ] Records accessible via Admin forms

---

### ENH-010: Seed Data: Knowledge Sources

**Priority**: High
**Component**: Dataverse, API
**Status**: Proposed

**Background**:
R3 spec specified 5 Knowledge sources but none were created. Knowledge sources provide context for RAG-enhanced analysis.

**Required Knowledge Sources (5)**:
| Source Name | Description | Content Type |
|-------------|-------------|--------------|
| Standard Contract Terms | Organization's standard clause library | Internal |
| Regulatory Guidelines | Industry compliance requirements | Reference |
| Best Practices | Legal review best practices | Internal |
| Risk Categories | Risk classification taxonomy | Internal |
| Defined Terms | Standard definitions and acronyms | Reference |

**Technical Requirements**:
- Each Knowledge source needs:
  - Dataverse record (sprk_aiknowledge)
  - Associated documents/content
  - Embeddings generated and indexed in Azure AI Search
  - RAG retrieval tested

**Acceptance Criteria**:
- [ ] 5 Knowledge source records created in Dataverse
- [ ] Sample content documents created for each source
- [ ] Documents embedded via EmbeddingService
- [ ] Content indexed in spaarke-knowledge-index
- [ ] RAG retrieval returns relevant context

---

### ENH-011: Seed Data: Sample Playbooks

**Priority**: High
**Component**: Dataverse
**Status**: Proposed

**Background**:
The PlaybookService infrastructure was built in R3 but no actual Playbooks were created. Users need sample playbooks to understand the system.

**Required Playbooks (5)**:
| Playbook Name | Target Document Type | Skills Used |
|---------------|---------------------|-------------|
| Quick Contract Review | Any Contract | Extract Entities, Detect Risks |
| Full NDA Analysis | Non-Disclosure Agreements | NDA Review, Clause Comparison |
| Invoice Validation | Invoices | Invoice Processing |
| Lease Due Diligence | Commercial Leases | Lease Review, Risk Assessment |
| Comprehensive Contract | Complex Contracts | All Skills |

**Required Tool Configurations**:
| Tool Name | Handler | Parameters |
|-----------|---------|------------|
| Entity Extractor | EntityExtractorHandler | entity_types, confidence_threshold |
| Clause Analyzer | ClauseAnalyzerHandler | risk_threshold, clause_categories |
| Document Classifier | DocumentClassifierHandler | classification_taxonomy |

**Acceptance Criteria**:
- [ ] 5 Playbook records created (sprk_aiplaybook)
- [ ] Each Playbook links to appropriate Skills
- [ ] Tool configuration records created for each tool
- [ ] Playbooks visible in AnalysisWorkspace dropdown
- [ ] Sample playbook documentation created

---

### ENH-012: End-to-End Playbook Testing

**Priority**: High
**Component**: All (PCF, API, Dataverse)
**Status**: Proposed

**Background**:
With seed data created (ENH-009 through ENH-011), comprehensive end-to-end testing is needed to verify the complete workflow.

**Test Scenarios**:

1. **Playbook Selection Flow**
   - User opens AnalysisWorkspace with document
   - User selects "Quick Contract Review" playbook
   - System loads playbook configuration
   - Analysis runs with playbook settings

2. **Full Analysis Workflow**
   - Upload test contract document
   - Select "Full NDA Analysis" playbook
   - Verify EntityExtractor runs and returns entities
   - Verify ClauseAnalyzer identifies clauses
   - Verify DocumentClassifier categorizes document
   - Verify RAG context retrieved from Knowledge sources
   - Verify analysis output formatted correctly
   - Test export to DOCX/PDF

3. **Playbook CRUD Operations**
   - Create new playbook via Admin form
   - Link Skills to playbook
   - Edit playbook configuration
   - Share playbook with team
   - Delete playbook

4. **Negative Testing**
   - Playbook with missing Skills
   - Knowledge source with no content
   - Tool handler timeout/failure

**Test Documents Required**:
| Document | Type | Purpose |
|----------|------|---------|
| sample-nda.pdf | NDA | Full NDA analysis test |
| sample-contract.pdf | Contract | Quick review test |
| sample-lease.pdf | Lease | Lease review test |
| sample-invoice.pdf | Invoice | Invoice processing test |
| malformed-doc.pdf | Invalid | Error handling test |

**Acceptance Criteria**:
- [ ] All 4 test scenarios pass
- [ ] Test documents created and stored
- [ ] Playbook UI works end-to-end
- [ ] RAG retrieval improves analysis quality
- [ ] Export produces valid documents
- [ ] Error handling tested

---

## Future Enhancements

*Add additional enhancement requests below as they are identified.*

---

## Implementation Notes

### Dependencies
- AnalysisWorkspace PCF v1.2.7+ (deployed in R3)
- Analysis orchestration endpoints (deployed in R3)

### Considerations
- These enhancements build on R3 foundation
- Should be implemented after R3 deployment stabilizes
- May require API changes for analysis context in chat

---

## Changelog

| Date | Change |
|------|--------|
| 2026-01-04 | Initial document created with ENH-001, ENH-002, ENH-003 |
| 2026-01-04 | Added ENH-004 through ENH-008 from user feedback |
| 2026-01-04 | Added ENH-009 through ENH-012 for R3 seed data gap (playbooks, actions, skills, knowledge) |

