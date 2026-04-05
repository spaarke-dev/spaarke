# Workspace Entity Creation Guide

> **Version:** 2.0.1
> **Last Updated:** April 5, 2026
> **Applies To:** Corporate Workspace SPA — Create New Matter, Project, Event, Todo, Work Assignment wizards

---

## TL;DR

The Legal Workspace provides multi-step wizards for creating Dataverse entity records (Matter, Project, Event, Todo, Work Assignment) from a standalone Vite SPA deployed as a Dataverse web resource. The wizard handles: file upload to SPE, Dataverse record creation with OData lookup bindings, document record linking, Document Profile AI analysis queuing, AI pre-fill from uploaded documents, and follow-on actions. The architecture is entity-agnostic — shared library components (`EntityCreationService`, `useAiPrefill`, `AiFieldTag`, `findBestLookupMatch`) work for any entity type via dependency injection and configuration.

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

#### Shared Library (`src/client/shared/Spaarke.UI.Components/`)

| Component | Location | Purpose |
|-----------|----------|---------|
| **EntityCreationService** | `src/services/EntityCreationService.ts` | Entity-agnostic: SPE upload, document record creation, AI analysis trigger. Constructor-injected `authenticatedFetch` and `bffBaseUrl`. |
| **useAiPrefill** | `src/hooks/useAiPrefill.ts` | Reusable hook for AI pre-fill from uploaded documents. Configurable field extractors and lookup resolvers. |
| **AiFieldTag** | `src/components/AiFieldTag/AiFieldTag.tsx` | "AI" sparkle badge for pre-filled form fields |
| **findBestLookupMatch** | `src/utils/lookupMatching.ts` | Fuzzy match AI display names against Dataverse lookup results |
| **LookupField** | `src/components/LookupField/` | Reusable search-as-you-type lookup component |
| **CreateRecordWizard** | `src/components/CreateRecordWizard/` | Reusable multi-step wizard shell (sidebar nav, step routing) |
| **FileUploadZone** | `src/components/FileUpload/` | Drag-and-drop file upload with preview |

#### Solution-Specific (`src/solutions/LegalWorkspace/`)

| Component | Location | Purpose |
|-----------|----------|---------|
| **MatterService** | `components/CreateMatter/matterService.ts` | Matter-specific orchestrator (entity payload, follow-on actions) |
| **ProjectService** | `components/CreateProject/projectService.ts` | Project-specific orchestrator |
| **xrmProvider.ts** | `services/xrmProvider.ts` | Frame-walk to find Xrm global, user ID, SPE container resolution |
| **bffAuthProvider.ts** | `services/bffAuthProvider.ts` | OBO token acquisition for BFF API calls |
| **runtimeConfig.ts** | `config/runtimeConfig.ts` | BFF base URL and MSAL configuration |

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
- BFF base URL configured in `runtimeConfig.ts`

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

The architecture separates entity-specific logic from reusable infrastructure via the shared library.

### Shared Library Components (import from `@spaarke/ui-components`)

| Component | Method | Purpose |
|-----------|--------|---------|
| `EntityCreationService` | `uploadFilesToSpe()` | Upload files to SPE container (any entity) |
| `EntityCreationService` | `createDocumentRecords()` | Create `sprk_document` records linked to parent entity |
| `EntityCreationService` | `_triggerDocumentAnalysis()` | Queue Document Profile AI analysis |
| `useAiPrefill` | (hook) | AI pre-fill from uploaded documents (any entity with a BFF endpoint) |
| `findBestLookupMatch` | (function) | Fuzzy match AI values to Dataverse lookups |
| `AiFieldTag` | (component) | Visual badge for AI-populated fields |
| `LookupField` | (component) | Search-as-you-type lookup with Dataverse integration |
| `CreateRecordWizard` | (component) | Multi-step wizard shell with sidebar navigation |

### EntityCreationService — Dependency Injection

The service uses constructor injection so it works in any environment:

