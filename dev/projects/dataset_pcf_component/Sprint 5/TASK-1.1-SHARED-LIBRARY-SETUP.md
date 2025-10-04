# Task 1.1: Create Shared Component Library

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Phase:** 1 - Project Scaffolding & Foundation
**Estimated Time:** 3 hours
**Prerequisites:** None
**Next Task:** [TASK-1.2-PCF-PROJECT-INIT.md](./TASK-1.2-PCF-PROJECT-INIT.md)

---

## Objective

Set up the shared React/TypeScript component library at `src/shared/Spaarke.UI.Components/` that will be consumed by the PCF control and future React SPA.

**Why:** ADR-012 mandates component reusability. We build components once in the shared library and import them into PCF controls, avoiding duplication.

---

## Critical Standards

**MUST READ BEFORE STARTING:**
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md) - Fluent UI v9 strict requirements
- [ADR-012: Shared Component Library](../../../docs/adr/ADR-012-shared-component-library.md)

**Key Rules:**
- ✅ Use `@fluentui/react-components` v9 ONLY (never v8)
- ✅ Use Griffel for all styling (`makeStyles`, `tokens`)
- ✅ All components must be generic (no entity-specific logic)

---

## Step 1: Create Directory Structure

```bash
# Navigate to shared libraries directory
cd c:\code_files\spaarke\src\shared

# Create shared component library directory
mkdir Spaarke.UI.Components
cd Spaarke.UI.Components

# Create source directories
mkdir -p src/components
mkdir -p src/hooks
mkdir -p src/services
mkdir -p src/renderers
mkdir -p src/types
mkdir -p src/theme
mkdir -p src/utils
mkdir -p __tests__/components
mkdir -p __tests__/hooks
mkdir -p __tests__/utils
```

**Validation:**
```bash
# Verify directory structure
ls -la src
# Should see: components, hooks, services, renderers, types, theme, utils
```

---

## Step 2: Initialize NPM Package

```bash
# Still in Spaarke.UI.Components directory
npm init -y
```

**Expected Output:**
```
Wrote to c:\code_files\spaarke\src\shared\Spaarke.UI.Components\package.json
```

---

## Step 3: Configure package.json

**Replace** the generated `package.json` with:

```json
{
  "name": "@spaarke/ui-components",
  "version": "1.0.0",
  "description": "Shared React/TypeScript component library for Spaarke - used by PCF controls, React SPA, and Office Add-ins",
  "main": "dist/index.js",
  "types": "dist/index.d.ts",
  "scripts": {
    "build": "tsc",
    "build:watch": "tsc --watch",
    "test": "jest --coverage",
    "test:watch": "jest --watch",
    "lint": "eslint src --ext .ts,.tsx",
    "lint:fix": "eslint src --ext .ts,.tsx --fix"
  },
  "keywords": [
    "spaarke",
    "fluent-ui",
    "react",
    "typescript",
    "components"
  ],
  "author": "Spaarke Engineering Team",
  "license": "UNLICENSED",
  "private": true,
  "peerDependencies": {
    "@fluentui/react-components": "^9.46.2",
    "@fluentui/react-icons": "^2.0.220",
    "react": "^18.2.0",
    "react-dom": "^18.2.0"
  },
  "devDependencies": {
    "@types/react": "^18.2.0",
    "@types/react-dom": "^18.2.0",
    "@testing-library/react": "^14.0.0",
    "@testing-library/jest-dom": "^6.1.5",
    "typescript": "^5.3.3",
    "jest": "^29.7.0",
    "eslint": "^9.17.0",
    "@typescript-eslint/eslint-plugin": "^6.0.0",
    "@typescript-eslint/parser": "^6.0.0"
  }
}
```

**Key Points:**
- Package name: `@spaarke/ui-components` (scoped package)
- Peer dependencies: Fluent UI v9, React 18 (consumers must provide)
- Private: true (not published to public npm)

---

## Step 4: Create TypeScript Configuration

**Create `tsconfig.json`:**

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "jsx": "react",
    "declaration": true,
    "declarationMap": true,
    "sourceMap": true,
    "outDir": "./dist",
    "rootDir": "./src",
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
    "allowSyntheticDefaultImports": true
  },
  "include": ["src/**/*"],
  "exclude": ["node_modules", "dist", "__tests__", "**/*.test.ts", "**/*.test.tsx"]
}
```

**Key Settings:**
- Strict mode enabled (catches type errors)
- Declaration maps for debugging
- ES2020 target (modern browsers)

---

## Step 5: Create ESLint Configuration

**Create `.eslintrc.json`:**

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
    "react/prop-types": "off"
  },
  "settings": {
    "react": {
      "version": "detect"
    }
  }
}
```

