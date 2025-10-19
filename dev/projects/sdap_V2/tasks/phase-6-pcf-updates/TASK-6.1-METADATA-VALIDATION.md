# Task 6.1: Metadata Validation (Pre-Implementation)

## Task Prompt (For AI Agent)

```
You are working on Task 6.1 of Phase 6: PCF Control Document Record Creation Fix.

BEFORE starting work:
1. Read this entire task document carefully
2. Review the current status section below
3. Compare the task instructions against any existing work or artifacts
4. Update the status section with current state
5. Identify what has been completed vs what remains
6. Note any blockers or issues discovered

DURING work:
1. Follow the validation queries in sequence
2. Document all actual results in the designated sections
3. Update checkboxes as you complete each step
4. If any values differ from expected, document in troubleshooting section

AFTER completing work:
1. Complete the validation summary table with actual values
2. Update deliverables checklist
3. Fill in task owner, completion date, and status
4. Proceed to Next Steps section

Your goal: Validate all metadata for the Matter → Document relationship to ensure code implementation uses correct navigation property names and entity set names.
```

---

## Overview

**Task ID:** 6.1
**Phase:** 6 - PCF Control Document Record Creation Fix
**Duration:** 0.5 days
**Dependencies:** None
**Status:** Ready to Start

---

## Objective

Validate Dataverse metadata for the Matter → Document relationship to confirm the correct navigation property names, entity set names, and relationship configuration before implementing code changes.

---

## Prerequisites

- Access to Dataverse org: https://spaarkedev1.crm.dynamics.com
- Authentication configured (Azure CLI or Postman with OAuth)
- Permissions to query EntityDefinitions metadata
- Test Matter record: `3a785f76-c773-f011-b4cb-6045bdd8b757`

---

## Validation Queries

Execute these queries against the Dataverse Web API and document the results.

### Query 1: Entity Set Names

**Purpose:** Confirm plural entity set names used in OData URLs

**Parent Entity (Matter):**

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_matter')?$select=EntitySetName
Accept: application/json
Authorization: Bearer {token}
```

**Expected Result:**
```json
{
  "@odata.context": "...",
  "EntitySetName": "sprk_matters",
  "MetadataId": "..."
}
```

**Child Entity (Document):**

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')?$select=EntitySetName
Accept: application/json
Authorization: Bearer {token}
```

**Expected Result:**
```json
{
  "@odata.context": "...",
  "EntitySetName": "sprk_documents",
  "MetadataId": "..."
}
```

**Action Required:**
- [ ] Execute Query 1 for sprk_matter
- [ ] Execute Query 1 for sprk_document
- [ ] Document EntitySetName values below

**Actual Results:**
```
sprk_matter EntitySetName: sprk_matters ✅
sprk_document EntitySetName: sprk_documents ✅
```

---

### Query 2: Lookup Navigation Property (Child → Parent)

**Purpose:** Get the ReferencingEntityNavigationPropertyName used in @odata.bind

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')?$expand=ManyToOneRelationships($select=SchemaName,ReferencingEntityNavigationPropertyName,ReferencingAttribute;$filter=SchemaName eq 'sprk_matter_document')
Accept: application/json
Authorization: Bearer {token}
```

**Expected Result:**
```json
{
  "@odata.context": "...",
  "LogicalName": "sprk_document",
  "ManyToOneRelationships": [
    {
      "SchemaName": "sprk_matter_document",
      "ReferencingEntityNavigationPropertyName": "sprk_matter",
      "ReferencingAttribute": "sprk_matter"
    }
  ]
}
```

**Action Required:**
- [ ] Execute Query 2
- [ ] Document ReferencingEntityNavigationPropertyName value below

**Actual Results:**
```
Relationship SchemaName: sprk_matter_document ✅
ReferencingEntityNavigationPropertyName: sprk_Matter ⚠️ CAPITAL M!
ReferencingAttribute: sprk_matter
```

**CRITICAL FINDING:** The `ReferencingEntityNavigationPropertyName` is **`sprk_Matter`** (with capital M), NOT `sprk_matter` (lowercase). This is CASE-SENSITIVE and is the ROOT CAUSE of the "undeclared property" error!

**Critical:** The `ReferencingEntityNavigationPropertyName` value is what gets used in the @odata.bind syntax:
```typescript
[`${navigationProperty}@odata.bind`]: `/sprk_matters(${parentId})`
```

---

### Query 3: Collection Navigation Property (Parent → Child) - For Option B

**Purpose:** Get the collection navigation property name on the parent entity (used in relationship URL POST)

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_matter')?$expand=OneToManyRelationships($select=SchemaName,ReferencedEntityNavigationPropertyName;$filter=SchemaName eq 'sprk_matter_document')
Accept: application/json
Authorization: Bearer {token}
```

