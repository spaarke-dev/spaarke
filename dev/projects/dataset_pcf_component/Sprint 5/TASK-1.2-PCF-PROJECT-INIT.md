# Task 1.2: Initialize PCF Dataset Control Project

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 1 - Project Scaffolding & Foundation
**Estimated Time:** 2 hours
**Prerequisites:** [TASK-1.1-SHARED-LIBRARY-SETUP.md](./TASK-1.1-SHARED-LIBRARY-SETUP.md)
**Next Task:** [TASK-1.3-WORKSPACE-LINKING.md](./TASK-1.3-WORKSPACE-LINKING.md)

---

## Objective

Initialize the PCF control project using Power Platform CLI, configure it for React/TypeScript dataset development, and establish the foundation for the Universal Dataset component.

**Why:** PCF projects require specific scaffolding and configuration. We use the dataset template as the starting point and configure it to work with our shared component library.

---

## Critical Standards

**MUST READ BEFORE STARTING:**
- [KM-PCF-CONTROL-STANDARDS.md](../../../docs/KM-PCF-CONTROL-STANDARDS.md) - PCF project setup, lifecycle, manifest
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) - React/TypeScript standards

**Key Rules:**
- ✅ Use `pac pcf init` with dataset template
- ✅ Configure for React 18 + TypeScript 5
- ✅ Enable ESLint strict mode
- ✅ No DOM manipulation in PCF code

---

## Step 1: Verify Power Platform CLI

```bash
# Check if pac CLI is installed
pac --version

# Expected output: Microsoft PowerApps CLI Version: 1.x.x
```

**If not installed:**
```bash
# Install Power Platform CLI
dotnet tool install --global Microsoft.PowerApps.CLI.Tool
```

---

## Step 2: Create PCF Project Directory

```bash
# Navigate to PCF projects directory
cd c:\code_files\spaarke\power-platform

# Create pcf directory if it doesn't exist
mkdir pcf
cd pcf
```

---

## Step 3: Initialize PCF Dataset Control

```bash
# Initialize PCF project with dataset template
pac pcf init --namespace Spaarke --name UniversalDataset --template dataset --run-npm-install

# Wait for initialization to complete
# Expected output: "PCF project created successfully"
```

**What this creates:**
- `UniversalDataset/` directory
- `ControlManifest.Input.xml` (manifest file)
- `index.ts` (control entry point)
- `package.json` (dependencies)
- `tsconfig.json` (TypeScript config)
- `.eslintrc.json` (linting config)

---

## Step 4: Navigate to Project

```bash
cd UniversalDataset
```

---

## Step 5: Update package.json

Replace the generated `package.json` with enhanced configuration:

```json
{
  "name": "pcf-universal-dataset",
  "version": "1.0.0",
  "description": "Universal Dataset PCF Control for Spaarke - Configurable grid/card/list views for any Dataverse entity",
  "main": "index.ts",
  "scripts": {
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
  },
  "keywords": [
    "spaarke",
    "pcf",
    "dataset",
    "dataverse",
    "fluent-ui",
    "react"
  ],
  "author": "Spaarke Engineering Team",
  "license": "UNLICENSED",
  "dependencies": {
    "@fluentui/react-components": "^9.46.2",
    "@fluentui/react-icons": "^2.0.220",
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    "react-window": "^1.8.10"
  },
  "devDependencies": {
    "@types/node": "^18.16.0",
    "@types/powerapps-component-framework": "^1.3.4",
    "@types/react": "^18.2.0",
    "@types/react-dom": "^18.2.0",
    "@types/react-window": "^1.8.8",
    "@typescript-eslint/eslint-plugin": "^6.0.0",
    "@typescript-eslint/parser": "^6.0.0",
    "eslint": "^9.17.0",
    "eslint-plugin-react": "^7.33.0",
    "eslint-plugin-react-hooks": "^4.6.0",
    "pcf-scripts": "^1.30.0",
    "pcf-start": "^1.30.0",
    "typescript": "^5.3.3",
    "jest": "^29.7.0",
    "@testing-library/react": "^14.0.0",
    "@testing-library/jest-dom": "^6.1.5"
  }
}
```

