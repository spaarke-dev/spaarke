---
description: Design or implement an MCP App widget (inline or side-by-side, Fluent v9, sandboxed)
tags: [widget, mcp-app, ui, fluent-ui, copilot]
techStack: [react, typescript, fluent-ui-v9, mcp]
appliesTo: ["**/widgets/**/*.tsx", "**/widgets/**/*.ts", "**/mcp-app/**/*"]
alwaysApply: false
---

# widget-design

> **Category**: Development
> **Last Updated**: 2026-11-25 (spaarkeai-mcp-bff-api-r1 task 051: implemented conventions)

---

## Purpose

Anchor MCP App widget design in current Microsoft idioms — inline vs side-by-side modes, the `useMcpApp` host-bridge contract, sandboxed iframe constraints, and the Spaarke skill→widget mapping. Without this skill, generated widgets drift toward generic React patterns that assume localStorage availability, leak DOM scope, or mix Fluent UI v8 with v9.

This skill complements `mcp-tool-handler` — the tool handler produces structured output; the widget renders it. They share the same MCP App.

---

## Applies When

- Designing a new widget for a Spaarke skill (Redline, TabularReview, Compare, PlaybookDraft, CitationCheck)
- Implementing a widget React component for inline or side-by-side rendering in Copilot Chat
- Wiring widget state via `useMcpApp` (receiving data from the MCP tool, returning user interactions)
- Choosing between inline summary cards vs deep-link side-by-side review panes
- **NOT applicable** for: PCF controls (use shared `@spaarke/ui-components`), Code Page React 18 dialogs (different lifecycle), the tool handler itself (use `mcp-tool-handler`)

---

## Spaarke conventions (implemented, binding)

The first shipped widget is `ui://spaarke/decisions.html` (source `src/client/widgets/spaarke-decisions/`), built by spaarkeai-mcp-bff-api-r1. Copy it. Where this section and the Microsoft samples below differ, this section applies; where it differs from the code, the code wins (root CLAUDE.md §2).

| Rule | What the code does |
|---|---|
| **URI** `ui://spaarke/{widget}.html` | `DecisionsWidgetResource.Uri` (`src/server/api/Sprk.Bff.Api/Api/Mcp/LegalOperations/DecisionsWidgetResource.cs`); a model-visible tool points at it with `_meta.ui.resourceUri` |
| **Source folder** `src/client/widgets/{skill-name}/` (folder = the skill name, e.g. `spaarke-decisions`) | One Vite build per widget into a single self-contained `dist/index.html` (no `<script src>`, no stylesheet `<link>`) |
| **Served by the BFF, not a separate package** | `dist/index.html` is an `EmbeddedResource` of `Sprk.Bff.Api.csproj`, returned by `resources/read` as `text/html;profile=mcp-app`. Build the widget before the BFF publish, or the BFF serves no widget (it logs one startup warning) |
| **Size: aim < 1 MB, fail > 3 MB** | `scripts/check-bundle-size.mjs` (warns above 1 MiB, fails above 3 MiB) runs in the widget's `npm run build`. Measured in Phase 0 (`projects/spaarkeai-mcp-bff-api-r1/notes/phase0-results.md`): in-widget init (script start → connected) stays flat at 9–80 ms from 1 to 6 MB, but the visible paint comes later as size grows (Chat: 3 MB drawn by ~15 s after send, 6 MB by ~23 s; Cowork 6 MB by ~28 s). The decisions widget is 1.54 MB (MCP SDK ~524 KB, Fluent ~404 KB, the email composer's editor ~254 KB) |
| **No network origins** | `_meta.ui.csp` is an empty object; the widget fetches nothing and holds no token |
| **Host bridge only** | `@modelcontextprotocol/ext-apps` (`useApp`, `app.callServerTool`, `app.openLink`, `app.ontoolresult`, `app.onhostcontextchanged`), wrapped in `src/mcp/mcpHost.ts`. All data comes through **app-only** tools via `callServerTool` |
| **Shims return a real `Response`** | Shared services that take a `fetch` (`createWorklistService`, `createDecisionWizardService`) get one built on `callServerTool` that wraps the tool result in a `Response` and passes `reasonCode` through (`src/mcp/mcpFetch.ts`). Reuse the shared components and services; never fork them |
| **Theme from the host only** | `hostContext.theme` (`src/theme/`), Fluent v9 semantic tokens; nothing stored |
| **No browser storage** | The sandbox blocks `localStorage`/`sessionStorage`/cookies; the build scans `dist` and fails on storage use |
| **Host differences** | Cowork paints the widget after the text answer, and a later widget in the same task can collapse an earlier frame. Copilot Chat gives this widget no host fullscreen, so the review opens inline, maximized |

