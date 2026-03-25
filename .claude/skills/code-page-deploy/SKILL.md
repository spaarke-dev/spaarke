---
description: Build and deploy React Code Page web resources to Dataverse
tags: [deploy, code-page, webresource, dataverse, react]
techStack: [react, typescript, webpack, vite, dataverse]
appliesTo: ["**/code-pages/**", "**/solutions/**", "deploy code page", "deploy web resource", "build webresource", "deploy wizard"]
alwaysApply: false
---

# Code Page Deploy

> **Category**: Operations (Tier 3)
> **Last Updated**: February 2026

Build and deploy React Code Page web resources to Dataverse. Code Pages are standalone React 18 dialogs opened via `Xrm.Navigation.navigateTo` — they are NOT PCF controls.

---

## Quick Reference

| Item | Value |
|------|-------|
| Source convention | `src/client/code-pages/{PageName}/` |
| Build tool | Webpack (bundled React 18 + Fluent v9) |
| Inline tool | `build-webresource.ps1` per page |
| Deployable output | `out/sprk_{pagename}.html` (single self-contained HTML) |
| Deploy target | Dataverse web resource (type: Webpage HTML) |

**When to Use**:
- "deploy code page", "deploy web resource", "build webresource"
- After modifying files in `src/client/code-pages/`
- Task tags include `code-page` + `deploy`

**When NOT to Use**:
- Deploying PCF controls → Use `pcf-deploy`
- Deploying BFF API → Use `bff-deploy`
- Deploying plugins → Use `dataverse-deploy`

---

## Path Map — DocumentRelationshipViewer

All paths relative to the repository root. **Claude Code MUST use these exact paths.**

| Purpose | Path |
|---------|------|
| **Code page root** | `src/client/code-pages/DocumentRelationshipViewer/` |
| **Source entry** | `src/client/code-pages/DocumentRelationshipViewer/src/index.tsx` |
| **HTML template** | `src/client/code-pages/DocumentRelationshipViewer/index.html` |
| **Webpack config** | `src/client/code-pages/DocumentRelationshipViewer/webpack.config.js` |
| **Inline script** | `src/client/code-pages/DocumentRelationshipViewer/build-webresource.ps1` |
| **Build bundle** | `src/client/code-pages/DocumentRelationshipViewer/out/bundle.js` |
| **DEPLOYABLE** | `src/client/code-pages/DocumentRelationshipViewer/out/sprk_documentrelationshipviewer.html` |
| **Dataverse name** | `sprk_documentrelationshipviewer` (type: Webpage HTML) |

### Path Map Template (for future Code Pages)

```
src/client/code-pages/{PageName}/
├── src/
│   └── index.tsx                    # React 18 entry point (createRoot)
├── index.html                       # HTML shell template (<script src="bundle.js">)
├── webpack.config.js                # Bundles everything (no externals)
├── package.json                     # npm scripts: "build" = webpack
├── build-webresource.ps1            # Inlines bundle.js into HTML
├── tsconfig.json
├── out/
│   ├── bundle.js                    # Step 1 output (intermediate — NOT deployable alone)
│   ├── bundle.js.LICENSE.txt
│   └── sprk_{pagename}.html         # Step 2 output (DEPLOYABLE — single self-contained file)
└── node_modules/                    # (gitignored)
```

---

## 🚨 Critical: Clear Build Cache Before EVERY Build (MANDATORY)

**Vite and Webpack cache resolved dependencies. If a shared library (`@spaarke/ui-components`, `@spaarke/auth`) was modified, the build cache will contain STALE code. The deployed bundle will silently use old code even though the source files are correct.**

This caused multiple production incidents where route constants, service changes, and auth fixes were NOT included in deployed bundles despite being correct in source.

### Cache Clearing Rules

| Build Tool | Cache Location | Clear Command |
|-----------|---------------|---------------|
| **Vite** (`src/solutions/`) | `node_modules/.vite/`, `.vite/` | `rm -rf dist/ node_modules/.vite/ .vite/` |
| **Webpack** (`src/client/code-pages/`) | Webpack internal cache | `rm -rf out/` |

**MUST clear cache before EVERY build. No exceptions.**

```bash
# Vite-based (src/solutions/)
cd src/solutions/{PageName}
rm -rf dist/ node_modules/.vite/ .vite/
npm run build

# Webpack-based (src/client/code-pages/)
cd src/client/code-pages/{PageName}
rm -rf out/
npm run build
```

### Shared Library Dependency (CRITICAL)

**If ANY files in `@spaarke/ui-components` or `@spaarke/auth` were modified, you MUST recompile those shared libraries BEFORE building code pages.** Vite/Webpack bundle pre-compiled JS from `dist/` — if `dist/` is stale, the code page bundle will contain OLD code.

