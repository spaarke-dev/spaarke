# AI File Entity Metadata Extraction

> **Last Updated**: 2025-12-09
>
> **Status**: In Progress

## Overview

This project enhances the existing AI Document Summary feature to provide structured metadata extraction from uploaded files, enabling improved readability via TL;DR bullet-point summaries, enhanced searchability via AI-extracted keywords, automated entity extraction (organizations, people, amounts, dates, references), and intelligent document-to-record matching for Dataverse records.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan and WBS |
| [Design Spec](./spec.md) | Technical design specification |
| [Tasks](./tasks/TASK-INDEX.md) | Task breakdown and status |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development |
| **Progress** | 0% |
| **Owner** | Spaarke Team |

## Problem Statement

Current document processing has several pain points:
1. **Prose summaries are slow to scan** - Users want quick bullet-point TL;DR format
2. **Search is keyword-dependent** - Users must know exact terms; AI-extracted keywords improve recall
3. **Manual record association** - Users must manually link files to Matters/Projects/Invoices
4. **Email processing gap** - Server-side sync brings emails but no intelligent linking

## Solution Summary

The system will generate prose summaries AND bullet-point TL;DRs, extract searchable keywords automatically, identify entities (organizations, people, amounts, dates, references), allow users to select target record types for matching, and suggest matching Dataverse records based on extracted entities. Additionally, the existing "Summarize" services will be renamed to "DocumentIntelligence" as these become the gateway to all Azure AI Document Intelligence capabilities.

## Graduation Criteria

The project is considered **complete** when:

- [ ] All services renamed from "Summarize" to "DocumentIntelligence"
- [ ] AI returns structured JSON with summary, TL;DR, keywords, and entities
- [ ] Email files (.eml, .msg) can be analyzed with subject line included
- [ ] All structured fields saved to Dataverse (`sprk_filetldr`, `sprk_fileentities`, `sprk_documenttype`)
- [ ] PCF displays TL;DR as bullet list, keywords as tags, entities in collapsible section
- [ ] Dataverse Relevance Search finds documents by AI-extracted keywords
- [ ] Azure AI Search index contains Dataverse records (Phase 2)
- [ ] Record matching API returns ranked suggestions with lookup field names (Phase 2)
- [ ] PCF displays suggested record matches with one-click association (Phase 2)
- [ ] All unit and integration tests pass

## Scope

### In Scope

**Phase 1a: Structured AI Output + Service Rename**
- Rename services from "Summarize" to "DocumentIntelligence"
- Update endpoint paths: `/api/ai/summarize/*` → `/api/ai/document-intelligence/*`
- Structured JSON output with summary, TL;DR, keywords, entities
- Fallback when JSON parsing fails

**Phase 1b: Dataverse + PCF Integration + Email Support**
- Store structured fields in Dataverse
- Enable Relevance Search on `sprk_filekeywords`
- Update PCF to display TL;DR, keywords, entities
- Email file support (.eml, .msg) with subject line in analysis

**Phase 2: Record Matching Service**
- Azure AI Search index for Dataverse records
- Record type filter (Matters/Projects/Invoices/All)
- Match API with confidence scores and reasoning
- PCF record type selector and match suggestions
- One-click record association with lookup field population

### Out of Scope

- Email server-side sync implementation (separate project)
- Full RAG pipeline for document Q&A
- Multi-language document support
- Historical re-analysis of existing documents (manual trigger only)
- Automatic record association (always requires user confirmation)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Rename Summarize → DocumentIntelligence | Service evolves to comprehensive document analysis gateway | — |
| Use Azure AI Search for record matching | Hybrid search with vector + keyword, filtering support | ADR-013 |
| Extend BFF, not separate AI service | AI Tool Framework per ADR-013 | [ADR-013](../../docs/reference/adr/ADR-013-ai-architecture.md) |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| JSON parsing failures | Degraded UX | Medium | Robust fallback to raw text |
| Entity extraction inaccuracy | Wrong matches | Medium | Confidence thresholds, user confirmation |
| Azure AI Search costs | Budget | Low | Monitor usage, implement caching |
| Email parsing edge cases | Missing data | Medium | Extensive test coverage |
| Service rename breaking changes | Integration issues | Low | Update all clients in same release |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Azure OpenAI | External | Ready | Existing resource |
| Azure AI Search | External | New (Phase 2) | Needs provisioning |
| MimeKit NuGet | External | New | EML parsing |
| MsgReader NuGet | External | New | MSG parsing |
| Dataverse solution update | Internal | Pending | New fields on sprk_document |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2025-12-09 | 1.0 | Initial project setup | Claude Code |

---

*Based on Spaarke development lifecycle*
