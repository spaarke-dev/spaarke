# Workspace Entity Creation Guide

> **Version:** 1.0.0
> **Last Updated:** February 20, 2026
> **Applies To:** Corporate Workspace SPA — Create New Matter wizard (extensible to Project, Invoice, etc.)

---

## TL;DR

The Legal Workspace provides a multi-step wizard for creating Dataverse entity records (starting with Matters) from a standalone Vite SPA deployed as a Dataverse web resource. The wizard handles: file upload to SPE, Dataverse record creation with OData lookup bindings, document record linking, Document Profile AI analysis queuing, and follow-on actions (assign counsel, send email, draft summary). The architecture is entity-agnostic — `EntityCreationService` and navigation property discovery work for any entity type.

---

## Architecture Overview

```
┌────────────────────────────────────────────────────────────────────┐
│  Dataverse Shell (Model-Driven App)                                │
│  └─ iframe: corporateworkspace.html (Vite SPA, single-file build)  │
│     ├─ Xrm.WebApi (frame-walked via xrmProvider.ts)                │
│     ├─ BFF API (authenticatedFetch with OBO token)                 │
│     └─ Metadata API (EntityDefinitions for nav-prop discovery)     │
└──────────┬────────────────────────┬────────────────────────────────┘
           │                        │
    Xrm.WebApi                 BFF OBO API
    (Dataverse CRUD)       (SPE file operations)
           │                        │
    ┌──────▼──────┐          ┌──────▼──────┐
    │  Dataverse   │          │  SharePoint  │
    │  sprk_matter │          │  Embedded    │
    │  sprk_document│         │  (SPE)       │
    └─────────────┘          └─────────────┘
```

### Key Components

| Component | Location | Purpose |
|-----------|----------|---------|
| **WizardDialog.tsx** | `src/solutions/LegalWorkspace/src/components/CreateMatter/` | Multi-step dialog shell (sidebar nav, step routing) |
| **MatterService** | `matterService.ts` | Matter-specific orchestrator (entity payload, follow-on actions) |
| **EntityCreationService** | `src/solutions/LegalWorkspace/src/services/EntityCreationService.ts` | Entity-agnostic: SPE upload, document record creation, AI analysis trigger |
| **xrmProvider.ts** | `src/solutions/LegalWorkspace/src/services/xrmProvider.ts` | Frame-walk to find Xrm global, user ID, SPE container resolution |
| **bffAuthProvider.ts** | `src/solutions/LegalWorkspace/src/services/bffAuthProvider.ts` | OBO token acquisition for BFF API calls |
| **navigation.ts** | `src/solutions/LegalWorkspace/src/utils/navigation.ts` | Open records via Xrm.Navigation.openForm |
| **LookupField.tsx** | `src/solutions/LegalWorkspace/src/components/CreateMatter/LookupField.tsx` | Reusable search-as-you-type lookup component |

---

## Creation Flow (Step by Step)

### Step 1: File Upload

User drops files into the upload zone. Files are held in browser memory (`IUploadedFile[]`) — they are NOT uploaded to SPE yet. This allows AI pre-fill to analyze them before the record exists.

### Step 2: Create Record Form

Form fields are populated via:
- **Manual entry** by user
- **AI pre-fill** from uploaded file analysis (calls `POST /api/workspace/matters/pre-fill`)
- **Lookup search** fields query Dataverse ref tables in real-time

#### Form Fields → Dataverse Mapping

| Form Field | Dataverse Column | Type |
|-----------|-----------------|------|
| Matter Name | `sprk_mattername` | Text |
| Matter Number | `sprk_matternumber` | Text (auto-generated: `{typeCode}-{random6}`) |
| Matter Type | `sprk_mattertype` | Lookup → `sprk_mattertype_ref` |
| Practice Area | `sprk_practicearea` | Lookup → `sprk_practicearea_ref` |
| Assigned Attorney | `sprk_assignedattorney` | Lookup → `contact` |
| Assigned Paralegal | `sprk_assignedparalegal` | Lookup → `contact` |
| Summary | `sprk_matterdescription` | Multiline text |
| Container ID | `sprk_containerid` | Text (SPE container from Business Unit) |

### Step 3: Next Steps (Follow-On Actions)

User selects optional follow-on actions:
- **Assign Outside Counsel** → Updates `sprk_assignedoutsidecounsel` lookup
- **Draft Summary** → Creates email activity linked to matter
- **Send Email** → Creates email activity with custom subject/body

### Step 4: Finish (Orchestrated by MatterService.createMatter)

