# Create New Project Wizard Enhancement

> **Created**: 2026-03-05
> **Branch**: `work/playbook-analysis-launcher-page-r1`
> **PR**: #209
> **Status**: Planning complete, ready for implementation

---

## Problem Statement

The "Create New Project" wizard in the Corporate Workspace is incomplete compared to the "Create New Matter" wizard. Currently it has:
- Only **1 step** (Create Record) — should have 3 steps matching Create Matter
- **Broken lookups** — Project Type and Practice Area searches fail in the dialog
- **No file upload pipeline** — files staged in step 1 but not uploaded to SPE or linked as documents
- **No AI pre-fill** — no BFF endpoint exists for project field extraction from uploaded documents
- **No Next Steps** — missing follow-on actions (Assign Counsel, Draft Summary, Send Email)

### Target State

The Create New Project wizard should match the Create New Matter wizard:
1. **Step 1: Add file(s)** — Upload documents, store in SPE, create `sprk_document` records, trigger Document Profile AI, index in Semantic Search
2. **Step 2: Create record** — AI pre-fills project fields from uploaded documents, user reviews/edits
3. **Step 3: Next Steps** — Optional follow-on actions (Assign Counsel, Draft Summary, Send Email)
4. **Dynamic follow-on steps** — Injected based on Next Steps selections

---

## Architecture Context

### How Create Matter Works (Reference Implementation)

The Create Matter wizard (`WizardDialog.tsx`) follows this flow:

#### Step 1: Add file(s) (Local staging)
- `FileUploadZone` validates files (PDF/DOCX/XLSX, max 10MB)
- Files stored in browser memory as `IUploadedFile[]` (not uploaded yet)
- `UploadedFileList` shows staged files with remove option

#### Step 2: Create Record (AI pre-fill on mount)
- `CreateRecordStep` mounts → sends files to BFF `POST /api/workspace/matters/pre-fill`
- BFF extracts text from files, invokes AI playbook, returns structured fields
- Frontend fuzzy-matches AI display names to Dataverse lookups (scoring: 1.0 exact, 0.8 starts-with, 0.7 contains, 0.5 single-result, 0.4 threshold)
- Form renders with pre-filled values + "AI Pre-filled" badges
- User can edit/override any field

#### Step 3: Next Steps (Optional follow-on actions)
- `NextStepsStep` shows 3 checkbox cards: Assign Counsel, Draft Summary, Send Email
- Selecting cards dynamically injects new wizard steps via `shellRef.current.addDynamicStep()`
- Can skip entirely (early finish)

#### Finish Handler (Full Pipeline)
1. Create `sprk_matter` record in Dataverse
2. Upload files to SPE via `EntityCreationService.uploadFilesToSpe()` — `PUT /api/obo/containers/{id}/files/{path}`
3. Create `sprk_document` records linking files to matter via `EntityCreationService.createDocumentRecords()`
   - Sets `sprk_filesummarystatus: 100000001` (Pending) → triggers Document Profile AI
4. Queue AI Document Profile via `EntityCreationService._triggerDocumentAnalysis()` — `POST /api/documents/{id}/analyze`
5. Execute follow-on actions (assign counsel, send email, etc.)

### Key Insight: Entity-Agnostic Services

`EntityCreationService.createDocumentRecords()` already accepts generic parameters:
```typescript
createDocumentRecords(
  parentEntityName: string,        // 'sprk_projects' for projects
  parentEntityId: string,          // Project GUID
  navigationProperty: string,      // 'sprk_Project'
  uploadedFiles: ISpeFileMetadata[],
  options?: { containerId?: string; parentRecordName?: string }
)
```
No changes needed to this service for project support.

---

## Task Breakdown

### Part A: Fix Lookup Errors (Quick Fix)

**Problem**: Project Type and Practice Area searches fail silently in the dialog.

**Root Cause**: The lookup entities `sprk_projecttype_ref` and `sprk_practicearea_ref` are confirmed correct (same as matters use). The likely cause is the `webApi` reference not being properly resolved or passed — same class of iframe issue we fixed for the Analysis Builder code page.

**File**: `src/solutions/LegalWorkspace/src/components/CreateProject/projectService.ts`

**Actions**:
1. Verify `webApi` is correctly received in `ProjectService` constructor
2. Add console logging to `searchProjectTypes()` and `searchPracticeAreas()` to trace failures
3. Check if `retrieveMultipleRecords` call syntax matches the working pattern in `CreateRecordStep.tsx`
4. Test lookups after fix

**Reference**: The Create Matter wizard lookups work via `searchMatterTypes()` in `CreateRecordStep.tsx` (lines ~270-290) — compare patterns.

