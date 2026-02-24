---
description: Build and deploy React Code Page web resources to Dataverse
tags: [deploy, code-page, webresource, dataverse, react]
techStack: [react, typescript, webpack, dataverse]
appliesTo: ["**/code-pages/**", "deploy code page", "deploy web resource", "build webresource"]
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

## Two-Step Build Pipeline (MANDATORY)

Code Pages require **both steps** to produce a deployable artifact. Skipping Step 2 means the code page cannot be deployed.

### Step 1: Webpack Build → `out/bundle.js`

```bash
cd src/client/code-pages/{PageName}
npm run build
```

- Bundles all TypeScript, React 18, Fluent v9, and dependencies into `out/bundle.js`
- Unlike PCF controls, Code Pages bundle everything (no platform libraries)
- Output: `out/bundle.js` (~1 MB)

### Step 2: Inline → `out/sprk_{pagename}.html`

```bash
powershell -File build-webresource.ps1
```

- Reads `index.html` template and `out/bundle.js`
- Replaces `<script src="bundle.js"></script>` with `<script>{inline bundle}</script>`
- Output: `out/sprk_{pagename}.html` (~1 MB, single self-contained file)

**This is the ONLY file that gets deployed to Dataverse.**

### Why Two Steps?

Dataverse web resources are single files. The convention for this repo is one self-contained HTML file with all JS inlined — matching how other code pages are built. Webpack cannot directly produce an inlined HTML, so `build-webresource.ps1` handles the merge.

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

- ❌ **DO NOT** deploy `index.html` to Dataverse — it references `bundle.js` externally and will not work
- ❌ **DO NOT** deploy `bundle.js` as a separate web resource — Code Pages are single-file HTML
- ❌ **DO NOT** skip `build-webresource.ps1` — the inline step is mandatory
- ❌ **DO NOT** add Code Page HTML files to a PCF solution ZIP — they are separate web resources
- ❌ **DO NOT** use `out/bundle.js` as the deployable artifact — it's an intermediate build output

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| Dialog shows blank white page | `build-webresource.ps1` not run (HTML has `<script src="bundle.js">`) | Run `powershell -File build-webresource.ps1` |
| Dialog shows old version | Uploaded old HTML file | Rebuild both steps, re-upload to Dataverse |
| `npm run build` fails | Missing node_modules | Run `npm install` first |
| `build-webresource.ps1` fails "bundle.js not found" | Step 1 not completed | Run `npm run build` first |
| Wrong theme in dialog | `theme` URL parameter not passed | Check PCF's NavigationService passes `theme=light` or `theme=dark` |

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