---

## Step 6: Create Spaarke Brand Theme

**Create `src/theme/brand.ts`:**

```typescript
/**
 * Spaarke Brand Theme
 * Based on Fluent UI v9 design tokens
 *
 * Reference: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */

import {
  BrandVariants,
  createLightTheme,
  createDarkTheme,
  Theme
} from "@fluentui/react-components";

/**
 * Spaarke 16-stop brand color ramp
 * Centralized color definition - NEVER hard-code these values elsewhere
 */
export const spaarkeBrand: BrandVariants = {
  10: "#020305",
  20: "#0b1a33",
  30: "#102a52",
  40: "#14386c",
  50: "#184787",
  60: "#1c56a2",
  70: "#1f64bc",
  80: "#2173d7",
  90: "#2683f2",
  100: "#4a98ff",
  110: "#73adff",
  120: "#99c1ff",
  130: "#b9d3ff",
  140: "#d2e2ff",
  150: "#e6eeff",
  160: "#f3f7ff"
};

/**
 * Spaarke Light Theme
 * Use this as default theme for Spaarke applications
 */
export const spaarkeLight: Theme = createLightTheme(spaarkeBrand);

/**
 * Spaarke Dark Theme
 * Use this for dark mode support
 */
export const spaarkeDark: Theme = createDarkTheme(spaarkeBrand);
```

**Create `src/theme/index.ts`:**

```typescript
export { spaarkeBrand, spaarkeLight, spaarkeDark } from "./brand";
```

---

## Step 7: Create Barrel Exports

**Create `src/index.ts` (main entry point):**

```typescript
/**
 * @spaarke/ui-components
 * Shared React/TypeScript component library for Spaarke
 *
 * Used by:
 * - PCF Controls (model-driven apps, custom pages)
 * - React SPA (future)
 * - Office Add-ins (future)
 */

// Export theme first (most commonly used)
export * from "./theme";

// Export components
export * from "./components";

// Export hooks
export * from "./hooks";

// Export services
export * from "./services";

// Export renderers
export * from "./renderers";

// Export types
export * from "./types";

// Export utilities
export * from "./utils";
```

**Create placeholder barrel exports for each directory:**

**`src/components/index.ts`:**
```typescript
// Components will be added in Phase 2
// Example: export { DataGrid } from "./DataGrid";
```

**`src/hooks/index.ts`:**
```typescript
// Hooks will be added in Phase 2
// Example: export { usePagination } from "./usePagination";
```

**`src/services/index.ts`:**
```typescript
// Services will be added in Phase 3
```

**`src/renderers/index.ts`:**
```typescript
// Renderers will be added in Phase 3
```

**`src/types/index.ts`:**
```typescript
// Common types
```

**`src/utils/index.ts`:**
```typescript
// Utilities will be added in Phase 2
```

---

## Step 8: Install Dependencies

```bash
# Install dev dependencies
npm install --save-dev \
  @types/react@^18.2.0 \
  @types/react-dom@^18.2.0 \
  @testing-library/react@^14.0.0 \
  @testing-library/jest-dom@^6.1.5 \
  typescript@^5.3.3 \
  jest@^29.7.0 \
  eslint@^9.17.0 \
  @typescript-eslint/eslint-plugin@^6.0.0 \
  @typescript-eslint/parser@^6.0.0

# Note: Peer dependencies will be provided by consumers
# (Fluent UI v9, React 18)
```

**Expected Output:**
```
added XXX packages in Xs
```

---

## Step 9: Configure NPM Workspace (Root Level)

```bash
# Navigate to repository root
cd c:\code_files\spaarke
```

**Create or update root `package.json`:**

```json
{
  "name": "spaarke-workspace",
  "version": "1.0.0",
  "private": true,
  "description": "Spaarke monorepo workspace",
  "workspaces": [
    "src/shared/Spaarke.UI.Components",
    "power-platform/pcf/*"
  ],
  "scripts": {
    "build:shared": "npm run build --workspace=@spaarke/ui-components",
    "test:shared": "npm test --workspace=@spaarke/ui-components",
    "lint:shared": "npm run lint --workspace=@spaarke/ui-components"
  }
}
```

**Install workspace:**
```bash
npm install
```

**Expected Output:**
```
npm WARN using --force Recommended protections disabled.
added 1 package, and audited XXX packages in Xs
```

---

## Step 10: Create README

