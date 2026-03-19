# ADR-006: UI Surface Architecture — Code Pages, PCF, and Web Resources

| Field | Value |
|-------|-------|
| Status | **Accepted** (Revised) |
| Date | 2025-09-27 |
| Updated | 2026-03-19 |
| Authors | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-006 Concise](../../.claude/adr/ADR-006-pcf-over-webresources.md) - Decision + constraints + Code Page pattern
- [Frontend Constraints](../../.claude/constraints/pcf.md) - MUST/MUST NOT rules for all frontend surfaces
- [Dialog Patterns](../../.claude/patterns/pcf/dialog-patterns.md) - Code Page dialog opening, WizardDialog, SidePanel

**When to load this full ADR**: Surface selection rationale, exception approval process, migration history

---

## Context

### Origin: Anti-Legacy-JS

ADR-006 originated as an **anti-legacy-JavaScript rule** — preventing untestable "random JS" sprinkled across Dataverse forms. Legacy web resources (no-framework JS, jQuery, ad hoc scripts) are hard to package, test, and lifecycle-manage in Power Platform. This anti-legacy rule remains in full effect.

### Evolution: Two-Tier → Surface Architecture

The original ADR framed the choice as "PCF vs web resources" with PCF as the default. As the codebase matured, the architecture evolved:

1. **React Code Pages** emerged as the preferred surface for dialogs, wizards, and full pages — they bundle React 19, support concurrent features, and are independently deployable
2. **PCF controls** remain essential for form-embedded UI that needs bound properties and the `updateView()` lifecycle
3. The distinction that matters is **hosting context** (form-embedded vs standalone), not technology preference

This revision reflects the current reality: **Code Pages are the default for new UI**, and PCF is the specialized choice for form binding.

---

## Decision

### Surface Architecture

| UI Surface | Technology | React | Location | When to Use |
|-----------|------------|-------|----------|-------------|
| **Code Page** (dialog, wizard, full page, side pane) | Standalone HTML web resource (Vite + React 19) | 19 (bundled) | `src/solutions/{Name}/` | **Default for all new UI** — standalone dialogs, wizards, pages, panels |
| **PCF control** (form-embedded) | PCF (TypeScript/React) | 16/17 (platform) | `src/client/pcf/` | Only when Dataverse form binding is needed (bound properties, `updateView()` lifecycle) |
| **Ribbon/command script** | Thin JS (invocation only) | N/A | Webresource JS | Invoke `navigateTo` to open Code Pages; no business logic |
| **Shared components** | React library | peer `>=16.14.0` | `src/client/shared/Spaarke.UI.Components/` | Consumed by all surfaces |
| **Office add-ins** | React | 19 (bundled) | `src/client/office-addins/` | Office integration |
| **Power Pages SPA** | React | 19 (bundled) | `src/client/external-spa/` | External portal |

### Decision Rules

1. **Building a new dialog, wizard, or standalone page?** → Code Page
2. **Need to embed a control on a Dataverse form with bound properties?** → PCF
3. **Need a command bar button to launch something?** → Thin JS ribbon script that calls `navigateTo` to open a Code Page
4. **Building reusable UI components?** → `@spaarke/ui-components` shared library
5. **None of the above?** → Ask — don't create legacy JS

---

## What Is a Code Page?

A **Code Page** is a standalone React 19 app deployed as a Dataverse HTML web resource. It bundles its own React, Fluent v9, and shared library components into a single HTML file via Vite + `vite-plugin-singlefile`.

**Key characteristics:**
- Self-contained (no external dependencies at runtime)
- React 19 with `createRoot` entry point
- Opened via `Xrm.Navigation.navigateTo` as a dialog or page
- Parameters passed via URL `data` envelope
- Gets Dataverse modal chrome when opened with `target: 2` (title bar, expand button, close button)
- Independently deployable — update the wizard without redeploying the workspace

```typescript
// Code Page entry point
import { createRoot } from "react-dom/client";
import { FluentProvider } from "@fluentui/react-components";
import { detectTheme, parseDataParams } from "@spaarke/ui-components/utils";

const params = parseDataParams();
const theme = detectTheme();
createRoot(document.getElementById("root")!).render(
    <FluentProvider theme={theme}>
        <App {...params} />
    </FluentProvider>
);
```