**Key Changes:**
- Added Fluent UI v9 dependencies
- Added React 18.2.0
- Added react-window for virtualization
- Added testing dependencies
- Added lint scripts

---

## Step 6: Update TypeScript Configuration

Replace `tsconfig.json` with enhanced configuration:

```json
{
  "extends": "./node_modules/pcf-scripts/tsconfig_base.json",
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "jsx": "react",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noImplicitReturns": true,
    "noFallthroughCasesInSwitch": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "forceConsistentCasingInFileNames": true,
    "moduleResolution": "node",
    "resolveJsonModule": true,
    "isolatedModules": true,
    "allowSyntheticDefaultImports": true,
    "sourceMap": true,
    "outDir": "./out",
    "baseUrl": ".",
    "paths": {
      "@spaarke/ui-components": ["../../src/shared/Spaarke.UI.Components/src/index"]
    }
  },
  "include": ["index.ts", "**/*.ts", "**/*.tsx"],
  "exclude": ["node_modules", "out"]
}
```

**Key Settings:**
- React JSX support
- Strict mode enabled
- Path mapping to shared library
- ES2020 target

---

## Step 7: Update ESLint Configuration

Replace `.eslintrc.json`:

```json
{
  "parser": "@typescript-eslint/parser",
  "extends": [
    "eslint:recommended",
    "plugin:@typescript-eslint/recommended",
    "plugin:react/recommended",
    "plugin:react-hooks/recommended"
  ],
  "parserOptions": {
    "ecmaVersion": 2020,
    "sourceType": "module",
    "ecmaFeatures": {
      "jsx": true
    }
  },
  "rules": {
    "@typescript-eslint/no-explicit-any": "warn",
    "@typescript-eslint/explicit-module-boundary-types": "off",
    "react/react-in-jsx-scope": "off",
    "react/prop-types": "off",
    "no-console": ["warn", { "allow": ["warn", "error"] }]
  },
  "settings": {
    "react": {
      "version": "detect"
    }
  },
  "env": {
    "browser": true,
    "es2020": true,
    "node": true
  }
}
```

---

## Step 8: Install Dependencies

```bash
# Install all dependencies
npm install

# Expected output: "added XXX packages in Xs"
```

**Wait for completion** - this may take 2-3 minutes.

---

## Step 9: Create Component Structure

```bash
# Create component directories
mkdir components
mkdir hooks
mkdir services
mkdir types
mkdir utils
```

**Directory Structure:**
```
UniversalDataset/
├── components/     # React UI components
├── hooks/          # Custom hooks (useDatasetMode, useHeadlessMode)
├── services/       # Business logic (CommandExecutor, EntityConfiguration)
├── types/          # TypeScript interfaces
├── utils/          # Helper functions
├── index.ts        # PCF entry point
└── ControlManifest.Input.xml
```

---

## Step 10: Create Placeholder index.ts

**Replace** the generated `index.ts` with a clean starting point:

```typescript
/**
 * Universal Dataset PCF Control for Spaarke
 * Configurable dataset grid/card/list component supporting any Dataverse entity
 *
 * Standards:
 * - KM-PCF-CONTROL-STANDARDS.md
 * - KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 * - ADR-012: Shared Component Library
 */

import { IInputs, IOutputs } from "./generated/ManifestTypes";
import * as React from "react";
import * as ReactDOM from "react-dom";

export class UniversalDataset implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement;
  private context: ComponentFramework.Context<IInputs>;
  private notifyOutputChanged: () => void;

  /**
   * PCF Lifecycle: Initialize
   * Called once when control is loaded
   */
  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.context = context;
    this.notifyOutputChanged = notifyOutputChanged;
    this.container = container;

    console.log("UniversalDataset: Initialized");
  }

  /**
   * PCF Lifecycle: Update View
   * Called when data changes or control is resized
   */
  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Placeholder: Will render React component in Phase 2
    const placeholderElement = React.createElement(
      "div",
      { style: { padding: "16px", fontFamily: "Segoe UI" } },
      "Universal Dataset Control - Ready for Phase 2 Implementation"
    );

    ReactDOM.render(placeholderElement, this.container);
  }

  /**
   * PCF Lifecycle: Get Outputs
   * Return values to Power Platform (e.g., selected records, last action)
   */
  public getOutputs(): IOutputs {
    return {};
  }

  /**
   * PCF Lifecycle: Destroy
   * Called when control is removed from DOM
   */
  public destroy(): void {
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
```

