# MetadataService Implementation - Expert Consultation Request

**Date:** 2025-10-19
**Project:** Phase 6 - PCF Control Document Record Creation Fix
**Issue:** Cannot query EntityDefinitions metadata via context.webAPI in PCF
**Status:** Seeking expert guidance before proceeding

---

## Problem Statement

We are implementing a PCF control that creates Document records in Dataverse with lookups to multiple parent entity types (Matter, Account, Contact, etc.). The navigation property names for these lookups are **case-sensitive** (e.g., `sprk_Matter` with capital M, not `sprk_matter`), and we discovered through metadata validation that the case varies by entity.

Our design calls for a **MetadataService** to dynamically query Dataverse metadata to get the correct navigation property names, eliminating the need to hardcode case-sensitive values. However, we encountered a blocking error when attempting to implement this.

---

## Original Design Intent

### From: PCF CONTROL FIX INSTRUCTIONS 10-19-2025.md

**Section 4: Metadata Helper (Required for Multi-Parent Support)**

```typescript
export class MetadataService {
  static async getLookupNavProp(
    context: ComponentFramework.Context,
    childEntityLogicalName: string,
    relationshipSchemaName: string
  ): Promise<string> {
    // Query ManyToOneRelationships on child entity to get navigation property
    const query =
      `EntityDefinitions(LogicalName='${childEntityLogicalName}')` +
      `?$expand=ManyToOneRelationships($select=SchemaName,ReferencingEntityNavigationPropertyName;` +
      `$filter=SchemaName eq '${relationshipSchemaName}')`;

    const result = await context.webAPI.retrieveMultipleRecords("EntityDefinitions", query);
    // ... return navigation property
  }
}
```

**Design Rationale:**
- **Multi-parent support:** Each parent entity may have different navigation property casing
- **Eliminates hardcoding:** No need to manually validate and hardcode each property
- **Future-proof:** Adapts automatically if Microsoft changes schema
- **Uses context.webAPI:** Document states "works across all PCF hosts"

**When to use:**
- **Single parent (Matter only):** Optional—can hardcode based on validation
- **Multiple parents:** **REQUIRED**—each relationship needs metadata lookup

---

## What We Attempted

### Attempt 1: Use context.webAPI with EntityDefinitions query

**Code:**
```typescript
const query = `?$filter=LogicalName eq '${childEntityLogicalName}'&$expand=ManyToOneRelationships(...)`;
const result = await context.webAPI.retrieveMultipleRecords('EntityDefinitions', query);
```

**Result:**
```
Error: The entity "EntityDefinitions" cannot be found.
Specify a valid query, and try again.
```

**Analysis:**
- `context.webAPI` in PCF controls appears to have restricted access to metadata entities
- This is likely a security restriction in the Power Apps Component Framework
- EntityDefinitions is a system entity that may require elevated permissions

### Attempt 2: Use OData entity key syntax

**Code:**
```typescript
const query = `EntityDefinitions(LogicalName='${entityLogicalName}')?$select=EntitySetName`;
const result = await context.webAPI.retrieveMultipleRecords('EntityDefinitions', query);
```

**Result:**
```
Error: Option Parameter should begin with "?"
```

**Analysis:**
- `context.webAPI.retrieveMultipleRecords()` expects query options format, not entity key syntax
- Changed to `?$filter=LogicalName eq '...'` but still got "EntityDefinitions cannot be found" error
- The issue is not OData syntax - it's that EntityDefinitions is not accessible

---

## Current Workaround (Deployed v2.2.0)

We reverted to **hardcoded navigation properties** in EntityDocumentConfig:

```typescript
export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
  'sprk_matter': {
    entityName: 'sprk_matter',
    lookupFieldName: 'sprk_matter',
    relationshipSchemaName: 'sprk_matter_document',
    navigationPropertyName: 'sprk_Matter',  // ⚠️ CAPITAL M - hardcoded from metadata validation
    containerIdField: 'sprk_containerid',
    displayNameField: 'sprk_matternumber',
    entitySetName: 'sprk_matters'
  },
  // ... other entities
}
```

