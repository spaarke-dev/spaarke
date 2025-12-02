# Sprint 7: Dataset Grid to SDAP Integration - Overview

**Status**: Planning Complete - Split into Sprint 7A and 7B
**Started**: 2025-10-05
**Estimated Duration**: 10-16 days (7A: 6-10 days, 7B: 4-6 days)
**Priority**: High

---

## Executive Summary

Sprint 7 builds **two universal PCF controls** that integrate with the SDAP BFF API to enable complete document management with SharePoint Embedded. This replaces the JavaScript web resource approach from Sprint 2 with a modern, type-safe, React-based solution.

### What We're Building

**Two Separate Universal PCF Controls**:
1. **Sprint 7A: Universal Dataset Grid** - Display and manage records with file operations (download, delete, replace)
2. **Sprint 7B: Universal Quick Create** - Create new records with file upload and SPE integration

**Why Two Controls?**
- ✅ **ADR Compliance**: No C# plugins, no naked JavaScript, TypeScript PCF only
- ✅ **Reusability**: Each control can be used independently across multiple entities
- ✅ **Standard UX**: Maintains native Power Apps Quick Create experience
- ✅ **Separation of Concerns**: Grid displays data, Quick Create handles creation

### Quick Status

- ✅ **Universal Dataset Grid v2.0.7**: React 18 + Fluent UI v9, 470 KB bundle, production-ready
- ✅ **SDAP BFF API**: All endpoints implemented and tested (8.5/10 production readiness)
- ✅ **Documentation**: Organized into discrete task files for efficient AI-directed coding
- ✅ **Architecture Decision**: Two-PCF approach documented in SPRINT-7-MASTER-RESOURCE.md
- 🚀 **Ready to Begin**: All prerequisites met

### Success Criteria

**Sprint 7A (Universal Dataset Grid)**:
- ✅ File download, delete, replace operations work from grid
- ✅ Clickable SharePoint URLs in grid
- ✅ Bundle size remains under 550 KB
- ✅ Zero breaking changes to existing grid functionality

**Sprint 7B (Universal Quick Create)**:
- ✅ Create new documents with file upload to SPE
- ✅ Auto-populate default values from parent entity (container ID, owner, etc.)
- ✅ Standard Power Apps Quick Create UX
- ✅ Configurable for multiple entity types (Document, Matter, Task, etc.)
- ✅ Bundle size under 400 KB

---

## Architecture

### Two-PCF Architecture

```
┌─────────────────────────────────────────────────────────────┐
│ Matter Form (Model-Driven App)                              │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Documents Subgrid (Universal Dataset Grid PCF)         │ │
│  │                                                         │ │
│  │  [+ New Document]  [Download]  [Delete]  [Replace]    │ │
│  │                                                         │ │
│  │  Document 1 | SPE URL | 1.2 MB | 2025-10-01           │ │
│  │  Document 2 | SPE URL | 3.5 MB | 2025-09-28           │ │
│  └────────────────────────────────────────────────────────┘ │
│                      ↓ User clicks "+ New Document"         │
│  ┌────────────────────────────────────────────────────────┐ │
│  │ Quick Create Form (Universal Quick Create PCF)         │ │
│  │                                                         │ │
│  │  Select File: [Choose File...]                        │ │
│  │  Document Title: "Matter XYZ" ← Auto from Matter      │ │
│  │  Description: _________________________________        │ │
│  │  Owner: John Doe ← Auto from Matter                   │ │
│  │                                                         │ │
│  │            [Save]  [Cancel]                            │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                         ↓
              ┌──────────────────────┐
              │  Spe.Bff.Api         │
              │  (.NET 8 BFF)        │
              └──────────────────────┘
                         ↓
        ┌────────────────┴──────────────────┐
        ↓                                   ↓
┌──────────────────┐              ┌──────────────────┐
│   Dataverse      │              │ SharePoint       │
│   (sprk_document)│              │ Embedded (SPE)   │
└──────────────────┘              └──────────────────┘
```

**Authentication**: PCF context → User token → SDAP API → OBO flow → Graph API

**Context Sharing**: Power Apps automatically provides parent entity context to Quick Create via `context.mode.contextInfo`

**See**: [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md) for detailed architecture, context sharing, field mappings, and code patterns.

---

## Task Breakdown

Sprint 7 is divided into **Sprint 7A** (Universal Dataset Grid with SDAP) and **Sprint 7B** (Universal Quick Create with SPE upload).

---

## Sprint 7A: Universal Dataset Grid + SDAP Integration

**Focus**: Enable file operations (download, delete, replace) on existing documents from the grid.

### Task 1: SDAP API Client Service Setup
**Time**: 1-2 days | **File**: [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md)

Create TypeScript API client with type-safe methods for all SDAP endpoints, authentication via PCF context, and comprehensive error handling.

**Deliverables**:
- `services/SdapApiClient.ts` - Main API client class
- `services/SdapApiClientFactory.ts` - Factory with PCF context integration
- Updated `types/index.ts` - SDAP type definitions

