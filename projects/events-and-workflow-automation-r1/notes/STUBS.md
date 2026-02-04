# Stub & Placeholder Tracking

> **Project**: Events and Workflow Automation R1
> **Last Updated**: 2026-02-01
> **Purpose**: Track all stubs, placeholders, and temporary implementations that need to be addressed before production.

---

## Code Convention

All stubs in code MUST use this comment format for grep-ability:

```typescript
// STUB: [category] - Description of what needs to be implemented
// Example: // STUB: [API] - Replace with actual field mapping API call
```

**Categories:**
- `[API]` - API endpoint that doesn't exist yet
- `[AUTH]` - Authentication/authorization placeholder
- `[CONFIG]` - Hardcoded configuration that should be dynamic
- `[DATA]` - Hardcoded data or mock data
- `[UI]` - UI placeholder or simplified implementation
- `[VALIDATION]` - Validation logic that needs enhancement
- `[ERROR]` - Error handling that needs improvement
- `[PERF]` - Performance optimization needed
- `[TEST]` - Test coverage needed

---

## Active Stubs

### Phase 1: Foundation (Tasks 001-005)

| ID | File | Line | Category | Description | Blocks | Target Task |
|----|------|------|----------|-------------|--------|-------------|
| S001 | AssociationResolver/AssociationResolverApp.tsx | ~165 | [API] | `applyFieldMapping()` calls non-existent `/api/field-mapping/apply` endpoint | Production use | 013, 014 |
| S002 | AssociationResolver/AssociationResolverApp.tsx | ~75 | [CONFIG] | `ENTITY_CONFIGS` hardcoded - should come from Dataverse metadata or config | Flexibility | 020 |
| S003 | EventFormController/EventFormControllerApp.tsx | ~85 | [API] | WebAPI query for `sprk_eventtype` - assumes entity exists with exact schema | Production use | 003 (done) |
| S004 | EventFormController/handlers/FieldVisibilityHandler.ts | ~40 | [UI] | Xrm.Page API usage - acceptable for now, may need update for modern formContext in future | Edge cases | Reviewed in 031 - acceptable |
| S005 | FieldMappingAdmin/FieldMappingAdminApp.tsx | ~95 | [API] | CRUD operations assume `sprk_fieldmappingprofile` and `sprk_fieldmappingrule` exist | Production use | 001, 002 (done) |
| S006 | FieldMappingAdmin/FieldMappingAdminApp.tsx | ~50 | [CONFIG] | `STRICT_COMPATIBLE` type matrix hardcoded - should be configurable | Flexibility | 011 |
| S007 | RegardingLink/RegardingLinkApp.tsx | ~45 | [CONFIG] | `ENTITY_TYPE_MAP` hardcoded - should match AssociationResolver config | Consistency | 020 |
| S008 | UpdateRelatedButton/UpdateRelatedButtonApp.tsx | ~70 | [API] | Calls non-existent `/api/field-mapping/execute` endpoint | Production use | 054 |
| S009 | All PCF controls | index.ts | [AUTH] | No authentication token handling - relies on browser session | Security review | TBD |

### Phase 2: Field Mapping Framework (Tasks 010-016)

| ID | File | Line | Category | Description | Blocks | Target Task |
|----|------|------|----------|-------------|--------|-------------|
| S013-01 | FieldMappingEndpoints.cs | ~77 | [API] | `QueryFieldMappingProfilesAsync()` returns empty array - needs Dataverse query for sprk_fieldmappingprofile | Production use | (Entity deployed via 001) |

### Phase 3: Association Resolver (Tasks 020-025)

| ID | File | Line | Category | Description | Blocks | Target Task |
|----|------|------|----------|-------------|--------|-------------|
| S021-01 | AssociationResolver/handlers/RecordSelectionHandler.ts | ~33 | [CONFIG] | `ENTITY_LOOKUP_CONFIGS` hardcoded - Lookup field names should come from configuration, not hardcoded | Flexibility | Future |
| S014-01 | FieldMappingEndpoints.cs | ~300 | [API] | `QueryProfileWithRulesByEntityPairAsync()` returns null - needs Dataverse query with $expand for rules | Production use | (Entity deployed via 001, 002) |
| S010-01 | FieldMappingService.ts | ~97 | [API] | `getProfiles()` returns empty array - needs Dataverse query when sprk_fieldmappingprofile entity exists | Production use | (Entity created in 001) |
| S010-02 | FieldMappingService.ts | ~153 | [API] | `getRulesForProfile()` returns empty array - needs Dataverse query when sprk_fieldmappingrule entity exists | Production use | (Entity created in 002) |
| S010-03 | FieldMappingService.ts | ~176 | [API] | `getSourceValues()` returns empty object - needs actual Dataverse retrieveRecord call | Production use | (Integration) |
| S010-04 | FieldMappingService.ts | ~337 | [FEATURE] | Resolve mode type resolution not implemented - future enhancement | Flexibility | Future |
| S010-05 | FieldMappingService.ts | ~459 | [API] | `mapProfileEntity()` mapping - ready to use when entity exists | Production use | (Entity created in 001) |
| S010-06 | FieldMappingService.ts | ~476 | [API] | `mapRuleEntity()` mapping - ready to use when entity exists | Production use | (Entity created in 002) |
| S012-01 | FieldMappingAdmin/FieldMappingAdminApp.tsx | ~215 | [CONFIG] | `getEntitySpecificFields()` returns hardcoded field list - should query EntityDefinitions API | Flexibility | Future |

---

## Stub Resolution Workflow

### When Creating a Stub

1. **Add code comment**: `// STUB: [category] - description`
2. **Add entry to this file** with:
   - Unique ID (S###)
   - File location
   - Category
   - Description
   - What it blocks (if anything)
   - Target task that will resolve it

### When Resolving a Stub

1. **Implement the real functionality**
2. **Remove the STUB comment** from code
3. **Update this file**: Move entry to "Resolved Stubs" section
4. **Add resolution date and task ID**

### Before Deployment

Run this command to find any remaining stubs:
```bash
grep -rn "// STUB:" src/client/pcf/
```

**Deployment blocker**: Any stub marked as blocking "Production use" MUST be resolved before deployment.

---

## Resolved Stubs

| ID | Resolved Date | Resolved By Task | Description |
|----|---------------|------------------|-------------|
| — | — | — | No stubs resolved yet |

---

## Stub Categories Summary

| Category | Active | Resolved | Notes |
|----------|--------|----------|-------|
| [API] | 11 | 0 | Most critical - need backend endpoints |
| [CONFIG] | 5 | 0 | Flexibility improvements |
| [AUTH] | 1 | 0 | Security review needed |
| [UI] | 1 | 0 | Edge case handling |
| [FEATURE] | 1 | 0 | Future enhancements |

---

## Integration with Task Execution

The `task-execute` skill should:
1. **Before implementation**: Check STUBS.md for stubs that THIS task should resolve
2. **After implementation**: Document any NEW stubs created
3. **Quality gate**: Flag if deployment tasks have unresolved blocking stubs

---

*This file should be updated whenever stubs are added or resolved.*
