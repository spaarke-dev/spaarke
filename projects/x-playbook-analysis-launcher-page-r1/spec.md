# Playbook & Analysis Launcher — Design Specification

> **Project**: `playbook-analysis-launcher-page-r1`
> **Author**: Ralph Schroeder + Claude
> **Date**: 2026-03-04
> **Status**: Draft

---

## 1. Executive Summary

Replace the existing **AnalysisBuilder PCF control** (v2.9.2) with two purpose-built experiences that share a common component library:

1. **Analysis Builder Code Page** — A standalone React 18 dialog launched from a Document form's Analysis subgrid ("+New Analysis"). Two-tab UI for selecting a playbook or configuring custom scope. Creates an `sprk_analysis` record.

2. **Quick Start Playbook Wizards** — Multi-step wizard dialogs embedded within the existing Corporate Workspace (`sprk_corporateworkspace`). Each Get Started action card launches a wizard tailored to its intent: upload document(s) → run playbook → follow-up actions.

Both experiences consume the same **Playbook component library** — shared UI components, services, and types for interacting with the AI Playbook data model in Dataverse.

---

## 2. Problem Statement

The current AnalysisBuilder PCF control has several limitations:

- **React 16** (platform-provided) — limits UI capabilities and component reuse with the React 18 workspace
- **No listener wired** — The workspace's Get Started cards post `openAnalysisBuilder` messages via `postMessage`, but no MDA-side handler exists; clicks do nothing
- **Wrong UX for workspace context** — The workspace Quick Start cards need a wizard flow (upload → analyze → follow-up), not a tab-based configuration dialog
- **Tightly coupled to PCF lifecycle** — Theme detection, environment variable loading, and dialog navigation use PCF-specific APIs that don't translate to code page patterns
- **Save Playbook / Save As** — Not implemented in current PCF

---

## 3. Scope

### In Scope

| Deliverable | Description |
|-------------|-------------|
| Shared Playbook component library | PlaybookCardGrid, ScopeConfigurator, PlaybookService, AnalysisService, types |
| Analysis Builder code page | `sprk_analysisbuilder.html` — standalone 2-tab dialog for Document subgrid |
| Quick Start wizard integration | Embedded wizard dialogs in workspace for 5 action cards |
| DocumentUploadStep component | Reusable upload step (shared with universal document upload pattern) |
| FollowUpActionsStep component | Post-analysis actions (email, share, assign, navigate) |
| Retire AnalysisBuilder PCF | Remove `src/client/pcf/AnalysisBuilder/` after code page is deployed |

### Out of Scope

- Changes to Dataverse schema (entities, relationships, fields) — reuse existing
- Changes to AI Playbook execution engine (BFF API endpoints)
- Changes to AnalysisWorkspace PCF (the results viewer)
- New playbook/action/skill/knowledge/tool records in Dataverse
- Save Playbook / Save As functionality (deferred — same as current PCF)

---

## 4. Architecture

### 4.1 Component Architecture

```
src/solutions/LegalWorkspace/src/
├── components/
│   ├── Playbook/                          ← NEW: Shared component library
│   │   ├── PlaybookCardGrid.tsx           ← Card selector grid (from PCF PlaybookSelector)
│   │   ├── ScopeConfigurator.tsx          ← Tabbed scope config (Action/Skills/Knowledge/Tools)
│   │   ├── ScopeList.tsx                  ← Generic checkbox/radio list (from PCF ScopeList)
│   │   ├── DocumentUploadStep.tsx         ← File upload step for wizards
│   │   ├── FollowUpActionsStep.tsx        ← Post-analysis actions step
│   │   ├── playbookService.ts             ← WebAPI queries for all playbook entities
│   │   ├── analysisService.ts             ← Create analysis record + N:N associations
│   │   ├── types.ts                       ← Shared interfaces (IPlaybook, IAction, etc.)
│   │   └── index.ts                       ← Public exports
│   │
│   ├── QuickStart/                        ← NEW: Workspace wizard dialogs
│   │   ├── QuickStartWizardDialog.tsx     ← Generic wizard shell for all Quick Start cards
│   │   ├── quickStartConfig.ts            ← Per-card wizard step configurations
│   │   └── index.ts
│   │
│   ├── GetStarted/                        ← EXISTING (modified)
│   │   ├── ActionCardHandlers.ts          ← Updated: open wizard dialogs instead of postMessage
│   │   └── ...
│   │
│   └── Shell/
│       └── WorkspaceGrid.tsx              ← Updated: wire Quick Start wizard handlers
│
src/solutions/AnalysisBuilder/             ← NEW: Standalone code page
├── src/
│   ├── main.tsx                           ← React 18 entry (createRoot)
│   ├── App.tsx                            ← 2-tab layout (Playbook | Custom Scope)
│   └── components/                        ← Imports from shared Playbook library OR local copies
├── index.html
├── vite.config.ts                         ← Single-file build (same pattern as LegalWorkspace)
├── package.json
└── dist/
    └── analysisbuilder.html               ← Deployable artifact → sprk_analysisbuilder
```

