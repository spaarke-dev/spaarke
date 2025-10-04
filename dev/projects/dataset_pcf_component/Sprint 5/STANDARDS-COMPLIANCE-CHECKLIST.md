# Sprint 5 Standards Compliance Checklist

**Sprint:** Sprint 5 - Universal Dataset PCF Component
**Date:** 2025-10-03
**Purpose:** Quick reference for mandatory standards compliance

---

## 🔴 CRITICAL STANDARDS DOCUMENTS

Before writing ANY code, read these documents:

1. **[KM-PCF-CONTROL-STANDARDS.md](../../../docs/KM-PCF-CONTROL-STANDARDS.md)**
   - PCF project setup and lifecycle
   - Dataset API integration patterns
   - Testing and deployment best practices

2. **[KM-UX-FLUENT-DESIGN-V9-STANDARDS.md](../../../docs/KM-UX-FLUENT-DESIGN-V9-STANDARDS.md)**
   - Fluent UI v9 package requirements (ZERO tolerance for v8)
   - Theme and styling patterns (Griffel mandatory)
   - Accessibility requirements (WCAG AA)
   - Performance requirements (virtualization >100 items)

---

## ✅ Pre-Implementation Checklist

### Phase 1: Project Setup

**Shared Component Library:**
- [ ] Create `src/shared/Spaarke.UI.Components/` directory
- [ ] Initialize NPM package with correct dependencies
- [ ] Set up workspace linking in root `package.json`
- [ ] Verify Fluent UI v9 packages ONLY (`@fluentui/react-components`, `@fluentui/react-icons`)
- [ ] Create Spaarke brand theme (16-stop ramp) in `theme/` directory

**PCF Project:**
- [ ] Initialize PCF project: `pac pcf init --namespace Spaarke --name UniversalDataset --template dataset`
- [ ] Install React 18 and TypeScript 5
- [ ] Add `@spaarke/ui-components` workspace dependency
- [ ] Verify NO v8 packages in `package.json`

---

## ✅ Development Phase Checklist

### Fluent UI v9 Compliance (KM-UX-FLUENT-DESIGN-V9-STANDARDS.md)

**Package Compliance:**
- [ ] ONLY `@fluentui/react-components` v9 imported
- [ ] ONLY `@fluentui/react-icons` imported
- [ ] ZERO imports from `@fluentui/react` (v8 - PROHIBITED)

**Provider Pattern:**
- [ ] Single `FluentProvider` wraps entire component
- [ ] Theme detection: Host theme OR Spaarke fallback
- [ ] NO components rendered outside `FluentProvider`

**Styling Compliance:**
- [ ] ALL styling uses `makeStyles` from Griffel
- [ ] ALL spacing uses `tokens.spacing*` (no hard-coded px values)
- [ ] ALL colors use `tokens.color*` (no hard-coded hex)
- [ ] Uses `shorthands` for border, padding, gap
- [ ] Class composition via `mergeClasses`
- [ ] ZERO global CSS targeting Fluent internals
- [ ] ZERO DOM queries/mutations for styling

**Component Usage:**
- [ ] Tables use `DataGrid` component
- [ ] Forms use `Field` wrapper for inputs
- [ ] Actions use `Toolbar` + `ToolbarButton`
- [ ] Summaries use `Card` component
- [ ] All components use slot-based customization (no DOM manipulation)

**Accessibility (WCAG AA):**
- [ ] Icon-only buttons have `aria-label`
- [ ] Async state changes use `aria-live="polite"` or `role="status"`
- [ ] Keyboard navigation tested (Tab, Enter, Escape)
- [ ] Focus order is logical
- [ ] Focus outlines NOT suppressed
- [ ] Body text contrast: 4.5:1
- [ ] Component contrast: 3:1

**Performance:**
- [ ] Lists >100 items use virtualization (`@tanstack/react-virtual` or similar)
- [ ] Components memoized with `React.memo`
- [ ] Callbacks wrapped in `useCallback`
- [ ] Expensive computations use `useMemo`
- [ ] Fixed row heights for virtual scrolling

---

### PCF Development Standards (KM-PCF-CONTROL-STANDARDS.md)

**Project Structure:**
- [ ] Manifest configured with all properties
- [ ] Resource strings in `.resx` files (no hard-coded text)
- [ ] Generated types used from `ManifestTypes.ts`

**Lifecycle Management:**
- [ ] `init()`: Subscribe to events, enable container resize tracking
- [ ] `updateView()`: Mount/update React component
- [ ] `getOutputs()`: Return selected IDs, counts, last action
- [ ] `destroy()`: Unmount React, remove listeners

**Dataset API:**
- [ ] Check `dataset.loading` before rendering
- [ ] Use `dataset.paging.loadNextPage()` for paging
- [ ] Sort columns by `column.order`
- [ ] Use `getFormattedValue()` for display
- [ ] NEVER call `setValue()` on dataset records

**Localization:**
- [ ] All UI text in `.resx` files
- [ ] Accessed via `context.resources.getString()`
- [ ] Support for multiple locales (1033, 1036, etc.)

---

### Component Reusability (ADR-012)