```bash
# Step 0: Recompile shared libraries FIRST
cd src/client/shared/Spaarke.UI.Components && npm run build
cd src/client/shared/Spaarke.Auth && npm run build

# Step 1: THEN clear cache and build code pages
```

### Verification After Build (MANDATORY)

After building, verify the deployed bundle contains expected changes:

```bash
# Check a known string from your change is in the built output
grep -oP '.{10}your_expected_string.{10}' dist/index.html
# OR for webpack code pages:
grep -oP '.{10}your_expected_string.{10}' out/sprk_{pagename}.html
```

**If the string is NOT found, the cache was stale. Clear and rebuild.**

---

## Two Build Pipelines

This repo has **two types** of code pages with different build tools:

### Type 1: Webpack Code Pages (`src/client/code-pages/`)

Two-step pipeline — both steps required to produce a deployable artifact.

**Step 1: Webpack Build → `out/bundle.js`**

```bash
cd src/client/code-pages/{PageName}
rm -rf out/
npm run build
```

- Bundles all TypeScript, React 18, Fluent v9, and dependencies into `out/bundle.js`
- Unlike PCF controls, Code Pages bundle everything (no platform libraries)
- Output: `out/bundle.js` (~1 MB)

**Step 2: Inline → `out/sprk_{pagename}.html`**

```bash
powershell -File build-webresource.ps1
```

- Reads `index.html` template and `out/bundle.js`
- Replaces `<script src="bundle.js"></script>` with `<script>{inline bundle}</script>`
- Output: `out/sprk_{pagename}.html` (~1 MB, single self-contained file)

**This is the ONLY file that gets deployed to Dataverse.**

**Why Two Steps?** Dataverse web resources are single files. Webpack cannot directly produce an inlined HTML, so `build-webresource.ps1` handles the merge.

### Type 2: Vite Code Pages (`src/solutions/`)

Single-step pipeline — Vite + vite-plugin-singlefile produces a self-contained HTML directly.

```bash
cd src/solutions/{PageName}
rm -rf dist/ node_modules/.vite/ .vite/
npm run build
```

- Output: `dist/index.html` (renamed to `{pagename}.html` by post-build script)
- Single self-contained HTML with all JS/CSS inlined
- Deploy via `Deploy-WizardCodePages.ps1` or `Deploy-CorporateWorkspace.ps1`

| Solution | Deployable File | Deploy Script |
|----------|----------------|---------------|
| CreateMatterWizard | `dist/index.html` → `sprk_creatematterwizard` | `Deploy-WizardCodePages.ps1` |
| CreateProjectWizard | `dist/index.html` → `sprk_createprojectwizard` | `Deploy-WizardCodePages.ps1` |
| CreateEventWizard | `dist/index.html` → `sprk_createeventwizard` | `Deploy-WizardCodePages.ps1` |
| CreateTodoWizard | `dist/index.html` → `sprk_createtodowizard` | `Deploy-WizardCodePages.ps1` |
| CreateWorkAssignmentWizard | `dist/index.html` → `sprk_createworkassignmentwizard` | `Deploy-WizardCodePages.ps1` |
| SummarizeFilesWizard | `dist/index.html` → `sprk_summarizefileswizard` | `Deploy-WizardCodePages.ps1` |
| FindSimilarCodePage | `dist/index.html` → `sprk_findsimilar` | `Deploy-WizardCodePages.ps1` |
| DocumentUploadWizard | `dist/index.html` → `sprk_documentuploadwizard` | `Deploy-WizardCodePages.ps1` |
| PlaybookLibrary | `dist/index.html` → `sprk_playbooklibrary` | `Deploy-WizardCodePages.ps1` |
| LegalWorkspace | `dist/corporateworkspace.html` → `sprk_corporateworkspace` | `Deploy-CorporateWorkspace.ps1` |

---

## Deployment

### Option A: Claude Code Deploys (Full Pipeline)

```bash
# Step 1: Build
cd src/client/code-pages/{PageName}
npm run build

# Step 2: Inline
powershell -File build-webresource.ps1

# Step 3: Upload to Dataverse (if PAC CLI available)
# Note: PAC CLI does not have a direct web resource upload command.
# Use Power Apps maker portal for upload (see Option B).
```

### Option B: Manual Quick Deploy (User-Performed)

When the user wants to deploy manually for fastest iteration:

1. **Build** (from code page directory):
   ```bash
   cd src/client/code-pages/DocumentRelationshipViewer
   npm run build && powershell -File build-webresource.ps1
   ```
