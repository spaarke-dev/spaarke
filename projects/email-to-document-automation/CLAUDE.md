# CLAUDE.md - Email-to-Document Automation

> **Project**: Email-to-Document Automation
> **Last Updated**: 2025-12-14

This file provides AI assistant context for working on this project.

---

## Project Context

This project converts Power Platform Email activities into SDAP Document records with RFC 5322 compliant `.eml` files stored in SharePoint Embedded (SPE).

**Key Design Decision**: Hybrid trigger model (webhook + polling backup) for reliability.

---

## CRITICAL: Reuse Existing Components

**DO NOT create new services that duplicate existing SDAP functionality.**

### MUST REUSE These Services

| Service | Import | Purpose |
|---------|--------|---------|
| `SpeFileStore` | `Infrastructure/Graph/SpeFileStore.cs` | All SPE file operations |
| `IDocumentIntelligenceService` | `Services/Ai/DocumentIntelligenceService.cs` | AI summarization |
| `TextExtractorService` | `Services/Ai/TextExtractorService.cs` | Text extraction |
| `IDataverseService` | `Spaarke.Dataverse/` | Dataverse CRUD operations |
| `IJobHandler` | `Services/Jobs/Handlers/` | Job handler pattern |
| `JobContract` | ADR-004 schema | Message format |

### Inject, Don't Recreate

```csharp
// ✅ CORRECT
public class EmailToDocumentJobHandler(
    SpeFileStore speFileStore,              // Inject existing
    IDataverseService dataverse,            // Inject existing
    IEmailToEmlConverter emlConverter)      // New service for this project
{
    // Use speFileStore.UploadSmallAsync() - don't create new upload logic
}

// ❌ WRONG - Don't create parallel services
public class EmailSpeService { }  // NO - use SpeFileStore
public class EmailAiService { }   // NO - use IDocumentIntelligenceService
```

---

## Knowledge Files to Load

When working on tasks, load these files first:

### Always Load (Core Context)
- `projects/email-to-document-automation/SPEC.md` - Detailed design
- `projects/email-to-document-automation/PLAN.md` - Implementation plan
- `CLAUDE.md` (root) - Repository conventions

### By Task Type

**Job Handler / Background Service:**
- `docs/reference/adr/ADR-001-minimal-api-and-workers.md`
- `docs/reference/adr/ADR-004-async-job-contract.md`
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/DocumentProcessingJobHandler.cs`

**SPE File Operations:**
- `docs/reference/adr/ADR-007-spe-storage-seam-minimalism.md`
- `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`

**AI Processing Integration:**
- `docs/ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md`
- `src/server/api/Sprk.Bff.Api/Services/Ai/IDocumentIntelligenceService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/TextExtractorService.cs`

**Dataverse Operations:**
- `docs/ai-knowledge/guides/DATAVERSE-AUTHENTICATION-GUIDE.md`
- `src/server/shared/Spaarke.Dataverse/IDataverseService.cs`

**Ribbon Button (Phase 4):**
- `docs/ai-knowledge/guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md`

**API Endpoints:**
- `docs/reference/adr/ADR-008-authorization-endpoint-filters.md`
- `src/server/api/Sprk.Bff.Api/CLAUDE.md`

---

## ADR Compliance Checklist

Before writing code, verify compliance:

| ADR | Requirement | Check |
|-----|-------------|-------|
| ADR-001 | BackgroundService, no Functions | ✓ Using EmailPollingBackupService |
| ADR-002 | No heavy plugins | ✓ All orchestration in BFF |
| ADR-004 | Standard JobContract | ✓ Using existing schema |
| ADR-007 | SpeFileStore facade | ✓ No Graph SDK above facade |
| ADR-008 | Endpoint filters for auth | ✓ Not global middleware |
| ADR-009 | Redis-first caching | ✓ Filter rules in Redis |
| ADR-010 | DI minimalism (≤15) | ✓ Only 4 new services |

---

## New Services (This Project Only)

These are the ONLY new services to create:

```
src/server/api/Sprk.Bff.Api/
├── Services/
│   └── Email/
│       ├── IEmailToEmlConverter.cs        # RFC 5322 generation
│       ├── EmailToEmlConverter.cs
│       ├── IEmailAssociationService.cs    # Smart linking
│       ├── EmailAssociationService.cs
│       ├── IEmailFilterService.cs         # Rules engine
│       ├── EmailFilterService.cs
│       ├── IEmailAttachmentProcessor.cs   # Attachment handling
│       └── EmailAttachmentProcessor.cs
├── Services/Jobs/
│   ├── EmailPollingBackupService.cs       # Polling backup
│   └── Handlers/
│       └── EmailToDocumentJobHandler.cs   # Job handler
└── Api/
    └── EmailEndpoints.cs                  # All email endpoints
```

---

## File Naming Conventions

| Type | Convention | Example |
|------|------------|---------|
| Interfaces | `I{Name}.cs` | `IEmailToEmlConverter.cs` |
| Implementations | `{Name}.cs` | `EmailToEmlConverter.cs` |
| Job Handlers | `{Name}JobHandler.cs` | `EmailToDocumentJobHandler.cs` |
| Endpoints | `{Entity}Endpoints.cs` | `EmailEndpoints.cs` |
| Tests | `{Name}Tests.cs` | `EmailToEmlConverterTests.cs` |

---

## Testing Patterns

Follow existing test patterns:

```csharp
// Unit tests go in tests/unit/Sprk.Bff.Api.Tests/Services/Email/
public class EmailToEmlConverterTests
{
    [Fact]
    public async Task ConvertToEmlAsync_ValidEmail_ReturnsRfc5322CompliantStream()
    {
        // Arrange - use test fixtures
        // Act - call converter
        // Assert - validate RFC 5322 structure
    }
}
```

---

## Common Mistakes to Avoid

1. **Creating new file upload service** - Use `SpeFileStore.UploadSmallAsync()`
2. **Creating new AI service for emails** - Enqueue `ai-document-processing` job
3. **Using IMemoryCache** - Use Redis per ADR-009
4. **Custom message format** - Use `JobContract` per ADR-004
5. **Global auth middleware** - Use endpoint filters per ADR-008
6. **Injecting GraphServiceClient** - Use `SpeFileStore` per ADR-007

---

## Quick Reference

```bash
# Build
dotnet build src/server/api/Sprk.Bff.Api/

# Test
dotnet test tests/unit/Sprk.Bff.Api.Tests/

# Run API
dotnet run --project src/server/api/Sprk.Bff.Api/
```

---

*For task execution, always load SPEC.md and PLAN.md first, then relevant knowledge files based on the specific task.*
