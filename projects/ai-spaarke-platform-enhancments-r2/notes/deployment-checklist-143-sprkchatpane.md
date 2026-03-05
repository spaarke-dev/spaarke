# Deployment Checklist: Task 143 - Deploy SprkChatPane Code Page

> **Task**: R2-143 - Deploy SprkChatPane Code Page to Dataverse
> **Status**: Preparation complete (code-only environment - no live Dataverse access)
> **Date**: 2026-02-26

---

## Pre-Deployment Verification

### Build Verification

- [x] Source files exist: `src/client/code-pages/SprkChatPane/src/index.tsx`, `App.tsx`, etc.
- [x] `package.json` configured with `npm run build` script (webpack)
- [x] `build-webresource.ps1` exists at `src/client/code-pages/SprkChatPane/build-webresource.ps1`
- [x] `node_modules/` present (npm install previously completed)
- [x] Existing build output present: `src/client/code-pages/SprkChatPane/out/bundle.js`
- [x] Existing inlined HTML present: `src/client/code-pages/SprkChatPane/out/sprk_SprkChatPane.html`
- [x] Launcher script exists: `src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts`

### Dependencies Confirmed

| Dependency | Task | Status |
|-----------|------|--------|
| SprkChatPane Code Page scaffold | R2-010 | Completed |
| SprkChat wired into Code Page | R2-012 | Completed |
| SprkChatBridge cross-pane communication | R2-011 | Completed |
| Side Pane Authentication | R2-013 | Completed |
| Context Auto-Detection | R2-014 | Completed |
| Side Pane Launcher Script | R2-015 | Completed |

---

## Deployment Steps (Requires Live Dataverse Environment)

### Step 1: Build SprkChatPane Webpack Bundle

```bash
cd src/client/code-pages/SprkChatPane
npm install
npm run build
```

- Verify: `out/bundle.js` exists with recent timestamp
- Verify: Zero build errors in console output

### Step 2: Inline Bundle into HTML Web Resource

```powershell
cd src/client/code-pages/SprkChatPane
powershell -File build-webresource.ps1
```

- Verify: `out/sprk_SprkChatPane.html` exists
- Verify: HTML contains inline `<script>` (no external bundle.js reference)
- Expected output file: `src/client/code-pages/SprkChatPane/out/sprk_SprkChatPane.html`

### Step 3: Upload Web Resource to Dataverse

**Target Environment**: https://spaarkedev1.crm.dynamics.com

**Option A: Power Apps Maker Portal (Manual)**

1. Open https://make.powerapps.com
2. Select the Spaarke Dev environment
3. Navigate to Solutions > Spaarke Core solution
4. Web Resources > New or find existing `sprk_SprkChatPane`
5. Configure:
   - **Name**: `sprk_SprkChatPane`
   - **Display Name**: SprkChatPane Code Page
   - **Type**: Webpage (HTML)
   - **Description**: SprkChat side pane - React 19 Code Page for interactive AI collaboration
6. Upload: `src/client/code-pages/SprkChatPane/out/sprk_SprkChatPane.html`
7. Save and Publish

**Option B: PAC CLI**

```powershell
# Authenticate
pac auth create --url https://spaarkedev1.crm.dynamics.com

# Upload web resource (requires solution context)
# Note: PAC CLI does not have direct web resource upload — use Maker Portal
```

### Step 4: Deploy Launcher Script as Web Resource

1. Upload `src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts` (compiled to JS)
2. Web Resource Name: `sprk_/scripts/openSprkChatPane.js`
3. Type: Script (JScript)

### Step 5: Configure Side Pane on Forms

The SprkChatPane opens as a Dataverse side pane via `Xrm.App.sidePanes.createPane()`.

Configure the side pane launcher on each target form:

#### Matter Form (`sprk_matter`)

1. Add ribbon button or form event to call the launcher script
2. Pass context: `entityType=sprk_matter`, `entityId={record ID}`
3. Side pane web resource: `sprk_SprkChatPane`

#### Project Form (`sprk_project`)

1. Add ribbon button or form event to call the launcher script
2. Pass context: `entityType=sprk_project`, `entityId={record ID}`
3. Side pane web resource: `sprk_SprkChatPane`

#### Analysis Form (`sprk_analysis`)

1. Add ribbon button or form event to call the launcher script
2. Pass context: `entityType=sprk_analysis`, `entityId={record ID}`
3. Side pane web resource: `sprk_SprkChatPane`

### Step 6: Publish All Customizations

```powershell
pac solution publish
```

### Step 7: Verify Deployment

For each form (Matter, Project, Analysis):

1. Open a record in the Dataverse app
2. Click the SprkChat side pane button (or trigger the launcher)
3. Verify: Side pane opens with SprkChat UI
4. Verify: SprkChat renders correctly (input box, message area)
5. Verify: Context parameters are correct (entityType, entityId match the form)
6. Verify: Can send a test message and receive streaming response
7. Verify: Dark mode / light mode renders correctly

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `src/client/code-pages/SprkChatPane/package.json` | NPM build configuration |
| `src/client/code-pages/SprkChatPane/webpack.config.js` | Webpack bundling config |
| `src/client/code-pages/SprkChatPane/src/index.tsx` | React 19 entry point |
| `src/client/code-pages/SprkChatPane/src/App.tsx` | Main App component |
| `src/client/code-pages/SprkChatPane/build-webresource.ps1` | HTML inlining script |
| `src/client/code-pages/SprkChatPane/out/sprk_SprkChatPane.html` | Deployable artifact |
| `src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts` | Side pane launcher |

---

## Notes

- **Actual deployment requires live Dataverse environment access** which is not available in this code-only session
- Build artifacts already exist from prior build steps (tasks 010-016)
- The SprkChatPane uses React 19 with `createRoot()` (bundled, not platform-provided)
- The web resource is a single self-contained HTML file with all JS inlined
- Authentication is independent per pane via `Xrm.Utility.getGlobalContext()`