**AI Prompt**: Create a TypeScript API client for SDAP BFF API that integrates with PCF control...

---

### Task 2: File Download Integration
**Time**: 0.5-1 day | **File**: [TASK-3-FILE-DOWNLOAD.md](TASK-3-FILE-DOWNLOAD.md)

Enable file download from SharePoint Embedded with browser download dialog.

**Deliverables**:
- `services/FileDownloadService.ts` - Download logic
- Updated `components/CommandBar.tsx` - Download button handler

**AI Prompt**: Implement file download functionality that retrieves files from SharePoint and triggers browser download...

---

### Task 3: File Delete Integration
**Time**: 1 day | **File**: [TASK-4-FILE-DELETE.md](TASK-4-FILE-DELETE.md)

Implement file deletion with confirmation dialog and cascade delete (SharePoint + Dataverse).

**Deliverables**:
- `components/ConfirmDialog.tsx` - Reusable confirmation component
- `services/FileDeleteService.ts` - Delete orchestration
- Updated `components/CommandBar.tsx` - Delete button handler

**AI Prompt**: Implement file deletion with user confirmation, cascade delete across SharePoint and Dataverse...

---

### Task 4: File Replace Integration
**Time**: 0.5-1 day | **File**: [TASK-5-FILE-REPLACE.md](TASK-5-FILE-REPLACE.md)

Enable file replacement (delete old + upload new) with metadata update.

**Deliverables**:
- Updated `services/FileUploadService.ts` - Replace methods
- Updated `components/CommandBar.tsx` - Replace button handler

**AI Prompt**: Implement file replacement that deletes old file, uploads new file, and updates metadata...

---

### Task 5: Field Mapping & SharePoint Links
**Time**: 0.5 day | **File**: [TASK-6-FIELD-MAPPING.md](TASK-6-FIELD-MAPPING.md)

Make SharePoint URLs clickable and verify metadata auto-population.

**Deliverables**:
- Updated `components/DatasetGrid.tsx` - Custom URL column renderer
- Verification of all metadata field mappings

**AI Prompt**: Enhance grid to render SharePoint URLs as clickable links and verify metadata auto-population...

---

### Task 6: Testing, Bundle Size & Deployment (Sprint 7A)
**Time**: 1-2 days | **File**: [TASK-7-TESTING-DEPLOYMENT.md](TASK-7-TESTING-DEPLOYMENT.md)

Create integration tests, validate bundle size, execute manual testing, and deploy to production.

**Deliverables**:
- `tests/SdapIntegration.test.ts` - Integration test suite
- Bundle size validation (<550 KB)
- Manual testing checklist (100% complete)
- Production deployment

**AI Prompt**: Create integration tests for SDAP operations, validate bundle size, execute testing, and deploy...

---

## Sprint 7B: Universal Quick Create with SPE Upload

**Focus**: Create new documents with file upload to SharePoint Embedded from Quick Create form.

**Note**: Task files for Sprint 7B will be created after Sprint 7A completion. Key deliverables include:

### Task 1: Universal Quick Create PCF Setup
**Time**: 1-2 days

Create new PCF control with React 18 + Fluent UI v9, context retrieval from Power Apps, and parent entity data loading.

**Deliverables**:
- New PCF project: `UniversalQuickCreate`
- `ControlManifest.Input.xml` - Manifest with default value mappings parameter
- `UniversalQuickCreatePCF.ts` - PCF wrapper class
- `components/QuickCreateForm.tsx` - Main React component

---

### Task 2: File Upload with SPE Integration
**Time**: 1-2 days

Implement file upload to SharePoint Embedded via SDAP API, Dataverse record creation, and metadata synchronization.

**Deliverables**:
- `services/FileUploadService.ts` - Upload orchestration (reuse SDAP client from 7A)
- File picker component
- Upload progress indicator

---

### Task 3: Configurable Default Value Mappings
**Time**: 0.5-1 day

Implement configurable default value mappings from parent entity to Quick Create form fields.

**Deliverables**:
- Default value mapping logic
- Power Apps form configuration examples
- Documentation for mapping configuration

---

### Task 4: Testing, Bundle Size & Deployment (Sprint 7B)
**Time**: 1 day

Create integration tests, validate bundle size, execute manual testing, and deploy to production.

**Deliverables**:
- Integration test suite
- Bundle size validation (<400 KB)
- Manual testing checklist
- Production deployment

---

## Timeline Estimate

### Sprint 7A: Universal Dataset Grid + SDAP

| Task | Days | Status |
|------|------|--------|
| Task 1: API Client | 1-2 | Pending |
| Task 2: Download | 0.5-1 | Pending |
| Task 3: Delete | 1 | Pending |
| Task 4: Replace | 0.5-1 | Pending |
| Task 5: Field Mapping | 0.5 | Pending |
| Task 6: Testing & Deployment | 1-2 | Pending |
| **Sprint 7A Total** | **6-10 days** | **0% Complete** |