---

### Part B: Add Next Steps (Step 3) to Project Wizard

**Goal**: Add the "Next Steps" step with Assign Counsel, Draft Summary, Send Email cards — identical to Create Matter.

**File**: `src/solutions/LegalWorkspace/src/components/CreateProject/ProjectWizardDialog.tsx`

**Imports to add**:
```typescript
import { WizardShell } from '../Wizard/WizardShell';
import type { IWizardShellHandle, IWizardStepConfig, IWizardSuccessConfig } from '../Wizard/wizardShellTypes';
import { NextStepsStep, FollowOnActionId, FOLLOW_ON_STEP_ID_MAP, FOLLOW_ON_STEP_LABEL_MAP } from '../CreateMatter/NextStepsStep';
import { AssignCounselStep } from '../CreateMatter/AssignCounselStep';
import { DraftSummaryStep } from '../CreateMatter/DraftSummaryStep';
import { SendEmailStep, buildDefaultEmailSubject, buildDefaultEmailBody } from '../CreateMatter/SendEmailStep';
```

**State to add**:
```typescript
const shellRef = React.useRef<IWizardShellHandle>(null);
const [selectedActions, setSelectedActions] = React.useState<FollowOnActionId[]>([]);
const [selectedContact, setSelectedContact] = React.useState<IContact | null>(null);
const [summaryText, setSummaryText] = React.useState('');
const [recipientEmails, setRecipientEmails] = React.useState<string[]>([]);
const [emailTo, setEmailTo] = React.useState('');
const [emailSubject, setEmailSubject] = React.useState('');
const [emailBody, setEmailBody] = React.useState('');
```

**Step config to add** (after 'create-record'):
```typescript
{
  id: 'next-steps',
  label: 'Next Steps',
  canAdvance: () => true,
  isEarlyFinish: () => selectedActions.length === 0,
  renderContent: () => (
    <NextStepsStep selectedActions={selectedActions} onSelectionChange={setSelectedActions} />
  ),
}
```

**Dynamic step sync**: Copy the `useEffect` from `WizardDialog.tsx` (lines 235-312) that watches `selectedActions` and calls `shellRef.current?.addDynamicStep()` / `removeDynamicStep()`.

**Reference file**: `src/solutions/LegalWorkspace/src/components/CreateMatter/WizardDialog.tsx` (lines 190-324)

---

### Part C: Wire Full File Pipeline in Finish Handler

**Goal**: When user clicks "Create", upload files to SPE, create document records, trigger AI Document Profile.

**Files**:
- `ProjectWizardDialog.tsx` — resolve SPE container, pass files to service
- `projectService.ts` — add file upload pipeline to `createProject()`

**SPE Container Resolution** (add to ProjectWizardDialog):
```typescript
import { getSpeContainerIdFromBusinessUnit } from '../../services/xrmProvider';

const [speContainerId, setSpeContainerId] = React.useState('');

React.useEffect(() => {
  if (open && webApi) {
    getSpeContainerIdFromBusinessUnit(webApi).then(setSpeContainerId);
  }
}, [open, webApi]);
```

**Finish handler additions**:
After creating the `sprk_project` record:
1. Call `EntityCreationService.uploadFilesToSpe(speContainerId, fileState.uploadedFiles)`
2. Call `EntityCreationService.createDocumentRecords('sprk_projects', projectId, 'sprk_Project', uploadedFiles, { containerId: speContainerId })`
3. Call `EntityCreationService._triggerDocumentAnalysis(documentIds, warnings)`

**Key parameters for project documents**:
- `parentEntityName`: `'sprk_projects'` (plural entity set name)
- `navigationProperty`: `'sprk_Project'` (OData navigation property on sprk_document → sprk_project)
- Verify these names in Dataverse entity metadata

**Reference**: `MatterService.createMatter()` in `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts` (lines ~100-200)

---

### Part D: New BFF Endpoint — `/workspace/projects/pre-fill`

**Goal**: Create a project-specific AI pre-fill endpoint on the BFF API.

**New files**:

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Workspace/ProjectPreFillService.cs` | AI analysis service (follows MatterPreFillService) |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceProjectEndpoints.cs` | Endpoint registration |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/Models/ProjectPreFillResponse.cs` | Response DTO |

**Modified files**:

| File | Change |
|------|--------|
| `src/server/api/Sprk.Bff.Api/Program.cs` | Add `app.MapWorkspaceProjectEndpoints();` (after line ~1808) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkspaceModule.cs` | Add `services.AddScoped<ProjectPreFillService>();` |