**DocumentRecordService:**
```typescript
// Use hardcoded navigation property from config
const navigationPropertyName = config.navigationPropertyName;
const payload = {
  [`${navigationPropertyName}@odata.bind`]: `/${entitySetName}(${guid})`
};
await context.webAPI.createRecord('sprk_document', payload);
```

**Limitations of This Approach:**
- ❌ Requires manual metadata validation for each parent entity
- ❌ Must hardcode correct case in configuration
- ❌ No auto-adaptation if Microsoft changes schema
- ❌ Configuration errors possible (typos, wrong case)
- ✅ Works for single parent (Matter)
- ⚠️ Scales poorly for multi-parent support

---

## Questions for Expert

### Question 1: Can PCF context.webAPI access EntityDefinitions?

**Background:**
- Design doc states `context.webAPI` "works across all PCF hosts"
- We get "EntityDefinitions cannot be found" error
- Is EntityDefinitions restricted in PCF for security reasons?

**If NO:**
- Is there an alternative API in PCF to query metadata?
- Should we use a different approach entirely?

**If YES:**
- What are we doing wrong in our query syntax?
- Is there a permission/security setup required?

### Question 2: Can we use Xrm.WebApi for metadata queries in PCF?

**Proposal:**
```typescript
// Use Xrm.WebApi for metadata queries (if available)
const result = await Xrm.WebApi.retrieveMultipleRecords('EntityDefinition', query);

// Continue using context.webAPI for record creation
await context.webAPI.createRecord('sprk_document', payload);
```

**Questions:**
- Is Xrm.WebApi available in all PCF contexts (Custom Pages, Model-Driven Apps, Canvas Apps)?
- Does Xrm.WebApi have access to EntityDefinitions metadata?
- Is mixing Xrm.WebApi (metadata) and context.webAPI (CRUD) acceptable?
- What about PCF hosts where Xrm is not available (Canvas Apps, Portals)?

### Question 3: Alternative approaches?

**Option A:** Server-side metadata resolution
- Create a Custom API that queries metadata and returns navigation properties
- PCF calls Custom API instead of querying EntityDefinitions directly
- Pros: Centralized, secure, works in all PCF hosts
- Cons: Additional API, deployment complexity

**Option B:** Build-time metadata resolution
- Query metadata during solution deployment
- Generate configuration file with all navigation properties
- PCF reads from static configuration
- Pros: No runtime queries, fast
- Cons: Requires deployment process, out-of-sync risk

**Option C:** Accept hardcoded configuration
- Document the metadata validation process
- Require manual configuration for each parent entity
- Use TypeScript types to enforce structure
- Pros: Simple, no runtime dependencies
- Cons: Manual process, error-prone

**Which approach is recommended for PCF controls?**

---

## Environment Details

**PCF Version:** Latest (pac CLI 1.46.1)
**Dataverse Environment:** https://spaarkedev1.crm.dynamics.com/
**PCF Host:** Custom Pages (Form Dialog)
**Control Type:** Standard (not virtual, not dataset)

**Current Manifest:**
```xml
<control namespace="Spaarke.Controls"
         constructor="UniversalDocumentUpload"
         version="2.2.0"
         control-type="standard">
  <feature-usage>
    <uses-feature name="WebAPI" required="true" />
    <uses-feature name="Utility" required="true" />
  </feature-usage>
</control>
```

---

## Validation Results (from Task 6.1)

We successfully queried EntityDefinitions metadata using **PowerShell + Web API** (not PCF):