```typescript
import { EntityCreationService } from '@spaarke/ui-components';
import { authenticatedFetch } from '../../services/authInit';
import { getBffBaseUrl } from '../../config/runtimeConfig';

// In your entity service constructor:
this._creationService = new EntityCreationService(
  webApi,                 // IWebApiWithCreate (Xrm.WebApi)
  authenticatedFetch,     // OBO token fetch function
  getBffBaseUrl(),        // BFF base URL string
);
```

### Entity-Specific (Service Pattern)

Each entity wizard creates an `{Entity}Service.ts` that:

1. Builds the entity payload with entity-specific fields
2. Calls `_discoverNavProps(entityLogicalName)` for OData lookup bindings
3. Delegates to `EntityCreationService` for file upload + document records
4. Implements entity-specific follow-on actions

**Implemented entities**: Matter (`matterService.ts`), Project (`projectService.ts`), Event (`eventService.ts`), Todo (`todoService.ts`), Work Assignment (`workAssignmentService.ts`)

### Checklist for New Entity Type

- [ ] Create `{Entity}Service.ts` with entity-specific payload builder
- [ ] Instantiate `EntityCreationService` with DI: `new EntityCreationService(webApi, authenticatedFetch, getBffBaseUrl())`
- [ ] Create form types (`{entity}FormTypes.ts`) for the entity's fields
- [ ] Create wizard step component — use `useAiPrefill` hook for AI pre-fill if applicable
- [ ] Create `{Entity}WizardDialog.tsx` using `CreateRecordWizard` (or `WizardShell` for non-standard flows)
- [ ] Add search functions for entity-specific ref tables
- [ ] Ensure `sprk_containerid` field exists on the entity (for PCF grid)
- [ ] Ensure `sprk_document` has a lookup to the new entity type
- [ ] Test nav-prop discovery for the entity's lookup columns
- [ ] Test document record creation with correct parent binding
- [ ] If adding AI pre-fill: create BFF pre-fill service + playbook with `$choices`

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

### Shared Library (`src/client/shared/Spaarke.UI.Components/src/`)

```
hooks/
│   └── useAiPrefill.ts           # AI pre-fill hook (shared across all entity wizards)
utils/
│   └── lookupMatching.ts         # findBestLookupMatch utility
components/
│   ├── AiFieldTag/               # "AI" badge for pre-filled fields
│   ├── LookupField/              # Search-as-you-type lookup
│   ├── CreateRecordWizard/       # Multi-step wizard shell
│   └── FileUpload/               # Drag-and-drop file upload
services/
│   └── EntityCreationService.ts  # Entity-agnostic: SPE upload, doc records, AI analysis
types/
│   └── WebApiLike.ts             # IWebApiLike, IWebApiWithCreate interfaces
```

### Solution (`src/solutions/LegalWorkspace/src/`)

```
components/CreateMatter/
│   ├── WizardDialog.tsx          # Matter wizard dialog
│   ├── CreateRecordStep.tsx      # Matter form step (uses useAiPrefill)
│   ├── NextStepsStep.tsx         # Follow-on action selection
│   ├── LookupField.tsx           # Thin wrapper (imports from shared lib)
│   ├── AiFieldTag.tsx            # Re-export from shared lib
│   ├── matterService.ts          # Matter-specific orchestrator
│   └── formTypes.ts              # Matter form state types
components/CreateProject/
│   ├── ProjectWizardDialog.tsx   # Project wizard dialog
│   ├── CreateProjectStep.tsx     # Project form step (uses useAiPrefill)
│   └── projectService.ts        # Project-specific orchestrator
services/
│   ├── EntityCreationService.ts  # Re-export from shared lib
│   ├── xrmProvider.ts            # Xrm frame-walk, userId, container resolution
│   └── bffAuthProvider.ts        # OBO token acquisition for BFF
config/
│   └── runtimeConfig.ts              # BFF base URL configuration
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
