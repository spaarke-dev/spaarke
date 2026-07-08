# Task 022 - Consumer Audit for FieldMappingHandler

**Audit date**: 2026-07-02
**Scope**: `grep -R "FieldMappingHandler" src/` across full repo

## Findings

### Source file (to be moved)
- `src/client/pcf/AssociationResolver/handlers/FieldMappingHandler.ts` — the definition (547 LOC)

### Direct consumers (imports)
| File | Symbols imported | Import path |
|---|---|---|
| `src/client/pcf/AssociationResolver/AssociationResolverApp.tsx:47-50` | `FieldMappingHandler`, `createFieldMappingHandler`, `IFieldMappingApplicationResult` | `./handlers/FieldMappingHandler` |
| `src/client/pcf/AssociationResolver/hooks/useMappingToast.tsx:17` | `IFieldMappingApplicationResult` | `../handlers/FieldMappingHandler` |

### Documentation / non-code references (informational only)
- `src/client/webresources/js/sprk_todo_regarding_presave.js:28` — comment reference to path; will be updated by task 072 (separate)
- `src/client/pcf/AssociationResolver/Solution/Controls/.../bundle.js` — compiled artifact; regenerated on build

### Tests
- **No existing tests reference FieldMappingHandler** (confirmed by scanning `__tests__/` dirs in both `AssociationResolver/` and `Spaarke.UI.Components/src/services/__tests__/`).
- No test migration needed. The existing `FieldMappingService.test.ts` in the shared lib tests the *other* (stubbed) `FieldMappingService` — unrelated to what we're moving.

## Type collision analysis

Shared lib `src/types/FieldMappingTypes.ts` already exports:
- `SyncMode` (different enum values but same name)
- `IFieldMappingProfile` (different shape: uses `sourceEntity`/`targetEntity` strings vs handler's `sourceRecordTypeId`/`sourceEntityLogicalName`)
- `IFieldMappingRule` (different shape: has `sourceFieldType`/`profileId`; handler's has `mappingType`/`executionOrder`)
- `IMappingResult` (different shape)

The PCF handler file has its own inline `export`ed versions of these types. If moved as-is, this causes duplicate-export errors from the shared lib's root barrel.

**Resolution**: Remove `export` keyword from the collision-prone types (`SyncMode`, `IFieldMappingProfile`, `IFieldMappingRule`, `IMappingResult`, `IFieldMappingServiceConfig`) since NO consumer imports them. Keep `export` on the consumer-facing surface: `FieldMappingHandler`, `createFieldMappingHandler`, `IFieldMappingApplicationResult`, `IFieldMappingHandlerConfig`.

## Behavior-preservation verification

- No signature change to consumer-facing exports
- No logic change inside `FieldMappingHandler` class
- Internal `FieldMappingService` class remains identical (private to the moved file)
- Types with matching names in shared lib are STRUCTURALLY DIFFERENT but not consumed by anyone using this handler, so no consumer breaks

## Post-move verification checklist

- [x] AR-app import switches to `@spaarke/ui-components`
- [x] `useMappingToast.tsx` import switches to `@spaarke/ui-components`
- [x] `grep -R "FieldMappingHandler" src/client/pcf/AssociationResolver` returns only `@spaarke/ui-components` imports
- [x] Both packages build clean