```powershell
$token = az account get-access-token --resource https://spaarkedev1.crm.dynamics.com --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token" }

# Query worked successfully outside PCF
$query = "EntityDefinitions(LogicalName='sprk_document')?`$expand=ManyToOneRelationships(...)"
$result = Invoke-RestMethod -Uri "$baseUrl/api/data/v9.2/$query" -Headers $headers
```

**Results:**
- Entity: sprk_document
- Relationship: sprk_matter_document
- **ReferencingEntityNavigationPropertyName:** `sprk_Matter` (capital M) ⚠️
- EntitySetName: sprk_matters

**Key Finding:** Navigation property is **case-sensitive** and differs from entity logical name casing.

---

## Impact Assessment

**Current State (Hardcoded):**
- ✅ Works for Matter entity
- ✅ Deployed and functional (v2.2.0)
- ❌ Requires manual configuration for each new parent entity
- ❌ Risk of misconfiguration (wrong case)

**If MetadataService Works:**
- ✅ Auto-discovery of navigation properties
- ✅ Supports unlimited parent entities
- ✅ Future-proof against schema changes
- ✅ Reduces configuration errors

**Business Impact:**
- **Current:** Limited to Matter entity unless we manually validate each parent
- **Desired:** Support Account, Contact, Project, Invoice, etc. with no code changes
- **Timeline:** Need to decide before implementing multi-parent support

---

## Request for Guidance

We need expert guidance on:

1. **Can PCF access EntityDefinitions metadata?** If yes, how?
2. **Should we use Xrm.WebApi for metadata + context.webAPI for CRUD?**
3. **What is the recommended pattern for dynamic metadata resolution in PCF?**
4. **Are there security/permission requirements we're missing?**

**Preferred Outcome:**
- MetadataService working as designed (dynamic metadata queries)
- Multi-parent support without hardcoding
- Works across all PCF host types

**Acceptable Fallback:**
- Clear guidance that metadata queries are not supported in PCF
- Recommended alternative approach
- Best practices for multi-entity configuration

---

## References

- **Design Document:** `C:\code_files\spaarke\dev\projects\sdap_V2\PCF CONTROL FIX INSTRUCTIONS 10-19-2025.md`
- **Task Documents:** `C:\code_files\spaarke\dev\projects\sdap_V2\tasks\phase-6-pcf-updates\`
- **Current Code:** `src\controls\UniversalQuickCreate\UniversalQuickCreate\services\MetadataService.ts`
- **Microsoft Docs:** https://learn.microsoft.com/en-us/power-apps/developer/component-framework/reference/webapi

---

## Attachments

### Browser Console Error
```
[UniversalQuickCreate][DocumentRecordService] Failed to create Document for test.pdf
Error: Metadata query failed for sprk_document.sprk_matter_document:
The entity "EntityDefinitions" cannot be found. Specify a valid query, and try again.
```

### Current MetadataService.ts (Not Working)
```typescript
export class MetadataService {
  static async getLookupNavProp(
    context: ComponentFramework.Context<any>,
    childEntityLogicalName: string,
    relationshipSchemaName: string
  ): Promise<string> {
    const query = `?$filter=LogicalName eq '${childEntityLogicalName}'&$expand=...`;

    // This throws error: "EntityDefinitions cannot be found"
    const result = await context.webAPI.retrieveMultipleRecords('EntityDefinitions', query);

    return result.entities[0].ManyToOneRelationships[0].ReferencingEntityNavigationPropertyName;
  }
}
```

### Current DocumentRecordService.ts (Workaround)
```typescript
// Hardcoded approach (currently deployed)
const navigationPropertyName = config.navigationPropertyName; // "sprk_Matter" from config
const payload = {
  [`${navigationPropertyName}@odata.bind`]: `/${entitySetName}(${guid})`
};
await context.webAPI.createRecord('sprk_document', payload);
```

---

**Next Steps After Consultation:**
1. Implement recommended approach
2. Update design documentation if needed
3. Deploy and test
4. Document pattern for future PCF controls
5. Complete Phase 6 tasks

---

**Prepared By:** Claude (AI Agent)
**Date:** 2025-10-19
**Phase:** 6 - PCF Control Document Record Creation Fix
**Task:** 6.5 - Deployment & MetadataService Investigation
