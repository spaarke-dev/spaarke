# Code Page Wizard Wrapper Pattern

> **Domain**: Web Resources / Code Pages
> **Last Validated**: 2026-03-19
> **Source ADRs**: ADR-006, ADR-012, ADR-026

---

## When to Use

Create a Code Page wrapper when:
- A shared library component (wizard, dialog, shell) needs to be opened as a standalone Dataverse modal via `Xrm.Navigation.navigateTo`
- The component was previously rendered inline and is being extracted for reuse across entity forms, workspace, and SPA

Do NOT create a Code Page when:
- The component is only used inline within a PCF control (use deep import instead)
- The component is a simple single-step dialog (use Fluent UI Dialog inline)

---

## Canonical Template

### Directory Structure

```
src/solutions/{WizardName}/
├── package.json
├── vite.config.ts
├── tsconfig.json
├── tsconfig.node.json
├── index.html
└── src/
    └── main.tsx          # ~30-50 LOC — THE ONLY CUSTOM FILE
```

### package.json

```json
{
  "name": "{wizard-name-kebab}",
  "version": "1.0.0",
  "private": true,
  "scripts": {
    "dev": "vite",
    "build": "vite build",
    "preview": "vite preview"
  },
  "dependencies": {
    "@fluentui/react-components": "^9.46.2",
    "@fluentui/react-icons": "^2.0.200"
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
  }
}
```

### vite.config.ts (CRITICAL: resolve aliases required)

```typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { viteSingleFile } from "vite-plugin-singlefile";
import path from "path";

export default defineConfig({
  plugins: [react(), viteSingleFile()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
      "@spaarke/ui-components": path.resolve(__dirname, "../../client/shared/Spaarke.UI.Components/src"),
      "@fluentui/react-components": path.resolve(__dirname, "node_modules/@fluentui/react-components"),
      "@fluentui/react-icons": path.resolve(__dirname, "node_modules/@fluentui/react-icons"),
    },
  },
  build: {
    outDir: "dist",
    sourcemap: false,
    assetsInlineLimit: 100000000,
    minify: true,
    rollupOptions: { output: { manualChunks: undefined } },
  },
  base: "./",
});
```

### index.html

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>{Display Name}</title>
    <style>
      html, body, #root { margin: 0; padding: 0; width: 100%; height: 100%; overflow: hidden; }
      #root > div { height: 100%; }
      #root { overflow: auto; scrollbar-width: none; -ms-overflow-style: none; }
      #root::-webkit-scrollbar { display: none; }
    </style>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

### src/main.tsx (THE PATTERN — ~40 LOC)

```typescript
import * as React from "react";
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { resolveCodePageTheme, setupCodePageThemeListener } from "@spaarke/ui-components/utils/codePageTheme";
import { parseDataParams } from "@spaarke/ui-components/utils/parseDataParams";
import { createXrmDataService } from "@spaarke/ui-components/utils/adapters/xrmDataServiceAdapter";
import { createXrmUploadService } from "@spaarke/ui-components/utils/adapters/xrmUploadServiceAdapter";
import { createXrmNavigationService } from "@spaarke/ui-components/utils/adapters/xrmNavigationServiceAdapter";
// Deep import to avoid pulling in heavy deps (Lexical, RichTextEditor)
import { MyWizard } from "@spaarke/ui-components/components/MyWizard";

function App() {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);
  const params = React.useMemo(() => parseDataParams(), []);

  React.useEffect(() => {
    return setupCodePageThemeListener(() => setTheme(resolveCodePageTheme()));
  }, []);

  const dataService = React.useMemo(() => createXrmDataService(), []);
  const uploadService = React.useMemo(() => createXrmUploadService(params.bffBaseUrl || ""), [params.bffBaseUrl]);
  const navigationService = React.useMemo(() => createXrmNavigationService(), []);

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <MyWizard
        open={true}
        embedded={true}
        dataService={dataService}
        uploadService={uploadService}
        navigationService={navigationService}
        onClose={() => navigationService.closeDialog({ confirmed: true })}
        authenticatedFetch={fetch.bind(window)}
        bffBaseUrl={params.bffBaseUrl || ""}
      />
    </FluentProvider>
  );
}

const root = document.getElementById("root");
if (root) createRoot(root).render(<React.StrictMode><App /></React.StrictMode>);
```

---

## Key Rules

| Rule | Why |
|------|-----|
| `open={true}` always | Code Page is the dialog — it's always "open" |
| `embedded={true}` always | Dataverse navigateTo modal provides chrome (title bar, close X) |
| Deep imports from `@spaarke/ui-components` | Barrel imports pull in Lexical/RichTextEditor (~500KB) |
| `resolveCodePageTheme()` not `detectTheme()` | Code Pages don't have PCF context; use 4-level cascade |
| `navigationService.closeDialog()` for onClose | Closes the Dataverse navigateTo modal |
| `fetch.bind(window)` for authenticatedFetch | Code Pages in Dataverse context use cookie-based auth |

---

## Build & Deploy

```bash
npm install && npm run build    # Produces dist/index.html (single file, ~550-600KB)
```

Deploy via `scripts/Deploy-WizardCodePages.ps1` or manually:
```powershell
pac webresource create --path "dist/index.html" --name "sprk_{name}" --type "Webpage (HTML)"
```

---

## Opening from Consumers

### From Corporate Workspace / Entity Form (navigateTo)
```typescript
await Xrm.Navigation.navigateTo(
  { pageType: "webresource", webresourceName: "sprk_{name}", data: "entityId=xxx&entityType=yyy" },
  { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" }, title: "Display Name" }
);
// Promise resolves when dialog closes — refresh data here
```

### From Ribbon Command Bar
```javascript
Spaarke.Commands.Wizards.openMyWizard = function(primaryControl) {
  var ctx = getEntityContext(primaryControl);
  openWizardDialog(primaryControl, "sprk_{name}", "entityId=" + ctx.entityId, "Display Name");
};
```

---

## Anti-Patterns

| Don't | Do Instead |
|-------|-----------|
| Barrel import from `@spaarke/ui-components` | Deep import: `@spaarke/ui-components/components/MyWizard` |
| Render wizard's own Dialog wrapper in Code Page | Pass `embedded={true}` to suppress inner chrome |
| Import `authenticatedFetch` from LegalWorkspace | Accept as prop or use `fetch.bind(window)` |
| Hard-code BFF URL | Parse from `params.bffBaseUrl` |
| Forget `open={true}` | Always pass — wizard won't render without it |
| Missing vite.config.ts resolve aliases | Build will fail — `@spaarke/ui-components` won't resolve |

---

## Reference Implementations

| Code Page | Web Resource | Component |
|-----------|-------------|-----------|
| `src/solutions/CreateMatterWizard/` | sprk_creatematterwizard | CreateMatterWizard |
| `src/solutions/EventsPage/` | sprk_eventspage | Full page (not wizard) |
| `src/solutions/AnalysisBuilder/` | sprk_analysisbuilder | PlaybookLibraryShell |

---

**Lines**: ~180
