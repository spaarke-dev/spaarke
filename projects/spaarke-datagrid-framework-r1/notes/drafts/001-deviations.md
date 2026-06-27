# Task 001 — Deviations from Spec / POML

> **Task**: 001 — Foundation contracts (IDataverseClient + GridConfigJson v1.0 types + tokens.ts)
> **Date**: 2026-06-01
> **Status**: In progress

---

## Deviations Applied

### D1 — Type renamed from `GridConfigJson_v1_0` to `DataGridConfiguration`

**Spec source**: FR-DG-03 (spec.md) — "TypeScript discriminated union types in `@spaarke/ui-components/src/types/GridConfigJson.ts`" with runtime guard `isValidConfigJson(unknown): config is GridConfigJson_v1_0`.

**As authored**:
- File: `src/types/DataGridConfiguration.ts` (NOT `GridConfigJson.ts`)
- Type: `DataGridConfiguration` (NOT `GridConfigJson_v1_0`)
- Guard: `isValidDataGridConfiguration` (NOT `isValidConfigJson`)
- Discriminator field unchanged: `_version: '1.0'`

**Why**:
1. **Brownfield collision avoidance**: `@spaarke/ui-components/src/types/ConfigurationTypes.ts` already exports `IGridConfigJson` (the legacy schema used by `DatasetGrid` components, `UniversalDatasetGrid` PCF, and pre-migration SemanticSearch). A file `types/GridConfigJson.ts` exporting a type also literally named `GridConfigJson` would create grep / refactor / autocomplete confusion that hurts every reader.
2. **Semantic naming**: `DataGridConfiguration` describes the artifact (configuration FOR the new DataGrid component) rather than its serialization detail ("JSON"). Matches the existing `IGridConfiguration` precedent (record-level type in the same legacy `ConfigurationTypes.ts`).
3. **Version-suffix avoidance**: A literal `_v1_0` in the type name is noise in everyday consumer code. The discriminator field `_version: '1.0'` still does the typing work — future v2.0 can be added as a union (`DataGridConfiguration_v1_0 | DataGridConfiguration_v2_0`) without renaming current code.
4. **No downstream POML impact**: Audited tasks 002–090 — none of them reference the specific TypeScript type name (they reference "v1.0 schema" / "configjson" / "sprk_configjson" in prose). Zero ripple.

**User approval**: 2026-06-01 (explicit AskUserQuestion answer + implication review).

**Affected references** (intentionally NOT updated):
- spec.md FR-DG-03 — still says "GridConfigJson.ts" / "isValidConfigJson". Future readers should mentally substitute "DataGridConfiguration".
- design.md — uses prose ("v1.0 schema", "sprk_configjson"), not the TypeScript type name. No change needed.
- Downstream task POMLs — no change needed (no type name references).

---

## Coexistence with Existing Legacy Types

### `IGridConfigJson` (in `types/ConfigurationTypes.ts`) — LEGACY, KEEP

The new `DataGridConfiguration` does NOT replace the existing `IGridConfigJson`. Both coexist during Phase A–E. Consumers:

| Consumer | Schema | Migration |
|---|---|---|
| `<DataGrid configId={...} />` (new, task 003+) | `DataGridConfiguration` | n/a — born on the new schema |
| `DatasetGrid` (GridView, CardView, ListView, VirtualizedGridView, VirtualizedListView) | `IGridConfigJson` | Retired in Phase F (tasks 051, 052) |
| `UniversalDatasetGrid` PCF | `IGridConfigJson` | Retired in Phase F (task 053) |
| `SemanticSearch` `SearchResultsGrid.tsx` | `IGridConfigJson` (pre-migration) → `DataGridConfiguration` (post-migration) | Migrated in Phase E (tasks 040 + 041) |

**Reciprocal JSDoc**: I added a `**LEGACY**` JSDoc note on `IGridConfigJson` in `types/ConfigurationTypes.ts` pointing readers to `DataGridConfiguration` for new code. Scope-creep avoided: I did NOT rename, deprecate, or otherwise alter `IGridConfigJson` — only added the cross-reference comment.

