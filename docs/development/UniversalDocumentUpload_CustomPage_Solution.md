# Universal Document Upload Custom Page Solution

## Overview
- **Purpose**: Provide a consistent quick-create dialog for uploading multiple documents to SharePoint Embedded while creating Dataverse records in a single workflow.
- **Scope**: Custom Page dialog (`sprk_documentuploaddialog_e52db`), `UniversalDocumentUpload` PCF control, ribbon launcher web resource (`sprk_subgrid_commands.js`), and supporting Dataverse solution packaging.
- **Current Version**: 3.0.3 (Custom Page dialog focus, phase-7 metadata support, hydration resiliency).

## Architecture
- **Invocation Flow**
  - Ribbon command button on the Documents subgrid executes `Spaarke_AddMultipleDocuments` (web resource).
  - Script gathers parent entity metadata, SharePoint container ID, and app context, then opens the Custom Page dialog via `Xrm.Navigation.navigateTo`.
  - Dialog hosts the canvas-based custom page which renders the PCF control once parameters hydrate.
- **Custom Page Composition**
  - App-level variables declared in `App.OnStart` for hydration state and parent context values.
  - Screen `OnVisible` performs dual-path parameter intake: direct `Param("key")` values with fallback to JSON payload (`Param("data")`).
  - A visibility gate (`Visible` property) ensures the PCF renders only when parent identifiers are present.
- **PCF Control**
  - `UniversalDocumentUpload` control detects custom page context, initializes services (MSAL auth, multi-file upload, document record service), and renders the React UI once hydrated.
  - `updateView` function is idempotent; it waits for non-empty `parentEntityName`, `parentRecordId`, and `containerId` before invoking `initializeAsync`.
- **Dataverse Solution**
  - `UniversalQuickCreateSolution.cdsproj` bundles the custom page, web resources, and PCF control. Build output lives at `src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/bin/Debug/UniversalQuickCreateSolution.zip`.

## Key Components
### 1. Ribbon Launcher (`sprk_subgrid_commands.js`)
- Collects parent entity details, validates saved state, resolves container ID, and determines display name.
- Normalizes GUIDs, includes `appId` when available, and sends both discrete parameters and a serialized JSON payload (`data: JSON.stringify(dialogParameters)`).
- Handles dialog completion by refreshing the subgrid on success.

### 2. Custom Page (`sprk_documentuploaddialog_e52db`)
- Variables: `varInit`, `varParentEntityName`, `varParentRecordId`, `varContainerId`, `varParentDisplayName` declared in `App.OnStart`.
- `OnVisible`: idempotent hydration logic that reads either discrete parameters or a JSON blob, commits to global variables, and defers re-hydration after first pass.
- PCF control bindings reference hydrated variables; `Visible` expression prevents premature render.

### 3. PCF Control (`UniversalDocumentUpload`)
- Determines hosting mode, renders React UI, orchestrates MSAL authentication, MultiFileUploadService, DocumentRecordService, nav-map lookups, and SharePoint Embedded uploads.
- Gracefully handles missing parameters by logging and showing instructions until values arrive.

## Configuration & Settings
- **Parameters Required**: `parentEntityName`, `parentRecordId`, `containerId`; optional `parentDisplayName`.
- **SharePoint Container Field**: Configured per entity via `ENTITY_CONFIGURATIONS` in `sprk_subgrid_commands.js`.
- **API Base URL**: Bound on the PCF control (`sdapApiBaseUrl` property) in the custom page definition.
- **Authentication**: MSAL provider uses `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation` scope.

## Deployment Steps
1. `npm run build` in `src/controls/UniversalQuickCreate` (PCF build).
2. `dotnet msbuild /t:build /restore` in `UniversalQuickCreateSolution` (solution packaging).
3. Import `UniversalQuickCreateSolution.zip` into Dataverse (unmanaged layer).
4. Publish the Custom Page and republish the hosting Model-Driven App.
5. Hard-refresh the model-driven shell and validate the dialog.

## Validation Checklist
- Add temporary label to custom page: `Concatenate("E=", Param("parentEntityName"), ...)` to inspect direct vs JSON payloads.
- Confirm PCF visibility toggles once `varParent*` variables populate.
- Ensure `bootstrap` logs exactly once; subsequent `updateView` calls should no-op.
- Verify ribbon launcher refreshes the Documents subgrid post-upload.

## Known Challenges & Resolutions
| Challenge | Symptom | Resolution Status |
|-----------|---------|-------------------|
| Parameter hydration timing | PCF rendered blank screen | Added `updateView` guard and custom page hydration gate (Completed).
| Missing `appId` in dialog | Custom page failed to load outside designer | Added appId retrieval fallback from URL (Completed).
| Mixed payload formats | Platform sometimes sends JSON blob instead of discrete keys | Dual-path hydration (`Param("key")` + `Param("data")`) implemented (In progress due to ongoing validation).
| `Invalid input to custom page` warning | Toast message during navigation | Investigating exact schema requirements for `pageInput.data`; current approach serializes payload (Open).
| Legacy/duplicate web resources | Confusion about authoritative script | Archived legacy assets and synchronized canonical file in solution packaging (Completed).

## Open Items / Next Steps
- Confirm correct structure for `pageInput.data` (possible need for object with wrapped `type`/`value` properties) and update launch payload + parser.
- Re-test hydration after importing the latest solution to ensure warnings are cleared.
- Document final resolution once dialog succeeds without warnings.

## Change History (Recent)
- **Oct 23, 2025**: Implemented custom page hydration refactor, gated PCF visibility, added JSON payload fallback, rebuilt solution.
- **Oct 24, 2025**: Adjusted launcher to serialize JSON payload, committed changes to GitHub, continuing investigation into navigation toast.

## References
- `src/controls/UniversalQuickCreate/UniversalQuickCreate/index.ts`
- `src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/WebResources/sprk_subgrid_commands.js`
- `src/controls/UniversalQuickCreate/UniversalQuickCreateSolution/src/canvaspages/sprk_universaldocumentupload_page.json`
- `dev/projects/quickcreate_pcf_component/custom-page-code.yaml`
