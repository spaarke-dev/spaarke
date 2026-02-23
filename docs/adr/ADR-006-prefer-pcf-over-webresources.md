# ADR-006: Anti-Legacy-JS — PCF for Form Controls, React Code Pages for Dialogs

| Field | Value |
|-------|-------|
| Status | **Accepted** (Revised) |
| Date | 2025-09-27 |
| Updated | 2026-02-23 |
| Authors | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-006 Concise](../../.claude/adr/ADR-006-pcf-over-webresources.md) - Decision + constraints + Code Page pattern
- [Frontend Constraints](../../.claude/constraints/pcf.md) - MUST/MUST NOT rules for all frontend surfaces
- [Dialog Patterns](../../.claude/patterns/pcf/dialog-patterns.md) - Code Page dialog opening, WizardDialog, SidePanel

**When to load this full ADR**: Exception approval process, surface selection rationale

---

## Context

ADR-006 is fundamentally an **anti-legacy-JavaScript rule**, not an anti-React rule. Legacy webresources (no-framework JS, jQuery, ad hoc scripts) are hard to package, test, and lifecycle-manage in Power Platform.

The original ADR treated all webresources as equivalent — but React-based HTML webresources ("Code Pages") are categorically different from legacy JS: they are typed, tested, component-based React applications. The distinction that matters is **field-bound form control vs standalone dialog/page**, not PCF vs webresource.

**What the rule prevents:**
- Untestable "random JS" sprinkled across Dataverse forms
- Poor lifecycle management and brittle dependencies
- UI logic becoming a shadow application runtime inside Dataverse

---

## Decision

### Two-Tier Architecture

| UI Surface | Technology | Location | React Version |
|-----------|------------|----------|---------------|
| Field-bound form controls | **PCF** (TypeScript/React) | `src/client/pcf/` | 16/17 (platform) |
| Standalone dialog / custom page | **React Code Page** (HTML webresource) | `src/client/code-pages/` | 18 (bundled) |
| Dataset list views (form-embedded) | **Dataset PCF** | `src/client/pcf/UniversalDatasetGrid/` | 16/17 (platform) |
| Shared UI components | **React library** | `src/client/shared/Spaarke.UI.Components/` | 18-compatible |
| Ribbon/command bar | **Thin JS** (invocation only) | Solution webresource | n/a |
| Office add-ins | **React** | `src/client/office-addins/` | 18 (bundled) |

### Rule

- **Field-bound → PCF**: Controls embedded on Dataverse entity forms with bound properties and `updateView()` lifecycle
- **Standalone dialog → React Code Page**: Dialogs, wizards, visualization pages opened via `navigateTo` with no form binding
- **Never**: Legacy JS webresources with business logic (no-framework JS, jQuery, ad hoc scripts)

---

## What Is a React Code Page?

A **React Code Page** is an HTML webresource that bundles React 18 + Fluent v9. It is opened as a dialog or page via Xrm.Navigation — **no custom page wrapper needed**.

```typescript
// Opening a Code Page dialog from a PCF control
Xrm.Navigation.navigateTo(
    {
        pageType: "webresource",
        webresourceName: "sprk_documentrelationshipviewer",
        data: `documentId=${documentId}`,
    },
    { target: 2, width: { value: 85, unit: "%" }, height: { value: 85, unit: "%" } }
);

// Inside the Code Page: read parameters
const params = new URLSearchParams(window.location.search);
const documentId = params.get("documentId") ?? "";
```

---

## Why Not PCF for Dialogs?

PCF controls cannot be opened directly as dialogs — they require a container (entity form or custom page). Using a custom page as a PCF wrapper adds:

| Cost | Detail |
|------|--------|
| Power Apps Studio dependency | Parameter wiring requires `Param()` formula in canvas app |
| React 16/17 ceiling | Platform constrains PCF to React 16/17; React 18 unavailable |
| No benefit | The dialog doesn't use form binding or `updateView()` lifecycle |
| Debugging friction | Canvas layer obscures errors; Studio required for config changes |

React Code Pages eliminate all of these costs for standalone dialog use cases.

---

## Consequences

**Positive (Two-Tier):**
- PCF: Better lifecycle management, Dataverse integration, typed bound properties
- Code Pages: React 18 concurrent features, simpler parameter passing, no canvas wrapper
- Both: Fluent v9 design system, `@spaarke/ui-components` shared library
- Shared components (WizardDialog, SidePanel) reused across surfaces

**Negative:**
- Two build systems to maintain (PCF + webpack/vite for Code Pages)
- Larger Code Page bundles (React bundled in, not platform-provided)

---

## Exceptions

### Allowed Webresources

| File | Purpose | Status |
|------|---------|--------|
| `sprk_subgrid_commands.js` | Ribbon/command bar button invocation | ✅ Allowed (invocation only) |
| `sprk_documentrelationshipviewer.html` | React Code Page dialog | ✅ Allowed (React app, not legacy JS) |
| Legacy SPE utility JS | Legacy SPE utility | ⚠️ Legacy — no new features |

### Exception Rules

1. **Ribbon/Command Bar Scripts**: Invocation only. No business logic, no remote calls.
2. **React Code Pages**: Allowed for standalone dialogs/pages. Must use React 18 + Fluent v9 + `@spaarke/ui-components`.
3. **Legacy files**: May remain if low-risk and read-only. No new features.
4. **New legacy JS webresources**: Require explicit approval with documented justification.

---

## AI-Directed Coding Guidance

- New interactive UI embedded on a form → implement as PCF under `src/client/pcf/`
- New standalone dialog opened via button → implement as React Code Page under `src/client/code-pages/`
- Wizard (multi-step form) → React Code Page using `WizardDialog` from `@spaarke/ui-components`
- Side panel (filter/detail pane) → React Code Page using `SidePanel` from `@spaarke/ui-components`
- Ribbon/command bar → keep JS webresource as thin invoker; call BFF endpoints
- If you think you need a custom page + PCF for a dialog: stop and use a React Code Page instead
- If you think you need legacy JS: stop and write an explicit exception with justification

---

## Success Metrics

| Metric | Target |
|--------|--------|
| New legacy JS webresources | Zero (without approval) |
| Custom page + PCF dialog wrappers | Zero (use Code Pages) |
| UI regressions | Reduced |
| Standalone dialogs using React 18 | 100% |