### Sprint 7B: Universal Quick Create + SPE Upload

| Task | Days | Status |
|------|------|--------|
| Task 1: Quick Create PCF Setup | 1-2 | Pending |
| Task 2: File Upload Integration | 1-2 | Pending |
| Task 3: Default Value Mappings | 0.5-1 | Pending |
| Task 4: Testing & Deployment | 1 | Pending |
| **Sprint 7B Total** | **4-6 days** | **0% Complete** |

### Combined Timeline

| Sprint | Days | Status |
|--------|------|--------|
| Sprint 7A | 6-10 | Pending |
| Sprint 7B | 4-6 | Pending |
| **Total Sprint 7** | **10-16 days** | **0% Complete** |

---

## Key Resources

### Master Reference
- **[SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md)** - Architecture, API endpoints, field mappings, code patterns, workflows

### Individual Task Files
1. [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md)
2. [TASK-2-FILE-UPLOAD.md](TASK-2-FILE-UPLOAD.md)
3. [TASK-3-FILE-DOWNLOAD.md](TASK-3-FILE-DOWNLOAD.md)
4. [TASK-4-FILE-DELETE.md](TASK-4-FILE-DELETE.md)
5. [TASK-5-FILE-REPLACE.md](TASK-5-FILE-REPLACE.md)
6. [TASK-6-FIELD-MAPPING.md](TASK-6-FIELD-MAPPING.md)
7. [TASK-7-TESTING-DEPLOYMENT.md](TASK-7-TESTING-DEPLOYMENT.md)

### Background Documentation
- [Sprint 2 Wrap-Up](../Sprint%202/SPRINT-2-WRAP-UP-REPORT.md) - SDAP current state
- [Sprint 5B Summary](../../UniversalDatasetGrid/SPRINT_5B_SUMMARY.md) - Grid v2.0.7 details
- [SDAP Assessment](../SDAP-PROJECT-COMPREHENSIVE-ASSESSMENT.md) - Full project status

---

## Known Issues & Mitigation

### Issue 1: SPE Container ID Format
**Impact**: Medium | **Mitigation**: Verify format in API, add conversion if needed

### Issue 2: Large Files (>4 MB)
**Impact**: Low | **Mitigation**: Implement chunked upload in future sprint

### Issue 3: Token Expiration
**Impact**: Medium | **Mitigation**: Implement refresh logic, retry with new token

---

## Success Metrics

### Technical
- Bundle size < 550 KB ✅
- All operations < 2s response time ✅
- Zero TypeScript errors ✅
- Zero runtime errors ✅

### User Experience
- Single-click file operations ✅
- Automatic metadata population ✅
- Clickable SharePoint URLs ✅
- Real-time grid updates ✅

### Business
- Replace JavaScript web resource ✅
- Improve reliability ✅
- Reduce training requirements ✅

---

## Dependencies & Prerequisites

### Required (All Met ✅)
- ✅ Spe.Bff.Api deployed and accessible
- ✅ Dataverse sprk_document entity deployed
- ✅ sprk_matter entity has sprk_containerid field
- ✅ SharePoint Embedded containers provisioned
- ✅ Universal Dataset Grid v2.0.7 deployed

### Configuration Required
- [ ] SDAP_API_URL environment variable
- [ ] CORS configured for Power Apps domain
- [ ] User permissions verified

---

## How to Use This Documentation

### For AI-Directed Coding Sessions

1. **Start with Task 1**: Open [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md)
2. **Read AI Prompt** at top of file
3. **Follow Implementation Steps** with code examples
4. **Reference Master Resource** ([SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md)) for:
   - API endpoint specifications
   - Field mappings
   - Code patterns
   - Common workflows
5. **Validate** against criteria in task file
6. **Move to Next Task** when complete

### For Context and Knowledge

Each task file includes:
- **AI Coding Prompt** - Copy/paste for AI sessions
- **Objective** - What you're building and why
- **Context & Knowledge** - Background and patterns
- **Implementation Steps** - Step-by-step with code
- **Validation Criteria** - How to verify completion
- **Troubleshooting** - Common issues and solutions
- **Expected Outcomes** - What success looks like

### For Reference

- **Architecture**: See [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#architecture-overview)
- **API Endpoints**: See [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#sdap-api-endpoints-reference)
- **Field Mappings**: See [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#field-mappings-dataverse--sdap-api)
- **Code Patterns**: See [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md#code-patterns--standards)

---

## Next Steps

1. **Review this overview** with stakeholders
2. **Verify SDAP API accessibility** from Power Apps environment
3. **Configure environment variables** (SDAP_API_URL)
4. **Begin Task 1**: [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md)
5. **Complete tasks sequentially** (Tasks 2-7)
6. **Deploy to production** after Task 7 validation

---

**Document Version**: 2.0 (Reorganized)
**Last Updated**: 2025-10-05
**Author**: Claude Code
**Status**: Ready for Implementation