**Create `src/shared/Spaarke.UI.Components/README.md`:**

```markdown
# @spaarke/ui-components

Shared React/TypeScript component library for Spaarke platform.

## Purpose

Provides reusable UI components, hooks, and utilities for:
- PCF Controls (Power Platform)
- React SPA (future)
- Office Add-ins (future)

## Architecture

Follows **ADR-012: Shared Component Library**
- Single source of truth for UI components
- Generic, configurable components (no entity-specific logic)
- Fluent UI v9 exclusively (ZERO v8 dependencies)
- Griffel for styling (tokens-based, no hard-coded values)

## Usage

### In PCF Control
```typescript
import { DataGrid, spaarkeLight } from "@spaarke/ui-components";
import { FluentProvider } from "@fluentui/react-components";

<FluentProvider theme={spaarkeLight}>
  <DataGrid items={data} columns={columns} />
</FluentProvider>
```

### In React SPA (future)
```typescript
import { DataGrid, CommandBar } from "@spaarke/ui-components";
```

## Development

```bash
# Build
npm run build

# Watch mode
npm run build:watch

# Test
npm test

# Lint
npm run lint
```

## Standards

**CRITICAL - Read before contributing:**
- [KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md)
- [ADR-012: Shared Component Library](../../../docs/adr/ADR-012-shared-component-library.md)

## Structure

```
src/
├── components/    # Reusable React components
├── hooks/         # Custom React hooks
├── services/      # Business logic services
├── renderers/     # Column renderers
├── types/         # TypeScript types
├── theme/         # Fluent UI themes
└── utils/         # Utility functions
```

## Version

1.0.0 - Sprint 5 (2025-10-03)
```

---

## Validation Checklist

Execute these commands to verify task completion:

```bash
# 1. Directory structure exists
cd c:\code_files\spaarke\src\shared\Spaarke.UI.Components
ls src
# Should see: components, hooks, services, renderers, types, theme, utils

# 2. Package.json is valid
cat package.json | grep "@spaarke/ui-components"
# Should show package name

# 3. TypeScript compiles (empty project)
npm run build
# Should succeed with: "Successfully compiled X files"

# 4. Workspace is configured
cd c:\code_files\spaarke
cat package.json | grep "workspaces"
# Should show workspace configuration

# 5. Theme exports correctly
cd src/shared/Spaarke.UI.Components
node -e "const { spaarkeLight } = require('./dist/theme'); console.log('Theme loaded:', !!spaarkeLight);"
# Should print: "Theme loaded: true"
```

---

## Success Criteria

- ✅ Directory `src/shared/Spaarke.UI.Components/` exists
- ✅ Package name is `@spaarke/ui-components`
- ✅ TypeScript builds successfully (0 errors)
- ✅ Spaarke brand theme created with 16-stop ramp
- ✅ NPM workspace configured at root
- ✅ No Fluent UI v8 dependencies
- ✅ README documents purpose and usage

---

## Deliverables

**Files Created:**
1. `src/shared/Spaarke.UI.Components/package.json`
2. `src/shared/Spaarke.UI.Components/tsconfig.json`
3. `src/shared/Spaarke.UI.Components/.eslintrc.json`
4. `src/shared/Spaarke.UI.Components/src/index.ts`
5. `src/shared/Spaarke.UI.Components/src/theme/brand.ts`
6. `src/shared/Spaarke.UI.Components/src/theme/index.ts`
7. `src/shared/Spaarke.UI.Components/README.md`
8. Barrel exports for each src/ subdirectory
9. Root `package.json` with workspace configuration

**Build Output:**
- `dist/index.js` (compiled JavaScript)
- `dist/index.d.ts` (TypeScript declarations)
- `dist/theme/brand.js`
- `dist/theme/brand.d.ts`

---

## Common Issues & Solutions

**Issue:** `npm install` fails with peer dependency warnings
**Solution:** This is expected - peer dependencies will be provided by consumers (PCF control)

**Issue:** TypeScript compilation fails
**Solution:** Ensure all barrel export files exist (even if empty)

**Issue:** Workspace not linking
**Solution:** Run `npm install` from repository root, not subdirectory

---

## Next Steps

After completing this task:
1. Proceed to [TASK-1.2-PCF-PROJECT-INIT.md](./TASK-1.2-PCF-PROJECT-INIT.md)
2. The shared library is now ready to receive components in Phase 2

---

**Task Status:** Ready for Execution
**Estimated Time:** 3 hours
**Actual Time:** _________ (fill in after completion)
**Completed By:** _________ (developer name)
**Date:** _________ (completion date)
