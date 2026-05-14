---
description: Design or implement an MCP App widget (inline or side-by-side, Fluent v9, sandboxed)
tags: [widget, mcp-app, ui, fluent-ui, copilot]
techStack: [react, typescript, fluent-ui-v9, mcp]
appliesTo: ["**/widgets/**/*.tsx", "**/widgets/**/*.ts", "**/mcp-app/**/*"]
alwaysApply: false
---

# widget-design

> **Category**: Development
> **Last Updated**: 2026-05-14

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
- [ ] Bundle size reviewed — widgets should be small (no 1+ MB bundles)

---

## Conventions

- Widgets live under `src/client/widgets/<skill-name>/` (parallel to `src/client/pcf/`)
- Widget bundles: built per-widget, not as a monorepo bundle (smaller, faster)
- Naming: widget folder matches the skill name (`redline-summary`, `tabular-review-grid`)
- Each widget is its own MCP App package (manifest + bundle) — not a shared app

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
