# Design: SDAP File Upload & Document Creation Dialog (R2)

> **Status**: Draft
> **Author**: AI-Assisted
> **Date**: 2026-03-09

---

## 1. Executive Summary

Migrate the current Document creation-from-files workflow away from the **UniversalQuickCreate PCF control embedded in a Custom Page** to a standalone **HTML Code Page dialog** following the multi-step wizard pattern established in the Legal Workspace ("Create New Matter" wizard). The new dialog provides a guided, multi-step experience: file upload, AI-powered document profiling, and contextual next steps.

### Why Migrate?

| Current State | Problem |
|---|---|
| UniversalQuickCreate is a **PCF control** (React 16, field-bound) embedded in a Dataverse Custom Page | Custom Page + PCF wrapper is an anti-pattern per ADR-006; standalone dialogs should be Code Pages |
| Upload, record creation, AI summary, and RAG indexing are tightly coupled in a single form | No guided workflow; users don't understand the multi-phase process |
| AI summary is opt-in via a tab toggle | Low adoption; users miss the value of Document Profiling |
| No "next steps" guidance after upload | Users must navigate away manually to send emails, run analysis, or explore relationships |
| React 16 constraint (PCF platform-provided) | Cannot use React 18 features, hooks patterns, or modern Fluent UI v9 fully |

### Goals

1. **Guided wizard experience** — Step-by-step flow: Add Files → Summary → Next Steps
2. **Document Profile by default** — Automatically run the Document Profile playbook (not a hard-coded OpenAI summary)
3. **Dual pipeline preserved** — Files saved to SPE AND sent to Azure AI Search pipeline for indexing
4. **Actionable next steps** — Send Email, Work on Analysis, Find Similar — all one click away
5. **ADR-006 compliant** — Standalone dialog as a React 18 Code Page, not a Custom Page + PCF wrapper

---

## 2. Current State: UniversalQuickCreate Flow

### Architecture

```
Dataverse Form → Ribbon Button → Custom Page → UniversalQuickCreate PCF
                                                    │
                                                    ├─ Tab 1: "Upload Files"
                                                    │   ├─ MultiFileUploadService → BFF API → SPE
                                                    │   ├─ DocumentRecordService → Dataverse WebAPI
                                                    │   └─ indexDocumentsToRag() → BFF API → AI Search
                                                    │
                                                    └─ Tab 2: "AI Summary" (opt-in)
                                                        └─ useAiSummary → SSE stream → BFF API
                                                           └─ Playbook UpdateRecord node writes to Dataverse
```

### Key Services (to be reused)

| Service | Location | Responsibility |
|---|---|---|
| `MultiFileUploadService` | `src/client/pcf/UniversalQuickCreate/control/services/` | Parallel file upload to SPE via BFF API |
| `FileUploadService` | Same directory | Single file upload wrapper around SdapApiClient |
| `SdapApiClient` | Same directory | MSAL-authenticated HTTP client for BFF API |
| `DocumentRecordService` | Same directory | Creates `sprk_document` records in Dataverse with dynamic nav property lookup |
| `useAiSummary` | Same directory | Manages concurrent SSE streams for Document Profile playbook |

### BFF API Endpoints (existing, no changes needed)

| Endpoint | Purpose |
|---|---|
| `PUT /api/obo/containers/{containerId}/files/{fileName}` | Upload file to SPE |
| `POST /api/ai/rag/index-file` | Enqueue document for RAG indexing (non-blocking) |
| `POST /api/ai/document-analysis` | Stream Document Profile analysis via SSE |
| `POST /api/ai/playbooks/{id}/execute` | Execute playbook with SSE streaming |

### Four-Phase Upload Pipeline (preserved in new design)

1. **Upload files to SPE** — `MultiFileUploadService.uploadFiles()` → BFF API → Graph API → SharePoint Embedded
2. **Create Dataverse records** — `DocumentRecordService.createDocuments()` → `context.webAPI.createRecord()`
3. **Run Document Profile** — `useAiSummary` → SSE stream → playbook writes results server-side
4. **Index to RAG** — `indexDocumentsToRag()` → fire-and-forget → Service Bus → AI Search dual-index pipeline

---

## 3. Proposed Solution: Document Upload Wizard Dialog

### Architecture

```
Dataverse Form → Ribbon Button / Workspace Action
    │
    └─ Xrm.Navigation.navigateTo({
           pageType: "webresource",
           webresourceName: "sprk_documentuploadwizard",
           data: "matterId={id}&matterName={name}&containerId={id}"
       }, { target: 2, width: 85%, height: 85% })
           │
           └─ React 18 Code Page (HTML web resource)
               │
               ├─ WizardShell (reused from LegalWorkspace)
               │   ├─ Step 1: Add Files
               │   ├─ Step 2: Summary (Document Profile)
               │   └─ Next Steps (dynamic)
               │       ├─ Send Email
               │       ├─ Work on Analysis
               │       └─ Find Similar
               │
               ├─ Services (extracted from UniversalQuickCreate)
               │   ├─ MultiFileUploadService
               │   ├─ DocumentRecordService
               │   ├─ SdapApiClient (MSAL auth)
               │   └─ useAiSummary (SSE streaming)
               │
               └─ Dual Pipeline
                   ├─ SPE storage (Graph API via BFF)
                   └─ AI Search indexing (Service Bus → dual-index)
```