2. **Upload**: Open https://make.powerapps.com
   - Navigate to the environment → Solutions → find existing solution containing the web resource
   - Open `sprk_documentrelationshipviewer` web resource
   - Click **Choose File** → select `out/sprk_documentrelationshipviewer.html`
   - **Save** → **Publish**
3. **Verify**: Open Dataverse form with the PCF control, trigger the dialog (e.g., click "Find Similar"), confirm it loads correctly

### When the User Says "I'll deploy manually"

- Ensure **both build steps** have been run (check that `out/sprk_{pagename}.html` exists and has a recent timestamp)
- Tell the user: "Upload `out/sprk_documentrelationshipviewer.html` to the web resource in Power Apps maker portal"
- Provide the exact file path

---

## Anti-Patterns (DO NOT)

- ❌ **DO NOT** build without clearing cache first — Vite/Webpack cache stale shared lib code (this caused multiple production incidents)
- ❌ **DO NOT** skip shared lib recompilation when shared components were modified — stale `dist/` causes silent failures
- ❌ **DO NOT** deploy `index.html` to Dataverse — it references `bundle.js` externally and will not work
- ❌ **DO NOT** deploy `bundle.js` as a separate web resource — Code Pages are single-file HTML
- ❌ **DO NOT** skip `build-webresource.ps1` for webpack code pages — the inline step is mandatory
- ❌ **DO NOT** add Code Page HTML files to a PCF solution ZIP — they are separate web resources
- ❌ **DO NOT** use `out/bundle.js` as the deployable artifact — it's an intermediate build output
- ❌ **DO NOT** assume `Deploy-WizardCodePages.ps1` clears Vite cache — it doesn't; clear manually before running

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Dialog shows blank white page | `build-webresource.ps1` not run (HTML has `<script src="bundle.js">`) | Run `powershell -File build-webresource.ps1` |
| Dialog shows old version | Uploaded old HTML file | Rebuild both steps, re-upload to Dataverse |
| `npm run build` fails | Missing node_modules | Run `npm install` first |
| `build-webresource.ps1` fails "bundle.js not found" | Step 1 not completed | Run `npm run build` first |
| Wrong theme in dialog | `theme` URL parameter not passed | Check PCF's NavigationService passes `theme=light` or `theme=dark` |
| **Shared lib changes not in bundle** | **Vite dependency cache stale** (`node_modules/.vite/`) | `rm -rf dist/ node_modules/.vite/ .vite/` then rebuild. Verify with `grep` on built output. |
| Shared lib changes not in bundle (webpack) | Stale `dist/` in shared lib — `tsc` not run | Recompile shared lib: `cd src/client/shared/Spaarke.UI.Components && npm run build` |
| Route constants missing `/api` prefix | `bffBaseUrl` no longer includes `/api` (normalization) | All route constants must start with `/api/...` |
| `Deploy-WizardCodePages.ps1` deploys stale code | Script rebuilds but doesn't clear Vite cache | Clear cache in EACH solution dir before running the deploy script |

---

## How to Add a New Code Page

1. Create directory: `src/client/code-pages/{NewPageName}/`
2. Copy the template structure from `DocumentRelationshipViewer/`:
   - `index.html` (update `<title>`)
   - `webpack.config.js` (update entry point if needed)
   - `package.json` (update name, adjust dependencies)
   - `build-webresource.ps1` (update `$OutFile` to `sprk_{newpagename}.html`)
   - `tsconfig.json`
3. Create `src/index.tsx` with React 18 `createRoot` entry point
4. Build: `npm install && npm run build && powershell -File build-webresource.ps1`
5. Deploy `out/sprk_{newpagename}.html` to Dataverse as a new web resource

---

## Related Skills

| Skill | When to Use |
|-------|-------------|
| `pcf-deploy` | PCF control deployment (solution ZIP import). Code Pages are NOT part of PCF solutions. |
| `bff-deploy` | BFF API deployment to Azure App Service |
| `dataverse-deploy` | General Dataverse operations (plugins, solution export/import) |

---

## Related ADRs

| ADR | Relevance |
|-----|-----------|
| [ADR-006](../../adr/ADR-006-pcf-over-webresources.md) | Two-tier architecture: PCF for forms, Code Pages for dialogs |
| [ADR-021](../../adr/ADR-021-fluent-design-system.md) | Fluent UI v9, dark mode, semantic tokens |
| [ADR-022](../../adr/ADR-022-pcf-platform-libraries.md) | Code Pages bundle React 18 (not platform-provided) |

---

*For Claude Code: This skill handles Code Page web resource deployment ONLY. For PCF controls, use `pcf-deploy`. For BFF API, use `bff-deploy`.*
