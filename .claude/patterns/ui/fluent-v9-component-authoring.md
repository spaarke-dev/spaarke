# Fluent v9 Component Authoring

> **Last Reviewed**: 2026-07-03 (added shared-lib `WizardShell.initialStepId` prop section for edit-mode entry)
> **Status**: Current

## When

Authoring a new component in `src/client/shared/Spaarke.UI.Components/` OR modifying any Fluent v9 React component in `src/client/{code-pages,external-spa,office-addins,pcf}/**/components/`.

## Read These Files

1. `src/client/shared/Spaarke.UI.Components/src/index.ts` — existing exports; check before creating new
2. `knowledge/fluent-ui-v9/samples/fluentui_react-v9/Button/ButtonAppearance.stories.tsx` — slot composition pattern
3. `knowledge/fluent-ui-v9/samples/fluentui_react-v9/Provider/FluentProviderDefault.stories.tsx` — root mount
4. Drill-down only if needed: `knowledge/fluent-ui-v9/docs/slots-architecture.md` (when authoring novel composition), `knowledge/fluent-ui-v9/docs/styling-griffel.md` (when debugging style precedence)

## Constraints

- **ADR-021**: All UI Fluent v9 only. No `@fluentui/react` (v8). No hard-coded colors.
- **ADR-012**: If a component fits >1 surface, it belongs in `Spaarke.UI.Components` — do NOT duplicate.

## Key Rules

- `makeStyles` at **module scope** (never inside the component body — re-creates styles every render).
- All colors / spacing / radius via `tokens.*` from `@fluentui/react-components`. NEVER raw hex / `var(--...)` / rgb literals.
- `mergeClasses(componentClasses, props.className)` — **props.className LAST** so callers override.
- Use `shorthands.border()` / `shorthands.padding()` / `shorthands.margin()` — Griffel rejects CSS shorthand properties.
- Default to slot composition (`<Component icon={...} />`). Fall back to the hooks API only when slots can't express it.
- `FluentProvider` mounts ONCE at the surface root — never per-component (except portal re-wrap, see [`fluent-v9-portal-gotcha.md`](./fluent-v9-portal-gotcha.md)).
- If shipping in `Spaarke.UI.Components` (consumed by PCF + Code Pages), the component MUST be React-16.14-safe — no `createRoot`, no React-18-only hooks. See [`fluent-v9-react-version-boundaries.md`](./fluent-v9-react-version-boundaries.md).

## Code Pattern

```tsx
import {
  makeStyles, mergeClasses, tokens, shorthands,  // ← always import tokens + shorthands
  Button, FluentProvider, webLightTheme,
} from '@fluentui/react-components';

const useStyles = makeStyles({                   // ← module scope, NOT inside component
  root: {
    color: tokens.colorNeutralForeground1,      // ← tokens, not hex
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalM),
  },
});

export const MyControl: React.FC<MyControlProps> = ({ className, label }) => {
  const styles = useStyles();
  return (
    <Button
      className={mergeClasses(styles.root, className)}  // ← props.className LAST
      appearance="primary"
    >
      {label}
    </Button>
  );
};
```

## See Also

- [`fluent-v9-theming.md`](./fluent-v9-theming.md) — FluentProvider setup + Spaarke theming convention
- [`fluent-v9-portal-gotcha.md`](./fluent-v9-portal-gotcha.md) — `Popover` / `Tooltip` / `Dialog` / `Menu` / `Toast` re-wrap
- [`../../knowledge/fluent-ui-v9/NOTES.md`](../../../knowledge/fluent-ui-v9/NOTES.md) — Spaarke-specific commentary (stub, fill from real use)

---

## Shared-lib `WizardShell` — `initialStepId` prop for edit-mode entry (added 2026-07-03)

**Context**: `WizardShell` (in `@spaarke/ui-components`) is the generic multi-step dialog shell used by every Spaarke wizard (`CreateEventWizard`, `CreateMatterWizard`, `WorkspaceLayoutWizard`, `DocumentUploadWizard`, `PlaybookLibrary`, `SummarizeFilesWizard`, `AllDocuments`, `FindSimilar`, `CreateTodoWizard`, `CreateWorkAssignmentWizard`, `CreateProjectWizard`).

**New prop** (since R2, 2026-07-03): `initialStepId?: string`.

When the wizard opens in "edit" (or "saveAs") mode, the operator typically wants to land on the **working step** directly — not walk through the pre-populated setup steps. Prior implementations tried to solve this with imperative `wizardRef.current?.nextStep()` calls inside a `requestAnimationFrame` after mount. That approach relied on `canAdvance()` returning true after async fetches settled — a race we could never reliably win on first mount.

**The correct pattern** is to pass `initialStepId` at mount:

```tsx
<WizardShell
  ref={wizardRef}
  open={true}
  embedded={true}
  steps={steps}
  initialStepId={mode === "edit" || mode === "saveAs" ? STEP_REVIEW_SAVE : undefined}
  // ...other props
/>
```

`WizardShell` reads `initialStepId` from the reducer's lazy-init callback (`useReducer(reducer, arg, init)` — the third arg runs ONCE on mount). If the id matches one of the step configs, that step becomes `'active'` and all earlier steps are `'completed'`. If the id is missing or unknown, the wizard opens at step 0 (unchanged behavior).

### When to use it

- **Edit mode** — jump to the primary editing step (e.g., `ArrangeStep` in the layout wizard, `EnterInfoStep` in the matter wizard)
- **URL-param-driven entry** — accept a `startAtStep` URL param and forward to `initialStepId`; enables deep-linking into wizard flows
- **Gear-icon edit affordances** — where a gear icon on a shell surface should open the wizard at a specific step (Choose Layout, Arrange Sections, Configure Fields, etc.)

### When NOT to use it

- **Create mode** — always start at step 0 (undefined `initialStepId` is correct)
- **Post-mount step changes** — `initialStepId` is captured ONCE on mount. Use the imperative handle (`wizardRef.current?.nextStep()` / `prevStep()`) for post-mount navigation.

### Related contract

- If `initialStepId` matches a step but that step's `canAdvance()` returns false, the wizard opens at the step normally (the Next button will be disabled — same behavior as reaching the step via manual navigation). This is intentional: the state's readiness is the step's responsibility, not the shell's.
- If earlier-step state is not populated when the wizard opens at the target step, the earlier steps' status will still be marked `'completed'` visually. Consumers should populate the state BEFORE mounting (e.g., via a fetch effect that shows a loading placeholder until data is ready — see `WorkspaceLayoutWizard/src/App.tsx` for the reference pattern).

### Live example

- Definition: [`WizardShell.tsx:206-215`](../../../src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell.tsx) + [`wizardShellReducer.ts:32-50`](../../../src/client/shared/Spaarke.UI.Components/src/components/Wizard/wizardShellReducer.ts) — reducer lazy-init picks the initial step
- Consumer: [`WorkspaceLayoutWizard/src/App.tsx:824-855`](../../../src/solutions/WorkspaceLayoutWizard/src/App.tsx) — resolves `initialStepId` from `mode` + optional `startAtStep` URL param, captures in a `useRef` at mount