---

## Validation Checklist

Execute these commands to verify task completion:

```bash
# 1. Verify project structure
cd c:\code_files\spaarke\power-platform\pcf\UniversalDataset
dir

# Should see:
# - components/
# - hooks/
# - services/
# - index.ts
# - ControlManifest.Input.xml
# - package.json
# - tsconfig.json

# 2. Verify dependencies installed
dir node_modules\@fluentui\react-components
# Should exist

# 3. Verify TypeScript compiles
npm run build

# Expected output: "Build succeeded"

# 4. Verify linting works
npm run lint

# Should complete with 0 errors (warnings OK for placeholder code)

# 5. Verify PCF types generated
dir generated\ManifestTypes.d.ts
# Should exist
```

---

## Success Criteria

- ✅ PCF project created at `power-platform/pcf/UniversalDataset/`
- ✅ Fluent UI v9 dependencies installed
- ✅ React 18 + TypeScript 5 configured
- ✅ Build succeeds with `npm run build`
- ✅ Directory structure created (components, hooks, services, types, utils)
- ✅ No Fluent UI v8 dependencies
- ✅ ESLint configured with React rules
- ✅ Placeholder index.ts renders without errors

---

## Deliverables

**Files Created:**
1. `power-platform/pcf/UniversalDataset/package.json` (updated)
2. `power-platform/pcf/UniversalDataset/tsconfig.json` (updated)
3. `power-platform/pcf/UniversalDataset/.eslintrc.json` (updated)
4. `power-platform/pcf/UniversalDataset/index.ts` (placeholder)
5. `power-platform/pcf/UniversalDataset/components/` (directory)
6. `power-platform/pcf/UniversalDataset/hooks/` (directory)
7. `power-platform/pcf/UniversalDataset/services/` (directory)
8. `power-platform/pcf/UniversalDataset/types/` (directory)
9. `power-platform/pcf/UniversalDataset/utils/` (directory)

**Build Output:**
- `out/` directory with compiled JavaScript
- `generated/ManifestTypes.d.ts` (type definitions)

---

## Common Issues & Solutions

**Issue:** `pac command not found`
**Solution:** Install Power Platform CLI: `dotnet tool install --global Microsoft.PowerApps.CLI.Tool`

**Issue:** `npm install` fails with ERESOLVE errors
**Solution:** Use `npm install --legacy-peer-deps` (PCF tools may have peer dependency conflicts)

**Issue:** TypeScript compilation fails with "Cannot find module '@spaarke/ui-components'"
**Solution:** This is expected until TASK-1.3 links the workspace. Ignore for now.

**Issue:** Build fails with "Cannot find name 'React'"
**Solution:** Ensure `@types/react` is installed: `npm install --save-dev @types/react@^18.2.0`

---

## Next Steps

After completing this task:
1. Proceed to [TASK-1.3-WORKSPACE-LINKING.md](./TASK-1.3-WORKSPACE-LINKING.md)
2. The PCF project is now ready to link with the shared component library

---

**Task Status:** Ready for Execution
**Estimated Time:** 2 hours
**Actual Time:** _________ (fill in after completion)
**Completed By:** _________ (developer name)
**Date:** _________ (completion date)