### Technology Stack

| Layer | Technology | Reason |
|---|---|---|
| Runtime | React 18 (bundled) | Code Page — not platform-provided; enables hooks, concurrent features |
| UI Framework | Fluent UI v9 | ADR-021 compliance; dark mode; semantic tokens |
| Wizard Shell | WizardShell from LegalWorkspace | Proven pattern; domain-free; dynamic steps |
| Auth | MSAL.js (@azure/msal-browser) | OBO token for BFF API calls |
| Build | Webpack (inline HTML) | Standard Code Page build pipeline |
| Deployment | Dataverse web resource | `sprk_documentuploadwizard` |

---

## 4. Wizard Steps

### Step 1: Add Files

**Purpose**: Select and upload files; create Document records in Dataverse.

```
┌─────────────────────────────────────────────────────────┐
│  📄 Add Documents                              [X]      │
├──────────┬──────────────────────────────────────────────┤
│          │                                              │
│ ● Add    │  ┌──────────────────────────────────┐        │
│   Files  │  │                                  │        │
│          │  │   Drag & drop files here          │        │
│ ○ Sum-   │  │   or click to browse              │        │
│   mary   │  │                                  │        │
│          │  └──────────────────────────────────┘        │
│ ○ Next   │                                              │
│   Steps  │  Selected Files:                             │
│          │  ┌──────────────────────────────────┐        │
│          │  │ 📄 Contract_Draft.pdf    2.1 MB  │        │
│          │  │ 📄 Amendment_01.docx     340 KB  │        │
│          │  │ 📄 Exhibit_A.pdf         1.5 MB  │        │
│          │  └──────────────────────────────────┘        │
│          │                                              │
│          │  Related To: Acme Corp v. Beta LLC (Matter)  │
│          │                                              │
├──────────┴──────────────────────────────────────────────┤
│  [Cancel]                              [Back]  [Next]   │
└─────────────────────────────────────────────────────────┘
```

**Behavior**:
- Drag-and-drop zone + file picker button
- Validation: max 10 files, 10 MB each, 100 MB total (matches current limits)
- File list with name, size, remove button
- "Related To" shows the parent entity (Matter, Project, etc.) — read-only, passed via URL params
- **On "Next"**: Executes Phases 1-2 and kicks off Phase 4:
  1. Upload all files to SPE via `MultiFileUploadService`
  2. Create `sprk_document` records in Dataverse via `DocumentRecordService`
  3. Fire-and-forget RAG indexing via `indexDocumentsToRag()`
  4. Progress indicator during upload (file-by-file progress)
- **canAdvance**: At least 1 file selected
- **Phase 3 (Document Profile) starts automatically** after records are created — runs in background while user views Step 2

### Step 2: Summary (Document Profile)

**Purpose**: Show AI-generated document profiles as they stream in; allow user to review.

```
┌─────────────────────────────────────────────────────────┐
│  📄 Add Documents                              [X]      │
├──────────┬──────────────────────────────────────────────┤
│          │                                              │
│ ✓ Add    │  Document Profiles                           │
│   Files  │                                              │
│          │  ┌──────────────────────────────────┐        │
│ ● Sum-   │  │ 📄 Contract_Draft.pdf            │        │
│   mary   │  │ ━━━━━━━━━━━━━━━━━ 100%           │        │
│          │  │ TL;DR: Professional services      │        │
│ ○ Next   │  │ agreement between Acme Corp and   │        │
│   Steps  │  │ Beta LLC for software consulting. │        │
│          │  │                                  │        │
│          │  │ Type: Contract                    │        │
│          │  │ Keywords: services, consulting,   │        │
│          │  │ software, Acme, Beta              │        │
│          │  └──────────────────────────────────┘        │
│          │  ┌──────────────────────────────────┐        │
│          │  │ 📄 Amendment_01.docx              │        │
│          │  │ ━━━━━━━━━░░░░░░░░░ 55%  ⏳        │        │
│          │  │ Analyzing...                      │        │
│          │  └──────────────────────────────────┘        │
│          │                                              │
├──────────┴──────────────────────────────────────────────┤
│  [Cancel]                              [Back]  [Next]   │
└─────────────────────────────────────────────────────────┘
```

**Behavior**:
- Shows each uploaded document with its Document Profile streaming results
- Uses `useAiSummary` hook — manages concurrent SSE streams (max 3 concurrent)
- Displays: TL;DR, Document Type, Keywords (from playbook output fields)
- Progress indicator per document (streaming progress)
- Documents that complete show expanded profile card; in-progress show spinner
- **Document Profile playbook** (JPS-based, not hardcoded OpenAI) runs server-side via the playbook orchestration system:
  1. Client calls `GET /api/ai/playbooks/by-name/Document%20Profile` to resolve playbook ID
  2. Client calls `POST /api/ai/analysis/execute` with `{ documentIds, playbookId }`
  3. `AnalysisOrchestrationService` delegates to `ExecutePlaybookAsync()` → `PlaybookOrchestrationService`
  4. Playbook executes tool nodes in topological order: SummaryHandler (TL;DR + summary), DocumentClassifierHandler (type), EntityExtractorHandler (parties, dates, amounts)
  5. Each handler uses JPS prompt templates (via `PromptSchemaRenderer`) — NOT hardcoded prompts
  6. Playbook's `UpdateRecord` node writes results to `sprk_document` fields (`sprk_filesummary`, `sprk_filetldr`, `sprk_filekeywords`, `sprk_documenttype`, `sprk_entities`)