**ProjectPreFillResponse model**:
```csharp
public record ProjectPreFillResponse(
    string? ProjectTypeName,
    string? PracticeAreaName,
    string? ProjectName,
    string? Description,
    double Confidence,
    string[] PreFilledFields)
{
    public static ProjectPreFillResponse Empty() => new(null, null, null, null, 0, []);
}
```

**ProjectPreFillService flow**:
1. Validate files (size ≤ 10MB, type PDF/DOCX/XLSX)
2. Store files temporarily via `SpeFileStore` (path: `ai-prefill/{requestId}/`)
3. Extract text via `ITextExtractor`
4. Invoke playbook via `IPlaybookOrchestrationService` with `entity_type="project"`
5. Parse structured JSON response → `ProjectPreFillResponse`
6. Handle timeouts gracefully (return empty response, confidence=0)

**ADR constraints**:
- ADR-007: Use `SpeFileStore` facade (no direct Graph SDK)
- ADR-008: Use `WorkspaceAuthorizationFilter` endpoint filter (no global middleware)
- ADR-010: Register concrete `ProjectPreFillService` (no interface needed)
- ADR-013: AI via `IPlaybookOrchestrationService` (no direct OpenAI calls)

**Reference**: `src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs`

---

### Part E: Wire AI Pre-fill in CreateProjectStep

**Goal**: When user enters Step 2, send uploaded files to BFF for AI field extraction, fuzzy-match results to Dataverse lookups.

**File**: `src/solutions/LegalWorkspace/src/components/CreateProject/CreateProjectStep.tsx`

**Changes**:
1. Accept new prop: `uploadedFiles?: IUploadedFile[]`
2. Add `useEffect` that sends files to `POST /api/workspace/projects/pre-fill` on mount
3. Parse response: map `projectTypeName` → fuzzy search `searchProjectTypes()`, etc.
4. Apply pre-filled values to form state
5. Show "AI Pre-filled" badge on auto-populated fields

**BFF endpoint path** (add to `bffConfig.ts` or inline):
```typescript
const PROJECT_PREFILL_PATH = '/workspace/projects/pre-fill';
```

**Fuzzy matching** (copy from `CreateRecordStep.tsx` lines 205-242):
```typescript
function findBestLookupMatch(aiValue: string, candidates: ILookupItem[]): ILookupItem | null {
  // Scoring: 1.0 exact, 0.8 starts-with, 0.7 contains, 0.5 single-result
  // Threshold: 0.4
}
```

**Reference**: `src/solutions/LegalWorkspace/src/components/CreateMatter/CreateRecordStep.tsx` (lines 389-506 for useEffect, lines 205-242 for fuzzy matching)

---

### Part F: Define "Create New Project" Playbook (Data Configuration)

**Goal**: Create a Dataverse playbook record for project-specific document profiling.

**This is NOT a code task** — it's Dataverse data configuration:
1. Create `sprk_analysisplaybook` record: "Create New Project"
2. Associate skills: project field extraction (project type, practice area, parties)
3. Associate knowledge: project-specific terminology and field definitions
4. Associate tools: document parsing tools

**The AI prompt should extract**:
- Project type (match to `sprk_projecttype_ref` display names)
- Practice area (match to `sprk_practicearea_ref` display names)
- Project name (suggested from document content)
- Description (summary of project scope)
- Key parties (potential assigned attorney/paralegal)

**This playbook is invoked by** `ProjectPreFillService` via `IPlaybookOrchestrationService` with `entity_type="project"`.

---

## Execution Order & Dependencies

```
Part A (Fix lookups) ─────────────────────── Can do now
Part B (Next Steps UI) ──────────────────── Can do now (depends on A for testing)
Part C (File pipeline) ──────────────────── Can do now (depends on A+B)
                                              │
Part D (BFF endpoint) ───────────────────── Can do now (independent, server-side)
                                              │
Part E (AI pre-fill UI) ─────────────────── Depends on D (needs BFF endpoint)
Part F (Playbook config) ────────────────── Depends on D (needs entity_type="project" support)
```

**Recommended parallel execution**:
- **Stream 1**: Parts A → B → C (frontend, LegalWorkspace)
- **Stream 2**: Part D (backend, BFF API)
- **Stream 3**: Parts E + F after D completes

---

## Key File Paths