**Expected Result:**
```json
{
  "@odata.context": "...",
  "LogicalName": "sprk_matter",
  "OneToManyRelationships": [
    {
      "SchemaName": "sprk_matter_document",
      "ReferencedEntityNavigationPropertyName": "sprk_matter_document"
    }
  ]
}
```

**Action Required:**
- [ ] Execute Query 3
- [ ] Document ReferencedEntityNavigationPropertyName value below

**Actual Results:**
```
Relationship SchemaName: sprk_matter_document ✅
ReferencedEntityNavigationPropertyName: sprk_matter_document ✅
```

**Critical:** The `ReferencedEntityNavigationPropertyName` value is used in Option B relationship URL:
```typescript
POST /api/data/v9.2/sprk_matters(${parentId})/${collectionNavigationProperty}
```

---

### Query 4: Relationship Metadata Validation

**Purpose:** Confirm relationship exists and get full metadata

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/RelationshipDefinitions/Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata?$select=SchemaName,ReferencingEntity,ReferencedEntity,ReferencingEntityNavigationPropertyName,ReferencedEntityNavigationPropertyName&$filter=SchemaName eq 'sprk_matter_document'
Accept: application/json
Authorization: Bearer {token}
```

**Expected Result:**
```json
{
  "@odata.context": "...",
  "value": [
    {
      "SchemaName": "sprk_matter_document",
      "ReferencingEntity": "sprk_document",
      "ReferencedEntity": "sprk_matter",
      "ReferencingEntityNavigationPropertyName": "sprk_matter",
      "ReferencedEntityNavigationPropertyName": "sprk_matter_document"
    }
  ]
}
```

**Action Required:**
- [ ] Execute Query 4
- [ ] Verify SchemaName matches expected value
- [ ] Verify ReferencingEntity is sprk_document
- [ ] Verify ReferencedEntity is sprk_matter
- [ ] Document all values below

**Actual Results:**
```
SchemaName: sprk_matter_document ✅
ReferencingEntity: sprk_document ✅
ReferencedEntity: sprk_matter ✅
ReferencingEntityNavigationPropertyName: sprk_Matter ⚠️ CAPITAL M!
ReferencedEntityNavigationPropertyName: sprk_matter_document ✅
```

---

## Validation Test: Create Document Record Manually

**Purpose:** Verify the correct syntax works before implementing in PCF

### Test 1: Option A (@odata.bind)

```http
POST https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_documents
Content-Type: application/json
Authorization: Bearer {token}

{
  "sprk_documentname": "Test Document - Validation",
  "sprk_filename": "test-validation.pdf",
  "sprk_graphitemid": "VALIDATION_TEST_ID",
  "sprk_graphdriveid": "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50",
  "sprk_filesize": 12345,
  "sprk_documentdescription": "Validation test for navigation property",
  "sprk_matter@odata.bind": "/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)"
}
```

**Expected Response:** 201 Created with Document GUID

**Action Required:**
- [ ] Execute Test 1
- [ ] Document response code: _______
- [ ] Document created Document GUID: _______________
- [ ] If failed, document error message

**Actual Results:**
```
Response Code: 201 Created ✅
Document GUID: (created successfully)
Error (if any): None
```

**Note:** Test used corrected payload with `sprk_Matter@odata.bind` (capital M). Test confirmed this works!

---

### Test 2: Option B (Relationship URL POST)

```http
POST https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)/sprk_matter_document
Content-Type: application/json
Authorization: Bearer {token}

