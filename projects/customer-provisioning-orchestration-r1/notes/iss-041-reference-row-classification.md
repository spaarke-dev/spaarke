# ISS-041: dev row ids found by widening ReferenceRowIdLiteralGuard

Issue: spaarke-dev/spaarke#3090. Guard: `tests/Spaarke.ArchTests/ReferenceRowIdLiteralGuardTests.cs`. Baseline: `tests/Spaarke.ArchTests/ReferenceRowIdLiteral.baseline.txt`.
Method: tables identified by read-only GET by primary key on `spaarkedev1` (2026-11-25); no writes. Only table and record purpose are recorded here.

## What the widening found

The pattern went from `-f011-8406-` to every dev-generated shape (`xxxxxxxx-xxxx-f[01]11-xxxx-xxxxxxxxxxxx`). In the files the guard scans (4,870 files):

| Outcome | Distinct ids |
|---|---|
| New-shape ids found (not `-f011-8406-`) | 60 |
| accepted: solution component id (`src/dataverse/solutions`) | 18 |
| accepted: harness-only file (`useCreateRecordFormData.ts`) | 5 |
| accepted: seed-file primary key (event-type rows with `-f111-8342-`) | 2 |
| **baselined with a reason (this change)** | **35** (62 path+id lines) |

The "~86 ids" in the issue also counts ids in `.md`, `.ps1`, `.xml`, test and `dev-review` files, which the guard does not scan.

Classes: **PC-RT** product configuration used at runtime, not seeded on a stamp (should be seeded or resolved by alternate key); **PC-CM** the id is named only in a comment, doc or `$comment`; **LB** likely bug on a stamp; **BA** build artifact; **ENV** per-environment identity, never portable.

## Grid configuration rows (`sprk_gridconfiguration`, 17 ids). The H12b DataGrid seeder only carries the 4 Communication Reconciliation rows

| Id | Row purpose | Files | Class | Follow-up |
|---|---|---|---|---|
| 61711823-1092-f111-b8dc-7ced8ddc4a05 | Outside Counsel Projects (external widget) | `ExternalAccessModule.cs`, `external-spa/.../ProjectsWidget.tsx` | PC-RT | Seed with fixed id (the seeder supports fixed ids), or look up by `sprk_name`/alternate key |
| 3af4102c-1092-f111-b8dc-7ced8ddc4a05 | Outside Counsel Documents (external widget) | `ExternalAccessModule.cs`, `DocumentsWidget.tsx` | PC-RT | same |
| 3ff4102c-1092-f111-b8dc-7ced8ddc4a05 | Outside Counsel Invoices (external widget) | `ExternalAccessModule.cs`, `InvoicesWidget.tsx` | PC-RT | same |
| 42f4102c-1092-f111-b8dc-7ced8ddc4a05 | Outside Counsel Work Assignments (external widget) | `ExternalAccessModule.cs`, `WorkAssignmentsWidget.tsx` | PC-RT | same |
| 583a2a33-1092-f111-b8dc-7ced8ddc4a05 | Outside Counsel Matters (external widget) | `ExternalAccessModule.cs`, `MattersWidget.tsx` | PC-RT | same |
| 403e5d37-cb94-f111-b8db-00224835447a | Internal Service Requests (external widget) | `ExternalAccessModule.cs`, `ServiceRequestsWidget.tsx` | PC-RT | same |
| e7c8126a-968b-f111-8077-7ced8ddc4a05 | Analysis Hub | `AnalysisHubWidget.tsx` | PC-RT | Seed with fixed id (see `projects/ai-advanced-capabilities-analysis-hub-r1/notes/hub-grid-config-deployment.md`) |
| 1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6 | Active Documents (Workspace) | `register-workspace-widgets.ts`, `LegalWorkspace/.../documents.registration.ts`, `GridOverviewHandler.cs` (tool text) | PC-RT | Seed with fixed id; in `GridOverviewHandler` stop listing ids in the model-facing text |
| 113ad380-9e63-f111-ab0c-70a8a53ec687 | Active Matters (Workspace) | `register-workspace-widgets.ts`, `matters.registration.ts`, `GridOverviewHandler.cs` | PC-RT | same |
| 97ee98e7-7a63-f111-ab0c-70a8a53ec687 | Active Projects (Workspace) | `register-workspace-widgets.ts`, `projects.registration.ts`, `GridOverviewHandler.cs` | PC-RT | same |
| d021827b-9b5e-f111-ab0c-7c1e521545d7 | Invoice Matter Budget Performance | `register-workspace-widgets.ts`, `invoices.registration.ts`, `sprk_invoicespage/src/main.tsx`, `GridOverviewHandler.cs` | PC-RT | same |
| 9c5b0ee7-7a63-f111-ab0c-000d3a4d8152 | Active Work Assignments (Workspace) | `register-workspace-widgets.ts`, `workAssignments.registration.ts`, `GridOverviewHandler.cs` | PC-RT | same |
| e1826c4c-9575-f111-ab0e-7ced8ddc4a05 | Active Communications (Workspace) | `register-workspace-widgets.ts`, `communications.registration.ts`, `sprk_communicationspage/src/main.tsx`, `GridOverviewHandler.cs` | PC-RT | same |
| ac05e4f1-8d85-f111-8075-7c1e5268570d | My Tasks (Assistant) | `register-workspace-widgets.ts`, `GridOverviewHandler.cs` | PC-RT | same |
| 5294c28a-f078-f111-ab0e-7ced8ddc4a05 | Event Default, Calendar Widget | `CalendarWorkspaceWidget.tsx`, `GridOverviewHandler.cs` | PC-RT | same |
| e15c2b93-a05f-f111-a825-70a8a59455f4 | Event Default (Events page) | `EventsPage/src/App.tsx` (runtime), `registerEventHandlers.ts` (comment) | PC-RT | same |
| 3019a06e-9b5e-f111-ab0c-7c1e521545d7 | KPI Assessment Matter Health | `sprk_kpiassessmentspage/src/main.tsx` | PC-RT | same |

