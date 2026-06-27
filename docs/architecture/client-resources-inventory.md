# Client Resources Inventory — Definitive Reference

> **Last Updated**: 2026-05-19 (added Dataverse-verified usage data)
> **Purpose**: Single source of truth for "what client-side components do we have, what's their status, when were they last updated, and ARE THEY ACTUALLY USED on forms?"
> **Method**:
> - Repo signal: last-commit dates from `git log`; `ControlManifest.Input.xml` existence as build-readiness signal.
> - **Deployment signal**: live `systemform.formxml` LIKE queries against Dataverse (via MCP `read_query`). Also queried `savedquery.layoutxml` + `customcontroldefaultconfig.controldescriptionxml` — both returned zero PCF bindings, confirming PCFs in this environment are bound only via forms.
> - Tenant queried: Spaarke Dev environment (484bc857-...) — re-run against other environments if they may differ.

> **Status of "missing source" PCFs**: confirmed via git history (commit `ded4e037`, 2026-03-15, "remove 10 deprecated PCF controls"). The 3 PCFs still referenced on forms (AnalysisBuilder, EventCalendarFilter, EventAutoAssociate) were **intentionally deleted because they were replaced by HTML Code Pages**. The form bindings are orphaned leftovers from incomplete cleanup, not lost source. See §2.1 — action is to remove the dead bindings from Dataverse forms, NOT to recover source.

## Summary

| Category | Source-present + form-bound | Source-present + no form binding | Source-deleted (orphaned form binding) | Source-missing + no binding |
|---|---|---|---|---|
| **PCF controls** | **5** (verified live) | 9 (deployed but no form binding found — verify usage) | 3 (clean up form bindings; PCFs intentionally retired per commit `ded4e037`) | 7 (safe to remove folder) |

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

## 1. PCF Controls — Source present in repo

> Path convention: `src/client/pcf/{name}/[control|{Name}]/ControlManifest.Input.xml`
> "Source present" = `ControlManifest.Input.xml` exists (control is buildable).
> "Forms bound to" verified via Dataverse `systemform.formxml LIKE '%{namespace}.{constructor}%'` query 2026-05-19.

### 1.1 Source present AND bound to forms — verified live in production

| Name | Namespace | Version | Forms bound to (Dataverse) | Last commit |
|---|---|---|---|---|
| **RelatedDocumentCount** | `Spaarke.Pcf.RelatedDocumentCount` | 1.21.2 | Document main form | 2026-05-13 |
| **SemanticSearchControl** | `Sprk` | 1.1.41 | Matter / Work Assignment / Invoice / Project main forms | 2026-05-13 |
| **SpeDocumentViewer** | `Spaarke` | 1.0.25 | Document main form | 2026-05-14 |
| **UniversalQuickCreate** (constructor: `UniversalDocumentUpload`) | `Spaarke.Controls` | 3.15.3 | Upload Documents form | 2026-05-14 |
| **VisualHost** | `Spaarke.Visuals` | 1.4.0 | Matter / Work Assignment / Project main forms | 2026-05-13 |

### 1.2 Source present BUT no form binding found — verify usage

> Zero matches in `systemform.formxml`, `savedquery.layoutxml`, AND `customcontroldefaultconfig.controldescriptionxml`. These PCFs may still be used via:
> - MDA app components / sitemap entries (not queried)
> - Field-level customControl JSON overrides on attribute metadata (rare)
> - Globally invoked by web resources (e.g., ThemeEnforcer via app load JS)
> - Deployed but not yet bound to anything (parked)
>
> **Action**: confirm each per row. If unused, retire and move to archive.

