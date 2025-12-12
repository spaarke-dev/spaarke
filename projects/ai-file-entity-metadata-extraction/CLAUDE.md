# AI File Entity Metadata Extraction - AI Context

## Project Status
- **Phase**: Development (Phase 1a)
- **Last Updated**: 2025-12-09
- **Next Action**: Start task 001 (Rename Service Files)

## Task Summary
- **Total Tasks**: 26
- **Phase 1a**: 9 tasks (001-009) - Service rename + structured output
- **Phase 1b**: 9 tasks (010-018) - Dataverse, PCF, email support
- **Phase 1 Deploy**: 1 task (019) - Deploy BFF, Dataverse solution, PCF
- **Phase 2**: 9 tasks (020-028) - Record matching service
- **Phase 2 Deploy**: 1 task (029) - Deploy all Phase 2 components
- **Wrap-up**: 1 task (090) - Project closure

### Parallel Start Options
Tasks 001, 002, 005, 016 have no dependencies and can start in parallel.

### Deployment Gates
- Task 019 (Deploy P1) must complete before Phase 2 begins
- Task 029 (Deploy P2) must complete before wrap-up

## Key Files
- `spec.md` - Original design specification (permanent reference)
- `README.md` - Project overview and graduation criteria
- `plan.md` - Implementation plan and WBS
- `tasks/` - Individual task files (POML format)

## Context Loading Rules
1. Always load this file first when working on ai-file-entity-metadata-extraction
2. Reference spec.md for design decisions and requirements
3. Load relevant task file from tasks/ based on current work
4. Check ADR-013 for AI architecture constraints

## Project Scope Summary

### Phase 1a: Service Rename + Structured Output
- Rename: SummarizeService → DocumentIntelligenceService
- Rename: AiOptions → DocumentIntelligenceOptions
- Update endpoints: `/api/ai/summarize/*` → `/api/ai/document-intelligence/*`
- Add structured JSON response with summary, TL;DR, keywords, entities
- Add fallback for JSON parsing failures

### Phase 1b: Dataverse + PCF + Email
- Add Dataverse fields: `sprk_filetldr`, `sprk_fileentities`, `sprk_documenttype`
- Enable Relevance Search on `sprk_filekeywords`
- Update PCF to display TL;DR bullets, keyword tags, entity section
- Add EmailExtractorService for .eml/.msg files

### Phase 2: Record Matching
- Azure AI Search index for Dataverse records
- RecordMatchService with ranking algorithm
- PCF record type selector + match suggestions
- One-click association with lookup field population

## Critical Constraints

| Source | Constraint |
|--------|------------|
| ADR-013 | Extend BFF, not separate AI service |
| ADR-001 | Use Minimal API endpoints |
| ADR-010 | Keep DI registrations minimal |
| Spec | Maintain streaming for real-time feedback |
| Spec | Always require user confirmation for record association |

## Key File Paths

### BFF API (to modify)
- `src/server/api/Sprk.Bff.Api/Services/Ai/SummarizeService.cs` → rename
- `src/server/api/Sprk.Bff.Api/Configuration/AiOptions.cs` → rename
- `src/server/api/Sprk.Bff.Api/Api/Ai/SummarizeEndpoints.cs` → rename

### PCF (to modify)
- `src/client/pcf/UniversalQuickCreate/control/services/useAiSummary.ts`
- `src/client/pcf/UniversalQuickCreate/control/components/AiSummaryPanel.tsx`

### New Files (to create)
- `src/server/api/Sprk.Bff.Api/Models/Ai/DocumentAnalysisResult.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/EmailExtractorService.cs`
- `src/server/api/Sprk.Bff.Api/Services/DocumentIntelligence/RecordMatchService.cs` (Phase 2)

## Decisions Made
<!-- Log key decisions here as project progresses -->
- 2025-12-09: Project initialized from spec.md

## Related ADRs
- [ADR-013](../../docs/reference/adr/ADR-013-ai-architecture.md) - AI Architecture (AI Tool Framework)
- [ADR-001](../../docs/reference/adr/ADR-001-minimal-api-and-workers.md) - Minimal API pattern
- [ADR-010](../../docs/reference/adr/ADR-010-di-minimalism.md) - DI minimalism

## NuGet Dependencies (New)
- `MimeKit` - EML file parsing
- `MsgReader` - MSG file parsing
- `Azure.Search.Documents` - Azure AI Search SDK (Phase 2)
