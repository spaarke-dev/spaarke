# Task 1.3: Configure NPM Workspace Linking

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 1 - Project Scaffolding & Foundation
**Estimated Time:** 1 hour
**Prerequisites:** [TASK-1.1-SHARED-LIBRARY-SETUP.md](./TASK-1.1-SHARED-LIBRARY-SETUP.md), [TASK-1.2-PCF-PROJECT-INIT.md](./TASK-1.2-PCF-PROJECT-INIT.md)
**Next Task:** [TASK-1.4-MANIFEST-CONFIGURATION.md](./TASK-1.4-MANIFEST-CONFIGURATION.md)

---

## Objective

Configure NPM workspace to link the PCF control with the shared component library, enabling local development without publishing packages. This allows the PCF control to import from `@spaarke/ui-components` and have changes hot-reload during development.

**Why:** ADR-012 mandates component reusability via shared library. NPM workspaces provide zero-configuration linking for monorepo development.

---

## Critical Standards

**MUST READ BEFORE STARTING:**
- [ADR-012: Shared Component Library](../../../docs/adr/ADR-012-shared-component-library.md)
- [KM-PCF-CONTROL-STANDARDS.md](../../../docs/KM-PCF-CONTROL-STANDARDS.md)

**Key Rules:**
- ✅ Use NPM workspaces (NOT npm link or symlinks)
- ✅ Single `npm install` at root handles all packages
- ✅ Shared library must build before PCF control
- ✅ Use `workspace:*` protocol in dependencies

---

## Step 1: Verify Workspace Configuration

```bash
# Navigate to repository root
cd c:\code_files\spaarke

# Check if root package.json exists
dir package.json
```

**If `package.json` does NOT exist at root**, create it:

```bash
# Create root package.json
npm init -y
```

---

## Step 2: Update Root package.json

**Edit `c:\code_files\spaarke\package.json`:**

```json
{
  "name": "spaarke-workspace",
  "version": "1.0.0",
  "private": true,
  "description": "Spaarke monorepo workspace - BFF API, Shared Components, PCF Controls",
  "workspaces": [
    "src/shared/Spaarke.UI.Components",
    "power-platform/pcf/*"
  ],
  "scripts": {
    "build:shared": "npm run build --workspace=@spaarke/ui-components",
    "build:pcf": "npm run build --workspace=pcf-universal-dataset",
    "build:all": "npm run build:shared && npm run build:pcf",
    "test:shared": "npm test --workspace=@spaarke/ui-components",
    "test:pcf": "npm test --workspace=pcf-universal-dataset",
    "test:all": "npm run test:shared && npm run test:pcf",
    "lint:shared": "npm run lint --workspace=@spaarke/ui-components",
    "lint:pcf": "npm run lint --workspace=pcf-universal-dataset",
    "lint:all": "npm run lint:shared && npm run lint:pcf",
    "clean": "npm run clean --workspaces --if-present"
  },
  "devDependencies": {}
}
```

**Key Points:**
- `workspaces` array includes shared library and PCF controls (wildcard pattern)
- Build scripts run in correct order (shared → PCF)
- All scripts support `--workspace` for targeted execution

---

## Step 3: Add Workspace Dependency to PCF Control

**Edit `c:\code_files\spaarke\power-platform\pcf\UniversalDataset\package.json`:**

Find the `dependencies` section and add:

```json
{
  "dependencies": {
    "@fluentui/react-components": "^9.46.2",
    "@fluentui/react-icons": "^2.0.220",
    "@spaarke/ui-components": "workspace:*",
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    "react-window": "^1.8.10"
  }
}
```

**What `workspace:*` means:**
- NPM will link to local `src/shared/Spaarke.UI.Components/` package
- Changes in shared library immediately available in PCF control
- No need to publish or run `npm link`

---

## Step 4: Install Workspace

```bash
# From repository root
cd c:\code_files\spaarke

# Install all workspace packages and create symlinks
npm install

# Expected output:
# added X packages, and audited Y packages in Zs
# X packages are looking for funding
```

**This command:**
- Installs dependencies for root, shared library, AND PCF control
- Creates symbolic links between workspace packages
- Deduplicates shared dependencies (React, Fluent UI)