### 4.2 Two Shells, Shared Components

```
┌─────────────────────────────────────────────────────────────┐
│                   SHARED PLAYBOOK LIBRARY                    │
│         src/solutions/LegalWorkspace/src/components/Playbook │
│                                                              │
│  PlaybookCardGrid     ScopeConfigurator    ScopeList         │
│  PlaybookService      AnalysisService      types.ts          │
│  DocumentUploadStep   FollowUpActionsStep                    │
└──────────────┬──────────────────────────────┬────────────────┘
               │                              │
    ┌──────────┴──────────┐      ┌───────────┴────────────────┐
    │  CODE PAGE           │      │  WORKSPACE WIZARDS          │
    │  AnalysisBuilder     │      │  (embedded in workspace)    │
    │                      │      │                             │
    │  Tab 1: PlaybookGrid │      │  Card → QuickStartWizard:  │
    │  Tab 2: ScopeConfig  │      │   Step 1: Upload doc(s)    │
    │  Execute → create    │      │   Step 2: Playbook select  │
    │                      │      │   Step 3: Follow-up        │
    │  Launched from:      │      │                             │
    │  Document subgrid    │      │  Launched from:             │
    │  "+New Analysis"     │      │  Get Started action cards   │
    └──────────────────────┘      └─────────────────────────────┘
```

### 4.3 Key Decision: Shared Library Location

The shared Playbook components live **inside the LegalWorkspace source tree** (`src/solutions/LegalWorkspace/src/components/Playbook/`). The AnalysisBuilder code page either:

- **Option A**: Imports from a published `@spaarke/playbook-components` npm package (more decoupled, higher overhead)
- **Option B**: Copies the shared components into its own source tree (duplicates code, simpler build)
- **Option C (Recommended)**: Lives in the same Vite workspace as LegalWorkspace, sharing source directly via path aliases

Option C keeps a single source of truth with zero duplication and zero package publishing overhead. Both `src/solutions/LegalWorkspace/` and `src/solutions/AnalysisBuilder/` reference the shared components from the same source location.

---

## 5. Experience 1: Analysis Builder Code Page

### 5.1 Launch Context

- **Triggered from**: Document form → Analysis subgrid → "+New Analysis" button
- **Opens via**: Existing command bar script `sprk_analysis_commands.js` (already deployed)
- **URL parameters**: `documentId`, `documentName`, `containerId`, `fileId`, `apiBaseUrl`
- **Has document context**: Yes (always)

#### Existing Command Bar Script (REUSE — already deployed)

The file `src/client/webresources/js/sprk_analysis_commands.js` is already deployed as a Dataverse web resource and wired to the "+New Analysis" ribbon button. It:

1. Extracts `documentId`, `documentName`, `containerId`, `fileId` from the form context
2. Loads `apiBaseUrl` from the `sprk_BffApiBaseUrl` environment variable
3. Stores params in `sessionStorage` as backup
4. Opens the dialog via `Xrm.Navigation.navigateTo`
5. Refreshes the Analysis subgrid on dialog close

**Current launch target** (Custom Page — will be updated to web resource):
```javascript
// CURRENT (Custom Page):
const pageInput = {
    pageType: "custom",
    name: "sprk_analysisbuilder_40af8",
    recordId: JSON.stringify(dataPayload)
};

// NEW (Code Page web resource):
const pageInput = {
    pageType: "webresource",
    webresourceName: "sprk_analysisbuilder",
    data: buildQueryString(dataPayload)  // URL-encoded params
};
```

**Migration**: Only the `openAnalysisBuilderDialog()` function needs updating — change from `pageType: "custom"` to `pageType: "webresource"` and pass params as URL query string instead of JSON `recordId`. All validation, enable rules, and subgrid refresh logic remain unchanged.

