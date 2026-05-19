# Client Resources Inventory — Definitive Reference

> **Last Updated**: 2026-05-19
> **Purpose**: Single source of truth for "what client-side components do we have, what's their status, when were they last updated?" Use this to answer the recurring question: "do we need to update X, or has it been replaced?"
> **Method**: Last-commit dates from `git log`; manifest existence as build-readiness signal; co-existence detection for PCF↔Code Page replacement patterns.
> **Maintenance**: When a control is created, retired, or replaced, update the relevant table.

## Summary

| Category | Active | Archived/Replaced | Total folders |
|---|---|---|---|
| PCF controls | **14** | 10 (no manifest — archived) | 24 |
| React Code Pages (in `src/client/code-pages/`) | **5** | — | 5 |
| Vite/React Code Pages (in `src/solutions/`) | **23** | — | 23 |
| JS Web Resources (`src/client/webresources/js/`) | **12** | — | 12 |
| JS Web Resources (`src/solutions/webresources/`) | **6** | — | 6 |
| Office Add-ins | **2** (Outlook, Word) | — | 2 |
| External SPA | **1** (secure-project-workspace) | — | 1 |
| Special solutions (not Code Pages) | **3** (CopilotAgent, SpaarkeCore, EventCommands) | — | 3 |
| **Icons / static assets** | _excluded from this inventory_ | — | — |

---

## 1. PCF Controls — Active (manifest exists, currently buildable)

> Path convention: `src/client/pcf/{name}/[control|{Name}]/ControlManifest.Input.xml`
> Active = `ControlManifest.Input.xml` present.

| Name | Namespace | Version | Manifest location | Last commit | Coexists with Code Page? |
|---|---|---|---|---|---|
| **AssociationResolver** | `Spaarke.Controls` | 1.1.0 | `pcf/AssociationResolver/ControlManifest.Input.xml` | 2026-05-14 | — |
| **DocumentRelationshipViewer** | `Spaarke.Controls` | 1.0.35 | `pcf/DocumentRelationshipViewer/DocumentRelationshipViewer/` | 2026-05-13 | ✅ Yes — `code-pages/DocumentRelationshipViewer` (different surfaces; share `@spaarke/ui-components`) |
| **DrillThroughWorkspace** | `Spaarke.Controls` | 1.1.1 | `pcf/DrillThroughWorkspace/control/` | 2026-03-30 | — |
| **EmailProcessingMonitor** | `Spaarke` | 1.1.0 | `pcf/EmailProcessingMonitor/control/` | 2026-05-13 | — |
| **RelatedDocumentCount** | `Spaarke.Pcf.RelatedDocumentCount` | 1.21.2 | `pcf/RelatedDocumentCount/RelatedDocumentCount/` | 2026-05-13 | — |
| **ScopeConfigEditor** | `Sprk` | 1.2.7 | `pcf/ScopeConfigEditor/ScopeConfigEditor/` | 2026-03-30 | — |
| **SemanticSearchControl** | `Sprk` | 1.1.41 | `pcf/SemanticSearchControl/SemanticSearchControl/` | 2026-05-13 | ✅ Yes — `code-pages/SemanticSearch` (different surfaces; share `@spaarke/ui-components`) |
| **SpaarkeGridCustomizer** | `Spaarke.Controls` | 1.0.0 | `pcf/SpaarkeGridCustomizer/ControlManifest.Input.xml` | 2026-05-14 | — |
| **SpeDocumentViewer** | `Spaarke` | 1.0.25 | `pcf/SpeDocumentViewer/control/` | 2026-05-14 | — (per project memory: SpeDocumentViewer remains PCF, not migrated) |
| **ThemeEnforcer** | `Spaarke` | 1.0.0 | `pcf/ThemeEnforcer/ControlManifest.Input.xml` | 2026-03-13 | — |
| **UniversalDatasetGrid** | `Spaarke.UI.Components` | 2.3.0 | `pcf/UniversalDatasetGrid/control/` | 2026-05-13 | — |
| **UniversalQuickCreate** (constructor: `UniversalDocumentUpload`) | `Spaarke.Controls` | 3.15.3 | `pcf/UniversalQuickCreate/control/` | 2026-05-14 | — |
| **UpdateRelatedButton** | `Spaarke.Controls` | 1.0.0 | `pcf/UpdateRelatedButton/ControlManifest.Input.xml` | 2026-05-14 | — |
| **VisualHost** | `Spaarke.Visuals` | 1.4.0 | `pcf/VisualHost/control/` | 2026-05-13 | — |

## 2. PCF Folders — Archived (NO manifest; not currently buildable)