---

## Step 5: Verify Workspace Linking

```bash
# Check that PCF control links to shared library
cd c:\code_files\spaarke\power-platform\pcf\UniversalDataset
dir node_modules\@spaarke\ui-components

# Should show: <SYMLINK> or <JUNCTION> pointing to ../../../src/shared/Spaarke.UI.Components
```

**On Windows**, you may see:
```
Directory of c:\code_files\spaarke\power-platform\pcf\UniversalDataset\node_modules\@spaarke

ui-components [..\..\..\..\src\shared\Spaarke.UI.Components]
```

---

## Step 6: Build Shared Library First

```bash
# From repository root
cd c:\code_files\spaarke

# Build shared library (required before PCF can consume it)
npm run build:shared

# Expected output:
# > @spaarke/ui-components@1.0.0 build
# > tsc
# Successfully compiled X files
```

**Verify build output:**
```bash
dir src\shared\Spaarke.UI.Components\dist

# Should see:
# - index.js
# - index.d.ts
# - theme/brand.js
# - theme/brand.d.ts
```

---

## Step 7: Test Import in PCF Control

**Create test file `c:\code_files\spaarke\power-platform\pcf\UniversalDataset\test-import.ts`:**

```typescript
/**
 * Test file to verify workspace linking
 * Delete this file after validation
 */

// This should NOT error if workspace is configured correctly
import { spaarkeLight, spaarkeDark } from "@spaarke/ui-components";

console.log("Shared library import successful!");
console.log("Spaarke Light Theme:", spaarkeLight);
console.log("Spaarke Dark Theme:", spaarkeDark);

export {};
```

**Compile test file:**
```bash
cd c:\code_files\spaarke\power-platform\pcf\UniversalDataset
npx tsc test-import.ts --noEmit

# Expected: No errors
# If you see "Cannot find module '@spaarke/ui-components'", workspace linking failed
```

**Delete test file after verification:**
```bash
del test-import.ts
```

---

## Step 8: Configure PCF Build to Depend on Shared Library

**Edit `c:\code_files\spaarke\power-platform\pcf\UniversalDataset\package.json`:**

Update the `build` script to ensure shared library is built first:

```json
{
  "scripts": {
    "prebuild": "npm run build --workspace=@spaarke/ui-components --if-present",
    "build": "pcf-scripts build",
    "clean": "pcf-scripts clean",
    "rebuild": "pcf-scripts rebuild",
    "start": "pcf-scripts start",
    "start:watch": "pcf-scripts start watch",
    "refreshTypes": "pcf-scripts refreshTypes",
    "lint": "eslint . --ext .ts,.tsx",
    "lint:fix": "eslint . --ext .ts,.tsx --fix",
    "test": "jest --coverage",
    "test:watch": "jest --watch"
  }
}
```

**Key Change:**
- Added `prebuild` script that runs before `build`
- Ensures shared library is compiled before PCF build starts

---

## Step 9: Test Full Build

```bash
# From repository root
cd c:\code_files\spaarke

# Build everything in correct order
npm run build:all

# Expected output:
# > @spaarke/ui-components@1.0.0 build
# > tsc
# Successfully compiled X files
#
# > pcf-universal-dataset@1.0.0 build
# > pcf-scripts build
# Build succeeded
```

---

## Step 10: Test Watch Mode (Optional but Recommended)

**Terminal 1 - Watch shared library:**
```bash
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
npm run build:watch

# Should output: "Watching for file changes..."
```

**Terminal 2 - Watch PCF control:**
```bash
cd c:\code_files\spaarke\power-platform\pcf\UniversalDataset
npm run start:watch

# Should output: PCF test harness running at http://localhost:8181
```

**Test hot reload:**
1. Edit `src/shared/Spaarke.UI.Components/src/theme/brand.ts`
2. Save file
3. Shared library should recompile automatically
4. PCF control should detect change and rebuild

**Stop watch mode:** Press `Ctrl+C` in both terminals when done testing.

---

## Validation Checklist

Execute these commands to verify task completion:

```bash
# 1. Verify workspace configured at root
cd c:\code_files\spaarke
type package.json | findstr "workspaces"
# Should show: "workspaces": [...]

# 2. Verify PCF has workspace dependency
cd power-platform\pcf\UniversalDataset
type package.json | findstr "@spaarke/ui-components"
# Should show: "@spaarke/ui-components": "workspace:*"

# 3. Verify symlink exists
dir node_modules\@spaarke\ui-components
# Should show SYMLINK or JUNCTION

# 4. Verify shared library compiled
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
dir dist\index.js
# Should exist

# 5. Verify PCF control builds
cd c:\code_files\spaarke\power-platform\pcf\UniversalDataset
npm run build
# Should succeed

# 6. Verify import works
cd c:\code_files\spaarke\power-platform\pcf\UniversalDataset
node -e "const { spaarkeLight } = require('../../src/shared/Spaarke.UI.Components/dist'); console.log('Import OK:', !!spaarkeLight);"
# Should print: "Import OK: true"
```

---

## Success Criteria

- ✅ Root `package.json` has workspaces configured
- ✅ PCF control has `@spaarke/ui-components` workspace dependency
- ✅ Symbolic link exists at `node_modules/@spaarke/ui-components`
- ✅ Shared library builds successfully
- ✅ PCF control builds successfully
- ✅ Test import compiles without errors
- ✅ Build scripts run in correct order (shared → PCF)
- ✅ Watch mode supports hot reload (optional)

---

## Deliverables

**Files Updated:**
1. `c:\code_files\spaarke\package.json` (workspace configuration)
2. `c:\code_files\spaarke\power-platform\pcf\UniversalDataset\package.json` (workspace dependency)

**Build Output:**
- `src/shared/Spaarke.UI.Components/dist/` (compiled shared library)
- `power-platform/pcf/UniversalDataset/out/` (compiled PCF control)

**Workspace Structure:**
```
c:\code_files\spaarke\
├── package.json (workspace root)
├── node_modules/ (shared dependencies)
├── src/shared/Spaarke.UI.Components/ (workspace package 1)
│   ├── package.json
│   ├── dist/ (build output)
│   └── node_modules/ (package-specific deps)
└── power-platform/pcf/UniversalDataset/ (workspace package 2)
    ├── package.json
    ├── out/ (build output)
    └── node_modules/
        └── @spaarke/ui-components/ → SYMLINK to ../../../../src/shared/Spaarke.UI.Components
```

---

## Common Issues & Solutions

**Issue:** `npm install` fails with "ERESOLVE unable to resolve dependency tree"
**Solution:** Use `npm install --legacy-peer-deps` if PCF tools have peer dependency conflicts

**Issue:** Symlink not created (no `@spaarke` folder in `node_modules`)
**Solution:**
1. Delete `node_modules` and `package-lock.json` at root
2. Run `npm install` again from root
3. Ensure workspace paths in root `package.json` are correct

**Issue:** TypeScript still can't find `@spaarke/ui-components`
**Solution:**
1. Verify `tsconfig.json` has correct path mapping (TASK-1.2)
2. Run `npm run build:shared` to ensure dist/ exists
3. Restart TypeScript language server (VS Code: Ctrl+Shift+P → "TypeScript: Restart TS Server")

**Issue:** Changes in shared library not reflected in PCF control
**Solution:**
1. Rebuild shared library: `npm run build:shared`
2. PCF automatically picks up changes from dist/ folder
3. For instant feedback, use watch mode in both terminals

**Issue:** Build fails with "Cannot find '@spaarke/ui-components/dist'"
**Solution:** Shared library must be built BEFORE PCF control. Use `npm run build:all` from root.

---

## Next Steps

After completing this task:
1. Proceed to [TASK-1.4-MANIFEST-CONFIGURATION.md](./TASK-1.4-MANIFEST-CONFIGURATION.md)
2. The PCF control can now import and use components from the shared library

---

**Task Status:** Ready for Execution
**Estimated Time:** 1 hour
**Actual Time:** _________ (fill in after completion)
**Completed By:** _________ (developer name)
**Date:** _________ (completion date)
