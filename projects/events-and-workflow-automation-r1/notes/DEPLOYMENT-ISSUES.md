# Deployment Issues and Workarounds

> **Created**: 2026-02-02
> **Last Updated**: 2026-02-02
> **Status**: Active - Issues to track for future improvements

---

## Deployment Status Summary

| Component | Status | Notes |
|-----------|--------|-------|
| BFF API | ✅ Deployed | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Dataverse Tables | ✅ Deployed | 5 entities (Event, EventType, EventLog, FieldMappingProfile, FieldMappingRule) |
| PCF Controls | ✅ Deployed | 5 controls in `PowerAppsToolsTemp_sprk` solution |
| Form Configuration | 🔲 Pending | Manual configuration needed in Dataverse |

---

## Issues Encountered During PCF Deployment

### Issue 1: pcfconfig.json outDir Mismatch

**Symptom:** `pac pcf push` fails with error:
```
[pcf-1007] [Error] The specified value for 'outDir' does not resolve to the value defined in 'pcfconfig.json'
```

**Root Cause:** `pac pcf push` expects `outDir` to be `./out/controls`, but controls were configured with `./out`

**Workaround Applied:** Updated all 5 controls' `pcfconfig.json`:
```json
// Before
{ "outDir": "./out" }

// After
{ "outDir": "./out/controls" }
```

**Files Changed:**
- `src/client/pcf/AssociationResolver/pcfconfig.json`
- `src/client/pcf/EventFormController/pcfconfig.json`
- `src/client/pcf/RegardingLink/pcfconfig.json`
- `src/client/pcf/UpdateRelatedButton/pcfconfig.json`
- `src/client/pcf/FieldMappingAdmin/pcfconfig.json`

**Permanent Fix Needed:** Update PCF project templates to use correct outDir

---

### Issue 2: @spaarke/ui-components React Version Conflict

**Symptom:** npm peer dependency conflict during `pac pcf push`:
```
npm error Could not resolve dependency:
npm error peer react@"^18.2.0" from @spaarke/ui-components@2.0.0
npm error Conflicting peer dependency: react@18.3.1
```

**Root Cause:**
- Shared library (`@spaarke/ui-components`) requires React 18
- PCF controls must use React 16 (per ADR-022 - Dataverse platform constraint)
- These peer dependencies are incompatible

**Solution Applied (AssociationResolver):**
1. Removed `@spaarke/ui-components` dependency from `package.json`
2. Inlined required types directly in `FieldMappingHandler.ts`:
   - `SyncMode` enum
   - `IFieldMappingProfile` interface
   - `IFieldMappingRule` interface
   - `IMappingResult` interface
3. Implemented **working** `FieldMappingService` that queries Dataverse directly via WebAPI

**Files Changed:**
- `src/client/pcf/AssociationResolver/package.json` (removed dependency)
- `src/client/pcf/AssociationResolver/handlers/FieldMappingHandler.ts` (inlined types + working service)

**Current State:**
- ✅ FieldMappingService queries `sprk_fieldmappingprofile` and `sprk_fieldmappingrule` directly
- ✅ Field mapping auto-application on record selection is FUNCTIONAL
- ✅ "Refresh from Parent" button is FUNCTIONAL

**Future Reconciliation (React Migration Project):**
- When `@spaarke/ui-components` is fixed for React 16, this local implementation can be replaced
- The local implementation uses the same interfaces, so replacement will be straightforward
- Both libraries can coexist until migration completes

---

### Issue 3: Missing @types/xrm

**Symptom:** TypeScript compilation error:
```
TS2503: Cannot find namespace 'Xrm'
```

**Root Cause:** Missing Xrm type definitions for Dataverse client API

**Workaround Applied:** Added `@types/xrm` to devDependencies:
```bash
npm install --save-dev @types/xrm@9
```

**Permanent Fix Needed:** Update PCF project templates to include `@types/xrm`

---

### Issue 4: TypeScript Strict Mode Errors

**Symptom:** Multiple TypeScript errors:
```
TS7006: Parameter 'e' implicitly has an 'any' type
TS2322: Type 'ReactNode' is not assignable to type...
```

**Root Cause:** TypeScript strict mode requiring explicit types

**Workarounds Applied:**

1. **FieldMappingHandler.ts** - Added explicit type annotation:
   ```typescript
   // Before
   result.errors = mappingResult.errors.map(e => e.message);

   // After
   result.errors = mappingResult.errors.map((e: { message: string }) => e.message);
   ```

2. **RulesList.tsx (FieldMappingAdmin)** - Fixed Badge icon type:
   ```typescript
   // Before
   ): { icon: React.ReactNode; className: string; label: string }

   // After
   ): { icon: JSX.Element | undefined; className: string; label: string }
   ```

**Files Changed:**
- `src/client/pcf/AssociationResolver/handlers/FieldMappingHandler.ts`
- `src/client/pcf/FieldMappingAdmin/components/RulesList.tsx`

**Permanent Fix Needed:** None - these are proper TypeScript fixes

---

### Issue 5: File Lock Error During pac pcf push

**Symptom:** Build fails with:
```
MSB3231: Unable to remove directory "obj\Debug\Metadata".
The process cannot access the file because it is being used by another process.
```

**Root Cause:** Windows file locking during cleanup after solution pack

**Workaround Applied:** Per deployment guide, this error is **harmless** - the solution ZIP was already packed successfully. Imported directly:
```bash
pac solution import --path "obj/PowerAppsToolsTemp_sprk/bin/Debug/PowerAppsToolsTemp_sprk.zip" --publish-changes
```

**Permanent Fix Needed:** None - known PAC CLI issue on Windows, workaround is documented

---

## Outstanding Items

### High Priority

| Item | Description | Impact |
|------|-------------|--------|
| **Form configuration** | PCF controls need to be added to Dataverse forms manually | Controls are deployed but not visible to users |
| **Redeploy AssociationResolver** | Need to redeploy with working FieldMappingService | Current deployed version has stub |

### Medium Priority

| Item | Description | Impact |
|------|-------------|--------|
| **React 16 shared library** | Build React 16-compatible version of @spaarke/ui-components | Currently using local implementation |
| **PCF template updates** | Update templates with correct pcfconfig.json and @types/xrm | Prevents issues in future PCF projects |

### Low Priority

| Item | Description | Impact |
|------|-------------|--------|
| **Shared library build** | Fix @spaarke/ui-components build (missing Fluent UI deps) | Library cannot be built locally |

### Resolved Items

| Item | Resolution | Date |
|------|------------|------|
| **FieldMappingService stub** | Implemented working service that queries Dataverse directly | 2026-02-02 |

---

## Form Configuration Checklist (Manual Steps)

After PCF deployment, these forms need manual configuration:

### Event Main Form (`sprk_event`)
- [ ] Add **AssociationResolver** to Regarding Record Type field
- [ ] Add **EventFormController** for dynamic field visibility
- [ ] Add **UpdateRelatedButton** for push mappings

### Field Mapping Rule Form (`sprk_fieldmappingrule`)
- [ ] Add **FieldMappingAdmin** control

### Event View
- [ ] Add **RegardingLink** to Regarding column in grid

---

## Related Files

- [deployment-guide.md](deployment-guide.md) - Full deployment procedures
- [STUBS.md](STUBS.md) - API stub documentation
- [phase5-deployment-readiness.md](phase5-deployment-readiness.md) - BFF API deployment status

---

*Document created: 2026-02-02*
