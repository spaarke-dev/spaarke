# Task 011 — BLOCKED (ESCALATION)

> **Verdict**: ESCALATION — the escalation trigger in `011-consolidate-navprop-discovery.poml` fired.
> **Date**: 2026-07-09
> **Rigor**: FULL · model-tier opus · effort high
> **Decision needed from**: main session / project owner (choose a resolution path below).

## Trigger

The POML `<escalation><trigger>` and the dispatch instruction both say:

> "FIRST diff all 7 private `_discoverNavProps` copies against each other. If any two differ in a non-trivial way (not just naming/whitespace), STOP — do NOT silently collapse them."

They do differ non-trivially. **`matterService._discoverNavProps` is a structural outlier** that returns a different data type and feeds the (task-011-forbidden) create-payload path.

## The diff (all 7 copies)

### Group A — behavior-identical array form (6 of 7): TRIVIALLY reconcilable

All return `Promise<INavPropEntry[]>` with the same 3 fields (`columnName`, `navPropName`, `referencedEntity`), the same URL, the same `$select` (**includes** `ReferencedEntity`), the same cache semantics (`Record<string, INavPropEntry[]>`), and the same empty-array-on-failure behavior. They differ ONLY in cosmetic/trivial ways:

| Service | Interface | `$select` incl. `ReferencedEntity` | `console.info` diagnostic | Extra param | Test-reset export |
|---|---|---|---|---|---|
| `eventService` | `NavPropEntry` (local, same shape) | YES | no | — | — |
| `invoiceService` | `INavPropEntry` (imported) | YES | yes | — | `_resetInvoiceServiceNavPropCacheForTests` |
| `projectService` | `NavPropEntry` (local, same shape) | YES | yes | — | — |
| `workAssignmentService` | `INavPropEntry` (imported) | YES | yes | — | — |
| `reportCardService` | `INavPropEntry` (imported) | YES | yes | — | `_resetReportCardServiceNavPropCacheForTests` |
| `todoService` | `INavPropEntry` (imported) | YES | no | `fetchImpl: typeof fetch = globalThis.fetch` (test seam; default = identical behavior) | `_resetTodoServiceNavPropCacheForTests` |

Differences here are log-tag strings, an optional `console.info`, local-vs-imported (identical-shape) interface, and todo's optional `fetchImpl` injection seam that defaults to `fetch`. These are the "naming/whitespace"-class differences the trigger explicitly permits collapsing. A shared `discoverNavProps(entityLogicalName, fetchImpl = fetch)` returning `INavPropEntry[]` would serve all 6 with identical runtime behavior.

### Group B — structural outlier (1 of 7): NON-TRIVIAL divergence → the blocker

**`matterService._discoverNavProps` (matterService.ts:107)** differs in three coupled ways:

1. **Return type**: `Promise<Record<string, string>>` — a `columnLogicalName → navPropName` **map**, NOT an `INavPropEntry[]` array. Cache type is `Record<string, Record<string, string>>`.
2. **Query shape**: its `$select` is `ReferencingAttribute,ReferencingEntityNavigationPropertyName` — it **omits `ReferencedEntity`**. So the map form has no referenced-entity data at all; it cannot be losslessly converted to the array form from cache (the referenced-entity dimension is simply not fetched).
3. **Downstream resolution contract**: matter resolves nav-props **by column logical name**, not by referenced entity:
   - `_resolveNavProp(map, col) => map[col] ?? col` (matterService.ts:147)
   - Consumed in the **create-payload path**:
     - `matterService.ts:317` — `const navProp = navPropMap[lk.col] ?? lk.col;` builds lookup `@odata.bind` keys written into the create payload.
     - `matterService.ts:354` — `_resolveNavProp(docNavProps, 'sprk_matter')` for the document association bind.
     - `matterService.ts:443` — `_resolveNavProp(navPropMap, 'sprk_assignedlawfirm1')` for counsel assignment.

The other 6 resolve **by referenced entity** via the shared `findNavProp(entries, referencedEntity, columnHint)`.

## Why this is a genuine STOP (not silently collapsible)

- Collapsing matter into the shared array form requires rewriting `_resolveNavProp` and the three `navPropMap[...]`/`_resolveNavProp(...)` call sites (317/354/443) from column-keyed to entity-keyed lookup. Those call sites **are the create-payload construction path**.
- Project constraint 3 (dispatch + POML `<constraint source="project">`) is explicit: **"Do NOT change any service's create-payload output in this task — only the SOURCE of the nav-prop discovery function."** Any matter collapse touches exactly that forbidden surface and carries real payload-regression risk (e.g. `sprk_assignedlawfirm1`, document-matter bind).
- matter's map form also drops `ReferencedEntity` from the fetch, so a shared array-returning function would change matter's network query shape and cached data even before any call-site rewrite.

## Resolution paths (choose one)

**Path A — Partial consolidation (recommended, lowest risk).**
Consolidate the **6 array-form services** onto one shared `discoverNavProps(entityLogicalName, fetchImpl = fetch): Promise<INavPropEntry[]>` (co-located with `findNavProp` in `PolymorphicResolverService.ts`), and **leave matterService's map form as-is** for now. Re-point the 3 test-reset exports (`_resetInvoiceServiceNavPropCacheForTests`, `_resetReportCardServiceNavPropCacheForTests`, `_resetTodoServiceNavPropCacheForTests`) at the shared cache. Outcome: 6→1 dedupe + 1 documented remaining outlier. Note this does NOT fully satisfy the POML's "ONE shared implementation the engine and ALL services reuse" — matter stays separate — so it needs owner sign-off that 6-of-7 is the accepted scope for task 011, with matter's convergence deferred (its own task, sequenced so the create-payload change gets its own review + `applyResolverFields`-style verification).

**Path B — Full convergence including matter (higher risk, larger scope).**
Rewrite matterService to consume the shared `INavPropEntry[]` and re-express `_resolveNavProp` / call sites 317/354/443 as entity- or column-keyed lookups over the array. This **does** change the nav-prop discovery consumption in matter's create-payload path and therefore **conflicts with constraint 3**; it should be surfaced under CLAUDE.md §6.5 (Path A project-scoped exception with explicit payload-equivalence tests, or split into a dedicated matter task). Requires a matterService create-payload regression test proving byte-identical binds before/after.

**Path C — Shared util serves both shapes.**
Shared `discoverNavProps` returns `INavPropEntry[]`; add a tiny shared adapter `toNavPropMap(entries): Record<string,string>` so matter keeps its map-based call sites unchanged while sourcing from the one discovery function. Lower payload risk than B (call sites unchanged) but matter's fetch must now include `ReferencedEntity`, and matter's cache key/shape changes — still a behavior-adjacent change to a create-payload-feeding function, so still wants owner sign-off + a matter payload test.

## What was NOT done (per STOP)

- No code changed. No shared `discoverNavProps` was written.
- No build/test run for a change (there is no change to verify).
- POML status set to `blocked` (not `completed`). TASK-INDEX.md / current-task.md left for the main session per dispatch boundary.

## Recommendation

Path A for task 011 (dedupe the 6 identical copies now; unblocks task 012's lookup `@odata.bind` binding via the shared array-form `discoverNavProps` + existing `findNavProp`), and file matter's convergence (Path B/C) as a **separate** task so the create-payload change gets isolated review. Awaiting owner/main-session decision before writing any code.
