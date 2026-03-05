# Task R2-016: SprkChatPane Deployment Requirements

> **Date**: 2026-02-26
> **Status**: Build verified; deployment pending Dataverse access

## Build Results

### Step 1: Webpack Build
- **Command**: `npm run build` (webpack --config webpack.config.js)
- **Status**: SUCCESS
- **Output**: `src/client/code-pages/SprkChatPane/out/bundle.js`
- **Bundle size**: 1,038,379 bytes (1,014 KB / 1,010 KiB)
- **Warnings**: 3 webpack performance warnings (bundle exceeds 244 KiB recommended limit)
  - This is expected for a Code Page that bundles React 19, Fluent UI v9, and all dependencies
- **Build time**: ~17 seconds

### Step 2: Inline HTML Build
- **Script**: `build-webresource.ps1` (created as part of this task, following SemanticSearch pattern)
- **Command**: `powershell -ExecutionPolicy Bypass -File build-webresource.ps1`
- **Status**: SUCCESS
- **Output**: `src/client/code-pages/SprkChatPane/out/sprk_SprkChatPane.html`
- **Inline HTML size**: 1,039,004 bytes (1,015 KB)
- The script inlines bundle.js into index.html to produce a single self-contained HTML file

### npm install note
- The `@spaarke/ui-components` dependency uses `workspace:*` protocol (pnpm workspaces)
- For standalone npm install, temporarily change to `file:../../shared/Spaarke.UI.Components`
- Webpack resolves `@spaarke/ui-components` via alias to `../../shared/Spaarke.UI.Components/src` regardless

## Deployment Requirements (Steps 3-7)

### Step 3: Deploy HTML Web Resource
- **Web Resource Name**: `sprk_SprkChatPane`
- **Display Name**: SprkChatPane
- **Type**: Webpage (HTML)
- **File**: `out/sprk_SprkChatPane.html`
- **Target Environment**: `https://spaarkedev1.crm.dynamics.com`
- **Deploy via**: Power Apps maker portal > Dataverse > Web resources > New
  - OR: `pac solution import` with solution containing the web resource
  - OR: `pac webresource push` (if supported for HTML resources)

### Step 4: Deploy Launcher Script
- **Web Resource Name**: `sprk_/scripts/openSprkChatPane`
- **Display Name**: Open SprkChat Pane Launcher
- **Type**: Script (JScript)
- **Source**: `src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts`
- **Note**: The launcher TypeScript needs to be compiled to plain JS before deployment
  - It uses no imports (pure standalone script with global namespace export)
  - Compile with: `tsc --target ES2020 --module none launcher/openSprkChatPane.ts`
  - Or manually transpile (it's a single file with no module dependencies)

### Step 5: Configure Ribbon Button
The launcher script is designed to be called from a ribbon/command bar button:
- **Library**: `$webresource:sprk_/scripts/openSprkChatPane.js`
- **Function**: `Spaarke.SprkChat.openPane`
- **CrmParameter**: `PrimaryControl`
- **Enable Rule**: `Spaarke.SprkChat.enable` (checks Xrm.App.sidePanes availability)
- **Visibility Rule**: `Spaarke.SprkChat.show` (always visible)

### Step 6: Publish Customizations
After deploying web resources and ribbon configuration:
```
pac solution publish
```
Or via maker portal: publish all customizations

### Step 7: Verification
1. Navigate to a model-driven app form
2. Click the SprkChat ribbon button
3. Verify the side pane opens at 400px width
4. Verify the SprkChatPane HTML loads in the pane
5. Verify form context (entityType, entityId) passes correctly via URL params
6. Test singleton behavior: clicking again should select the existing pane, not create a new one

## Bundle Size Analysis

The 1,010 KiB bundle includes:
- React 19 + ReactDOM (~530 KB minified)
- Fluent UI v9 components (selective imports)
- `@spaarke/ui-components` shared library (including Lexical rich text editor dependencies)
- SprkChatPane application code

### Size Optimization Opportunities
- The shared library includes Lexical editor modules (~583 KB) that may not all be needed by SprkChatPane
- Consider tree-shaking review of `@spaarke/ui-components` imports
- Fluent UI v9 selective imports are already in use (good)
- Terser compression with 2 passes and console stripping is enabled
