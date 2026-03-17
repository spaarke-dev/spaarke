# React Versioning Constraint

## Rule

The shared UI library (`@spaarke/ui-components`) serves two consumer tiers with different React versions:

| Consumer | React Version | Import Path |
|----------|--------------|-------------|
| PCF controls | 16/17 (platform-provided, immutable) | `@spaarke/ui-components/src/pcf-safe` |
| Code pages | 19 (bundled) | `@spaarke/ui-components` |
| Solutions (Vite) | 18+ (bundled) | `@spaarke/ui-components` |

## MUST

- PCF controls MUST import from `pcf-safe.ts` barrel — never from the main `index.ts`
- Components exported from `pcf-safe.ts` MUST NOT use React 18+ APIs:
  - `useId()`, `useDeferredValue()`, `useTransition()`, `useSyncExternalStore()`
  - `use()` (React 19)
  - `createRoot()`, `hydrateRoot()` (React 18 render API)
  - Lexical editor (requires React 18+)
- Code pages using webpack MUST include React dedup aliases in webpack.config.js
- Code pages using Vite MUST include React dedup in vite.config.ts `resolve.dedupe`

## MUST NOT

- MUST NOT import from main barrel (`@spaarke/ui-components`) in PCF controls
- MUST NOT add React 18+ APIs to any component listed in `pcf-safe.ts`
- MUST NOT assume shared library components work with React 16 unless they are in `pcf-safe.ts`

## Verification

When modifying a component that is exported from `pcf-safe.ts`:
1. Check: does this change use any React 18+ API?
2. If yes: remove from `pcf-safe.ts`, add to main barrel only
3. If removing would break a PCF control: the change cannot proceed without refactoring the PCF control

## Background

- ADR-022: PCF controls use platform-provided React 16/17 (cannot be changed)
- React 19 came out Dec 2024; code pages upgraded, PCF cannot
- Shared library declares `peerDependencies: { react: ">=16.14.0" }` to support both
- Without this constraint, React 18+ APIs in shared components cause silent runtime failures in PCF controls (hooks error, no compile-time warning)
