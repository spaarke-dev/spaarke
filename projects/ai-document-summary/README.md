# AI Document Summary

## Overview

AI-powered document summarization for Spaarke. When users upload documents via the Universal Quick Create dialog, an AI summary is automatically generated and stored in the `sprk_document` record.

## Status

🟡 **Specification Complete** - Ready for implementation

## Documents

- [spec.md](spec.md) - Full design specification

## User Experience

```
┌─────────────────────────────────────────────────────────────┐
│  + New Document                                        [X]  │
├─────────────────────────────────────────────────────────────┤
│  Name: [Quarterly Report Q4.pdf                    ]        │
│  File: 📄 Quarterly Report Q4.pdf  ✓ Uploaded               │
│                                                             │
│  📝 AI Summary                                              │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ This quarterly report covers Q4 2025 performance    │    │
│  │ including revenue growth of 12%, expansion into     │    │
│  │ three new markets, and the acquisition of...█       │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                             │
│  ℹ️ You can close - summary will complete in background     │
├─────────────────────────────────────────────────────────────┤
│                        [Cancel]  [Upload & Create Document] │
└─────────────────────────────────────────────────────────────┘
```

## Architecture

Extends `Sprk.Bff.Api` using the **BFF orchestration pattern**:

```
BFF Orchestrates: SPE (files) + Dataverse (records) + Azure AI (summarization)
```

## Components

| Component | Type | Description |
|-----------|------|-------------|
| `SummarizeEndpoints.cs` | BFF API | Streaming + enqueue endpoints |
| `SummarizeService.cs` | BFF Service | Orchestrates the summarization flow |
| `TextExtractorService.cs` | BFF Service | Text extraction (native + Doc Intel) |
| `OpenAiClient.cs` | BFF Service | Azure OpenAI wrapper (shared) |
| `SummarizeJobHandler.cs` | BFF Job | Background processing |
| `AiSummaryPanel.tsx` | PCF Component | Embedded in UniversalQuickCreate |

## Dataverse Fields

| Field | Type | Description |
|-------|------|-------------|
| `sprk_filesummary` | Multi-line (4000) | AI-generated summary |
| `sprk_filesummarystatus` | Choice | Pending/Completed/Failed/NotSupported |
| `sprk_filesummarydate` | DateTime | When generated |

## Supported File Types

- **Native**: TXT, MD, JSON, CSV
- **Document Intelligence**: PDF, DOCX, DOC

## ADR Compliance

| ADR | Requirement |
|-----|-------------|
| ADR-001 | Minimal API endpoints |
| ADR-004 | Job contract for background processing |
| ADR-007 | SpeFileStore for file access |
| ADR-008 | Endpoint filter auth |
| ADR-009 | Redis caching |
| ADR-010 | DI minimalism (3 new services) |
| ADR-013 | AI architecture patterns |

## Implementation Phases

1. **Phase 1**: Backend services + endpoints
2. **Phase 2**: Frontend integration (AiSummaryPanel)
3. **Phase 3**: Dataverse schema + deployment
4. **Phase 4**: Document Intelligence integration
5. **Phase 5**: Production hardening

## Cost Estimate

~$0.005 per document (~$50/month for 10K documents)