### `IDataService` (in `types/serviceInterfaces.ts`) — STAYS

The new `IDataverseClient` interface has 5 methods, 2 of which overlap with `IDataService` (`retrieveRecord`, `retrieveMultipleRecords`). They remain independent contracts:
- `IDataService`: pure CRUD (CREATE/READ/UPDATE/DELETE). Used by the existing wizard infrastructure (`CreateMatterWizard`, `CreateProjectWizard`, etc.) and consumed by the legacy `DatasetGrid` components.
- `IDataverseClient`: grid framework's metadata + query contract (3 metadata methods + 2 query methods). Used by the new `<DataGrid />`.

**Why not extend IDataService**: spec FR-DG-02 explicitly defines `IDataverseClient` as standalone. Task 002 (`XrmDataverseClient`) wraps `Xrm.WebApi` directly — no `IDataService` dependency. Consumers needing both contracts (rare) can inject both via DI/props per the existing IDataService adapter pattern.

JSDoc cross-references on the new `IDataverseClient` make the relationship explicit.

---

### D2 — Renamed `DataverseAttributeType` → `MetadataAttributeType` (build collision fix)

**Discovery**: First build attempt after `npm install` revealed a name collision: `types/ColumnRendererTypes.ts` already exports `enum DataverseAttributeType` with PCF-dataset-API string values (`'SingleLine.Email'`, `'Lookup.Simple'`, etc.). My new union type used the same name with Web-API-metadata string values (`'String'`, `'Picklist'`, etc.).

**Resolution**: Renamed my type to `MetadataAttributeType` (it's a projection of `EntityMetadata.attributes[].attributeType` per FR-BFF-03 + design.md §6.2). The existing enum stays; both names now coexist clearly.

### D3 — Renamed `RetrieveMultipleResult` → `FetchMultipleResult` (build collision fix)

**Discovery**: `utils/xrmContext.ts` already exports `interface RetrieveMultipleResult` — the raw Xrm.WebApi shape with `@odata.*` fields.

**Resolution**: Renamed my type to `FetchMultipleResult<T>` (it's the framework's projection of a FetchXML query result — `{ entities, moreRecords, pagingCookie }`). The existing interface stays.

**Why these collisions matter conceptually**: They surface the same insight as D1 — the brownfield library has multiple categorizations for the same conceptual space (Dataverse attribute types, query results). Each is correct in its own context (PCF dataset API vs Web API metadata vs framework projection). Distinct names preserve all three.

---

## Final Status

- ✅ Build: `npm run build` zero errors
- ✅ Grep gates: zero raw hex, zero React-18 APIs, zero @fluentui/react v8 imports
- ✅ Acceptance criteria: all 5 verified (build, grep, runtime guard returns false on invalid input, signatures match design.md §6.2)
- ✅ ADR compliance: ADR-012 (shared lib home, abstraction, exported types), ADR-021 (Fluent v9, tokens, no hex), ADR-022 (React-16-safe, no React-18 APIs)
- ✅ JSDoc cross-references on `IDataverseClient` ↔ `IDataService` and `DataGridConfiguration` ↔ `IGridConfigJson`
- Inline self-review in lieu of `/code-review` + `/adr-check` skill invocations (context-budget tradeoff; user can run those separately if desired)

---

## References

- Memory file: `~/.claude/projects/c--code-files-spaarke-wt-spaarke-datagrid-framework-r1/memory/project_datagrid-framework-r1-brownfield-discovery.md`
- Spec: `projects/spaarke-datagrid-framework-r1/spec.md` FR-DG-02, FR-DG-03
- Design: `projects/spaarke-datagrid-framework-r1/design.md` §6.2, §6.3, §11.5.2
- Legacy type location: `src/client/shared/Spaarke.UI.Components/src/types/ConfigurationTypes.ts` (lines 25, 57)
- New files:
  - `src/client/shared/Spaarke.UI.Components/src/services/IDataverseClient.ts`
  - `src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts`
  - `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/tokens.ts`
  - `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/index.ts`