Note: `ExternalAccessModule.OutsideCounselGridConfigurationIds` is a security allowlist, so on a stamp it fails closed (external widgets blocked). One more id in that set, `286be8b4-f96f-4d5a-b1d5-82fd83ca9693` (external record documents), is a random GUID that the guard cannot see by design; it is created by `scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1`.
The `GridOverviewHandler` hits are one `Description` string naming 8 of the ids above, so the model is told ids that do not exist on a stamp.

## Workspace layout rows (`sprk_workspacelayout`, 3 ids)

| Id | Row | Files | Class | Follow-up |
|---|---|---|---|---|
| 6d41d537-fe55-f111-a824-3833c5ed9c11 | Daily Briefing (system) | `surfaceLaunchRegistry.ts` (`widgetData.layoutId`) | LB | The H12b `WorkspaceLayoutSeeder` creates system layouts by name with a server-generated id, so a stamp's row differs and `WorkspaceLayoutWidget` gets an unknown `initialWorkspaceId`. Resolve by `layoutName`, or seed with fixed ids |
| a49f9e3a-fe55-f111-a824-7ced8dd899c3 | Smart To Do List (system) | `surfaceLaunchRegistry.ts` | LB | same |
| c09d26be-e173-f111-ab0e-7ced8ddc4a05 | Compose (system) | `sectionMetadataCatalog.ts`, `composeEditor.registration.ts` (comments only) | PC-CM | Reword the comments to drop the id |

## AI content rows (`sprk_analysisplaybook`, `sprk_analysisaction`, `sprk_aipersona`; 14 ids). AI content needs seeding by H12a (tracked as L56)

