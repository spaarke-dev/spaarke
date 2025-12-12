# Project Plan: AI File Entity Metadata Extraction

> **Last Updated**: 2025-12-11
>
> **Status**: Complete
>
> **Related**: [Project README](./README.md) | [Design Spec](./spec.md)

---

## 1. Executive Summary

### 1.1 Purpose

Enhance the existing AI Document Summary feature to provide structured metadata extraction from uploaded files, enabling TL;DR bullet-point summaries, AI-extracted keywords for search, entity extraction, and intelligent document-to-record matching.

### 1.2 Business Value

- **Reduced time to review documents**: TL;DR format enables 80% faster scanning
- **Improved search success rate**: +25% via AI-extracted keywords
- **Reduced manual effort**: -50% time to associate documents to records
- **Enhanced data quality**: Automated entity extraction vs. manual entry

### 1.3 Success Criteria

| Metric | Target | Measurement Method |
|--------|--------|-------------------|
| User satisfaction with TL;DR | >80% positive | User feedback survey |
| Search success improvement | +25% | A/B test with/without keywords |
| Record match acceptance rate | >60% | Click-through on suggestions |
| Time to associate document | -50% | Before/after comparison |

---

## 2. Background & Context

### 2.1 Current State

The Spaarke platform currently provides:
- Document upload via UniversalQuickCreate PCF control
- AI-powered document summarization (prose format, 2-4 paragraphs)
- Storage in SharePoint Embedded (SPE) with metadata in Dataverse
- Basic file association to Dataverse records (manual user selection)

### 2.2 Desired State

Users upload a document or receive an email via server-side sync, and the system:
1. Generates a prose summary AND bullet-point TL;DR
2. Extracts searchable keywords automatically
3. Identifies entities (organizations, people, amounts, dates, references)
4. Suggests matching Dataverse records based on extracted entities
5. Enables one-click association with automatic lookup field population

### 2.3 Gap Analysis

| Area | Current State | Desired State | Gap |
|------|--------------|---------------|-----|
| Summary format | Prose only | Prose + TL;DR bullets | Add TL;DR extraction |
| Search | Manual keywords | AI-extracted keywords | Add keyword extraction |
| Entities | None | Organizations, people, dates, amounts | Add entity extraction |
| Record linking | Manual selection | AI suggestions | Add matching service |
| Email support | Not processed | Full analysis | Add email extractors |

---

## 3. Solution Overview

### 3.1 Approach

1. **Service Rename**: Rename "Summarize" → "DocumentIntelligence" across codebase
2. **Structured Output**: Update AI prompt for JSON response with summary, TL;DR, keywords, entities
3. **Email Support**: Add extractors for .eml and .msg files
4. **Dataverse Storage**: Add new fields for structured data
5. **PCF Updates**: Display TL;DR, keywords, entities in UI
6. **Record Matching** (Phase 2): Azure AI Search index + matching API