**Opening a Code Page dialog:**
```typescript
Xrm.Navigation.navigateTo(
    {
        pageType: "webresource",
        webresourceName: "sprk_creatematterwizard",
        data: `matterId=${matterId}`,
    },
    { target: 2, width: { value: 70, unit: "%" }, height: { value: 70, unit: "%" } }
);
```

---

## Why Code Pages Are the Default

| Factor | Code Page | PCF |
|--------|-----------|-----|
| React version | 19 (bundled, full features) | 16/17 (platform-constrained) |
| Reusability | Callable from any context (workspace, entity forms, ribbon, SPA) | Tied to the form it's embedded on |
| Deployment | Independent — update without redeploying parent | Coupled to solution deployment |
| Dialog support | Native via `navigateTo({ target: 2 })` | Cannot open directly as dialog |
| Bundle isolation | Own bundle, no impact on other pages | Shares bundle with form |
| Shared library | Full barrel import (React 19 safe) | Deep imports required (React 16 limitation) |

### Why PCF Still Matters

PCF controls are essential when you need:
- **Bound properties**: Reading/writing Dataverse field values directly
- **`updateView()` lifecycle**: Reacting to form context changes
- **Form embedding**: Controls that replace standard form fields or subgrids
- **Platform integration**: Dataset API, navigation context, user settings

**Do NOT use PCF** just because the UI appears on an entity form. If it's a dialog launched from a button, use a Code Page.

---

## Consequences

**Positive:**
- Code Pages get React 19 concurrent features, Suspense, `useId`
- Wizards and dialogs are reusable across all contexts
- Independent deployment reduces change risk
- Shared components (`@spaarke/ui-components`) work in both surfaces
- Clear decision criteria eliminate ambiguity

**Negative:**
- Two build systems to maintain (PCF + Vite for Code Pages)
- Code Pages have slightly larger bundles (~500KB+ vs ~50KB for PCF)
- Code Pages opened as dialogs get Dataverse modal chrome (title bar, expand button) — this is a side effect, not controllable

---

## Exceptions

### Allowed Web Resources

| Type | Purpose | Status |
|------|---------|--------|
| React Code Pages (`.html`) | Standalone React apps | Default for new UI |
| Ribbon/command scripts (`.js`) | Invocation only — calls `navigateTo` | Allowed (minimal JS) |
| Legacy SPE utility JS | Historical utility | Legacy — no new features |

### Exception Rules

1. **Ribbon/Command Bar Scripts**: Invocation only. No business logic, no remote calls.
2. **New legacy JS web resources**: Require explicit approval with documented justification.
3. **Embedding complex dialogs inline in PCF**: Extract to Code Page for reusability. Only simple inline dialogs (e.g., confirm/cancel) should remain in PCF.

---

## AI-Directed Coding Guidance

- New standalone dialog or wizard → **Code Page** under `src/solutions/{WizardName}/`
- New full page or side pane → **Code Page** under `src/solutions/{PageName}/`
- New form-embedded control with bound properties → **PCF** under `src/client/pcf/`
- Command bar button to launch a wizard → **Thin JS** that calls `navigateTo` to open a Code Page
- Reusable component → Add to `@spaarke/ui-components` shared library
- If you think you need a PCF for a dialog: **stop and use a Code Page instead**
- If you think you need legacy JS: **stop and write an explicit exception with justification**

---

## Success Metrics

| Metric | Target |
|--------|--------|
| New legacy JS web resources | Zero (without approval) |
| Custom page + PCF dialog wrappers | Zero (use Code Pages) |
| New standalone dialogs using Code Pages | 100% |
| Wizard components in shared library | 100% (single source of truth) |

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-09-27 | 1.0 | Initial ADR: PCF preferred over legacy webresources | Spaarke Engineering |
| 2026-02-23 | 1.1 | Revised to two-tier architecture: PCF for forms, React Code Pages for dialogs | Spaarke Engineering |
| 2026-03-19 | 2.0 | Reframed as UI Surface Architecture. Code Pages are now the default for all new UI. PCF is specialized for form binding only. Added Power Pages SPA and Office add-ins to surface matrix. Updated React version to 19 per ADR-021. Added guidance on extracting inline PCF dialogs to standalone Code Pages for reusability. | Spaarke Engineering |
