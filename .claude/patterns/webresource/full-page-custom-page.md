# Full-Page Custom Page Template

> **Category**: Web Resource / Custom Page
> **Last Updated**: 2026-02-18
> **Source ADRs**: ADR-006, ADR-021, ADR-022, ADR-026

> **CRITICAL**: Full-page surfaces MUST use this pattern, NOT PCF controls. PCF platform-library injects React 16 — `createRoot` and `useId()` will fail at runtime.

---

## When to Use

Building a **full navigation page** or **side pane** hosted in a Dataverse Model-Driven App. The output is a single self-contained HTML file deployed as a Dataverse web resource.

**Use PCF instead** if the UI is embedded in a form (field, subgrid, tab).

---

## Project Structure

```
src/solutions/{PageName}/
├── index.html              # Entry shell — CSS reset + #root div
├── package.json            # React 18, Fluent v9, Vite (see template below)
├── vite.config.ts          # Vite + viteSingleFile (identical for all pages)
├── tsconfig.json           # TypeScript strict (identical for all pages)
├── tsconfig.node.json      # Vite config TS (identical for all pages)
├── dist/                   # Build output (gitignored)
│   └── index.html          # ← Single file to deploy
└── src/
    ├── main.tsx            # React 18 createRoot entry
    ├── App.tsx             # FluentProvider + theme + page content
    ├── components/         # Page-specific components
    ├── hooks/              # Custom hooks (data fetching, BFF API calls)
    ├── services/           # Xrm.WebApi wrappers, FetchXML helpers
    ├── providers/
    │   └── ThemeProvider.ts  # ADR-021 theme detection (reusable — copy from EventsPage)
    └── config/
        └── {page}Config.ts   # Page-specific constants (view GUIDs, entity names)
```

---

## Template Files (Copy-Paste Ready)

### package.json

```json
{
  "name": "{page-name}",
  "version": "1.0.0",
  "description": "{Page description}",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "vite build",
    "preview": "vite preview",
    "lint": "eslint src --ext .ts,.tsx",
    "lint:fix": "eslint src --ext .ts,.tsx --fix"
  },
  "dependencies": {
    "@fluentui/react-components": "^9.46.2",
    "@fluentui/react-icons": "^2.0.225"
  },
  "devDependencies": {
    "@types/react": "^18.3.27",
    "@types/react-dom": "^18.3.7",
    "@vitejs/plugin-react": "^4.2.1",
    "react": "^18.3.1",
    "react-dom": "^18.3.1",
    "typescript": "^5.0.0",
    "vite": "^5.0.0",
    "vite-plugin-singlefile": "^2.3.0"
  },
  "peerDependencies": {
    "react": "^16.14.0 || ^17.0.0 || ^18.0.0",
    "react-dom": "^16.14.0 || ^17.0.0 || ^18.0.0"
  }
}
```

**Key decisions**:
- React/react-dom in `devDependencies` — bundled by Vite, not installed at runtime
- Fluent UI in `dependencies` — also bundled, but semantically a runtime dependency
- `peerDependencies` — broad range for library consumers (shared components)

---

### vite.config.ts (Identical for all pages)

```typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";

export default defineConfig({
  plugins: [
    react(),
    viteSingleFile(),
  ],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  build: {
    outDir: "dist",
    sourcemap: false,
    assetsInlineLimit: 100000000,
    minify: true,
    rollupOptions: {
      output: {
        manualChunks: undefined,
      },
    },
  },
  base: "./",
});
```

**Key decisions**:
- `viteSingleFile()` — inlines ALL JS/CSS/assets into one HTML file
- `assetsInlineLimit: 100000000` — forces everything inline (no separate asset files)
- `manualChunks: undefined` — disables code splitting (single bundle required)
- `sourcemap: false` — useless when inlined into HTML
- `base: "./"` — relative paths for Dataverse web resource URL resolution

---

### tsconfig.json (Identical for all pages)

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "useDefineForClassFields": true,
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "skipLibCheck": true,
    "moduleResolution": "bundler",
    "allowImportingTsExtensions": true,
    "resolveJsonModule": true,
    "isolatedModules": true,
    "noEmit": true,
    "jsx": "react",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true,
    "baseUrl": ".",
    "paths": {
      "@/*": ["src/*"]
    }
  },
  "include": ["src"],
  "references": [{ "path": "./tsconfig.node.json" }]
}
```

### tsconfig.node.json (Identical for all pages)

```json
{
  "compilerOptions": {
    "composite": true,
    "skipLibCheck": true,
    "module": "ESNext",
    "moduleResolution": "bundler",
    "allowSyntheticDefaultImports": true
  },
  "include": ["vite.config.ts"]
}
```

---

### index.html

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>{Page Title}</title>
    <style>
      /* Reset for Dataverse iframe context — prevents double scrollbars and margin gaps */
      html, body, #root {
        margin: 0;
        padding: 0;
        width: 100%;
        height: 100%;
        overflow: hidden;
      }
      /* FluentProvider renders a wrapping div — ensure it fills the container */
      #root > div {
        height: 100%;
      }
    </style>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

---

### src/main.tsx

```typescript
import * as React from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";

// Note: This is a standalone web resource (not a PCF control), so it uses
// React 18 which includes native useId() support required by Fluent UI v9.
// See ADR-026 for the full-page Custom Page standard.

const rootElement = document.getElementById("root");

if (rootElement) {
  const root = createRoot(rootElement);
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
} else {
  console.error("[{PageName}] Root element not found");
}
```

---

### src/App.tsx (Skeleton)

```typescript
import * as React from "react";
import { FluentProvider } from "@fluentui/react-components";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";