### 3.2 Architecture Impact

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           User Interface                                 │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │  UniversalQuickCreate PCF                                        │    │
│  │  - File Upload → AI Analysis → Display Summary/TL;DR/Entities   │    │
│  │  - Record Type Selector (Matters/Projects/Invoices/All)         │    │
│  │  - Show Suggested Record Matches → One-Click Association        │    │
│  └─────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         Spaarke BFF API                                  │
│  ┌──────────────────────────┐  ┌──────────────────────────────────┐    │
│  │ DocumentIntelligenceService│ │ RecordMatchService               │    │
│  │ - Prompt mgmt              │ │ - Query AI Search                │    │
│  │ - AI streaming             │ │ - Apply record type filter       │    │
│  │ - JSON parsing             │ │ - Rank matches                   │    │
│  │ - Entity extraction        │ │ - Return suggestions             │    │
│  └──────────────────────────┘  └──────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────┘
```

### 3.3 Key Technical Decisions

| Decision | Options Considered | Selected | Rationale |
|----------|-------------------|----------|-----------|
| AI output format | Streaming text, JSON | Streaming with JSON at end | Real-time feedback + structured result |
| Email parsing | Azure Document Intelligence, Libraries | MimeKit + MsgReader | Cost-effective, no external dependency |
| Record matching | Dataverse search, Azure AI Search | Azure AI Search | Hybrid vector+keyword, better ranking |

---

## 4. Scope Definition

### 4.1 In Scope

| Item | Description | Priority |
|------|-------------|----------|
| Service rename | Summarize → DocumentIntelligence throughout | Must Have |
| Structured JSON output | Summary, TL;DR, keywords, entities | Must Have |
| Email file support | .eml and .msg extraction | Must Have |
| Dataverse fields | Store TL;DR, keywords, entities, document type | Must Have |
| PCF TL;DR display | Bullet list in AI Summary panel | Must Have |
| PCF keywords display | Tags/chips below summary | Should Have |
| PCF entities display | Collapsible section | Should Have |
| Azure AI Search index | Dataverse record indexing | Must Have (Phase 2) |
| Record matching API | Ranked suggestions with confidence | Must Have (Phase 2) |
| PCF record suggestions | Display matches, one-click association | Must Have (Phase 2) |

### 4.2 Out of Scope

| Item | Reason | Future Consideration |
|------|--------|---------------------|
| Email server-side sync | Separate project | No |
| Full RAG pipeline | Complexity, different use case | Phase 3 |
| Multi-language support | Scope creep | Phase 3 |
| Historical re-analysis | Performance concern | Manual trigger only |
| Automatic record association | User confirmation required | No |

### 4.3 Assumptions

- Azure OpenAI resource is available and configured
- Existing AI summarization feature is working
- UniversalQuickCreate PCF control can be extended
- Dataverse solution can be updated with new fields

### 4.4 Constraints

- Must follow ADR-013 (AI Architecture): Extend BFF, not separate service
- Must maintain backward compatibility during rename transition
- Phase 2 requires new Azure AI Search resource
- Email parsing libraries must be .NET 8 compatible

---

## 5. Work Breakdown Structure

### 5.1 Phase 1a: Structured AI Output + Service Rename

| Task | Description | Dependencies |
|------|-------------|--------------|
| Rename service files | SummarizeService → DocumentIntelligenceService | — |
| Rename options class | AiOptions → DocumentIntelligenceOptions | — |
| Update endpoint paths | /api/ai/summarize/* → /api/ai/document-intelligence/* | — |
| Update DI registrations | Change config section, service registrations | Rename complete |
| Create response models | DocumentAnalysisResult, ExtractedEntities | — |
| Update AI prompt | Structured JSON output template | Models created |
| Add JSON parsing | Parse AI response, handle fallback | Prompt updated |
| Update SSE streaming | Include structured result at completion | JSON parsing |
| Add unit tests | Cover parsing, fallback, renamed endpoints | All changes |

### 5.2 Phase 1b: Dataverse + PCF Integration + Email Support

| Task | Description | Dependencies |
|------|-------------|--------------|
| Add Dataverse fields | sprk_filetldr, sprk_fileentities, sprk_documenttype | Phase 1a complete |
| Configure Relevance Search | Index sprk_filekeywords | Fields added |
| Update DocumentRecordService | Save structured fields | Fields added |
| Update useAiSummary hook | Call new endpoints | Phase 1a complete |
| Update AiSummaryPanel | Display TL;DR as bullets | Hook updated |
| Add keyword tags | Chip/tag display | Panel updated |
| Add entities section | Collapsible display | Panel updated |
| Add EmailExtractorService | Parse .eml and .msg files | — |
| Add email file support | Register in TextExtractorService | Extractor created |
| Integration tests | End-to-end flow | All Phase 1b |

### 5.3 Phase 2: Record Matching Service

| Task | Description | Dependencies |
|------|-------------|--------------|
| Provision Azure AI Search | Create resource via Bicep | — |
| Create index schema | spaarke-records-index definition | Resource ready |
| Implement DataverseIndexSyncService | Bulk and incremental sync | Index created |
| Implement RecordMatchService | Query and rank matches | Index populated |
| Add match-records endpoint | POST /api/ai/document-intelligence/match-records | Service created |
| Add associate-record endpoint | POST /api/ai/document-intelligence/associate-record | — |
| Add RecordTypeSelector PCF | Dropdown for record type filter | — |
| Add RecordMatchSuggestions PCF | Display ranked matches | Selector created |
| Add one-click association | Populate correct lookup field | Suggestions created |
| End-to-end tests | Full matching flow | All Phase 2 |

### 5.4 Phase 3: Project Wrap-up

| Task | Description | Dependencies |
|------|-------------|--------------|
| Final README update | Set status complete, check criteria | All phases |
| Document lessons learned | Capture insights | — |
| Archive project | Clean ephemeral files | Lessons documented |

---

## 6. Key Milestones

| Milestone | Criteria | Status |
|-----------|----------|--------|
| M1: Phase 1a Complete | Services renamed, structured JSON working | ✅ Complete |
| M2: Phase 1b Complete | PCF displays TL;DR/keywords/entities, email support | ✅ Complete |
| M3: Phase 2 Complete | Record matching with one-click association | ✅ Complete |
| M4: Project Complete | All graduation criteria met | ✅ Complete |

---

## 7. Risk Management

| ID | Risk | Impact | Likelihood | Score | Mitigation |
|----|------|--------|------------|-------|------------|
| R1 | JSON parsing failures | Medium | Medium | 4 | Robust fallback to raw text |
| R2 | Entity extraction inaccuracy | Medium | Medium | 4 | Confidence thresholds, user confirmation |
| R3 | Azure AI Search costs | Low | Low | 1 | Monitor usage, implement caching |
| R4 | Email parsing edge cases | Medium | Medium | 4 | Extensive test coverage |
| R5 | Service rename breaking changes | Medium | Low | 2 | Update all clients in same release |

---

## 8. Dependencies

### 8.1 Internal Dependencies

| Dependency | Status | Impact if Delayed |
|------------|--------|-------------------|
| Existing AI summarization feature | Ready | Cannot proceed |
| UniversalQuickCreate PCF | Ready | Cannot add UI features |
| Dataverse solution | Ready | Cannot add fields |

### 8.2 External Dependencies

| Dependency | Status | Fallback Plan |
|------------|--------|---------------|
| Azure OpenAI | Ready | — |
| Azure AI Search (Phase 2) | New | Delay Phase 2 |
| MimeKit NuGet | Available | — |
| MsgReader NuGet | Available | — |

---

## 9. Acceptance Criteria

### 9.1 Phase 1a Acceptance

| ID | Requirement | Acceptance Test |
|----|-------------|-----------------|
| FR-1.1 | AI returns structured JSON | Parse response, verify all fields present |
| FR-1.2 | TL;DR has 3-7 bullets | Count array elements |
| FR-1.3 | Keywords are searchable terms | Verify proper nouns, technical terms |
| FR-1.6 | Fallback when JSON fails | Send malformed response, verify text extracted |

### 9.2 Phase 1b Acceptance

| ID | Requirement | Acceptance Test |
|----|-------------|-----------------|
| FR-2.1-2.5 | Structured fields saved | Query Dataverse, verify all fields |
| FR-3.1 | TL;DR as bullet list | Visual inspection of PCF |
| FR-4.1-4.5 | Email files analyzed | Upload .eml/.msg, verify summary |

### 9.3 Phase 2 Acceptance

| ID | Requirement | Acceptance Test |
|----|-------------|-----------------|
| FR-5.1 | User selects record type | Dropdown filters results |
| FR-5.4 | Match API returns suggestions | Query with entities, verify ranked results |
| FR-5.8 | One-click association | Click match, verify lookup field populated |

---

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2025-12-09 | 1.0 | Initial plan | Claude Code |
| 2025-12-11 | 2.0 | Project completed - all milestones achieved | Claude Code |

---

*Based on Spaarke development lifecycle*