```
1. Discover OData nav-prop names (metadata API)
2. Build entity payload (scalar fields + @odata.bind lookups)
3. Generate matter number ({typeCode}-{random6})
4. Create sprk_matter record (Xrm.WebApi.createRecord)
5. Upload files to SPE (BFF: PUT /api/obo/containers/{id}/files/{path})
6. Create sprk_document records with:
   - File metadata (name, size, drive item ID, graph IDs)
   - Parent matter binding (@odata.bind)
   - SPE container/drive IDs (for BFF preview/download)
   - Source type (User Upload) and profile status (Pending)
   - Association resolver field (sprk_regardingrecordid)
7. Queue Document Profile analysis for each document (BFF → Service Bus)
8. Execute follow-on actions (assign counsel, email activities)
9. Return result (success / partial / error)
```

---

## SDAP / SPE Integration

### Container Resolution

The SPE container ID is resolved dynamically from the user's Business Unit:

```
User ID (Xrm global context)
  → systemuser record (Xrm.WebApi.retrieveRecord)
    → businessunitid ($expand)
      → businessunit.sprk_containerid
```

**Code**: `xrmProvider.ts → getSpeContainerIdFromBusinessUnit(webApi)`

The container ID is the Graph Drive ID (format: `b!...`). It's stored on:
- The **matter record** (`sprk_containerid`) for the PCF document grid to use
- Each **document record** (`sprk_graphdriveid`, `sprk_containerid`) for BFF preview/download

### File Upload Pattern

```
Client (browser)
  → authenticatedFetch (OBO Bearer token)
    → BFF: PUT /api/obo/containers/{containerId}/files/{fileName}
      → Microsoft Graph API (OBO)
        → SharePoint Embedded
```