export const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveTheme);

  React.useEffect(() => {
    const cleanup = setupThemeListener(() => {
      setTheme(resolveTheme());
    });
    return cleanup;
  }, []);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      {/* Page content here */}
    </FluentProvider>
  );
};
```

---

## Standard Patterns

### Xrm Access (Frame-Walk)

```typescript
/* eslint-disable @typescript-eslint/no-explicit-any */
declare const Xrm: any;

function getXrm(): any | null {
  // 1. Current window (direct embedding)
  if (typeof Xrm !== "undefined" && Xrm?.WebApi) return Xrm;
  // 2. Parent window (iframe in Custom Page)
  try {
    const p = (window.parent as any)?.Xrm;
    if (p?.WebApi) return p;
  } catch { /* cross-origin */ }
  // 3. Top window (nested iframes)
  try {
    const t = (window.top as any)?.Xrm;
    if (t?.WebApi) return t;
  } catch { /* cross-origin */ }
  return null;
}
```

### Entity Navigation

```typescript
function openRecord(entityName: string, entityId: string, formId?: string): void {
  const xrm = getXrm();
  if (!xrm?.Navigation) return;

  const options: any = { entityName, entityId };
  if (formId) options.formId = formId;

  xrm.Navigation.navigateTo(
    { pageType: "entityrecord", ...options },
    { target: 1 }  // 1 = inline, 2 = dialog
  );
}
```

### BFF API Calls (with MSAL)

```typescript
async function callBffApi<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${BFF_BASE_URL}${path}`, {
    ...options,
    headers: {
      "Content-Type": "application/json",
      ...options?.headers,
    },
  });
  if (!response.ok) throw new Error(`BFF ${response.status}: ${response.statusText}`);
  return response.json();
}
```

### Cross-Iframe Communication (Side Panes)

```typescript
const CHANNEL_NAME = "spaarke-{page}-channel";

// Send
function broadcast(type: string, payload: unknown): void {
  const ch = new BroadcastChannel(CHANNEL_NAME);
  ch.postMessage({ type, payload });
  ch.close();
}

// Receive
function subscribe(handler: (msg: { type: string; payload: unknown }) => void): () => void {
  const ch = new BroadcastChannel(CHANNEL_NAME);
  ch.onmessage = (e) => handler(e.data);
  return () => ch.close();
}
```

---

## Deployment

### Build

```bash
cd src/solutions/{PageName}
npm install
npm run build
# Output: dist/index.html (single self-contained file)
```

### Deploy to Dataverse

```powershell
pac webresource push `
  --path "dist/index.html" `
  --name "sprk_{pagename}" `
  --type "Webpage" `
  --solution-unique-name "SpaarkeCore"
```

### Web Resource Naming

| Type | Convention | Example |
|------|-----------|---------|
| Full page | `sprk_{pagename}` | `sprk_legalworkspace` |
| Side pane | `sprk_{panename}` | `sprk_calendarsidepane` |

---

## Anti-Patterns

### DON'T: Use PCF for full-page surfaces

```xml
<!-- ❌ WRONG: PCF manifest — platform injects React 16, createRoot fails -->
<platform-library name="React" version="16.14.0" />
```

### DON'T: Produce multiple output files

```typescript
// ❌ WRONG: code splitting breaks single-file deployment
rollupOptions: { output: { manualChunks: { vendor: ["react"] } } }
```

### DON'T: Use React Router

```typescript
// ❌ WRONG: client-side routing has no meaning inside Dataverse
import { BrowserRouter, Route } from "react-router-dom";
```

### DON'T: Hard-code Xrm access without guards

```typescript
// ❌ WRONG: crashes outside Dataverse (dev server, tests)
const result = Xrm.WebApi.retrieveMultipleRecords("account", "?$top=10");

// ✅ CORRECT: guarded access
const xrm = getXrm();
if (!xrm) { console.warn("Xrm not available"); return []; }
const result = await xrm.WebApi.retrieveMultipleRecords("account", "?$top=10");
```

---

## New Project Checklist

- [ ] Create `src/solutions/{PageName}/` directory
- [ ] Copy template files (vite.config.ts, tsconfig.json, tsconfig.node.json, index.html)
- [ ] Create package.json from template (update name, description)
- [ ] Create src/main.tsx from template (update page name)
- [ ] Create src/App.tsx with FluentProvider + theme wiring
- [ ] Copy ThemeProvider.ts from EventsPage (`src/solutions/EventsPage/src/providers/`)
- [ ] Run `npm install`
- [ ] Verify `npm run build` produces single `dist/index.html`
- [ ] Deploy with `pac webresource push`
- [ ] Test in Dataverse Custom Page context

---

## Reference Implementations

| Page | Location | Notes |
|------|----------|-------|
| EventsPage | `src/solutions/EventsPage/` | Canonical reference — full grid + side panes |
| CalendarSidePane | `src/solutions/CalendarSidePane/` | Side pane pattern |
| EventDetailSidePane | `src/solutions/EventDetailSidePane/` | Side pane + entity form |

---

## Related

- **ADR-026**: [Full-Page Custom Page Standard](../../adr/ADR-026.md) — architectural decision
- **ADR-006**: [PCF over webresources](../../adr/ADR-006.md) — PCF is for form-level UI
- **ADR-021**: [Fluent v9 Design System](../../adr/ADR-021.md) — theme, typography, tokens
- **ADR-022**: [PCF Platform Libraries](../../adr/ADR-022.md) — React 16 constraint for PCF
- [PCF Control Initialization](../pcf/control-initialization.md) — for form-level controls (React 16)

---

**Lines**: ~200
**Purpose**: Standard template for full-page Dataverse Custom Page web resources