| Id | Table / row | Files | Class | Follow-up |
|---|---|---|---|---|
| 4a72f99c-a119-f111-8343-7ced8d1dc988 | playbook "Summarize File" | `DocumentEmailWizard.tsx` (runtime const `SUMMARIZE_NEW_FILES_PLAYBOOK_ID`), `summarize-document-for-chat.playbook.json` (`$comment`), PCF `bundle.js` | PC-RT, BA | Seed under H12a (L56) or resolve by playbook code. The checked-in `bundle.js` (`src/client/pcf/SemanticSearchControl/Solution/Controls/.../bundle.js`) is a build output; rebuild after the fix |
| 1e657651-9308-f111-8407-7c1e520aa4df | playbook "Finance Invoice Processing" | `IPlaybookLookupService.cs` (doc example) | PC-CM | Reword the example |
| 47686eb1-9916-f111-8343-7c1e520aa4df | playbook "Document Summary" | `compose-contracts.ts` (doc comment) | PC-CM | Reword |
| 2d660cad-d418-f111-8343-7ced8d1dc988 | playbook "Create New Matter Pre-Fill" | `MatterPreFillService.cs` (comment: "removed in Phase 1") | PC-CM | Reword |
| fc343e9c-3460-f111-ab0b-7c1e521b425f | playbook "Create New Project Pre-Fill" | `ProjectPreFillService.cs` (same) | PC-CM | Reword |
| eeb05bfd-1260-f111-ab0b-70a8a59455f4 | action "Summarize Document for Chat" (SUM-CHAT@v1) | `summarize-document-for-workspace.playbook.json` (`$comment`) | PC-CM | Reword; the playbook resolves it by actionCode |
| 5981632e-4165-f111-ab0c-7ced8ddc4cc6 | action "Matter Health Synthesis (Single Mode)" | `matter-health-single.playbook.json` (`$comment`) | PC-CM | Reword |
| 5137365a-825e-f111-a825-6045bdebafa9 | action "Insights, Live Fact Resolver" (INS-FACT@v1) | `matter-health-single.playbook.json` (`$comment-actionCode`) | PC-CM | Reword |
| 23939266-825e-f111-a825-6045bdebafa9 | action "Insights, Index Retrieve" (INS-IDXR@v1) | same | PC-CM | Reword |
| 6139aa6c-825e-f111-a825-6045bdebafa9 | action "Insights, Evidence Sufficiency" (INS-EVID@v1) | same | PC-CM | Reword |
| 32eafa72-825e-f111-a825-6045bdebafa9 | action "Insights, Grounding Verify" (INS-GRND@v1) | same | PC-CM | Reword |
| 96d52e7f-825e-f111-a825-6045bdebafa9 | action "Insights, Return Insight Artifact" (INS-RART@v1) | same | PC-CM | Reword |
| d1121079-825e-f111-a825-6045bdebafa9 | action "Insights, Decline to Find" (INS-DECL@v1) | same | PC-CM | Reword |
| 4fe49430-aa62-f111-ab0c-70a8a58ae145 | persona "SYS-DEFAULT" | `PlaybookChatContextProvider.cs` (comment) | PC-CM | Reword |

## Identity (1 id)

| Id | Table / row | Files | Class | Follow-up |
|---|---|---|---|---|
| 3121bf1b-9fbf-f111-aaaf-0022482913fc | systemuser: the dev ontology-writer managed identity's application user | `OntologyWriterCredentialFactory.cs`, `PredicateCompiler.cs` (doc comments) | ENV | Reword; each stamp creates its own application user |

## Totals (35 ids / 62 baseline lines)

- PC-RT (should be seeded or resolved by alternate key): 17 grid configurations + 1 playbook (`4a72f99c`) = 18 ids.
- LB (likely bug on a stamp): 2 ids (`surfaceLaunchRegistry.ts` layout ids), counted separately from PC-RT.
- PC-CM (comment or `$comment` only): 14 ids (1 layout, 4 playbooks, 8 actions, 1 persona).
- ENV: 1 id. BA: the PCF `bundle.js` line (id `4a72f99c`, already counted).
- Not classified: none; every id resolved to a table on `spaarkedev1`.

Cheapest fix for PC-RT grid configurations: add the 17 rows to the H12b DataGrid seed with their dev ids (the seeder already supports fixed ids), so the code stays valid and the 40 grid-configuration baseline lines can be deleted.