**Key functions in `sprk_analysis_commands.js`**:
| Function | Purpose | Changes Needed |
|----------|---------|---------------|
| `Spaarke_NewAnalysis(primaryControl)` | Main handler for form command bar | None |
| `Spaarke_NewAnalysisFromSubgrid(selectedControl)` | Handler for subgrid "+New Analysis" | None |
| `openAnalysisBuilderDialog(params, formContext)` | Opens the dialog | Update `pageType` and param passing |
| `Spaarke_EnableNewAnalysis(primaryControl)` | Enable rule (saved + has file) | None |
| `getEnvironmentVariable(schemaName)` | Loads BFF API URL | None |

### 5.2 UI Layout

```
┌──────────────────────────────────────────────────────┐
│  New Analysis                              [X Close]  │
├──────────────────────────────────────────────────────┤
│                                                       │
│  ┌─────────────────┐  ┌───────────────────────────┐  │
│  │ Select Playbook  │  │  Custom Scope             │  │
│  └─────────────────┘  └───────────────────────────┘  │
│                                                       │
│  [Tab 1 Content — Playbook card grid]                 │
│                                                       │
│  ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐        │
│  │ Doc    │ │Contract│ │Complnce│ │ Custom │        │
│  │Summary │ │Analysis│ │ Check  │ │Analysis│        │
│  └────────┘ └────────┘ └────────┘ └────────┘        │
│                                                       │
│  OR                                                   │
│                                                       │
│  [Tab 2 Content — Scope configuration]                │
│  Action:     (radio) Summarize / Extract / Generate   │
│  Skills:     (check) Entity Extraction, Sentiment...  │
│  Knowledge:  (check) Company Policies...              │
│  Tools:      (check) Web Search...                    │
│                                                       │
├──────────────────────────────────────────────────────┤
│  [Cancel]                              [Run Analysis] │
└──────────────────────────────────────────────────────┘
```

### 5.3 Behavior

1. **On open**: Parse URL params for document context. Load playbooks, actions, skills, knowledge, tools in parallel from Dataverse WebAPI.
2. **Tab 1 — Select Playbook**: User clicks a playbook card. The playbook's pre-configured scopes are locked (no editing). This is the "one-click" fast path.
3. **Tab 2 — Custom Scope**: User manually selects action (single), skills (multi), knowledge (multi), tools (multi). No playbook association.
4. **Run Analysis**: Creates `sprk_analysis` record with lookups + N:N associations (same logic as current PCF). Navigates to Analysis Workspace form.
5. **Cancel**: Closes dialog, returns to Document form.

### 5.4 Differences from Current PCF

| Aspect | Current PCF | New Code Page |
|--------|------------|---------------|
| React version | 16 (platform) | 18 (bundled) |
| Tab 1 behavior | Playbook selects, user CAN change scopes | Playbook selects, scopes are **locked** |
| Tab 2 | N/A (same tab) | Separate "Custom Scope" tab |
| Output mechanism | PCF output properties | Direct `navigateTo` after creation |
| Theme | Complex PCF detection chain | App-level theme detection (see Section 10) |
| Bundle | PCF solution ZIP | Single HTML file (Vite singlefile) |

---

## 6. Experience 2: Quick Start Playbook Wizards

### 6.1 Launch Context

- **Triggered from**: Workspace Get Started action cards (5 cards)
- **Opens as**: Inline wizard dialog (React overlay in workspace, using WizardShell)
- **Has document context**: No — wizard collects it in Step 1
- **Each card maps to a pre-configured playbook intent**

### 6.2 Card → Wizard Mapping

| Card | Intent | Step 1 (Upload) | Step 2 (Playbook) | Step 3 (Follow-up) |
|------|--------|-----------------|-------------------|---------------------|
| Analyze New Document | `document-analysis` | Upload document | "Document Analysis" playbook | Share, Assign, View |
| Search Document Files | `document-search` | Upload document(s) | "Document Search" playbook | Export, Share |
| Send Email Message | `email-compose` | Upload attachment(s) | "Email Compose" playbook | Send, Save Draft |
| Assign to Counsel | `assign-counsel` | Select matter/document | "Assign Counsel" playbook | Notify, Assign |
| Schedule New Meeting | `meeting-schedule` | Select context (matter) | "Meeting Schedule" playbook | Send Invite |

### 6.3 Wizard UI Layout