### Frontend (LegalWorkspace)
| File | Purpose |
|------|---------|
| `src/solutions/LegalWorkspace/src/components/CreateProject/ProjectWizardDialog.tsx` | Main wizard component |
| `src/solutions/LegalWorkspace/src/components/CreateProject/CreateProjectStep.tsx` | Create record form step |
| `src/solutions/LegalWorkspace/src/components/CreateProject/projectService.ts` | Dataverse CRUD + lookups |
| `src/solutions/LegalWorkspace/src/components/CreateProject/projectFormTypes.ts` | Form state types |
| `src/solutions/LegalWorkspace/src/services/EntityCreationService.ts` | File upload + document creation (entity-agnostic) |
| `src/solutions/LegalWorkspace/src/services/xrmProvider.ts` | `getSpeContainerIdFromBusinessUnit()` |
| `src/solutions/LegalWorkspace/src/config/bffConfig.ts` | BFF API base URL |

### Reference (Create Matter — pattern to follow)
| File | Purpose |
|------|---------|
| `src/solutions/LegalWorkspace/src/components/CreateMatter/WizardDialog.tsx` | Reference wizard with all 3 steps + dynamic steps |
| `src/solutions/LegalWorkspace/src/components/CreateMatter/CreateRecordStep.tsx` | AI pre-fill pattern + fuzzy matching |
| `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts` | Full creation pipeline (files + documents + follow-ons) |
| `src/solutions/LegalWorkspace/src/components/CreateMatter/NextStepsStep.tsx` | Next Steps cards component |
| `src/solutions/LegalWorkspace/src/components/CreateMatter/FileUploadZone.tsx` | File upload zone (already reused) |

### Backend (BFF API)
| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs` | Reference pre-fill service |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceMatterEndpoints.cs` | Reference endpoint registration |
| `src/server/api/Sprk.Bff.Api/Api/Workspace/Models/PreFillResponse.cs` | Reference response model |
| `src/server/api/Sprk.Bff.Api/Program.cs` | Endpoint registration (line ~1808) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/WorkspaceModule.cs` | DI registration |

---

## Reusable Code (DO NOT re-implement)

| What | Where | How to Reuse |
|------|-------|-------------|
| `NextStepsStep` component | `components/CreateMatter/NextStepsStep.tsx` | Direct import |
| `AssignCounselStep` | `components/CreateMatter/AssignCounselStep.tsx` | Direct import |
| `DraftSummaryStep` | `components/CreateMatter/DraftSummaryStep.tsx` | Direct import |
| `SendEmailStep` | `components/CreateMatter/SendEmailStep.tsx` | Direct import |
| `FileUploadZone` | `components/CreateMatter/FileUploadZone.tsx` | Already imported |
| `UploadedFileList` | `components/CreateMatter/UploadedFileList.tsx` | Already imported |
| `EntityCreationService` | `services/EntityCreationService.ts` | Call existing methods |
| `getSpeContainerIdFromBusinessUnit` | `services/xrmProvider.ts` | Direct import |
| `findBestLookupMatch` logic | `components/CreateMatter/CreateRecordStep.tsx` (lines 205-242) | Copy function |
| `authenticatedFetch` | `services/` | Direct import |
| Dynamic step sync pattern | `components/CreateMatter/WizardDialog.tsx` (lines 235-312) | Copy useEffect |

---

## Verification Checklist

### Build Verification
- [ ] `cd src/solutions/LegalWorkspace && npm run build` — zero errors
- [ ] `cd src/server/api/Sprk.Bff.Api && dotnet build` — zero errors (after Part D)

### Deployment
- [ ] Upload `dist/corporateworkspace.html` → Dataverse web resource `sprk_corporateworkspace`
- [ ] Deploy BFF API to Azure App Service `spe-api-dev-67e2xz` (after Part D)

### Functional Testing
- [ ] **Lookup fix**: Project Type search returns results
- [ ] **Lookup fix**: Practice Area search returns results
- [ ] **File upload**: Files accepted in Step 1 (PDF/DOCX/XLSX, ≤10MB)
- [ ] **SPE upload**: Files uploaded to SharePoint Embedded on finish
- [ ] **Document records**: `sprk_document` records created and linked to `sprk_project`
- [ ] **AI trigger**: Document Profile analysis queued for each document
- [ ] **AI pre-fill**: Step 2 form auto-populates from uploaded documents (after Part D+E)
- [ ] **Next Steps**: 3 follow-on action cards displayed
- [ ] **Assign Counsel**: Contact search works, assignment saved
- [ ] **Draft Summary**: Summary editable, recipients configurable
- [ ] **Send Email**: Email composed and sent
- [ ] **Success screen**: Project created message with "View Project" button
- [ ] **Regression**: Create New Matter wizard still works
- [ ] **Regression**: Create New Project without files works (skip step 1)