---

## Workflow

### Step 1: Load knowledge context (mandatory)

Read in this order:

1. **`knowledge/mcp-apps/NOTES.md`** — Spaarke skill→widget mapping (Redline/TabularReview/Compare/PlaybookDraft/CitationCheck patterns).
2. **`knowledge/mcp-apps/trey-research/`** — Closest pattern to a Spaarke workspace widget. Study:
   - `trey-research/src/mcpserver/widgets/src/dashboard/Dashboard.tsx` (Fluent v9 dashboard layout)
   - `trey-research/src/mcpserver/widgets/src/hooks/useMcpApp.tsx` (host-bridge contract — this is the canonical pattern)
   - `trey-research/src/mcpserver/widgets/src/hooks/useThemeColors.ts` (theme variable resolution)
3. **`knowledge/mcp-apps/approvals-box/`** — Closest pattern to Spaarke's redline review queue (risk-triaged approval queue, bulk actions).
4. **`knowledge/mcp-apps/agents-toolkit-screenshots/`** — Visual reference for inline vs side-by-side rendering.
5. **`knowledge/mcp-apps/docs/plugin-mcp-apps-ui-guidelines.md`** — Microsoft UX guidelines.
6. **`.claude/patterns/ui/fluent-v9-component-authoring.md`** — Spaarke conventions for ANY Fluent v9 component (token-only colors, `makeStyles` at module scope, `mergeClasses` ordering, slot vs hook decision).
7. **`.claude/patterns/ui/fluent-v9-portal-gotcha.md`** — MANDATORY if widget uses `Popover` / `Tooltip` / `Toast` / `Dialog` / `Menu`. Portals escape `FluentProvider` styling — re-wrap inside the portal surface or set `applyStylesToPortals` explicitly.
8. Drill into `knowledge/fluent-ui-v9/docs/` only if a pattern's `→` link punts there; use [`knowledge/fluent-ui-v9/docs/INDEX.md`](../../../knowledge/fluent-ui-v9/docs/INDEX.md) to navigate.

### Step 2: Choose the rendering mode

| Skill / use case | Mode | Why |
|---|---|---|
| Redline summary (issue counts, severity badges) | **Inline** | At-a-glance signal in chat thread |
| Redline review (per-clause edit pane) | **Side-by-side** | Needs full screen real estate |
| TabularReview grid | **Side-by-side** | Tabular data needs width |
| Compare diff view | **Side-by-side** | Two-column layout |
| PlaybookDraft rationale | **Inline** card | Read-only annotation |
| CitationCheck status | **Inline** status cards | Quick verification UI |

**Default for Spaarke skills**: produce both — an inline summary that signals the result, with a "Open in side-by-side" affordance that deep-links to the full pane.

### Step 3: Apply Spaarke contracts (ADR-021, ADR-012)

- **ADR-021 (Fluent UI v9)**: All widgets MUST use Fluent UI v9 exclusively. No v8 components. No hard-coded colors. Dark mode required — resolve theme via `useThemeColors` (pattern in trey-research).
- **ADR-012 (Shared component library)**: Where a Spaarke component exists in `@spaarke/ui-components`, reuse it. Do NOT duplicate components inside widgets. If a component needs to work in both PCF and widget contexts, it must be in the shared library (or extracted there).

### Step 4: Sandboxed iframe constraints

MCP App widgets run in a sandboxed iframe. The following are NOT available:

- `localStorage` / `sessionStorage` — state lives in widget memory or in the round-trip via `useMcpApp`
- `document.cookie` — no cross-origin auth
- `window.parent` — host communication is via `useMcpApp` postMessage bridge, not direct DOM access
- Custom CSS that targets parent DOM — scoped to the widget root only
- Theme variables — resolved via `useThemeColors`, not via CSS variables on `:root`

Build widgets assuming these constraints; do not write fallback logic for environments where they would be available.

### Step 5: `useMcpApp` host-bridge contract

Pattern (verbatim from `knowledge/mcp-apps/trey-research/src/mcpserver/widgets/src/hooks/useMcpApp.tsx`):

```tsx
const { data, theme, sendMessage } = useMcpApp<MyWidgetData>();
```

- **`data`**: typed input from the MCP tool result. Shape it via TypeScript interfaces — no `any`.
- **`theme`**: resolved theme tokens for Fluent v9 (light/dark/high-contrast).
- **`sendMessage`**: invoke when the user takes an action that needs to flow back to the tool (e.g., "approve this redline", "select these rows"). The host marshals the message back to the agent.

### Step 6: State management

- Lift state into the widget hook (or a small zustand store inside the widget) — never localStorage
- Optimistic UI for user actions: render immediately, reconcile when `sendMessage` resolves
- Loading states: show a skeleton, not a spinner (matches Fluent v9 conventions and the Microsoft samples)

### Step 7: Generation aid — Microsoft's `/generate-mcp-app-ui` skill

Microsoft published a skill for Claude Code and GitHub Copilot CLI that generates MCP App widget scaffolds. Reference it in `knowledge/mcp-apps/NOTES.md`. Use it for scaffolding, then refine to Spaarke's Fluent v9 + shared-component conventions.

### Step 8: Code review checklist

- [ ] Fluent UI v9 only (no v8, no hard-coded colors)
- [ ] Dark mode tested (resolve theme via `useThemeColors`)
- [ ] No `localStorage` / `sessionStorage` / `document.cookie` usage
- [ ] `useMcpApp` typed with a concrete data interface (no `any`)
- [ ] Shared components from `@spaarke/ui-components` reused where applicable
- [ ] Mode (inline vs side-by-side) matches the skill-to-mode table
- [ ] Skeleton loading state (not a spinner)
- [ ] Bundle size gate passes (aim < 1 MB, hard fail > 3 MB; see "Spaarke conventions")
- [ ] `_meta.ui.csp` empty; data only through app-only tools via `callServerTool`

---

## Conventions

- Widgets live under `src/client/widgets/<skill-name>/` (parallel to `src/client/pcf/`)
- Widget bundles: built per-widget, not as a monorepo bundle (smaller, faster)
- Naming: widget folder matches the skill name (`spaarke-decisions`); URI `ui://spaarke/{widget}.html`
- Each widget is served by the BFF's MCP server as a `ui://` resource (embedded in the BFF assembly), not shipped as its own package

## Resources

| Resource | Purpose |
|----------|---------|
| `knowledge/mcp-apps/NOTES.md` | Spaarke skill→widget mapping |
| `knowledge/mcp-apps/trey-research/src/mcpserver/widgets/src/hooks/useMcpApp.tsx` | Host-bridge contract (canonical) |
| `knowledge/mcp-apps/trey-research/src/mcpserver/widgets/src/dashboard/Dashboard.tsx` | Fluent v9 dashboard layout |
| `knowledge/mcp-apps/approvals-box/` | Risk-triaged approval queue pattern |
| `knowledge/mcp-apps/agents-toolkit-screenshots/` | Visual reference for modes |
| `knowledge/mcp-apps/docs/plugin-mcp-apps-ui-guidelines.md` | Microsoft UX guidelines |

## Output

When this skill completes, expect:
- A new widget package under `src/client/widgets/<skill-name>/`
- Fluent v9 components, dark mode, shared library reuse
- `useMcpApp` typed with concrete data interface
- Mode (inline or side-by-side) matches the skill purpose
- Reference added to `knowledge/mcp-apps/NOTES.md` if a new Spaarke widget pattern emerges