```
┌──────────────────────────────────────────────────────┐
│  Analyze New Document                      [X Close]  │
├──────────────┬───────────────────────────────────────┤
│  ● Upload    │                                        │
│  ○ Analyze   │  [Step 1: Upload Document]             │
│  ○ Actions   │                                        │
│              │  Drag and drop files here, or browse    │
│              │  ┌─────────────────────────────────┐   │
│              │  │         Upload Area              │   │
│              │  │     (same as universal upload)   │   │
│              │  └─────────────────────────────────┘   │
│              │                                        │
│              │  Uploaded: contract_v2.docx  ✓         │
│              │                                        │
├──────────────┴───────────────────────────────────────┤
│  [Cancel]                           [Next →]          │
└──────────────────────────────────────────────────────┘
```

```
┌──────────────────────────────────────────────────────┐
│  Analyze New Document                      [X Close]  │
├──────────────┬───────────────────────────────────────┤
│  ✓ Upload    │                                        │
│  ● Analyze   │  [Step 2: Running Analysis]            │
│  ○ Actions   │                                        │
│              │  Playbook: Document Analysis            │
│              │  Action: Summarize & Extract            │
│              │                                        │
│              │  ┌─────────────────────────────┐       │
│              │  │  ◐  Analyzing document...   │       │
│              │  │     Extracting key info      │       │
│              │  └─────────────────────────────┘       │
│              │                                        │
├──────────────┴───────────────────────────────────────┤
│  [Cancel]                                             │
└──────────────────────────────────────────────────────┘
```

```
┌──────────────────────────────────────────────────────┐
│  Analyze New Document                      [X Close]  │
├──────────────┬───────────────────────────────────────┤
│  ✓ Upload    │                                        │
│  ✓ Analyze   │  [Step 3: What's Next?]                │
│  ● Actions   │                                        │
│              │  Analysis complete! Here's what you     │
│              │  can do next:                           │
│              │                                        │
│              │  ┌────────┐ ┌────────┐ ┌────────┐     │
│              │  │ View   │ │ Share  │ │ Assign │     │
│              │  │Results │ │ Report │ │Counsel │     │
│              │  └────────┘ └────────┘ └────────┘     │
│              │                                        │
│              │  [Open Analysis Workspace]              │
│              │                                        │
├──────────────┴───────────────────────────────────────┤
│  [Close]                                              │
└──────────────────────────────────────────────────────┘
```

### 6.4 Wizard Implementation Pattern

Uses the existing `WizardShell` component with dynamic steps:

```typescript
// QuickStartWizardDialog.tsx
const QuickStartWizardDialog: React.FC<IQuickStartWizardProps> = ({
  open, onClose, webApi, intent
}) => {
  const config = QUICKSTART_CONFIGS[intent]; // Per-card configuration

  const stepConfigs: IWizardStepConfig[] = [
    {
      id: 'upload',
      label: config.uploadLabel,        // e.g., "Upload Document"
      canAdvance: () => hasUploadedFiles,
      renderContent: () => (
        <DocumentUploadStep
          webApi={webApi}
          accept={config.acceptedFileTypes}
          multiple={config.allowMultiple}
          onFilesReady={setUploadedFiles}
        />
      ),
    },
    {
      id: 'analyze',
      label: config.analyzeLabel,        // e.g., "Run Analysis"
      canAdvance: () => analysisComplete,
      renderContent: () => (
        <PlaybookExecutionStep
          webApi={webApi}
          playbookIntent={intent}
          documentIds={uploadedDocIds}
          onComplete={setAnalysisResult}
        />
      ),
    },
    {
      id: 'actions',
      label: 'Next Steps',
      canAdvance: () => true,
      renderContent: () => (
        <FollowUpActionsStep
          webApi={webApi}
          analysisId={analysisResult.id}
          availableActions={config.followUpActions}
        />
      ),
    },
  ];

  return (
    <WizardShell
      open={open}
      title={config.title}
      steps={stepConfigs}
      onClose={onClose}
      onFinish={handleFinish}
      finishLabel="Done"
    />
  );
};
```

### 6.5 Portability: Playbook Library Beyond This Workspace

The Quick Start wizard system is designed for **reuse across multiple workspaces and launch contexts**:

#### Design for Portability

1. **Config-driven, not hardcoded** — `QuickStartWizardDialog` accepts an `intent` string and looks up step configuration from a config map. New workspaces add entries to the config, not new components.

2. **No workspace-specific imports** — The `QuickStart/` components import only from `Playbook/` (shared library) and `Wizard/` (WizardShell). They do NOT import from `GetStarted/`, `Shell/`, or other workspace-specific modules.