**Shared Library:**
- [ ] Reusable components built in `src/shared/Spaarke.UI.Components/`
- [ ] Generic, configurable (no entity-specific logic)
- [ ] Exported via barrel exports (`index.ts`)
- [ ] Documented with JSDoc comments
- [ ] Unit tested (80%+ coverage)

**PCF Integration:**
- [ ] Imports from `@spaarke/ui-components`
- [ ] Minimal PCF-specific code (lifecycle adapter only)
- [ ] Converts PCF context to generic component props
- [ ] NO duplication of shared logic

---

## ✅ Code Review Checklist

**Fluent UI v9:**
- [ ] `grep -r "@fluentui/react\"" src/` returns ZERO results (no v8)
- [ ] All styling files use `makeStyles`, `tokens`, `shorthands`
- [ ] No hard-coded colors (`#`, `rgb(`, `rgba(`)
- [ ] No hard-coded spacing (`px` values outside tokens)
- [ ] `FluentProvider` present at component root

**Accessibility:**
- [ ] Run `npm run lint` - zero a11y errors
- [ ] Run axe-core accessibility tests
- [ ] Test keyboard navigation manually
- [ ] Test with screen reader (NVDA/JAWS)

**Performance:**
- [ ] Run Lighthouse performance audit (>90 score)
- [ ] Test with 10,000 records (should render <500ms)
- [ ] Verify virtualization (DOM <100 elements for large lists)
- [ ] React DevTools Profiler shows no excessive renders

**TypeScript:**
- [ ] `npm run build` succeeds with ZERO errors
- [ ] Strict mode enabled in `tsconfig.json`
- [ ] All props have interfaces
- [ ] No `any` types (except PCF context casting)

**Testing:**
- [ ] Unit tests: 80%+ coverage (statements, branches, functions, lines)
- [ ] Integration tests: Dataset binding, navigation, commands
- [ ] E2E tests: Sort, select, delete workflows
- [ ] Tests use `renderWithFluent` helper

---

## ✅ Deployment Checklist

**Build:**
- [ ] `npm run build` succeeds (shared library)
- [ ] `npm run build` succeeds (PCF control)
- [ ] No console errors or warnings

**Package:**
- [ ] Solution packaging succeeds
- [ ] Solution `.zip` size <10MB
- [ ] All resources included (strings, CSS, manifest)

**Documentation:**
- [ ] Component README created
- [ ] Props documented with examples
- [ ] Migration guide for future consumers
- [ ] Deployment guide updated

**Validation:**
- [ ] Import solution to test environment
- [ ] Add control to test form
- [ ] Verify all view modes work (Grid, Card, List)
- [ ] Verify commands execute (open, create, delete, refresh)
- [ ] Verify theme inheritance from host

---

## 🚫 Common Violations to Avoid

### Fluent UI v8 Import (CRITICAL)
```typescript
// ❌ PROHIBITED - Will fail code review
import { PrimaryButton } from "@fluentui/react";
import { Dropdown } from "@fluentui/react/lib/Dropdown";

// ✅ CORRECT
import { Button } from "@fluentui/react-components";
import { Dropdown } from "@fluentui/react-components";
```

### Hard-Coded Colors (CRITICAL)
```typescript
// ❌ PROHIBITED
style={{ backgroundColor: "#ffffff", color: "#0078d4" }}
const styles = { padding: "16px", margin: "8px" };

// ✅ CORRECT
import { makeStyles, tokens } from "@fluentui/react-components";
const useStyles = makeStyles({
  root: {
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorBrandForeground1
  }
});
```

### Missing FluentProvider (CRITICAL)
```typescript
// ❌ PROHIBITED
ReactDOM.render(<MyComponent />, container);

// ✅ CORRECT
ReactDOM.render(
  <FluentProvider theme={theme}>
    <MyComponent />
  </FluentProvider>,
  container
);
```

### DOM Manipulation (CRITICAL)
```typescript
// ❌ PROHIBITED
document.querySelector(".ms-Button").style.color = "red";
document.getElementById("myDiv").classList.add("active");

// ✅ CORRECT
const useStyles = makeStyles({ active: { color: tokens.colorPaletteRedForeground1 } });
<div className={mergeClasses(styles.root, isActive && styles.active)} />
```

### Missing Accessibility Labels (CRITICAL)
```typescript
// ❌ PROHIBITED
<Button icon={<DeleteIcon />} />

// ✅ CORRECT
<Button icon={<DeleteIcon />} aria-label="Delete document" />
```

---

## 📋 Sign-Off

**Before merging to main, confirm:**

- [ ] All critical standards documents read
- [ ] All checklist items verified
- [ ] Code review passed (2+ reviewers)
- [ ] All tests passing (unit, integration, E2E)
- [ ] Accessibility tests passing (axe-core)
- [ ] Performance benchmarks met (<500ms render, >90 Lighthouse)
- [ ] Documentation complete
- [ ] Deployment validated in test environment

**Reviewers:**
- Developer: _________________  Date: _________
- Tech Lead: ________________  Date: _________
- QA: ______________________  Date: _________

---

**Document Version:** 1.0
**Last Updated:** 2025-10-03
**Status:** Active - Sprint 5