- **canAdvance**: Always true (user can skip ahead; profiles continue in background)
- **isEarlyFinish**: Returns true if user clicks "Next" — proceeds to Next Steps selection

### Step 3: Next Steps

**Purpose**: Offer contextual follow-on actions after document upload.

```
┌─────────────────────────────────────────────────────────┐
│  📄 Add Documents                              [X]      │
├──────────┬──────────────────────────────────────────────┤
│          │                                              │
│ ✓ Add    │  What would you like to do next?             │
│   Files  │                                              │
│          │  ┌──────────────────────────────────┐        │
│ ✓ Sum-   │  │ ☐  📧 Send Email                 │        │
│   mary   │  │     Compose an email with links   │        │
│          │  │     to the matter and documents   │        │
│ ● Next   │  └──────────────────────────────────┘        │
│   Steps  │  ┌──────────────────────────────────┐        │
│          │  │ ☐  🔬 Work on Analysis            │        │
│          │  │     Open Analysis Builder to       │        │
│          │  │     select a playbook              │        │
│          │  └──────────────────────────────────┘        │
│          │  ┌──────────────────────────────────┐        │
│          │  │ ☐  🔗 Find Similar                │        │
│          │  │     Search for semantically        │        │
│          │  │     related documents              │        │
│          │  └──────────────────────────────────┘        │
│          │                                              │
├──────────┴──────────────────────────────────────────────┤
│  [Cancel]                                     [Finish]  │
└─────────────────────────────────────────────────────────┘
```

**Behavior**:
- Checkbox cards for each next-step action (multi-select)
- Each card can be checked/unchecked freely
- **isEarlyFinish**: Returns true — "Next" button becomes "Finish" (if no dynamic steps selected) or advances to the first selected dynamic step (e.g., Send Email)
- **Skip support**: When dynamic steps are injected (e.g., Send Email), each dynamic step includes a **"Skip" footer action** button allowing the user to bypass that step and move to the next. This follows the same pattern as the Corporate Workspace wizard where users can opt out of a previously-selected step without going back to deselect it.

**Skip implementation** (per dynamic step):
```typescript
const emailStepConfig: IWizardStepConfig = {
    id: "send-email",
    label: "Send Email",
    renderContent: () => <SendEmailStep ... />,
    canAdvance: () => emailToRef.current.length > 0,
    footerActions: (
        <Button appearance="subtle" onClick={() => shellRef.current?.nextStep()}>
            Skip
        </Button>
    ),
};
```

- On finish: show success screen with buttons for post-wizard actions (Analysis Builder, Find Similar)

### Next Step Actions (Mixed: Dynamic Steps + Post-Wizard)

The three next-step actions use two different patterns depending on whether they need wizard context:

| Action | Pattern | Behavior |
|---|---|---|
| **Send Email** | **Dynamic wizard step** (injected after Next Steps) | `addDynamicStep()` adds a "Send Email" step with pre-filled subject/body/recipients. Email saved as Dataverse activity on finish. |
| **Work on Analysis** | **Post-wizard dialog** (opens after wizard closes) | Success screen button opens Analysis Builder Code Page via `navigateTo` → `sprk_analysisbuilder` with `documentId`, `containerId`, `fileId`, `apiBaseUrl` |
| **Find Similar** | **Post-wizard dialog** (opens after wizard closes) | Success screen button opens shared `FindSimilarDialog` with uploaded documents pre-loaded (no re-upload needed) |

**Dynamic step injection** (Send Email):
```typescript
// When user checks "Send Email" in Next Steps:
const emailStepConfig: IWizardStepConfig = {
    id: "send-email",
    label: "Send Email",
    renderContent: () => (
        <SendEmailStep
            defaultSubject={`New Documents: ${parentEntityName}`}
            defaultBody={buildDocumentEmailBody(uploadedDocuments, parentEntityName)}
            regardingEntityType={parentEntityType}
            regardingEntityId={parentEntityId}
            emailTo={emailTo} onEmailToChange={setEmailTo}
            emailSubject={emailSubject} onEmailSubjectChange={setEmailSubject}
            emailBody={emailBody} onEmailBodyChange={setEmailBody}
            onSearchUsers={searchDataverseUsers}
        />
    ),
    canAdvance: () => emailToRef.current.length > 0,
};
shellRef.current?.addDynamicStep(emailStepConfig, NEXT_STEPS_CANONICAL_ORDER);

const NEXT_STEPS_CANONICAL_ORDER = ["send-email"];
```