3. **Launch from any context**:
   - **Workspace cards** (current): `handleOpenQuickStartWizard(intent)` sets React state → dialog opens
   - **Future command bar**: A lightweight JS launcher (same pattern as `sprk_analysis_commands.js`) can open the workspace code page with `?mode=quickstart&intent=document-analysis` as a URL param, rendering only the wizard
   - **Future workspaces**: Import `QuickStartWizardDialog` from the shared Playbook library; each workspace provides its own card configs

4. **Standalone mode** — The workspace code page already supports URL params for mode (`?mode=todo` opens the Todo dialog). Adding `?mode=quickstart&intent=X` follows this established pattern, enabling command bar or ribbon buttons to launch a specific playbook wizard without the full workspace.

#### Future Workspace Integration Pattern

```typescript
// In any new workspace (e.g., HRWorkspace, FinanceWorkspace):
import { QuickStartWizardDialog } from "../Playbook";

// Each workspace defines its own card → intent mapping
const hrCardHandlers = {
  "onboard-employee": () => openQuickStart("employee-onboarding"),
  "run-background-check": () => openQuickStart("background-check"),
};
```

### 6.6 DocumentUploadStep — Reuse of Existing Components

#### Existing Upload Components Inventory

The codebase has a mature, layered upload architecture. The Quick Start wizard MUST reuse these — not duplicate them.

| Component | Location | What It Does | Reusable? |
|-----------|----------|-------------|-----------|
| **FileUploadZone** | `LegalWorkspace/src/components/CreateMatter/FileUploadZone.tsx` | Drag-drop UI with validation (PDF, DOCX, XLSX, 10 MB). Returns `File` objects — no upload logic. | **Yes — directly reusable** |
| **UploadedFileList** | `LegalWorkspace/src/components/CreateMatter/UploadedFileList.tsx` | Displays collected files with size/type badges | **Yes — directly reusable** |
| **DocumentUploadForm** | `pcf/UniversalQuickCreate/control/DocumentUploadForm.tsx` | Full orchestrator: file pick → SPE upload → Dataverse record creation → AI summary | Reference pattern |
| **MultiFileUploadService** | `pcf/UniversalQuickCreate/control/services/MultiFileUploadService.ts` | Parallel SPE upload via `Promise.allSettled`, 10-file limit | **Yes — service layer** |
| **FileUploadService** | `pcf/UniversalQuickCreate/control/services/FileUploadService.ts` | Single-file upload → BFF → SPE, returns `SpeFileMetadata` | **Yes — service layer** |
| **SdapApiClient** | `shared/Spaarke.SdapClient/src/SdapApiClient.ts` | Low-level upload: small files (PUT <4MB) or chunked (320KB chunks >=4MB). Progress callback, abort signal. | **Yes — infrastructure** |
| **EntityCreationService** | `LegalWorkspace/src/services/EntityCreationService.ts` | Two-phase: upload to SPE, then create `sprk_document` records with navigation property bindings | **Yes — orchestration** |

#### Reuse Strategy

```
Quick Start Wizard DocumentUploadStep
  │
  ├── UI Layer (REUSE from CreateMatter)
  │   ├── FileUploadZone.tsx      ← drag-drop + validation
  │   └── UploadedFileList.tsx    ← file display
  │
  ├── Upload Layer (REUSE from UniversalQuickCreate services)
  │   ├── MultiFileUploadService  ← parallel upload to SPE
  │   └── FileUploadService       ← single file upload
  │
  └── Record Layer (REUSE from EntityCreationService)
      └── createDocumentRecords() ← creates sprk_document in Dataverse
```

**What's NEW**: Only the `DocumentUploadStep` wrapper component that wires these existing layers together with wizard-specific state management (canAdvance, onFilesReady callbacks).

**What's NOT new**: Zero new upload UI, zero new SPE integration, zero new Dataverse record creation logic.

---

## 7. Shared Playbook Component Library

### 7.1 Components

| Component | Props | Used By |
|-----------|-------|---------|
| `PlaybookCardGrid` | `playbooks`, `selectedId`, `onSelect`, `isLoading` | Analysis Builder Tab 1, Quick Start Step 2 (display only) |
| `ScopeConfigurator` | `actions`, `skills`, `knowledge`, `tools`, `selectedIds`, `onChange` | Analysis Builder Tab 2 |
| `ScopeList<T>` | `items`, `multiSelect`, `onSelectionChange`, `isLoading` | Used by ScopeConfigurator internally |
| `DocumentUploadStep` | `webApi`, `accept`, `multiple`, `onFilesReady` | Quick Start Step 1 |
| `FollowUpActionsStep` | `webApi`, `analysisId`, `availableActions` | Quick Start Step 3 |

