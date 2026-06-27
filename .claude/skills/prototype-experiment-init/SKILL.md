---
description: Scaffold a Mode 1 standalone UX experiment in spaarke-prototype for greenfield design work (no production code yet)
tags: [prototype, experiment, greenfield, ux, design]
techStack: [vite, react, fluent-v9, typescript]
appliesTo: ["start UX experiment", "design new prototype", "greenfield design exploration"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-06-18
---

# prototype-experiment-init

> **Category**: UI / Prototyping
> **Last Reviewed**: 2026-06-18
> **Exemplar rationale**: Experiment names are user-named per-effort; no canonical artifact. The 6-step workflow is the contract.

---

## Purpose

Scaffold a **Mode 1 standalone UX experiment** at `c:/code_files/spaarke-prototype/projects/{date}-{slug}/` for greenfield design work — exploring a new UI pattern that has **no production source yet**.

Mode 1 vs Mode 2:
- **Mode 1 (this skill)**: Greenfield. No alias. Invented mock data. Output = visual contract (screenshots + working prototype) for the dev team to implement
- **Mode 2 (`prototype-harness-setup`)**: Production iteration. Aliases real source from a worktree. Output = sub-second HMR for visual UAT polish

The phase pivot is **first production code**: before it lands, use Mode 1; after it lands, use Mode 2.

**Why this skill** (and why it's LOW priority): the manual ceremony for a Mode 1 experiment is small — copy a recent example, rename, edit `App.tsx`. Most senior devs don't need automation. This skill is for AI-agent-driven flows + onboarding.

---

## Applies When

- A new UI pattern needs visual exploration BEFORE any production code is written
- A design needs to be validated with stakeholders before spec/implementation
- An existing pattern needs a fresh design variant to compare against
- **Trigger phrases**: `/prototype-experiment-init`, "start UX experiment", "design new prototype for X", "greenfield design for Y", "new UI exploration"

**Do NOT use** when:
- Production code already exists for the component → use `prototype-harness-setup` (Mode 2) instead
- The work is just one quick screen — use a CodeSandbox or single-file React playground; a full experiment dir is overkill
- The exploration is for a backend/non-UI surface — this skill is UI-only

---

## Prerequisites

| Check | Why |
|---|---|
| `c:/code_files/spaarke-prototype/` exists | The experiments live there |
| On a branch suitable for experiments | Typically `main` or a project-specific branch |
| User has a clear design question to answer | Experiments without a hypothesis become abandoned scaffolds |

---

## Workflow

### Step 1: Gather Inputs

**Experiment slug** (required):
```
ASK: "Experiment slug? (kebab-case, e.g., 'matter-form-redesign', 'semantic-search-results').
      Resulting dir = projects/{YYYY-MM}-{slug}/"
```

**Date prefix** (auto, but offer override):
- Default to current month: `2026-06`
- User can override (e.g., backdate to align with sprint planning)

**Reference design** (optional):
```
ASK: "Reference a recent experiment as a starting point? (none / path to existing experiment)
      Recent options:
        - projects/2026-05-matter-form-redesign/
        - projects/2026-03-action-workspace/
        - projects/2026-03-product-demo/"
```

**Design hypothesis** (required — gates abandonware):
```
ASK: "One-sentence design hypothesis? (e.g., 'A side-pane variant of the matter form will reduce
       average time-to-edit by removing the page navigation step.')"

This goes into the experiment's README.md as the first heading. Experiments without a hypothesis
tend to be abandoned — the prompt forces the user to commit to what they're exploring.
```

---

### Step 2: Confirm Plan

```
PLAN
────
Experiment dir:   c:/code_files/spaarke-prototype/projects/{date}-{slug}/
Reference start:  {none | path}
Hypothesis:       "{user's sentence}"

Proceed? [Y/n]
```

---

### Step 3: Scaffold the Project Dir

**IF reference design specified**:
```powershell
Copy-Item -Recurse `
  c:/code_files/spaarke-prototype/projects/{reference-path} `
  c:/code_files/spaarke-prototype/projects/{date}-{slug}
```
Then clean: delete the reference's `node_modules/`, `dist/`, `package-lock.json` (regenerate fresh).

**IF starting from blank**:
```powershell
mkdir c:/code_files/spaarke-prototype/projects/{date}-{slug}/src
```

Create these files (use the canonical template at `c:/code_files/spaarke-prototype/projects/_templates/prod-component-harness/` as structural reference, but DO NOT use installMocks — Mode 1 doesn't mock production code):

**`package.json`**:
```json
{
  "name": "{date}-{slug}",
  "version": "0.0.1",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build"
  },
  "dependencies": {
    "react": "^18.3.1",
    "react-dom": "^18.3.1",
    "@fluentui/react-components": "^9.55.1",
    "@fluentui/react-icons": "^2.0.260"
  },
  "devDependencies": {
    "@types/react": "^18.3.12",
    "@types/react-dom": "^18.3.1",
    "@vitejs/plugin-react": "^4.3.4",
    "typescript": "^5.6.3",
    "vite": "^5.4.10"
  }
}
```

**`vite.config.ts`**:
```typescript
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: { port: 5173, open: true },
});
```

**`tsconfig.json`** (standard React+Vite):
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
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noFallthroughCasesInSwitch": true
  },
  "include": ["src"]
}
```

**`index.html`**:
```html
<!DOCTYPE html>
<html lang="en">
  <head><meta charset="UTF-8" /><title>{slug}</title></head>
  <body><div id="root"></div><script type="module" src="/src/main.tsx"></script></body>
</html>
```

---

### Step 4: Generate Entry Points

**`src/main.tsx`** (Mode 1 — NO mocks; just FluentProvider + your app):

```tsx
import * as React from "react";
import * as ReactDOM from "react-dom/client";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { App } from "./App";

const root = ReactDOM.createRoot(document.getElementById("root")!);
root.render(
  <React.StrictMode>
    <FluentProvider theme={webLightTheme}>
      <App />
    </FluentProvider>
  </React.StrictMode>,
);
```

**`src/App.tsx`** (placeholder — user fills in the design):

```tsx
import * as React from "react";
import { makeStyles, tokens, Title2, Body1 } from "@fluentui/react-components";

const useStyles = makeStyles({
  page: {
    minHeight: "100vh",
    padding: tokens.spacingHorizontalXL,
    backgroundColor: tokens.colorNeutralBackground2,
  },
});

export function App(): React.ReactElement {
  const s = useStyles();
  return (
    <div className={s.page}>
      <Title2>{slug}</Title2>
      <Body1 block>
        Edit src/App.tsx to start designing.
      </Body1>
    </div>
  );
}
```

**`src/mockData.ts`** (Mode 1 mocks — invented, not from real schema):

```typescript
/**
 * INVENTED mock data — this experiment is greenfield (no production schema yet).
 *
 * If/when this design moves to production, the dev team will define the real
 * Dataverse schema; THIS file does NOT need to match. Mode 2 production
 * harnesses (via prototype-harness-setup) seed against the real schema via
 * factories in _infra/seed/factories/.
 */

export interface MockItem {
  id: string;
  title: string;
  // ... whatever shape the design needs
}

export const mockItems: MockItem[] = [
  { id: "1", title: "Sample 1" },
  // ...
];
```

---

### Step 5: Generate README

`README.md` at the experiment root:

```markdown
# {slug} — {date}

## Hypothesis

{user's one-sentence hypothesis}

## Status

- [ ] Initial scaffold
- [ ] Design draft v1
- [ ] Stakeholder review
- [ ] Signed off as visual contract for production implementation
- [ ] Production code merged
- [ ] Experiment archived

## Decisions

(Add bullets as design choices crystallize — these become the visual contract
for the dev team to implement.)

## Reference

- Started from: {reference design path, or "blank"}
- Production target: (to be filled in when this graduates to Mode 2)
```

---

### Step 6: Install + First Run

```powershell
cd c:/code_files/spaarke-prototype/projects/{date}-{slug}
npm install
npm run dev   # → http://localhost:5173 (or next available port)
```

Verify the placeholder page renders. Iterate from there.

---

### Step 7: Report + Next Steps

```
✅ Experiment scaffolded at: c:/code_files/spaarke-prototype/projects/{date}-{slug}/

To iterate:
  cd c:/code_files/spaarke-prototype/projects/{date}-{slug}
  npm run dev

Edit src/App.tsx to design. Add mock data shapes to src/mockData.ts.

When the design is signed off:
  1. Capture screenshots / record a video of the working prototype
  2. Update README.md "Status" checklist + add visual contract decisions
  3. Spec the production implementation with /design-to-spec
  4. Implement in a production worktree via /project-pipeline
  5. (Optional) Set up a Mode 2 harness with prototype-harness-setup once
     production code lands, for sub-second iteration during UAT polish

This experiment does NOT touch production code. It's a sandbox.
```

---

## Anti-Patterns (do NOT do)

| ❌ Don't | Why |
|---|---|
| Skip the hypothesis | Experiments without one become abandoned scaffolds; force commitment up front |
| Reach into production source via Vite alias | That's Mode 2 territory; Mode 1 is invented + self-contained |
| Use real Dataverse schema field names in mockData.ts unnecessarily | Greenfield = freedom to invent. Real schema decisions belong in `/design-to-spec` |
| Skip the README | Stakeholder review needs a written hypothesis + decision log; the working prototype alone isn't enough |
| Treat the experiment as production code | No tests, no CI gates needed. Experiments are throwaway-by-design |
| Build a full feature in Mode 1 | Mode 1 is for visual + interaction design. Implement the full feature in production via the spec → pipeline flow |
| Mix Mode 1 and Mode 2 in the same dir | If you find yourself wanting both, spin up a Mode 2 harness via `prototype-harness-setup`; keep Mode 1 for the original design exploration |

---

## Failure Modes & Recovery

| Symptom | Cause | Fix |
|---|---|---|
| Port 5173 already in use | Another prototype harness or experiment is running | Vite auto-picks the next port; or kill the other dev server |
| Fluent UI components render unstyled | Forgot `<FluentProvider theme={...}>` | Wrap `<App />` in FluentProvider in `main.tsx` |
| Experiment looks great but team can't reproduce | Reference design has env-specific config | Document any setup steps in README "Reference" section |
| Experiment dragged on for weeks without sign-off | Missing decision gates | Use README "Status" checklist; tag stakeholders explicitly when each gate is hit |

---

## Related Skills

| Skill | Relationship |
|---|---|
| `prototype-harness-setup` | Mode 2 successor — invoke once production code lands |
| `design-to-spec` | Next step after sign-off — transforms experiment into a production spec |
| `project-pipeline` | Follows `design-to-spec` — initialises the production project |

---

## Reference

- Mode 1 examples (recent):
  - `c:/code_files/spaarke-prototype/projects/2026-05-matter-form-redesign/`
  - `c:/code_files/spaarke-prototype/projects/2026-03-action-workspace/`
  - `c:/code_files/spaarke-prototype/projects/2026-03-product-demo/`
- Developer guide: PROTOTYPE-UI-SYSTEM-GUIDE.md "Mode 1 — Standalone UX experiment" section
- Prototype repo: `c:/code_files/spaarke-prototype/`