> Folder exists under `src/client/pcf/` but **no `ControlManifest.Input.xml` source file**. Build artifacts (`generated/`, `out/`) may remain. Status: archived — superseded by Code Pages or removed from use.

| Folder | Last commit | Likely replacement | Folder contents |
|---|---|---|---|
| `pcf/AnalysisBuilder` | 2026-03-15 | Likely subsumed into PlaybookBuilder Code Page | `control/generated/` only |
| `pcf/AnalysisWorkspace` | 2026-03-15 | Replaced by `code-pages/AnalysisWorkspace` (active 2026-05-16) | `control/generated/`, `out/`, `solution/` |
| `pcf/AIMetadataExtractor` | 2025-12-02 | Unknown — oldest folder; likely deprecated | — |
| `pcf/DueDatesWidget` | 2026-03-15 | Unknown | `control/generated/` |
| `pcf/EventAutoAssociate` | 2026-03-15 | Unknown | — |
| `pcf/EventCalendarFilter` | 2026-03-15 | Unknown | `control/generated/` |
| `pcf/EventFormController` | 2026-03-15 | Unknown | — |
| `pcf/PlaybookBuilderHost` | 2026-03-15 | Replaced by `code-pages/PlaybookBuilder` (active 2026-04-04) | `control/generated/` |
| `pcf/RegardingLink` | 2026-03-15 | Unknown | — |
| `pcf/SpeFileViewer` | 2026-03-15 | Likely replaced by `SpeDocumentViewer` (active 2026-05-14) | `control/generated/`, `out/`, `solution/` |

**Recommendation**: Confirm replacement status for each, then either:
- Delete folder entirely (build artifacts are not source-of-truth), or
- Move to `.archive/2026-XX-XX/pcf-removed/` with a README noting replacement

## 3. React Code Pages (in `src/client/code-pages/`)

> Separate from `src/solutions/` Code Pages. These are React 18 SPAs that **replace or complement** PCF controls (different deployment surface — full-page web resources vs form-embedded PCFs).

| Name | Last commit | Description | Replaces / Complements |
|---|---|---|---|
| **AnalysisWorkspace** | 2026-05-16 | SprkChat analysis collaboration workspace (full-page HTML web resource) | **Replaces** `pcf/AnalysisWorkspace` |
| **DocumentRelationshipViewer** | 2026-05-13 | HTML web resource dialog | **Complements** `pcf/DocumentRelationshipViewer` (different surfaces) |
| **PlaybookBuilder** | 2026-04-04 | Full-page HTML web resource for AI Playbook visual node builder | **Replaces** `pcf/PlaybookBuilderHost` |
| **SemanticSearch** | 2026-05-13 | Full-page HTML web resource for system-wide AI-powered search | **Complements** `pcf/SemanticSearchControl` (different surfaces) |
| **SprkChatPane** | 2026-03-26 | SprkChat pane (full-page or pane?) | (Verify usage — older commit) |

## 4. Vite/React Code Pages (in `src/solutions/`)

> Each is a Vite-built React 18 SPA deployed as a Power Apps Code Page or single-HTML web resource. Build output goes to Dataverse via `code-page-deploy` skill.

### 4.1 SDAP / AI Platform Code Pages

| Name | Last commit | Purpose (from package.json or folder context) |
|---|---|---|
| **SpaarkeAi** | 2026-05-16 | AI Platform Unification target (three-pane layout: Conversation/Workspace/Context) |
| **DailyBriefing** | 2026-05-13 | Daily briefing UI |
| **LegalWorkspace** | 2026-04-05 | Corporate workspace / legal operations workspace UI |
| **AllDocuments** | 2026-04-02 | All documents view |
| **PlaybookLibrary** | 2026-04-02 | Playbook library browser |
| **Reporting** | 2026-03-31 | Power BI Embedded reporting host |
| **SpeAdminApp** | 2026-03-30 | SPE admin app |

### 4.2 Wizards (CreateRecordWizard family)

| Name | Last commit | Purpose |
|---|---|---|
| **CreateMatterWizard** | 2026-04-03 | Matter creation wizard (thin wrapper on `@spaarke/create-record-wizard`) |
| **CreateProjectWizard** | 2026-04-03 | Project creation wizard (thin wrapper) |
| **CreateEventWizard** | 2026-04-03 | Event creation wizard |
| **CreateTodoWizard** | 2026-04-03 | Todo creation wizard |
| **CreateWorkAssignmentWizard** | 2026-04-03 | Work assignment wizard (uses `WizardShell` directly) |
| **DocumentUploadWizard** | 2026-04-05 | Document upload wizard |
| **SummarizeFilesWizard** | 2026-03-30 | File summarization wizard |
| **WorkspaceLayoutWizard** | 2026-04-24 | Workspace layout configuration wizard |

