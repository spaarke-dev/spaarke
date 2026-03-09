# Deployment Checklist: Task 144 - Deploy AnalysisWorkspace Code Page

> **Task**: R2-144 - Deploy AnalysisWorkspace Code Page to Dataverse
> **Status**: Preparation complete (code-only environment - no live Dataverse access)
> **Date**: 2026-02-26

---

## Pre-Deployment Verification

### Build Verification

- [x] Source files exist: `src/client/code-pages/AnalysisWorkspace/src/index.tsx`, `App.tsx`, etc.
- [x] `package.json` configured with `npm run build` script (webpack) and `npm test` (jest)
- [x] `build-webresource.ps1` exists at `src/client/code-pages/AnalysisWorkspace/build-webresource.ps1`
- [x] Existing build output present: `src/client/code-pages/AnalysisWorkspace/out/bundle.js`
- [x] Existing inlined HTML present: `src/client/code-pages/AnalysisWorkspace/out/sprk_analysisworkspace.html`
- [x] Launcher script exists: `src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js`
- [x] DEPRECATED.md created for legacy PCF: `src/client/pcf/AnalysisWorkspace/DEPRECATED.md`

### Source Components (38 files)

Key source files in `src/client/code-pages/AnalysisWorkspace/src/`:

| Category | Files |
|----------|-------|
| Entry Point | `index.tsx`, `App.tsx` |
| Components | `EditorPanel.tsx`, `PanelSplitter.tsx`, `AnalysisToolbar.tsx`, `StreamingIndicator.tsx`, `DocumentStreamBridge.tsx`, `ReAnalysisProgressOverlay.tsx`, `DiffReviewPanel.tsx`, `SourceViewerPanel.tsx` |
| Hooks | `usePanelResize.ts`, `useDocumentStreaming.ts`, `useAuth.ts`, `useThemeDetection.ts`, `useAnalysisLoader.ts`, `useAutoSave.ts`, `useExportAnalysis.ts`, `useSelectionBroadcast.ts`, `useReAnalysisProgress.ts`, `useDiffReview.ts` |
| Services | `authService.ts`, `hostContext.ts`, `analysisApi.ts` |
| Context | `AuthContext.tsx` |
| Types | `index.ts`, `xrm.d.ts` |
| Tests | `App.test.tsx`, `useAnalysisLoader.test.ts`, `useAutoSave.test.ts`, `useSelectionBroadcast.test.ts`, `AnalysisToolbar.test.tsx`, `useDiffReview.test.ts`, `DiffReviewPanel.test.tsx`, `streaming-e2e.test.ts` |

### Dependencies Confirmed

| Dependency | Task | Status |
|-----------|------|--------|
| AnalysisWorkspace Code Page implementation | R2-072 | Completed |
| SprkChatPane deployment | R2-143 | Completed (preparation) |

---

## Deployment Steps (Requires Live Dataverse Environment)

### Step 1: Build AnalysisWorkspace Webpack Bundle

```bash
cd src/client/code-pages/AnalysisWorkspace
npm install
npm run build
```

- Verify: `out/bundle.js` exists with recent timestamp
- Verify: Zero build errors

### Step 2: Inline Bundle into HTML Web Resource

```powershell
cd src/client/code-pages/AnalysisWorkspace
powershell -File build-webresource.ps1
```

- Verify: `out/sprk_analysisworkspace.html` exists
- Verify: HTML contains inline `<script>` (no external bundle.js reference)
- Output file name: `sprk_analysisworkspace.html` (lowercase)

### Step 3: Upload Web Resource to Dataverse

**Target Environment**: https://spaarkedev1.crm.dynamics.com

1. Open https://make.powerapps.com
2. Select the Spaarke Dev environment
3. Navigate to Solutions > Spaarke Core solution
4. Web Resources > New or find existing `sprk_AnalysisWorkspace`
5. Configure:
   - **Name**: `sprk_AnalysisWorkspace`
   - **Display Name**: AnalysisWorkspace Code Page
   - **Type**: Webpage (HTML)
   - **Description**: Analysis Workspace - React 19 Code Page with 2-panel layout (RichTextEditor + SprkChat)
6. Upload: `src/client/code-pages/AnalysisWorkspace/out/sprk_analysisworkspace.html`
7. Save and Publish

### Step 4: Deploy Launcher Script as Web Resource

1. Upload `src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js`
2. Web Resource Configuration:
   - **Name**: `sprk_/scripts/analysisWorkspaceLauncher.js`
   - **Display Name**: Analysis Workspace Code Page Launcher
   - **Type**: Script (JScript)
   - **Description**: Opens the AnalysisWorkspace Code Page dialog via navigateTo webresource
3. Save and Publish

### Step 5: Update Analysis Form - Add Code Page Launcher

1. Open `make.powerapps.com` > Tables > Analysis (`sprk_analysis`) > Forms
2. Open the Main form in the form designer
3. Add a command bar button OR form onLoad handler:

**Ribbon Button Configuration:**
- Command ID: `Spaarke.AnalysisWorkspace.Open.Command`
- Function: `Spaarke.AnalysisWorkspace.openAnalysisWorkspace`
- Library: `$webresource:sprk_/scripts/analysisWorkspaceLauncher.js`
- CrmParameter: `PrimaryControl`
- Enable Rule: `Spaarke.AnalysisWorkspace.enableOpenWorkspace`
- Label: "Open Workspace"

**OR Form OnLoad Configuration:**
- Entity: `sprk_analysis`
- Form: Main Form
- Event: OnLoad
- Library: `sprk_/scripts/analysisWorkspaceLauncher.js`
- Function: `Spaarke.AnalysisWorkspace.openAnalysisWorkspace`
- Pass execution context: Yes

The launcher opens the Code Page via:
```javascript
Xrm.Navigation.navigateTo(
    { pageType: "webresource", webresourceName: "sprk_AnalysisWorkspace", data: "analysisId=...&documentId=..." },
    { target: 2, width: { value: 95, unit: "%" }, height: { value: 95, unit: "%" } }
);
```

### Step 6: Remove Legacy PCF Control Binding from Form

Per `src/client/pcf/AnalysisWorkspace/DEPRECATED.md`:

1. **Remove PCF Control from Analysis Form**:
   - Entity: `sprk_analysis`
   - Control: `Spaarke.Controls.AnalysisWorkspace`
   - Bound to: `sprk_analysisid` (SingleLine.Text field)
   - Open form designer, locate the section, remove custom control binding
   - Remove or hide the section if it was solely for the PCF

2. **Remove PCF Solution** (if imported as managed):
   ```powershell
   pac solution delete --solution-name AnalysisWorkspaceSolution
   pac solution publish
   ```

3. **Remove Custom Page** (if still present):
   - Delete `sprk_analysisworkspace_8bc0b` Canvas/Custom Page app
   - Remove from any solution

4. **Update Ribbon Commands**:
   - Replace `Spaarke_OpenAnalysisWorkspaceFromSubgrid` in `sprk_analysis_commands.js`
   - With `Spaarke.AnalysisWorkspace.openAnalysisWorkspace` from `sprk_/scripts/analysisWorkspaceLauncher.js`

### Step 7: Publish All Customizations

```powershell
pac solution publish
```

### Step 8: Verify Deployment

1. Open the Analysis form in the Dataverse app
2. Trigger the Code Page launcher (button click or auto-open on load)
3. Verify: 2-panel layout renders (RichTextEditor left, SprkChat right)
4. Verify: Panels resize correctly via splitter
5. Verify: Analysis document loads in the editor
6. Verify: SprkChat is functional (send message, receive streaming response)
7. Verify: Context parameters (analysisId, entityType) are correctly passed
8. Verify: Layout is responsive at different viewport sizes
9. Verify: Dark mode and light mode render correctly

### Step 9: Verify Legacy PCF Removed

1. Confirm Analysis form no longer renders the AnalysisWorkspace PCF control
2. Confirm form shows launcher button/link instead of embedded PCF
3. Confirm `AnalysisWorkspaceSolution` is removed from solutions list

---

## Legacy PCF Deprecation Reference

Full deprecation details: `src/client/pcf/AnalysisWorkspace/DEPRECATED.md`

| Legacy (PCF) | Replacement (Code Page) |
|--------------|------------------------|
| `src/client/pcf/AnalysisWorkspace/` | `src/client/code-pages/AnalysisWorkspace/` |
| PCF: `Spaarke.Controls.AnalysisWorkspace` | Web resource: `sprk_AnalysisWorkspace` (HTML) |
| Custom Page: `sprk_analysisworkspace_8bc0b` | `Xrm.Navigation.navigateTo({ pageType: "webresource" })` |
| Solution: `AnalysisWorkspaceSolution` | Standard web resource in main Spaarke solution |
| `sprk_analysis_commands.js` | `sprk_/scripts/analysisWorkspaceLauncher.js` |

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `src/client/code-pages/AnalysisWorkspace/package.json` | NPM build configuration |
| `src/client/code-pages/AnalysisWorkspace/webpack.config.js` | Webpack bundling config |
| `src/client/code-pages/AnalysisWorkspace/src/index.tsx` | React 19 entry point |
| `src/client/code-pages/AnalysisWorkspace/src/App.tsx` | Main 2-panel App component |
| `src/client/code-pages/AnalysisWorkspace/build-webresource.ps1` | HTML inlining script |
| `src/client/code-pages/AnalysisWorkspace/out/sprk_analysisworkspace.html` | Deployable artifact |
| `src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js` | Form launcher script |
| `src/client/pcf/AnalysisWorkspace/DEPRECATED.md` | Legacy PCF deprecation guide |

---

## Notes

- **Actual deployment requires live Dataverse environment access** which is not available in this code-only session
- Build artifacts already exist from prior build steps (tasks 060-072)
- The AnalysisWorkspace uses React 19 with `createRoot()` (bundled, not platform-provided)
- The web resource is a single self-contained HTML file with all JS inlined
- Authentication is independent per pane via `Xrm.Utility.getGlobalContext()`
- The legacy PCF control at `src/client/pcf/AnalysisWorkspace/` is preserved for reference/rollback only