### 7.2 Services

| Service | Methods | Dataverse Entities |
|---------|---------|-------------------|
| `PlaybookService` | `loadPlaybooks()`, `loadActions()`, `loadSkills()`, `loadKnowledge()`, `loadTools()`, `loadPlaybookScopes(id)` | `sprk_analysisplaybook`, `sprk_analysisaction`, `sprk_analysisskill`, `sprk_analysisknowledge`, `sprk_analysistool` |
| `AnalysisService` | `createAnalysis(config)`, `associateScopes(analysisId, scopes)` | `sprk_analysis` + N:N relationships |

### 7.3 Types

```typescript
// Shared interfaces (extracted from current PCF types)
interface IPlaybook { id: string; name: string; description: string; icon?: string; }
interface IAction { id: string; name: string; description?: string; }
interface ISkill { id: string; name: string; description?: string; }
interface IKnowledge { id: string; name: string; description?: string; }
interface ITool { id: string; name: string; description?: string; }

interface IAnalysisConfig {
  documentId: string;
  documentName?: string;
  playbookId?: string;
  actionId: string;
  skillIds: string[];
  knowledgeIds: string[];
  toolIds: string[];
}

interface IPlaybookScopes {
  actionIds: string[];
  skillIds: string[];
  knowledgeIds: string[];
  toolIds: string[];
}
```

---

## 8. Dataverse Integration

### 8.1 Entities (No Schema Changes)

All existing entities are reused as-is:

| Entity | Purpose | Key Fields |
|--------|---------|-----------|
| `sprk_analysisplaybook` | Playbook templates | `sprk_name`, `sprk_description` |
| `sprk_analysisaction` | Available actions | `sprk_name`, `sprk_description` |
| `sprk_analysisskill` | Available skills | `sprk_name`, `sprk_description` |
| `sprk_analysisknowledge` | Knowledge sources | `sprk_name`, `sprk_description` |
| `sprk_analysistool` | Available tools | `sprk_name`, `sprk_description` |
| `sprk_analysis` | Analysis record (created) | `sprk_name`, `sprk_documentid`, `sprk_actionid`, `sprk_Playbook` |
| `sprk_document` | Source document | (lookup target) |

### 8.2 N:N Relationships (No Changes)

| Relationship | Primary | Related | Used For |
|-------------|---------|---------|----------|
| `sprk_playbook_skill` | sprk_analysisplaybook | sprk_analysisskill | Playbook → Skills |
| `sprk_playbook_knowledge` | sprk_analysisplaybook | sprk_analysisknowledge | Playbook → Knowledge |
| `sprk_playbook_tool` | sprk_analysisplaybook | sprk_analysistool | Playbook → Tools |
| `sprk_analysisplaybook_action` | sprk_analysisplaybook | sprk_analysisaction | Playbook → Actions |
| `sprk_analysis_skill` | sprk_analysis | sprk_analysisskill | Analysis → Skills |
| `sprk_analysis_knowledge` | sprk_analysis | sprk_analysisknowledge | Analysis → Knowledge |
| `sprk_analysis_tool` | sprk_analysis | sprk_analysistool | Analysis → Tools |

---

## 9. Theming and Dark Mode (ADR-021)

### 9.1 Constraint: App Properties, NOT System Preferences

Theme detection follows the **established LegalWorkspace pattern** — theme is determined by the Dataverse app context and user preference stored in the app, NOT by the operating system `prefers-color-scheme` media query.

### 9.2 Theme Detection Priority Chain

Both the Analysis Builder code page and workspace wizards use the same priority:

| Priority | Source | Storage Key | Notes |
|----------|--------|-------------|-------|
| 1 (highest) | `localStorage('spaarke-workspace-theme')` | User's explicit choice via ThemeToggle | Persists across sessions |
| 2 | URL `?theme=dark` or `?theme=light` param | N/A | For programmatic launch (command bar can pass) |
| 3 | Dataverse navbar color detection | N/A | Reads `[data-id="navbar-container"]` background color from parent MDA frame |
| 4 (fallback) | System preference | N/A | Only if no app-level preference exists |

### 9.3 Theme Implementation

#### Analysis Builder Code Page (new)

