# Lessons Learned -- Legal Operations Workspace (Home Corporate) R1

> **Project**: home-corporate-workspace-r1
> **Completed**: 2026-02-18
> **Total Tasks**: 42 tasks across 5 phases
> **Execution Model**: Agent teams with parallel subagent execution

---

## PCF Build and React Compatibility

### React Version Declaration vs. Runtime

**Finding**: The PCF manifest (`ControlManifest.Input.xml`) must declare React 16.14.0 for `pcf-scripts` compatibility, even though the Custom Page runs React 18 at runtime.

**Details**: The pcf-scripts toolchain validates against the declared React version. Declaring React 18 directly causes build failures. The workaround is to declare 16.14.0 in the manifest for build compatibility while using React 18 `createRoot` API at runtime since the Custom Page runs in its own iframe with its own React instance.

**Recommendation**: Document this pattern for future Custom Page projects. This is an ADR-022 exception specific to Custom Pages.

---

## Fluent UI v9 makeStyles and Griffel Type System

### Token Type Narrowing

**Finding**: Fluent UI v9 `tokens` (e.g., `tokens.colorNeutralBackground1`) return `string` at the type level, but Griffel's `makeStyles` type definitions expect narrower CSS property types. This causes TypeScript errors when assigning tokens to CSS properties that have specific union types.

**Workaround**: Use `as string` casts when passing Fluent tokens to Griffel style properties. Example:

```typescript
const useStyles = makeStyles({
  root: {
    backgroundColor: tokens.colorNeutralBackground1 as string,
  },
});
```

**Recommendation**: This is a known Fluent UI / Griffel type mismatch. Track upstream fixes in `@fluentui/react-components`. The `as string` cast is safe because the tokens always resolve to valid CSS values.

---

## SkeletonItem Size Constraints

**Finding**: The `SkeletonItem` component from `@fluentui/react-components` only accepts specific numeric values for its `size` prop: 8, 12, 16, 20, 24, 28, 32, 36, 40, 48, 56, 64, 72, 96, 120, 128.

**Details**: Passing arbitrary pixel values (e.g., `size={44}`) causes a runtime error or renders incorrectly. This is not immediately obvious from the TypeScript types.

**Recommendation**: Always check the allowed `size` values when using `SkeletonItem`. Use the closest valid size or combine multiple skeleton items for custom dimensions.

---

## Fluent UI Icon Names

**Finding**: Icon names in `@fluentui/react-icons` must be verified against the actual package exports. Common assumptions about icon names are often wrong.

**Example**: `BellRegular` does not exist -- the correct name is `AlertRegular`. Similarly, other icons may have non-obvious names.

**Recommendation**: Always verify icon names by checking the `@fluentui/react-icons` package documentation or using IDE autocomplete. Do not guess icon names based on visual appearance.

---

## Bundle Size Management

**Finding**: The final PCF bundle reached approximately 4.5 MiB even with platform libraries (React, Fluent UI) externalized via the PCF platform library mechanism.

**Root Cause**: Fluent UI icon chunks are large. Each imported icon set adds significant weight. Tree-shaking helps but icon bundles remain substantial.

**Mitigations Applied**:
- Platform library externalization (React + Fluent UI core)
- Code splitting by component block
- Lazy loading for dialog components
- Selective icon imports (named imports, not barrel imports)

**Recommendation**: For future projects, audit icon usage early. Consider a shared icon utility that re-exports only needed icons to improve tree-shaking. The 5 MiB budget is tight with Fluent v9.

---

## Solution Packaging: ZIP Format

**Finding**: The `pack.ps1` script must use `System.IO.Compression` (.NET compression API) instead of PowerShell's built-in `Compress-Archive` cmdlet for creating solution ZIP files.

**Root Cause**: `Compress-Archive` creates ZIP entries with backslash path separators on Windows. Dataverse solution import expects forward slashes in ZIP entry paths. Using `System.IO.Compression.ZipFile` allows explicit control over entry path separators.

**Recommendation**: Always use `System.IO.Compression` in PowerShell scripts that create ZIP files for Dataverse deployment. This is a Windows-specific issue that does not surface on macOS/Linux.

---

## CSS-in-JS: styles.css is Optional

**Finding**: When using Griffel (`makeStyles`) for all component styling, the traditional `styles.css` file included in PCF project templates is unnecessary. Griffel injects all styles at runtime via JavaScript.

**Details**: The `css` resource entry in `ControlManifest.Input.xml` can be omitted entirely when Griffel handles 100% of styling. This simplifies the build and avoids maintaining a separate CSS file.

**Recommendation**: For new Fluent v9 PCF projects, skip the `styles.css` file from the start. Only add it if there are genuine needs for static CSS (e.g., third-party library overrides).

---

## Parallel Task Execution

### What Went Well

**42 tasks executed across 5 phases**, with most parallelized via subagents:

- **Batch 1** (4 tasks in parallel): Tasks 001, 008, 017, 018 -- zero-dependency foundation
- **Batch 2** (2 tasks): Tasks 002, 009 -- unblocked after batch 1
- **Batch 3** (10 tasks simultaneously): Massive parallel execution across independent modules
- **Batch 4** (7 tasks): Cross-module parallel with careful file ownership
- **Batch 5** (3 tasks): Quick follow-up batch
- **Batch 6** (2 tasks): Final dependency chain unblocked
- **Sequential**: Tasks 030, 037, 040, 041, 043, 090 -- dependency-gated

**Key Success Factor**: Clear file ownership boundaries per task prevented merge conflicts. Each parallel task operated in its own component directory or API endpoint file.

### What Could Be Improved

- **Context window pressure**: Large parallel batches consume significant context. Checkpointing after every 3 tasks is essential.
- **Dependency tracking**: The TASK-INDEX.md parallel group system worked well. Recommend keeping it for future projects.
- **Subagent coordination**: When tasks share interfaces (e.g., shared TypeScript types), define the interface in a dedicated task first before parallelizing consumers.

---

## General Recommendations for Future Projects

1. **Define shared types early**: Task 002 (Shared Interfaces) was a critical foundation. Having types defined before UI and API work begins prevents rework.

2. **Use platform libraries**: PCF platform library support (React, Fluent) significantly reduces bundle size. Always configure this in the manifest.

3. **Test scoring logic in isolation**: The priority and effort scoring engines (Tasks 017-018) benefited from comprehensive unit tests (Task 035). Deterministic rule-based scoring is straightforward to test and verify.

4. **Dark mode from the start**: Building with Fluent tokens and `makeStyles` from day one made the dark mode audit (Task 031) a verification task rather than a rework task.

5. **BFF endpoint separation**: Creating separate endpoint files per feature area (portfolio, health, scoring, AI) kept the API clean and enabled parallel development.

---

*This document captures lessons from the home-corporate-workspace-r1 project for reference by future Spaarke projects.*