### 4.3 Side panes + companion UIs

| Name | Last commit | Purpose |
|---|---|---|
| **CalendarSidePane** | 2026-03-30 | Calendar side pane |
| **EventDetailSidePane** | 2026-03-30 | Event detail side pane |
| **TodoDetailSidePane** | 2026-03-30 | Todo detail side pane |
| **EventsPage** | 2026-03-30 | Events page |
| **SmartTodo** | 2026-03-30 | Smart todo (Kanban-style) |
| **FindSimilarCodePage** | 2026-04-04 | Find similar documents wizard/page |
| **DemoRegistration** | 2026-04-03 | Self-service demo registration |

### 4.4 Older / verify status

| Name | Last commit | Notes |
|---|---|---|
| **EventCommands** | 2026-02-06 | Ribbon commands + dialogs (not a Code Page — see §8) |
| `src/solutions/webresources/` | 2026-03-20 | Sub-folder of additional JS web resources (see §6.2) |

## 5. Suspected PCF↔Code Page co-existence / replacement analysis

| PCF (`pcf/`) | Code Page (`code-pages/`) | Status |
|---|---|---|
| `pcf/AnalysisWorkspace` (archived, no manifest) | `code-pages/AnalysisWorkspace` (active) | **Replaced**: Code Page is the active version |
| `pcf/PlaybookBuilderHost` (archived, no manifest) | `code-pages/PlaybookBuilder` (active) | **Replaced**: Code Page is the active version |
| `pcf/DocumentRelationshipViewer` (active) | `code-pages/DocumentRelationshipViewer` (active) | **Both active**: different deployment surfaces; share `@spaarke/ui-components` |
| `pcf/SemanticSearchControl` (active) | `code-pages/SemanticSearch` (active) | **Both active**: different deployment surfaces; share `@spaarke/ui-components` |
| `pcf/SpeFileViewer` (archived, no manifest) | (none) | **Likely replaced by** `pcf/SpeDocumentViewer` (different name, similar purpose) |
| `pcf/SpeDocumentViewer` (active) | (none) | **PCF remains canonical** per project memory |

**Decision rule documented in project memory**: when a PCF has a Code Page version, the PCF stays for form-embedded scenarios and the Code Page covers standalone scenarios. They MUST share UI via `@spaarke/ui-components` to avoid duplication.

## 6. Web Resources (JavaScript)

> Single-file JS web resources for ribbon commands, form scripts, command bar actions. Deployed via solution import. **Icons are excluded from this inventory.**

### 6.1 `src/client/webresources/js/` (core, 12 files)

| File | Last commit | Purpose (inferred) |
|---|---|---|
| `sprk_wizard_commands.js` | 2026-05-12 | Wizard launch / command bar actions |
| `sprk_registrationribbon.js` | 2026-04-06 | Demo registration ribbon actions |
| `sprk_emailactions.js` | 2026-04-05 | Email ribbon actions |
| `sprk_DocumentOperations.js` | 2026-04-05 | Document ribbon operations |
| `sprk_communication_send.js` | 2026-04-05 | Communication / email send action |
| `sprk_analysis_commands.js` | 2026-04-05 | AI analysis commands |
| `sprk_ThemeMenu.js` | 2026-03-30 | Theme menu ribbon actions |
| `sprk_openSprkChatPane.js` | 2026-03-16 | SprkChat pane launcher (ribbon) |
| `sprk_aichatcontextmap_ribbon.js` | 2026-03-15 | AI chat context map ribbon |
| `sprk_playbook_commands.js` | 2026-03-13 | Playbook ribbon commands |
| `sprk_event_sidepane_form.js` | 2026-02-09 | Event side-pane form script |
| `sprk_updaterelated_commands.js` | 2026-02-03 | Update related records ribbon |

### 6.2 `src/solutions/webresources/` (KPI-related, 6 files)

| File | Last commit | Purpose (inferred) |
|---|---|---|
| `sprk_kpi_subgrid_refresh.js` | 2026-03-23 | KPI subgrid refresh |
| `sprk_kpiassessment_quickcreate.js` | 2026-03-23 | KPI assessment quick-create form |
| `sprk_matter_kpi_refresh.js` | 2026-03-23 | Matter KPI refresh |
| `sprk_subgrid_parent_rollup.js` | 2026-03-20 | Subgrid parent rollup |
| `sprk_kpi_ribbon_actions.js` | 2026-02-13 | KPI ribbon actions |
| `sprk_chartdefinition_form.js` | 2026-01-03 | Chart definition form script (oldest — verify still used) |

## 7. Office Add-ins (`src/client/office-addins/`)

Static Web App-deployed Microsoft 365 add-ins.