**Endpoint**: `PUT /api/obo/containers/{containerId}/files/{path}`
**Auth**: Bearer token (user's Entra ID token, exchanged to Graph OBO by BFF)
**Response**: `{ id, name, size, webUrl }` — SPE drive item metadata

### Document Record Fields

Each `sprk_document` record stores both Dataverse metadata and SPE pointers:

| Field | Source | Purpose |
|-------|--------|---------|
| `sprk_documentname` | File name | Primary name field |
| `sprk_filename` | File name | Original file name |
| `sprk_driveitemid` | Upload response `.id` | SPE item reference |
| `sprk_graphitemid` | Upload response `.id` | BFF view-url resolution |
| `sprk_graphdriveid` | Container ID | BFF view-url resolution |
| `sprk_containerid` | Container ID | SPE container reference |
| `sprk_filepath` | Upload response `.webUrl` | Direct URL to file |
| `sprk_filesize` | Upload response `.size` | File size in bytes |
| `sprk_sourcetype` | `659490000` | User Upload |
| `sprk_filesummarystatus` | `100000001` | Pending (for Document Profile) |
| `sprk_regardingrecordid` | Parent GUID (text) | Association resolver |
| `sprk_Matter@odata.bind` | `/sprk_matters({id})` | Parent entity lookup |

---

## OData Navigation Property Discovery

Dataverse requires PascalCase navigation property names for `@odata.bind`, but column logical names are lowercase. A metadata discovery pattern resolves the correct names at runtime.

### Problem

```
❌  sprk_mattertype@odata.bind     → "undeclared property" error
✅  sprk_MatterType@odata.bind     → works
```

### Solution

Query the entity metadata API:
```
GET /api/data/v9.0/EntityDefinitions(LogicalName='sprk_matter')/ManyToOneRelationships
  ?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName
```

Returns a map: `{ sprk_mattertype: "sprk_MatterType", ... }`

**Code**: `matterService.ts → _discoverNavProps(entityLogicalName)`

Results are cached per entity for the session. The `_resolveNavProp()` helper falls back to the column logical name if metadata discovery fails.

---

## Authentication & Authorization

### Xrm.WebApi (Dataverse Operations)

- Accessed via frame-walking (`xrmProvider.ts → getXrm()`)
- Uses the current user's Dataverse session (no additional auth needed)
- Respects Dataverse security roles and field-level security

### BFF API (SPE + AI Operations)

- `bffAuthProvider.ts → authenticatedFetch()` acquires an OBO token
- Token flow: User's Entra ID token → BFF → Graph API (OBO exchange)
- BFF base URL configured in `bffConfig.ts`

### Required Permissions

| Operation | Auth Method | Permission Required |
|-----------|-------------|-------------------|
| Create sprk_matter | Xrm.WebApi | Dataverse Create privilege on sprk_matter |
| Create sprk_document | Xrm.WebApi | Dataverse Create privilege on sprk_document |
| Upload to SPE | BFF OBO | Microsoft Graph: Files.ReadWrite.All (delegated) |
| Trigger analysis | BFF API | Authenticated user (any role) |
| Read entity metadata | Fetch API | Dataverse Read privilege on EntityDefinitions |
| Search contacts | Xrm.WebApi | Dataverse Read privilege on contact |

---

## Document Profile Integration

After document records are created, the wizard triggers AI profiling via the BFF:

```
Client → POST /api/documents/{documentId}/analyze (for each document)
  → BFF queues AppOnlyDocumentAnalysis job to Service Bus
    → Background handler downloads file from SPE (app-only auth)
    → Runs "Document Profile" playbook (summary, keywords, entities, classification)
    → Updates sprk_document fields (sprk_filesummary, sprk_filetldr, etc.)
```

**Endpoint**: `POST /api/documents/{documentId}/analyze`
**Response**: `202 Accepted` with `{ jobId, documentId, status: "queued" }`
**Handler**: `AppOnlyDocumentAnalysisJobHandler` (Service Bus queue: `sdap-jobs`)

Failures are non-fatal — the document exists and can be profiled later.

---

## Extending to Other Entity Types

The architecture separates entity-specific logic from reusable infrastructure:

### Reusable (EntityCreationService)

- `uploadFilesToSpe()` — works for any entity type
- `createDocumentRecords()` — parameterized by parent entity name and nav-prop
- `_triggerDocumentAnalysis()` — works for any document
- `requestAiPreFill()` — could accept entity type parameter

### Entity-Specific (MatterService pattern)

To add **Create New Project**, create a `ProjectService` that:

1. Builds the `sprk_project` entity payload with project-specific fields
2. Calls `_discoverNavProps('sprk_project')` for lookup bindings
3. Calls `EntityCreationService.uploadFilesToSpe()` for file handling
4. Calls `EntityCreationService.createDocumentRecords()` with:
   - `parentEntityName: 'sprk_projects'`
   - `navigationProperty: docNavProps['sprk_project']` (discovered at runtime)
5. Implements project-specific follow-on actions

### Checklist for New Entity Type

- [ ] Create `{Entity}Service.ts` with entity-specific payload builder
- [ ] Create form types (`formTypes.ts`) for the entity's fields
- [ ] Create wizard steps (reuse `LookupField.tsx`, `FileUploadStep`)
- [ ] Add search functions for entity-specific ref tables
- [ ] Ensure `sprk_containerid` field exists on the entity (for PCF grid)
- [ ] Ensure `sprk_document` has a lookup to the new entity type
- [ ] Test nav-prop discovery for the entity's lookup columns
- [ ] Test document record creation with correct parent binding

---

## Error Handling Strategy

| Failure | Severity | User Impact |
|---------|----------|-------------|
| Matter record creation fails | **Hard error** | Abort — nothing created |
| File upload fails | Soft warning | Matter created; files can be added later |
| Document record creation fails | Soft warning | Files in SPE; records can be created later |
| Document Profile queue fails | Silent | Documents exist; profiling runs on next trigger |
| Follow-on action fails | Soft warning | Matter created; user can do manually |
| Nav-prop discovery fails | Fallback | Uses lowercase column name (may work for some fields) |
| SPE container not found | Soft warning | Matter created; file upload skipped |

---

## Key Files Reference

```
src/solutions/LegalWorkspace/src/
├── components/CreateMatter/
│   ├── WizardDialog.tsx          # Multi-step dialog shell
│   ├── FileUploadStep.tsx        # Step 1: drag-and-drop upload
│   ├── CreateRecordStep.tsx      # Step 2: form with lookups
│   ├── NextStepsStep.tsx         # Step 3: follow-on action selection
│   ├── LookupField.tsx           # Reusable search lookup component
│   ├── matterService.ts          # Matter-specific orchestrator
│   ├── formTypes.ts              # Form state types and reducer actions
│   └── wizardTypes.ts            # Shared wizard types
├── services/
│   ├── EntityCreationService.ts  # Entity-agnostic CRUD + SPE + AI
│   ├── xrmProvider.ts            # Xrm frame-walk, userId, container resolution
│   └── bffAuthProvider.ts        # OBO token acquisition for BFF
├── config/
│   └── bffConfig.ts              # BFF base URL configuration
├── utils/
│   └── navigation.ts             # Xrm.Navigation.openForm wrapper
└── types/
    ├── entities.ts               # Dataverse entity interfaces
    └── xrm.ts                    # IWebApi, WebApiEntity types
```

### BFF API (Server-Side)

```
src/server/api/Sprk.Bff.Api/Api/
├── DocumentOperationsEndpoints.cs  # POST /api/documents/{id}/analyze
├── OBOEndpoints.cs                 # PUT /api/obo/containers/{id}/files/{path}
└── Ai/AnalysisEndpoints.cs         # POST /api/ai/analysis/execute (streaming)

src/server/api/Sprk.Bff.Api/Services/Jobs/
├── JobContract.cs                  # ADR-004 job contract
├── JobSubmissionService.cs         # Service Bus submission
└── Handlers/
    └── AppOnlyDocumentAnalysisJobHandler.cs  # Document Profile processor
```