| Name | Namespace | Version | Likely usage (verify) | Last commit | Notes |
|---|---|---|---|---|---|
| **AssociationResolver** | `Spaarke.Controls` | 1.1.0 | Possibly only used in Wizards (Code Pages) | 2026-05-14 | Verify if any MDA component binds it |
| **DocumentRelationshipViewer** | `Spaarke.Controls` | 1.0.35 | Code Page version (`code-pages/DocumentRelationshipViewer`) is canonical; this PCF may be retired | 2026-05-13 | Coexists with Code Page; project memory mentions sharing `@spaarke/ui-components` |
| **DrillThroughWorkspace** | `Spaarke.Controls` | 1.1.1 | Dataset PCF — expected to bind on subgrids or views, but zero matches | 2026-03-30 | Confirm whether deployed but unbound |
| **EmailProcessingMonitor** | `Spaarke` | 1.1.0 | Standalone admin app/page — likely in MDA sitemap, not on a form | 2026-05-13 | Plausibly active; verify via app config |
| **ScopeConfigEditor** | `Sprk` | 1.2.7 | Possibly field-level (column override on `sprk_aichatscope`?) | 2026-03-30 | Specialized editor — confirm binding |
| **SpaarkeGridCustomizer** | `Spaarke.Controls` | 1.0.0 | Grid customizer — expected at entity default level, but zero matches | 2026-05-14 | Check MDA grid components |
| **ThemeEnforcer** | `Spaarke` | 1.0.0 | Likely invoked at app load (e.g., via webresource JS), not bound on a form | 2026-03-13 | Plausibly active globally |
| **UniversalDatasetGrid** | `Spaarke.UI.Components` | 2.3.0 | Dataset PCF — expected on subgrids in forms, but zero matches | 2026-05-13 | Confirm whether deployed but unbound |
| **UpdateRelatedButton** | `Spaarke.Controls` | 1.0.0 | Lookup field control — expected on a form, but zero matches | 2026-05-14 | Confirm binding

## 2. PCF Folders — Source MISSING from repo

> Folder exists under `src/client/pcf/` but **no `ControlManifest.Input.xml` source file**. Build artifacts (`generated/`, `out/`) may remain.
> Dataverse query results split these into two very different groups: §2.1 are CRITICAL (deployed but unbuildable); §2.2 are safe to remove.

### 2.1 Orphaned form bindings — PCFs intentionally deleted but Dataverse forms still reference them