**Post-wizard actions** (Analysis Builder, Find Similar):
- Shown as buttons on the `WizardSuccessScreen`
- "Work on Analysis" opens: `Xrm.Navigation.navigateTo({ webresourceName: "sprk_analysisbuilder", data: params })`
- "Find Similar" opens the shared `FindSimilarDialog` component (rendered inline, not via `navigateTo`)

### Success Screen

After finish, display the `WizardSuccessScreen` with:
- Checkmark icon
- Title: "{N} documents added" (or "added with warnings" if partial failures)
- Body: Summary of uploaded files with links
- Warnings: Any failed uploads or profile errors
- Actions: Buttons to open selected next-step dialogs, plus "Close"

---

## 5. Service Architecture

### Service Extraction Strategy

The upload services currently live inside the UniversalQuickCreate PCF control. For the new Code Page, these services need to be **extracted to a shared location** or **copied and adapted** for React 18 / Code Page context.

**Recommended approach**: Extract core services to `src/client/shared/services/document-upload/` for reuse by both the existing PCF (backward compatibility) and the new Code Page.

| Service | Current Location | New Location | Changes |
|---|---|---|---|
| `MultiFileUploadService` | `src/client/pcf/UniversalQuickCreate/control/services/` | `src/client/shared/services/document-upload/` | Minimal — decouple from PCF context |
| `FileUploadService` | Same | Same shared location | None |
| `SdapApiClient` | Same | Same shared location | Use `@spaarke/auth` for MSAL instead of PCF-specific auth |
| `DocumentRecordService` | Same | Same shared location | Replace `context.webAPI` with direct Dataverse OData calls (Code Pages don't have PCF context) |
| `useAiSummary` | Same | Same shared location | None — already a React hook |

### Authentication in Code Page Context

The Code Page runs inside a Dataverse iframe and uses **`@spaarke/auth`** (the shared MSAL wrapper) instead of the PCF `context.webAPI`:

```typescript
// Code Page auth pattern (from existing code pages)
import { getAccessToken } from "@spaarke/auth";

const token = await getAccessToken(["https://graph.microsoft.com/.default"]);
// Use token for BFF API calls via SdapApiClient
```

For Dataverse record creation, the Code Page uses **direct OData HTTP calls** (not `context.webAPI`):

```typescript
// Direct Dataverse OData call (Code Page pattern)
const response = await fetch(
    `${Xrm.Utility.getGlobalContext().getClientUrl()}/api/data/v9.2/sprk_documents`,
    {
        method: "POST",
        headers: {
            "Content-Type": "application/json",
            "OData-MaxVersion": "4.0",
            "OData-Version": "4.0",
        },
        body: JSON.stringify(payload),
    }
);
```

### Dual Pipeline: SPE + AI Search (Critical Path)

Both pipelines MUST execute for every uploaded file. This is non-negotiable.

```
File Selected by User
    │
    ├─── Phase 1: Upload to SPE ──────────────────────────────┐
    │    MultiFileUploadService.uploadFiles()                  │
    │    → PUT /api/obo/containers/{containerId}/files/{name}  │
    │    → Returns: SpeFileMetadata (driveId, itemId, webUrl)  │
    │                                                          │
    ├─── Phase 2: Create Dataverse Record ─────────────────────┤
    │    DocumentRecordService.createDocuments()                │
    │    → POST .../sprk_documents (OData)                     │
    │    → Returns: documentId                                 │
    │                                                          │
    ├─── Phase 3: Document Profile (background) ───────────────┤
    │    useAiSummary → POST /api/ai/document-analysis (SSE)   │
    │    → Playbook UpdateRecord writes to sprk_document       │
    │                                                          │
    └─── Phase 4: RAG Indexing (fire-and-forget) ──────────────┘
         POST /api/ai/rag/index-file
         → Service Bus → RagIndexingPipeline
         → Dual-index: Knowledge (512-token) + Discovery (1024-token)
         → text-embedding-3-large (3072-dim vectors)
```

---

## 6. Wizard Shell Reuse

### WizardShell from LegalWorkspace

The new dialog reuses the **domain-free** `WizardShell` component and its supporting infrastructure:

| Component | Source | Reuse Strategy |
|---|---|---|
| `WizardShell` | `src/solutions/LegalWorkspace/src/components/Wizard/WizardShell.tsx` | Import directly or copy to shared |
| `WizardStepper` | Same Wizard directory | Import or copy |
| `WizardSuccessScreen` | Same Wizard directory | Import or copy |
| `wizardShellReducer` | Same Wizard directory | Import or copy |
| `wizardShellTypes` | Same Wizard directory | Import or copy |

**Recommended**: Move wizard shell components to `src/client/shared/components/Wizard/` so all Code Pages can reuse them. The LegalWorkspace and this new dialog both import from shared.

### Step Configuration

```typescript
const steps: IWizardStepConfig[] = [
    {
        id: "add-files",
        label: "Add Files",
        renderContent: (handle) => <AddFilesStep ... />,
        canAdvance: () => selectedFiles.length > 0,
    },
    {
        id: "summary",
        label: "Summary",
        renderContent: (handle) => <SummaryStep ... />,
        canAdvance: () => true, // Can always proceed; profiles run in background
    },
    {
        id: "next-steps",
        label: "Next Steps",
        renderContent: (handle) => <NextStepsStep ... />,
        canAdvance: () => true,
        isEarlyFinish: () => true, // "Next" becomes "Finish"
    },
];
```

---

## 7. URL Parameters (Dialog Input)

The dialog receives context via URL query parameters (standard Code Page pattern):

| Parameter | Type | Required | Description |
|---|---|---|---|
| `parentEntityType` | string | Yes | Logical name of parent entity (e.g., `sprk_matter`) |
| `parentEntityId` | GUID | Yes | Record ID of parent entity |
| `parentEntityName` | string | Yes | Display name for "Related To" label |
| `containerId` | string | Yes | SPE container ID for file upload |
| `apiBaseUrl` | string | No | BFF API base URL (defaults to app setting) |

**Example invocation**:
```typescript
Xrm.Navigation.navigateTo(
    {
        pageType: "webresource",
        webresourceName: "sprk_documentuploadwizard",
        data: `parentEntityType=sprk_matter&parentEntityId=${matterId}&parentEntityName=${encodeURIComponent(matterName)}&containerId=${containerId}`,
    },
    {
        target: 2,
        position: 1,
        width: { value: 85, unit: "%" },
        height: { value: 85, unit: "%" },
    }
);
```

---

## 8. File Structure

```
src/solutions/DocumentUploadWizard/
├── index.html                          # HTML entry point
├── webpack.config.js                   # Build config (inline HTML pattern)
├── package.json
├── tsconfig.json
├── src/
│   ├── main.tsx                        # React 18 createRoot entry
│   ├── App.tsx                         # FluentProvider + theme + main component
│   ├── DocumentUploadWizardDialog.tsx  # Domain-specific wizard (like WizardDialog.tsx)
│   ├── components/
│   │   ├── AddFilesStep.tsx            # Step 1: file selection + drag-drop
│   │   ├── SummaryStep.tsx             # Step 2: Document Profile streaming results
│   │   ├── NextStepsStep.tsx           # Step 3: follow-on action selection
│   │   └── FileUploadProgress.tsx      # Upload progress overlay/indicator
│   ├── services/
│   │   ├── uploadOrchestrator.ts       # Coordinates the 4-phase pipeline
│   │   └── nextStepLauncher.ts         # Opens selected next-step dialogs
│   └── types/
│       └── wizardTypes.ts              # Dialog-specific type definitions
```

**Shared components** (to be extracted if not already shared):
```
src/client/shared/
├── components/
│   └── Wizard/                         # WizardShell, Stepper, SuccessScreen
├── services/
│   └── document-upload/                # MultiFileUploadService, SdapApiClient, etc.
└── hooks/
    └── useAiSummary.ts                 # SSE streaming hook
```

---

## 9. ADR Compliance

| ADR | Requirement | How This Design Complies |
|---|---|---|
| ADR-006 | No legacy JS webresources; standalone dialog → Code Page | New dialog is an HTML Code Page (React 18), not a Custom Page + PCF wrapper |
| ADR-007 | SpeFileStore facade; no Graph SDK leaks | All SPE operations go through BFF API; no direct Graph calls from client |
| ADR-012 | Reuse `@spaarke/ui-components` | WizardShell, shared components from library |
| ADR-013 | AI Tool Framework; extend BFF | Document Profile runs via existing BFF playbook endpoints |
| ADR-021 | Fluent UI v9; no hard-coded colors; dark mode | All UI uses Fluent v9 semantic tokens; theme from FluentProvider |
| ADR-022 | Code Pages bundle React 18; PCFs use platform React 16 | Code Page bundles its own React 18; no platform dependency |

---

## 10. Migration Strategy

### Phase 1: Build New Dialog
- Create `DocumentUploadWizard` Code Page solution
- Extract shared services from UniversalQuickCreate
- Implement 3-step wizard with full pipeline

### Phase 2: Integration
- Add ribbon button / workspace action to open new dialog
- Wire up next-step dialog launchers
- Test dual pipeline (SPE + RAG indexing)

### Phase 3: Deprecation
- Update existing ribbon commands to open new dialog instead of Custom Page
- Mark UniversalQuickCreate Custom Page as deprecated
- Keep UniversalQuickCreate PCF available for backward compatibility (other embedding scenarios)

---

## 11. Success Criteria

1. Files are uploaded to SPE and `sprk_document` records created in Dataverse
2. Files are indexed to Azure AI Search (dual-index: knowledge + discovery)
3. Document Profile playbook runs automatically and writes results to `sprk_document` fields
4. User can select and launch next-step actions (Send Email, Analysis Builder, Find Similar)
5. Dialog works in dark mode and light mode
6. No regressions in upload reliability or performance
7. Custom Page dependency eliminated per ADR-006

---

## 12. Design Decisions (Resolved)

### Decision 1: Extract Services to Shared (Day One)

**Choice**: Extract upload services to `src/client/shared/` as part of this project.

**Rationale**: Both the existing UniversalQuickCreate PCF and the new Document Upload Wizard Code Page need the same services. Extracting now prevents drift and enables reuse by future Code Pages.

**Services to extract**:

| Service | From | To | Adaptation |
|---|---|---|---|
| `MultiFileUploadService` | `src/client/pcf/UniversalQuickCreate/control/services/` | `src/client/shared/services/document-upload/` | Decouple from PCF context; accept auth token provider as parameter |
| `FileUploadService` | Same | Same | Minimal changes |
| `SdapApiClient` | Same | Same | Use `@spaarke/auth` for MSAL instead of PCF-specific auth |
| `DocumentRecordService` | Same | Same | Dual API: `context.webAPI` (PCF) or direct OData (Code Page) via strategy pattern |
| `useAiSummary` | Same | `src/client/shared/hooks/` | Already a React hook; no changes needed |

**UniversalQuickCreate PCF** will be updated to import from shared (thin wrapper for backward compatibility).

---

### Decision 2: Email — Follow Workspace Playbook Pattern (SendEmailStep)

**Choice**: Use the same inline email step pattern as the Corporate Workspace wizards (`SendEmailStep.tsx` / `SummarizeSendEmailStep.tsx`).

**Pattern**: An embedded wizard step (not a separate dialog) with:
- `LookupField` for recipient selection (searches `systemuser` table)
- Pre-filled subject: `"New Documents: {parentEntityName}"`
- Pre-filled body template with matter link + document list with URLs
- Saved as a **Dataverse email activity** on the parent entity (draft or sent via BFF communication endpoint)

**Implementation**: Extract the email step pattern to shared alongside the wizard components:
```
src/client/shared/components/
├── Wizard/           # WizardShell, Stepper, SuccessScreen
└── EmailStep/        # SendEmailStep (generic), LookupField, template builders
```

The `SendEmailStep` becomes a reusable component accepting:
```typescript
interface ISendEmailStepProps {
  defaultSubject: string;
  defaultBody: string;
  regardingEntityType: string;
  regardingEntityId: string;
  onSearchUsers: (query: string) => Promise<ILookupItem[]>;
  // Controlled fields
  emailTo: string;
  onEmailToChange: (value: string) => void;
  emailSubject: string;
  onEmailSubjectChange: (value: string) => void;
  emailBody: string;
  onEmailBodyChange: (value: string) => void;
}
```

**In the wizard**: "Send Email" is a **dynamic step** injected when the user checks it in the Next Steps step (following the `addDynamicStep` + canonical ordering pattern from CreateMatter wizard).

---

### Decision 3: Find Similar — Tenant-Wide, Extract to Shared

**Choice**: Use the same Find Similar dialog as the Corporate Workspace (`FindSimilarDialog.tsx`) — tenant-wide semantic search. Extract to a shared component.

**Current state**: `FindSimilarDialog` lives only in `src/solutions/LegalWorkspace/src/components/FindSimilar/`. It needs to be shared since it's now used by both the Workspace and this upload dialog.

**Components to extract to shared**:
```
src/client/shared/components/
└── FindSimilar/
    ├── FindSimilarDialog.tsx         # Two-step wizard: upload → results
    ├── FindSimilarResultsStep.tsx    # Three-tab grid (Documents, Matters, Projects)
    ├── findSimilarService.ts         # Extract text → semantic search pipeline
    ├── findSimilarTypes.ts           # IDocumentResult, IRecordResult, IFindSimilarResults
    └── FilePreviewDialog.tsx         # Document preview modal
```

**Search pipeline** (unchanged):
1. Extract text from uploaded files → `POST /workspace/files/extract-text`
2. In parallel: search documents (`POST /ai/search`), matters (`POST /ai/search/records`), projects (`POST /ai/search/records`)
3. Display in tabbed DataGrid with lazy loading (PAGE_SIZE = 10)

**In the upload wizard**: "Find Similar" is a **post-wizard action** — after the wizard success screen, clicking "Find Similar" opens the `FindSimilarDialog` with the uploaded documents already loaded (no re-upload needed).

---

### Decision 4: Deprecation — Remove Custom Page After 1 Release

**Choice**: Keep the old Custom Page + UniversalQuickCreate for one release cycle as fallback, then remove.

**Timeline**:
| Release | Action |
|---|---|
| R2 (this project) | Ship new Document Upload Wizard Code Page. Update default ribbon commands to use it. Keep Custom Page available via alternate ribbon button. |
| R3 (next release) | Remove Custom Page wrapper. UniversalQuickCreate PCF remains available (it's still useful for embedded quick-create scenarios). |

---

### Decision 5: All Parent Entity Types from Day One

**Choice**: Support all parent entity types (Matter, Project, Invoice, Account, Contact, etc.) from day one.

**Rationale**: The dynamic navigation property lookup is already built in `DocumentRecordService` (Phase 7 — NavMapClient → BFF API → Dataverse metadata). The URL parameters already pass `parentEntityType`, `parentEntityId`, and `parentEntityName`. No extra work needed.

---

## 13. Search Profile Enrichment — Integrated into Document Profile

### Decision: Add `sprk_searchprofile` as Another Output in the Existing Handler

Rather than a separate job, chained event, or external service, the search profile is generated **inside the Document Profile playbook execution** as an additional output — the same way TL;DR, Summary, Keywords, Document Type, and Entities are produced today.

### Why This Works

The Document Profile playbook already extracts everything needed for a search profile:
- `sprk_filesummary` (narrative summary)
- `sprk_filetldr` (concise summary)
- `sprk_filekeywords` (keyword list)
- `sprk_documenttype` (classification)
- `sprk_entities` (extracted organizations, people, dates, fees, references)

The search profile is a **synthesis** of these outputs — a 150-200 word BM25-optimized dense prose block. It doesn't require re-reading the document. It can be computed from the other outputs at the end of the same playbook run.

### Implementation

**1. Add a new output type to the Document Profile playbook** (JPS definition):

Add a `searchProfile` output to the JPS schema that instructs the model to synthesize a search-optimized profile from the other outputs:

```json
{
  "outputs": {
    "tldr": { ... },
    "summary": { ... },
    "keywords": { ... },
    "documentType": { ... },
    "entities": { ... },
    "searchProfile": {
      "type": "string",
      "description": "150-200 word BM25-optimized dense prose for search ranking. Include: document type stated naturally, all party names, reference numbers, key domain terms and synonyms, parent entity context. No filler words. No term repetition beyond 3x.",
      "maxLength": 1500
    }
  }
}
```

**2. Extend `DocumentProfileFieldMapper`** — add the mapping + builder function:

```csharp
// In GetFieldName switch:
"searchprofile" => "sprk_searchprofile",
"search profile" => "sprk_searchprofile",
```

**3. Add `BuildSearchProfile` function** to `DocumentProfileFieldMapper`:

The search profile value needs a dedicated builder function that assembles a BM25-optimized dense prose block from the other profile outputs. This runs in `PrepareValue` (or as a post-processing step in `CreateFieldMapping`) after all other outputs are collected:

```csharp
/// <summary>
/// Builds a search-optimized profile from Document Profile outputs.
/// Produces 150-200 word dense prose optimized for BM25 field-length
/// normalization and semantic ranking.
/// </summary>
/// <param name="outputs">All Document Profile outputs (summary, tldr, keywords, entities, type)</param>
/// <param name="parentEntityName">Display name of parent entity (e.g., "Acme Corp v. Beta LLC")</param>
/// <param name="parentEntityType">Logical name of parent entity (e.g., "sprk_matter")</param>
/// <param name="fileName">Original file name</param>
/// <returns>Dense search profile string, or null if insufficient data</returns>
public static string? BuildSearchProfile(
    Dictionary<string, string?> outputs,
    string? parentEntityName = null,
    string? parentEntityType = null,
    string? fileName = null)
{
    var parts = new List<string>();

    // Document type stated naturally (high semantic signal)
    var docType = outputs.GetValueOrDefault("Document Type")
                  ?? outputs.GetValueOrDefault("documentType");
    if (!string.IsNullOrWhiteSpace(docType))
        parts.Add($"{docType}.");

    // TL;DR as the lead (concise, information-dense)
    var tldr = outputs.GetValueOrDefault("TL;DR")
               ?? outputs.GetValueOrDefault("tldr");
    if (!string.IsNullOrWhiteSpace(tldr))
        parts.Add(tldr);

    // Extracted entities — parties, people, organizations (high IDF value)
    var entities = outputs.GetValueOrDefault("Entities")
                   ?? outputs.GetValueOrDefault("entities");
    if (!string.IsNullOrWhiteSpace(entities))
    {
        var entityNames = ExtractEntityNames(entities); // Parse JSON, flatten names
        if (entityNames.Any())
            parts.Add($"Parties: {string.Join(", ", entityNames)}.");
    }

    // Keywords (domain terms, synonyms — high IDF)
    var keywords = outputs.GetValueOrDefault("Keywords")
                   ?? outputs.GetValueOrDefault("keywords");
    if (!string.IsNullOrWhiteSpace(keywords))
        parts.Add($"Topics: {keywords}.");

    // Parent entity context (cross-entity discoverability)
    if (!string.IsNullOrWhiteSpace(parentEntityName))
    {
        var entityLabel = parentEntityType?.Replace("sprk_", "") ?? "record";
        parts.Add($"Related {entityLabel}: {parentEntityName}.");
    }

    // File name (exact-match search target)
    if (!string.IsNullOrWhiteSpace(fileName))
        parts.Add($"File: {fileName}.");

    if (parts.Count < 2) return null; // Not enough data

    return string.Join(" ", parts);
}
```

This is a **deterministic builder** — no AI call needed. It assembles the search profile from the outputs already produced by the playbook's AI calls. This means:
- Zero additional latency or cost
- Consistent, predictable output format
- Always in sync with the other profile fields (built from the same data)

The builder runs inside `CreateFieldMapping` after all other outputs are collected, adding `sprk_searchprofile` to the field mapping dictionary before the `UpdateRecord` node writes to Dataverse.

**4. No new handler, no new job, no new service** — the search profile is built deterministically from existing outputs and rides the existing playbook execution pipeline end-to-end.

### Advantages Over Separate Job/Service

| Concern | Integrated (chosen) | Separate job (rejected) |
|---|---|---|
| **Latency** | Zero additional — computed in same AI call | Extra Service Bus round-trip + separate AI call |
| **Dependencies** | None — changes only the JPS definition + field mapper | Requires `RecordEnrichmentService` from search-optimization project |
| **Coverage** | Every Document Profile run (wizard, email automation, bulk re-profile) | Must wire up triggers for every entry point |
| **Consistency** | Profile always in sync with summary/keywords/entities (same AI call) | Risk of stale profile if job fails or lags |
| **Complexity** | ~10 lines of code change | New handler, Service Bus queue, job tracking |

### Relationship to `ai-semantic-search-optimization-r1`

The search-optimization project defines `RecordEnrichmentService` for **all 10 entity types**. For documents specifically, that project noted:

> *"For documents, the search profile is synthesized from existing enrichment fields rather than re-analyzing the document content."*

By building it into the Document Profile handler here, we **fulfill the document portion** of the search-optimization project's scope. The search-optimization project still handles the other 9 entity types (matters, projects, invoices, etc.) which don't have a playbook pipeline — those need the `RecordEnrichmentService` approach.

### What This Means for the Pipeline

The pipeline stays at 4 phases (not 5). The search profile is generated as part of Phase 3:

```
Phase 1: Upload to SPE              ─── MultiFileUploadService (parallel)
Phase 2: Create Dataverse records    ─── DocumentRecordService (sequential, OData)
Phase 3: Document Profile playbook   ─── useAiSummary → JPS playbook orchestration (SSE streaming)
         └─ Outputs: sprk_filesummary, sprk_filetldr, sprk_filekeywords,
                     sprk_documenttype, sprk_entities, sprk_searchprofile (NEW)
Phase 4: RAG indexing                ─── POST /api/ai/rag/index-file (fire-and-forget)
```

---

## 14. Shared Component Extraction Summary

This project requires extracting several components from `LegalWorkspace` and `UniversalQuickCreate` to shared locations. This is a prerequisite task before building the new dialog.

### Components to Extract

```
src/client/shared/
├── components/
│   ├── Wizard/                          # FROM: LegalWorkspace/src/components/Wizard/
│   │   ├── WizardShell.tsx
│   │   ├── WizardStepper.tsx
│   │   ├── WizardSuccessScreen.tsx
│   │   ├── wizardShellReducer.ts
│   │   └── wizardShellTypes.ts
│   │
│   ├── EmailStep/                       # FROM: LegalWorkspace/src/components/CreateMatter/
│   │   ├── SendEmailStep.tsx            # Genericized (configurable templates)
│   │   ├── LookupField.tsx
│   │   └── emailHelpers.ts             # extractEmailFromUserName, template builders
│   │
│   ├── FindSimilar/                     # FROM: LegalWorkspace/src/components/FindSimilar/
│   │   ├── FindSimilarDialog.tsx
│   │   ├── FindSimilarResultsStep.tsx
│   │   ├── findSimilarService.ts
│   │   ├── findSimilarTypes.ts
│   │   └── FilePreviewDialog.tsx
│   │
│   └── FileUpload/                      # FROM: LegalWorkspace/src/components/CreateMatter/
│       ├── FileUploadZone.tsx           # Drag-drop zone
│       └── UploadedFileList.tsx         # File list with remove
│
├── services/
│   └── document-upload/                 # FROM: UniversalQuickCreate/control/services/
│       ├── MultiFileUploadService.ts
│       ├── FileUploadService.ts
│       ├── SdapApiClient.ts
│       ├── DocumentRecordService.ts
│       └── types.ts                     # SpeFileMetadata, UploadFilesResult, etc.
│
└── hooks/
    └── useAiSummary.ts                  # FROM: UniversalQuickCreate/control/services/
```

### Impact on Existing Solutions

| Solution | Change |
|---|---|
| **LegalWorkspace** | Update imports from local `./Wizard/`, `./FindSimilar/`, `./CreateMatter/SendEmailStep` to `@spaarke/shared/...` |
| **UniversalQuickCreate** | Update imports from local `./services/` to `@spaarke/shared/services/document-upload/` |
| **DocumentUploadWizard** (new) | Import everything from shared |

---

## 15. Updated Pipeline Summary

```
Phase 1: Upload to SPE              ─── MultiFileUploadService (parallel, up to 10 files)
Phase 2: Create Dataverse records    ─── DocumentRecordService (sequential, OData)
Phase 3: Document Profile playbook   ─── useAiSummary → JPS playbook orchestration (SSE streaming)
         └─ Outputs: summary, tldr, keywords, type, entities, searchProfile (NEW)
Phase 4: RAG indexing                ─── POST /api/ai/rag/index-file (fire-and-forget, Service Bus)
```

All four phases are non-negotiable for complete document ingestion. The search profile is generated as part of Phase 3 (inside the playbook), not as a separate phase.