```typescript
// main.tsx — use ThemeProvider from workspace shared code
import { resolveTheme, setupThemeListener } from './providers/ThemeProvider';

const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveTheme);

  React.useEffect(() => {
    return setupThemeListener(() => setTheme(resolveTheme()));
  }, []);

  return (
    <FluentProvider theme={theme} style={{ height: '100%' }}>
      <AnalysisBuilderApp />
    </FluentProvider>
  );
};
```

#### Workspace Quick Start Wizards (existing)

No theme work needed — wizards render inside the workspace's existing `FluentProvider` which already handles theme detection via `useTheme()` hook and `ThemeToggle` component.

### 9.4 Existing Theme Files (REUSE)

| File | Purpose | Reuse? |
|------|---------|--------|
| `LegalWorkspace/src/providers/ThemeProvider.ts` | Stateless theme resolution (localStorage → URL → navbar → system) | **Copy to AnalysisBuilder code page** |
| `LegalWorkspace/src/hooks/useTheme.ts` | Stateful hook with React state + persistence | Used by workspace (no change needed) |
| `LegalWorkspace/src/components/Shell/ThemeToggle.tsx` | UI toggle button (light → dark → high-contrast) | Workspace only (not in Analysis Builder dialog) |

### 9.5 Theme Modes Supported

| Mode | Fluent Theme Object | Where Available |
|------|-------------------|-----------------|
| Light | `webLightTheme` | Both |
| Dark | `webDarkTheme` | Both |
| High-contrast | `teamsHighContrastTheme` | Workspace only (via ThemeToggle) |

### 9.6 Dark Mode Rules (ADR-021)

- **ZERO hardcoded colors** — all components use Fluent v9 semantic tokens
- **PlaybookCardGrid cards** must use `colorNeutralBackground1` / `colorBrandBackground2` (not `#fff` / `#1a1a2e`)
- **ScopeList checkboxes** inherit Fluent theme automatically
- **Dialog overlay** uses Fluent `Dialog` which handles backdrop in both themes
- **File upload zone** border/background must use semantic tokens

---

## 10. Deployment

### 10.1 Analysis Builder Code Page

| Step | Action |
|------|--------|
| Build | `cd src/solutions/AnalysisBuilder && npm run build` |
| Output | `dist/analysisbuilder.html` (single self-contained file) |
| Deploy | Upload to Dataverse as web resource `sprk_analysisbuilder` |
| Wire | Update `openAnalysisBuilderDialog()` in `sprk_analysis_commands.js`: change `pageType: "custom"` → `pageType: "webresource"`, pass params as URL query string |

### 10.2 Command Bar Script Update

Only the `openAnalysisBuilderDialog()` function in `sprk_analysis_commands.js` changes:

```javascript
// BEFORE (Custom Page):
const pageInput = {
    pageType: "custom",
    name: "sprk_analysisbuilder_40af8",
    recordId: JSON.stringify(dataPayload)
};

// AFTER (Code Page web resource):
const dataString = "documentId=" + encodeURIComponent(params.documentId)
    + "&documentName=" + encodeURIComponent(params.documentName)
    + "&containerId=" + encodeURIComponent(params.containerId || "")
    + "&fileId=" + encodeURIComponent(params.fileId || "")
    + "&apiBaseUrl=" + encodeURIComponent(apiBaseUrl);

const pageInput = {
    pageType: "webresource",
    webresourceName: "sprk_analysisbuilder",
    data: dataString
};
```

All other functions (`Spaarke_NewAnalysis`, `Spaarke_EnableNewAnalysis`, etc.) remain unchanged.

### 10.3 Quick Start Wizards (Workspace)

| Step | Action |
|------|--------|
| Build | `cd src/solutions/LegalWorkspace && npm run build` |
| Output | `dist/corporateworkspace.html` (already includes wizard code) |
| Deploy | Upload to Dataverse as web resource `sprk_corporateworkspace` |
| Wire | Replace `ActionCardHandlers.ts` postMessage handlers with wizard dialog state handlers |

### 10.4 Retire AnalysisBuilder PCF

| Step | Action |
|------|--------|
| Remove | Delete `src/client/pcf/AnalysisBuilder/` directory |
| Remove | Remove PCF from Dataverse solution |
| Remove | Delete Custom Page `sprk_analysisbuilder_40af8` from Dataverse |
| Verify | Confirm `sprk_analysis_commands.js` now targets the new web resource |

---

## 11. Success Criteria

