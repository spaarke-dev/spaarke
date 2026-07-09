# Task 010 — FieldMappingService engine shell + types — Notes

**Completed**: 2026-07-09 · Rigor: FULL · Model: opus@high · Verdict: COMPLETED

## What was done

Replaced the stubbed, PCF-`ComponentFramework.WebApi`-bound `FieldMappingService.ts`
(dead code — every Dataverse method returned `[]`) with a context-agnostic engine
SHELL, and rewrote `FieldMappingTypes.ts` to mirror the extended BFF DTO (task 002).

### Files changed
- **`src/client/shared/Spaarke.UI.Components/src/services/FieldMappingService.ts`** (rewrite)
  — the engine shell + BFF fetch + graceful 404 + result contract + per-rule dispatch seams.
- **`src/client/shared/Spaarke.UI.Components/src/types/FieldMappingTypes.ts`** (rewrite)
  — profile/rule interfaces mirroring the DTO (all 5 new fields) + `IMappingResult`.
- **`src/client/shared/Spaarke.UI.Components/src/services/index.ts`** (barrel) — replaced
  `export { FieldMappingService }` with `export { applyFieldMappings }` + `IApplyFieldMappingsArgs`.
- **`src/client/shared/Spaarke.UI.Components/src/types/index.ts`** (barrel) — removed the stray
  `FieldMappingService` re-export (a service exported from the *types* barrel). Removing it also
  eliminates the pre-existing ambiguous double-export, so `applyFieldMappings` is now cleanly
  importable from the top-level `@spaarke/ui-components` barrel.
- **`src/client/shared/Spaarke.UI.Components/src/services/__tests__/FieldMappingService.test.ts`**
  (deleted, `git rm`) — it tested the removed dead-code API (`getProfiles`, `applyMappings`,
  `validateTypeCompatibility`, `new FieldMappingService({ webApi })`). New engine tests are owned by
  tasks 012–014 (incl. the negative test asserting no `source === target` guard). Flagged here for the
  project-close `/test-diet` reconciliation.

## The contract established (tasks 012/013/014 build on this)

### Public entrypoint
```ts
applyFieldMappings(args: IApplyFieldMappingsArgs): Promise<IMappingResult>

interface IApplyFieldMappingsArgs {
  sourceEntity: string;
  sourceId: string;
  targetEntity: string;
  payload: Record<string, unknown>;   // mutated in place by the apply engines
  dataService: IDataService;           // injected (types/serviceInterfaces)
  authenticatedFetch: AuthenticatedFetchFn;  // injected (reused from EntityCreationService)
  bffBaseUrl: string;
}

interface IMappingResult {
  profileFound: boolean;
  fieldsMapped: string[];
  warnings: string[];
}
```
Free function (not a class), mirroring `applyResolverFields` — matches the
`WizardHostProps`-injected surface so a Create*Wizard service can call it directly.

### Dispatch seams for 012/013
`applyRule()` contains a `switch (rule.mappingType)` with one branch per type, each marked:
- `SEAM[012:Copy]`     — verbatim copy incl. lookup `@odata.bind` (task 012)
- `SEAM[013:Default]`  — literal from `rule.defaultValue` when source empty (task 013)
- `SEAM[013:Concat]`   — format string from `rule.expression` (task 013)
- `SEAM[013:Template]` — format template from `rule.expression` (task 013)
- `default`            — unknown type → warn + skip (forward-compat)

Today every branch appends a `"…not yet implemented (task NNN)…"` warning via
`seamPendingMessage()` and skips. A banner comment above the switch fixes the
dispatch contract (switch on `mappingType`, never throw, append to
`ctx.fieldsMapped`/`ctx.warnings`) — later tasks fill branch bodies only.
The shared `IRuleApplyContext` already carries `dataService` + `sourceEntity`/`sourceId`
+ `payload` so the Copy seam (012) can read source values and write the payload without
signature churn.

## Constraint confirmations
- **No `ComponentFramework`/PCF import** — verified by grep; the only occurrences of
  "ComponentFramework" / "source === target" are in explanatory comments documenting their
  intentional absence.
- **Exactly ONE BFF call per invocation** — the single `authenticatedFetch(...)` is in
  `fetchProfile` (GET `/api/v1/field-mappings/profiles/{source}/{target}`). No other fetch.
- **404 / no-profile → graceful no-op** — returns `{ profileFound:false, fieldsMapped:[], warnings:[] }`
  with NO warning on a clean 404 (matches constraint 2 + NFR-06). Non-404 non-OK / network errors
  return `profileFound:false` WITH a diagnostic warning.
- **Never throws** — the top-level fetch and each per-rule apply are independently try/caught;
  failures become warnings and execution continues.
- **No `source === target` guard** anywhere (same-entity support is task 014).

## Verification
- `npm run build` (tsc, shared lib) → **0 errors**. `dist/services/FieldMappingService.{js,d.ts}`
  and `dist/types/FieldMappingTypes.d.ts` emitted. Barrels resolve.
- `npx eslint` on the 4 changed source files → **0 problems** (exit 0).
- Prereqs already satisfied: `node_modules` present, `@spaarke/sdap-client/dist` present (no rebuild needed).

## Quality gates (Step 9.5)
- **code-review**: CLEAN — 0 Critical / 0 Warning / 1 low Suggestion (defensive `bffBaseUrl ?? ''`
  on a typed string, retained intentionally for untyped JS-host callers). No AI smells.
- **adr-check**: CLEAN — ADR-012 ✅ (no PCF types; injected `IDataService`+`AuthenticatedFetchFn`;
  entity names are parameters), ADR-028 ✅ (injected `authenticatedFetch`, no token bridge). §10 N/A (client-only).

## Notes for downstream tasks
- **Type shape is string-based** to mirror the DTO: `mappingType`/`sourceFieldType`/`compatibilityMode`
  are string-literal unions widened with `| (string & {})`, NOT the old numeric `FieldType` enum
  (which had no external consumers — verified). `FieldMappingHandler.ts` keeps its own private
  `IFieldMappingProfile`/etc. and was unaffected.
- `normalizeProfile`/`normalizeRule` provide defensive JSON parsing so a malformed body degrades to
  an empty-rules no-op rather than throwing.