| Add-in | Path | Manifest | Build tooling | Last commit (folder root) |
|---|---|---|---|---|
| **Outlook Add-in** | `office-addins/outlook/` | `outlook-manifest.xml`, `manifest.json`, `manifest.prod.json` | Webpack 5 (`webpack.config.js`) | Track with `office-addins-deploy` skill |
| **Word Add-in** | `office-addins/word/` | `word-manifest.xml` | (same build as Outlook) | Track with `office-addins-deploy` skill |
| **Shared infrastructure** | `office-addins/shared/` | — | Mocks, adapters, API client, auth, services, taskpane base | — |

## 8. Special solutions (not Code Pages, despite living under `src/solutions/`)

| Folder | Type | Purpose |
|---|---|---|
| **CopilotAgent** | Declarative agent | `declarativeAgent.json` + `spaarke-api-plugin.json` + `spaarke-bff-openapi.yaml` + `cards/` + `appPackage/` — M365 Copilot declarative agent definition (NOT a React SPA) |
| **SpaarkeCore** | Dataverse solution | `solution.xml`, `customizations.xml`, `entities/`, `security-roles/`, `environment-variables/` — core Dataverse schema solution (NOT a Code Page) |
| **EventCommands** | Ribbon commands + dialogs | `EventRibbonDiffXml.xml` + `sprk_event_ribbon_commands.js` + `dialogs/` — event ribbon command bundle (not a Vite Code Page) |

## 9. External SPA (`src/client/external-spa/`)

| App | Purpose | Build |
|---|---|---|
| **secure-project-workspace** (`@spaarke/secure-project-workspace`) | Power Pages Code Page SPA for external user access to secure projects | Vite (`vite.config.ts` + `powerpages.config.json`) |

Deployment via `power-page-deploy` skill (not `code-page-deploy`).

## 10. Shared libraries (NOT independently deployable — referenced by the above)

| Path | Purpose |
|---|---|
| `src/client/shared/Spaarke.UI.Components/` | Shared Fluent UI v9 component library (`@spaarke/ui-components`) — referenced by PCFs and Code Pages |
| `src/client/shared/Spaarke.Auth/` | Client-side auth library (`@spaarke/auth`, npm/MSAL.js) — **does NOT validate inbound tokens** (see Insights Engine decisions.md D-25) |
| `src/client/pcf/shared/` | PCF-specific shared utilities |

## 11. How to use this inventory

**Question: "Do I need to update PCF X?"**
1. Look up X in §1 (active) or §2 (archived) above
2. If active: cite the version in the manifest as your starting point
3. If archived: check §5 for replacement; if none listed, ask before reviving

**Question: "Has PCF X been replaced by a Code Page?"**
- See §5 (replacement analysis) for the explicit list. Both surfaces co-existing is intentional in some cases (SemanticSearch, DocumentRelationshipViewer).

**Question: "What's the latest version of Code Page Y?"**
- See §3 (`src/client/code-pages/`) or §4 (`src/solutions/`). Last commit date is the freshness signal.

**Question: "Where do I add new ribbon commands?"**
- §6.1 (`src/client/webresources/js/`). Group by entity / domain. Bind in solution ribbon XML.

## 12. Maintenance protocol

When adding/changing client resources:

| Action | Update |
|---|---|
| New PCF goes live | Add row to §1; bump `Total folders` in summary |
| PCF replaced by Code Page | Move PCF row from §1 to §2 (archived); add to §5 (replacement analysis); add the Code Page to §3 if it lives in `src/client/code-pages/` or §4 if in `src/solutions/` |
| New ribbon command | Add row to §6 |
| Office add-in change | Update §7 last commit |
| New Code Page | Add to §3 or §4 based on location |

**Doc location**: [docs/architecture/client-resources-inventory.md](client-resources-inventory.md)
**Trigger phrase to update**: "client inventory", "is X deprecated", "PCF list", "Code Page list"

## 13. Open questions for confirmation

The following items need owner confirmation (marked with ❓ in tables above):

1. **Older PCF folders without manifests** (§2): confirm replacement status for each. Recommendation: delete or move to `.claude/archive/2026-XX-XX/pcf-removed/` with a brief note.
2. **`code-pages/SprkChatPane`** (§3): older commit (2026-03-26) than peers; verify it's still in use or has been superseded by `SpaarkeAi`.
3. **`pcf/AIMetadataExtractor`** (§2): oldest folder (2025-12-02); confirm dead.
4. **`sprk_chartdefinition_form.js`** (§6.2): oldest web resource (2026-01-03); confirm still bound to a form.
5. **`solutions/webresources/`** vs **`client/webresources/js/`**: why two locations? Consider consolidating.