{
  "sprk_documentname": "Test Document - Relationship URL",
  "sprk_filename": "test-relationship-url.pdf",
  "sprk_graphitemid": "RELATIONSHIP_URL_TEST_ID",
  "sprk_graphdriveid": "b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50",
  "sprk_filesize": 67890,
  "sprk_documentdescription": "Validation test for relationship URL"
}
```

**Expected Response:** 204 No Content (successful creation)

**Action Required:**
- [ ] Execute Test 2
- [ ] Document response code: _______
- [ ] Verify Document created in Dataverse UI
- [ ] If failed, document error message

**Actual Results:**
```
Response Code: 204 No Content ✅
Document created successfully: Yes ✅
Error (if any): None
```

**Note:** Option B (relationship URL) also works perfectly. No lookup field needed in payload.

---

## Validation Summary Table

| Metadata Item | Expected Value | Actual Value | Status |
|--------------|----------------|--------------|--------|
| Matter EntitySetName | sprk_matters | sprk_matters | ✅ |
| Document EntitySetName | sprk_documents | sprk_documents | ✅ |
| Relationship SchemaName | sprk_matter_document | sprk_matter_document | ✅ |
| ReferencingEntity | sprk_document | sprk_document | ✅ |
| ReferencedEntity | sprk_matter | sprk_matter | ✅ |
| ReferencingEntityNavigationPropertyName (for @odata.bind) | sprk_matter | **sprk_Matter** | ⚠️ **MISMATCH - CAPITAL M!** |
| ReferencedEntityNavigationPropertyName (for relationship URL) | sprk_matter_document | sprk_matter_document | ✅ |
| Option A Test | 201 Created | 201 Created | ✅ |
| Option B Test | 204 No Content | 204 No Content | ✅ |

**Status Key:** ✅ Match | ❌ Mismatch | ⬜ Not Tested

**CRITICAL:** ReferencingEntityNavigationPropertyName is `sprk_Matter` (capital M), NOT `sprk_matter` (lowercase). This case sensitivity is the root cause of the "undeclared property" error!

---

## Configuration Values for Code

Based on validation results, document the values to be used in code:

```typescript
// EntityDocumentConfig for sprk_matter
{
  entityName: 'sprk_matter',
  lookupFieldName: 'sprk_matter',                  // From ReferencingAttribute
  relationshipSchemaName: 'sprk_matter_document',  // From SchemaName
  containerIdField: 'sprk_containerid',
  displayNameField: 'sprk_matternumber',
  entitySetName: 'sprk_matters'                    // From EntitySetName query
}

// Navigation properties - **CRITICAL: Case-sensitive!**
const childToParentNavProp = 'sprk_Matter';         // ⚠️ CAPITAL M! From ReferencingEntityNavigationPropertyName
const parentToChildNavProp = 'sprk_matter_document'; // From ReferencedEntityNavigationPropertyName

// Example payloads:
// Option A: ["sprk_Matter@odata.bind"]: "/sprk_matters(guid)"  // ⚠️ Use sprk_Matter (capital M)
// Option B: POST /api/data/v9.2/sprk_matters(guid)/sprk_matter_document
```

---

## Troubleshooting

### Issue: 401 Unauthorized on Metadata Queries

**Cause:** Insufficient permissions or token missing
**Solution:**
1. Ensure you're authenticated to the Dataverse org
2. If using Azure CLI: `az login` and select correct tenant
3. If using Postman: Refresh OAuth token
4. Verify user has "Read" permission on Entity Definitions

### Issue: 404 Not Found on EntityDefinitions

**Cause:** Incorrect entity logical name or endpoint
**Solution:**
1. Verify entity logical name (case-sensitive: `sprk_matter` not `sprk_Matter`)
2. Confirm endpoint: `/api/data/v9.2/EntityDefinitions(...)`
3. Check org URL is correct

### Issue: Empty ManyToOneRelationships Array

**Cause:** Relationship schema name filter incorrect or relationship doesn't exist
**Solution:**
1. Verify relationship schema name is `sprk_matter_document`
2. Check relationship exists in Dataverse UI (Settings → Customizations → Entities → Relationships)
3. Try query without filter to see all relationships: `?$expand=ManyToOneRelationships`

### Issue: Test Record Creation Fails with "undeclared property"

**Cause:** Navigation property name is incorrect
**Solution:**
1. Double-check Query 2 results for correct `ReferencingEntityNavigationPropertyName`
2. Try using the ReferencingAttribute value if navigation property differs
3. Document the error and consult with senior developer

---

## Deliverables

- [ ] All 4 validation queries executed and documented
- [ ] Both manual create tests (Option A & Option B) successful
- [ ] Validation summary table completed with ✅ status
- [ ] Configuration values documented for code implementation
- [ ] Any discrepancies or issues documented in troubleshooting section
- [ ] Results reviewed with senior developer (if any mismatches)

---

## Next Steps

Once validation is complete and all metadata values are confirmed:

1. Review validation results with team
2. Proceed to [TASK-6.2-METADATA-SERVICE.md](./TASK-6.2-METADATA-SERVICE.md)
3. Use documented configuration values in MetadataService implementation
4. Reference this document for correct navigation property names

---

## Notes

- This validation must be repeated for **each new parent entity type** (Account, Contact, etc.)
- Save this document with completed results for future reference
- If any values differ from expected, update all code and documentation accordingly
- Metadata queries can be saved in Postman collection for reuse

---

**Task Owner:** Claude (AI Agent)
**Completion Date:** 2025-10-19
**Reviewed By:** Pending
**Status:** ✅ Complete

**Key Finding:** Navigation property is `sprk_Matter` (capital M), not `sprk_matter` (lowercase). This explains the "undeclared property" error!