| Criteria | Measurement |
|----------|-------------|
| Analysis Builder code page opens from Document subgrid | "+New Analysis" opens dialog with document context via existing command bar |
| Playbook tab shows available playbooks | Cards load from Dataverse, click selects with locked scopes |
| Custom Scope tab allows manual configuration | User can select action, skills, knowledge, tools |
| Analysis record created with correct relationships | `sprk_analysis` + all N:N associations verified |
| Quick Start wizards open from workspace cards | All 5 action cards open their respective wizard |
| Upload step works | Files upload to SPE via existing services, documentId returned |
| Playbook execution step runs | Analysis created and executed with correct playbook |
| Follow-up actions work | Email, share, assign, navigate all functional |
| Dark mode works in both experiences | All components render correctly in light, dark, and high-contrast |
| No upload code duplication | FileUploadZone, MultiFileUploadService, EntityCreationService reused — not copied |
| Playbook Library is portable | QuickStartWizardDialog can be used in other workspaces without modification |
| AnalysisBuilder PCF retired | PCF removed, Custom Page removed, command bar updated |

---

## 12. Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|-----------|
| Shared components create build coupling between two code pages | Build failures cascade | Option C (shared source) with clear interface boundaries |
| Document upload step complexity (SPE integration) | Large scope | Reuse existing FileUploadZone + MultiFileUploadService — zero new upload code |
| Quick Start Step 2 (playbook execution) depends on BFF API | Blocked if API not ready | Use existing analysis creation flow (Dataverse-only), BFF executes async |
| 5 different wizard configurations | High combinatorial testing | Single `QuickStartWizardDialog` with config-driven steps |
| Command bar script update breaks existing flow | Users can't create analyses | Minimal change (one function), can be tested independently before PCF retirement |

---

## 13. Dependencies

| Dependency | Status | Notes |
|-----------|--------|-------|
| WizardShell component | Exists | Already in workspace, used by Create Matter + Create Project |
| Dataverse playbook entities | Exist | All 7 entities + N:N relationships already deployed |
| BFF API endpoints | Exist | Analysis creation + SPE file upload already available |
| FileUploadZone + UploadedFileList | Exist | `LegalWorkspace/src/components/CreateMatter/` — UI-only, directly reusable |
| MultiFileUploadService + FileUploadService | Exist | `pcf/UniversalQuickCreate/control/services/` — SPE upload layer |
| EntityCreationService | Exists | `LegalWorkspace/src/services/` — Dataverse record creation |
| SdapApiClient | Exists | `shared/Spaarke.SdapClient/` — low-level SPE operations |
| ThemeProvider | Exists | `LegalWorkspace/src/providers/ThemeProvider.ts` — copy to Analysis Builder |
| `sprk_analysis_commands.js` | Deployed | Command bar script — update one function for code page launch |
| Fluent UI v9 | Bundled | Both code pages use React 18 + bundled Fluent v9 |

---

## 14. Reference: Existing Files

| Category | File | Purpose |
|----------|------|---------|
| **Command Bar** | `src/client/webresources/js/sprk_analysis_commands.js` | Ribbon "+New Analysis" launcher (update `openAnalysisBuilderDialog` only) |
| **Command Bar** | `src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js` | Reference pattern for code page launcher scripts |
| **Upload UI** | `src/solutions/LegalWorkspace/src/components/CreateMatter/FileUploadZone.tsx` | Drag-drop zone — reuse directly |
| **Upload UI** | `src/solutions/LegalWorkspace/src/components/CreateMatter/UploadedFileList.tsx` | File list display — reuse directly |
| **Upload Service** | `src/client/pcf/UniversalQuickCreate/control/services/MultiFileUploadService.ts` | Parallel SPE upload — reuse |
| **Upload Service** | `src/client/pcf/UniversalQuickCreate/control/services/FileUploadService.ts` | Single file upload — reuse |
| **Record Creation** | `src/solutions/LegalWorkspace/src/services/EntityCreationService.ts` | SPE upload + Dataverse records — reuse |
| **SPE Client** | `src/client/shared/Spaarke.SdapClient/src/SdapApiClient.ts` | Low-level upload (small/chunked) — infrastructure |
| **Theme** | `src/solutions/LegalWorkspace/src/providers/ThemeProvider.ts` | Theme resolution — copy to Analysis Builder |
| **Theme** | `src/solutions/LegalWorkspace/src/hooks/useTheme.ts` | Stateful theme hook — workspace only |
| **Current PCF** | `src/client/pcf/AnalysisBuilder/control/` | Current implementation to retire |

---

*End of specification.*