> **Resolved 2026-05-19 via git history**: commit [`ded4e037`](https://github.com/spaarke-dev/spaarke/commit/ded4e037) ("feat(quality): remove 10 deprecated PCF controls, version bump + rebuild 4 modified controls", 2026-03-15) intentionally deleted these PCFs because they were replaced by HTML Code Pages. The form bindings in Dataverse were not cleaned up at the same time and remain as orphaned `<customControl>` references in form XML.
>
> **Action is form-binding cleanup, NOT source recovery.** These PCF source folders should be deleted entirely; the live forms should have the orphaned bindings removed.

| Folder | Forms with orphaned binding | Likely Code Page replacement | Cleanup action |
|---|---|---|---|
| `pcf/AnalysisBuilder` | Analysis main form (sprk_analysis) | `code-pages/AnalysisWorkspace` (2026-05-16) | Remove `<customControl>` element from Analysis main form XML; replace with default field control or the AnalysisWorkspace Code Page surface |
| `pcf/EventAutoAssociate` | Event quick create form (sprk_event) | `CreateEventWizard` Code Page (2026-04-03) handles event creation with associations | Remove `<customControl>` from form XML; rely on CreateEventWizard for event creation |
| `pcf/EventCalendarFilter` | Event main form / Event modal form / Event Assign Work main form (3 forms on sprk_event) | Calendar functionality moved to Code Pages (`CalendarSidePane`, `EventsPage`) | Remove `<customControl>` from all 3 Event forms |

**Cleanup procedure**:
1. In Power Apps maker, open each form listed above
2. Find the dead control (it likely renders as a blank/error placeholder today since the PCF source has been gone since 2026-03-15)
3. Remove the customControl override → field reverts to default control
4. Save + publish the form
5. Once all bindings are removed, delete the PCF folder from the repo (`git rm -rf src/client/pcf/AnalysisBuilder src/client/pcf/EventAutoAssociate src/client/pcf/EventCalendarFilter`)

**Why this got missed**: commit `ded4e037` deleted source + solution packages, but it didn't remove the bindings from the live forms in Dataverse. The Code Page replacements were deployed separately, and the old form bindings became silent dead weight. Worth adding to the PCF retirement checklist: "Step N: remove all `<customControl>` bindings from production form XML BEFORE deleting source."

### 2.2 Source missing AND no form binding — safe to clean up

| Folder | Last commit | Likely replacement | Action |
|---|---|---|---|
| `pcf/AnalysisWorkspace` | 2026-03-15 | Replaced by `code-pages/AnalysisWorkspace` (2026-05-16) | Delete folder; build artifacts are not source-of-truth |
| `pcf/AIMetadataExtractor` | 2025-12-02 | Unknown — never deployed | Delete folder |
| `pcf/DueDatesWidget` | 2026-03-15 | Unknown — never deployed | Delete folder |
| `pcf/EventFormController` | 2026-03-15 | Unknown — never deployed | Delete folder |
| `pcf/PlaybookBuilderHost` | 2026-03-15 | Replaced by `code-pages/PlaybookBuilder` (2026-04-04) | Delete folder |
| `pcf/RegardingLink` | 2026-03-15 | Unknown — never deployed | Delete folder |
| `pcf/SpeFileViewer` | 2026-03-15 | Likely replaced by `SpeDocumentViewer` (2026-05-14) | Delete folder |

**Recommended action**: For each of the 7 entries above, delete the folder via `git rm -rf src/client/pcf/{name}`. The build artifacts left behind don't help anyone. Alternative: move to `.claude/archive/2026-XX-XX/pcf-removed/` with a brief README if you want a history breadcrumb.

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
| **EventsPage** | 2026-03-30 | Events page |
| **SmartTodo** | 2026-03-30 | Smart todo (Kanban-style) |
| **FindSimilarCodePage** | 2026-04-04 | Find similar documents wizard/page |
| **DemoRegistration** | 2026-04-03 | Self-service demo registration |

### 4.4 Older / verify status

| Name | Last commit | Notes |
|---|---|---|
| **EventCommands** | 2026-02-06 | Ribbon commands + dialogs (not a Code Page — see §8) |
| `src/solutions/webresources/` | 2026-03-20 | Sub-folder of additional JS web resources (see §6.2) |

## 5. PCF↔Code Page co-existence / replacement analysis — verified

> Updated 2026-05-19 with Dataverse usage data.

| PCF (`pcf/`) | Code Page (`code-pages/`) | Status |
|---|---|---|
| `pcf/AnalysisWorkspace` (source missing, NOT form-bound) | `code-pages/AnalysisWorkspace` (active) | **Replaced**: Code Page is the canonical version. PCF folder is safe to delete. |
| `pcf/PlaybookBuilderHost` (source missing, NOT form-bound) | `code-pages/PlaybookBuilder` (active) | **Replaced**: Code Page is the canonical version. PCF folder is safe to delete. |
| `pcf/DocumentRelationshipViewer` (source present, **NO form binding found**) | `code-pages/DocumentRelationshipViewer` (active) | **Code Page is canonical**; PCF may be retired. Verify no MDA component uses the PCF. |
| `pcf/SemanticSearchControl` (source present, **bound to 4 forms**) | `code-pages/SemanticSearch` (active) | **Both active**: PCF on Matter/Work Assignment/Invoice/Project forms; Code Page for standalone surfaces. Share `@spaarke/ui-components`. |
| `pcf/SpeFileViewer` (source missing, NOT form-bound) | (none) | **Truly retired**; superseded by `pcf/SpeDocumentViewer`. PCF folder safe to delete. |
| `pcf/SpeDocumentViewer` (source present, **bound to Document main form**) | (none) | **PCF is canonical**. |
| `pcf/AnalysisBuilder` 🚨 | (none) | **Deployed on Analysis main form but source MISSING** — see §2.1 |
| `pcf/EventCalendarFilter` 🚨 | (none) | **Deployed on 3 Event forms but source MISSING** — see §2.1 |
| `pcf/EventAutoAssociate` 🚨 | (none) | **Deployed on Event quick create but source MISSING** — see §2.1 |

**Decision rule** (documented in project memory): when a PCF has a Code Page version, the PCF stays for form-embedded scenarios and the Code Page covers standalone scenarios. They MUST share UI via `@spaarke/ui-components` to avoid duplication. The DocumentRelationshipViewer case suggests the PCF may have already been quietly retired (no form binding) — confirm before deleting source.

## 6. Web Resources (JavaScript)

> Single-file JS web resources for ribbon commands, form scripts, command bar actions. Deployed via solution import. **Icons are excluded from this inventory.**

### 6.1 `src/client/webresources/js/` (core, 12 files)

| File | Last commit | Purpose (inferred) |
|---|---|---|
| `sprk_wizard_commands.js` | 2026-06-25 | Wizard launch / command bar actions. Includes `openCreateTodoWizard(primaryControl)` (R4-118, 2026-06-25 deploy) for parent-form "Create To Do" buttons — currently wired to Matter ribbon via `spaarke_insights/Entities/sprk_Matter/RibbonDiff.xml`. Future entities (Project/Event/Invoice/WorkAssignment/Communication) deferred to R5 R-9 per `projects/smart-todo-r5/design.md`. |
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

### 12.1 Re-verifying PCF usage via Dataverse query (the load-bearing check)

Repo evidence (last commit dates + manifest existence) is necessary but NOT sufficient. The **definitive** "is this PCF used?" answer comes from Dataverse. Re-run this check whenever the inventory feels stale or a major refactor is planned.

**Method** — for each PCF, query `systemform.formxml`:

```sql
-- via mcp__dataverse__read_query
SELECT TOP 20 name, objecttypecode, type
FROM systemform
WHERE formxml LIKE '%{Namespace.Constructor}%'
```

Where `{Namespace.Constructor}` is e.g., `Spaarke.Controls.AssociationResolver`, `Sprk.SemanticSearchControl`, `Spaarke.SpeDocumentViewer`.

**Other binding surfaces to check** (verified 2026-05-19 — none in use for any Spaarke PCF, but check on each re-run in case the pattern changes):
- `savedquery.layoutxml LIKE '%{PCF}%'` — view-level bindings
- `customcontroldefaultconfig.controldescriptionxml LIKE '%{PCF}%'` — entity-default bindings

**Findings interpretation**:
- Form bindings found → PCF is in §1.1 (verified live)
- No form bindings, source present → §1.2 (verify usage via MDA app config; possibly retire)
- No form bindings, source missing → §2.2 (safe to delete folder)
- Form bindings found, source missing → §2.1 🚨 CRITICAL (recover source or replace)

**Cross-environment caveat**: queries above were run against Spaarke Dev. Production may differ. Re-run against Prod and Demo before any cleanup that touches §2.

### 12.2 Long-term prevention

To prevent "source missing but deployed" drift in the future, consider:
1. CI check: enumerate all PCF folders without manifests; fail build if any are found on a deployed form (requires a tool to introspect deployed solutions)
2. Solution export hook: after exporting a Spaarke solution, run an inventory script that flags any PCF binding whose source is missing from the repo
3. Documentation rule: when retiring a PCF, the retirement PR MUST include removing it from all form bindings before deleting source

## 13. Open questions for confirmation

> Many items resolved 2026-05-19 via Dataverse query (§12.1). Remaining open items:

### 13.1 Orphaned form binding cleanup (§2.1) — not urgent, but tidy

> Resolved 2026-05-19: source was intentionally deleted in commit `ded4e037`; Code Page replacements exist. Action is form-XML cleanup in Dataverse, not source recovery.

| # | PCF | Form cleanup |
|---|---|---|
| Q1 | `pcf/AnalysisBuilder` | Remove customControl binding from Analysis main form |
| Q2 | `pcf/EventCalendarFilter` | Remove customControl bindings from 3 Event forms (Event main, Event modal, Event Assign Work main) |
| Q3 | `pcf/EventAutoAssociate` | Remove customControl binding from Event quick create form |

### 13.2 Verify "source present but no form binding" PCFs (§1.2)

For each of the 9 PCFs in §1.2, confirm whether they are:
- In use via MDA app component / sitemap (verify via Power Apps admin)
- In use via field-level customControl override (verify via `attribute` metadata)
- In use globally via web resource JS (verify webresource bindings)
- **Truly unused** — candidates for retirement

The cleanest verification is via PAC CLI or solution export: check if any solution component includes the PCF binding.

### 13.3 Minor (lower priority)

| # | Item | Notes |
|---|---|---|
| Q4 | `code-pages/SprkChatPane` | Older commit (2026-03-26) than peers; verify it's still in use or superseded by `SpaarkeAi` |
| Q5 | `sprk_chartdefinition_form.js` (§6.2) | Oldest web resource (2026-01-03); confirm still bound to a form |
| Q6 | Two webresources directories | `solutions/webresources/` vs `client/webresources/js/` — why both? Consider consolidating |
