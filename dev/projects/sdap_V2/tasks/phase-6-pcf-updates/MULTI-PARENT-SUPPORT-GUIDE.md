# Multi-Parent Support Reference Guide

## Overview

This guide explains how to extend the Universal Quick Create PCF control to support additional parent entity types beyond Matter (e.g., Account, Contact, Project, Invoice).

---

## Current Implementation (v2.2.0)

Currently supports:
- **Matter → Document** (sprk_matter → sprk_document)

Designed to support (future):
- **Account → Document** (account → sprk_document)
- **Contact → Document** (contact → sprk_document)
- **Project → Document** (project → sprk_document)
- **Any parent entity with 1:N relationship to Document**

---

## Prerequisites for Adding New Parent Entity

### 1. Dataverse Relationship Exists

The parent entity must have a **1:N relationship** to the Document entity (sprk_document).

**Verify in Dataverse:**
1. Settings → Customizations → Entities → [Parent Entity] → Relationships
2. Confirm 1:N relationship to sprk_document exists
3. Note the **relationship schema name** (e.g., `account_document`)

---

### 2. Parent Entity Has Container ID Field

The parent entity must have a field to store the SPE Container ID.

**Common field names:**
- sprk_containerid
- new_containerid
- Custom field created for SPE integration

---

### 3. Lookup Field Exists on Document Entity

The Document entity must have a lookup field pointing to the parent entity.

**Verify in Dataverse:**
1. Settings → Customizations → Entities → sprk_document → Fields
2. Confirm lookup field exists (e.g., `sprk_account`, `sprk_contact`)

---

## Step-by-Step: Add New Parent Entity

### Step 1: Run Metadata Validation Queries

For each new parent entity, run the validation queries from [TASK-6.1-METADATA-VALIDATION.md](./TASK-6.1-METADATA-VALIDATION.md).

**Example for Account:**

#### Query 1: Entity Set Names

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='account')?$select=EntitySetName
```

**Expected:** `"EntitySetName": "accounts"`

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')?$select=EntitySetName
```

**Expected:** `"EntitySetName": "sprk_documents"`

---

#### Query 2: Lookup Navigation Property (Document → Account)

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')?$expand=ManyToOneRelationships($select=SchemaName,ReferencingEntityNavigationPropertyName,ReferencingAttribute;$filter=SchemaName eq 'account_document')
```

**Extract:**
- `SchemaName`: account_document (or actual relationship name)
- `ReferencingEntityNavigationPropertyName`: sprk_account (or actual nav property)
- `ReferencingAttribute`: sprk_account (lookup field name)

---

#### Query 3: Collection Navigation Property (Account → Document) - For Option B

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='account')?$expand=OneToManyRelationships($select=SchemaName,ReferencedEntityNavigationPropertyName;$filter=SchemaName eq 'account_document')
```

**Extract:**
- `ReferencedEntityNavigationPropertyName`: account_document (or actual collection nav property)

---

### Step 2: Add Configuration to EntityDocumentConfig

**File:** `src/controls/UniversalQuickCreate/UniversalQuickCreate/config/EntityDocumentConfig.ts`

Add new entry to `ENTITY_DOCUMENT_CONFIGS`:

```typescript
export const ENTITY_DOCUMENT_CONFIGS: Record<string, EntityDocumentConfig> = {
  // Existing Matter configuration
  'sprk_matter': {
    entityName: 'sprk_matter',
    lookupFieldName: 'sprk_matter',
    relationshipSchemaName: 'sprk_matter_document',
    containerIdField: 'sprk_containerid',
    displayNameField: 'sprk_matternumber',
    entitySetName: 'sprk_matters'
  },

  // NEW: Account configuration
  'account': {
    entityName: 'account',
    lookupFieldName: 'sprk_account',                    // From Query 2: ReferencingAttribute
    relationshipSchemaName: 'account_document',         // From Query 2: SchemaName
    containerIdField: 'sprk_containerid',               // Field on Account holding Container ID
    displayNameField: 'name',                           // Display field for Account
    entitySetName: 'accounts'                           // From Query 1
  },

  // NEW: Contact configuration
  'contact': {
    entityName: 'contact',
    lookupFieldName: 'sprk_contact',
    relationshipSchemaName: 'contact_document',
    containerIdField: 'sprk_containerid',
    displayNameField: 'fullname',
    entitySetName: 'contacts'
  }

  // Add more as needed...
} as const;
```

---

### Step 3: Update UI to Support Multiple Parent Types (If Applicable)

If the PCF control is used on multiple entity forms, ensure it can detect the parent entity type dynamically.

**Example:**

```typescript
// In index.ts or main component
const parentEntityName = context.page.entityTypeName;  // e.g., "account", "contact", "sprk_matter"
const config = ENTITY_DOCUMENT_CONFIGS[parentEntityName];

if (!config) {
  throw new Error(`Unsupported parent entity type: ${parentEntityName}`);
}

// Pass config to DocumentRecordService
const results = await this.documentRecordService.createDocuments(
  files,
  parent,
  config,      // <-- Configuration for detected parent type
  formData
);
```

---

### Step 4: Test with New Parent Entity

Follow testing procedures from [TASK-6.6-TESTING-VALIDATION.md](./TASK-6.6-TESTING-VALIDATION.md):

1. Create test record of new parent entity type (e.g., Account)
2. Ensure Container ID field is populated
3. Upload files via PCF control
4. Verify Document records created
5. Verify lookup field points to correct parent record
6. Verify all metadata queries cached (1 query per relationship)

---

### Step 5: Increment Version (Optional)

If adding multiple new parent types is a significant feature release:

- Update version to 2.3.0 or 2.2.1
- Update `ControlManifest.Input.xml`
- Update `index.ts` version badge
- Re-deploy

---

## Metadata Query Summary by Parent Entity

| Parent Entity | Entity Set Name | Lookup Field (Document) | Relationship Schema | Collection Nav Property |
|--------------|----------------|------------------------|---------------------|------------------------|
| sprk_matter | sprk_matters | sprk_matter | sprk_matter_document | sprk_matter_document |
| account | accounts | sprk_account | account_document | account_document |
| contact | contacts | sprk_contact | contact_document | contact_document |
| incident | incidents | sprk_incident | incident_document | incident_document |
| opportunity | opportunities | sprk_opportunity | opportunity_document | opportunity_document |

**Note:** Actual values must be validated via metadata queries—do NOT assume these values.

---

## Common Patterns

### Pattern 1: Standard Entities (Account, Contact, etc.)

- Entity set name = entity name + 's' (accounts, contacts)
- Lookup field = prefix + parent entity name (sprk_account, sprk_contact)
- Relationship schema = parent_document (account_document, contact_document)

**⚠️ Warning:** This is a common pattern but NOT guaranteed. Always validate with metadata queries.

---

### Pattern 2: Custom Entities

- Entity set name = often custom plural (e.g., new_projects → new_projectses or new_projects)
- Lookup field = publisher prefix + parent entity name
- Relationship schema = often auto-generated with GUIDs or custom names

**⚠️ Warning:** Custom entities have more variation. Metadata queries are CRITICAL.

---

## Troubleshooting

### Issue: "No ManyToOneRelationship found for schema name"

**Cause:** Relationship doesn't exist or schema name is incorrect.

**Solution:**
1. Verify relationship exists in Dataverse UI
2. Get actual relationship schema name from Dataverse
3. Update configuration with correct schema name

---

### Issue: "undeclared property" error with new parent entity

**Cause:** Navigation property name doesn't match what's in the payload.

**Solution:**
1. Run Query 2 to get actual `ReferencingEntityNavigationPropertyName`
2. Verify `lookupFieldName` in config matches navigation property
3. Clear cache and re-test

---

### Issue: Cache pollution between parent entity types

**Cause:** Cache key not specific enough.

**Solution:**
- MetadataService cache key already includes relationship schema name
- Each parent/child relationship has unique cache entry
- Cache key format: `{env}::lookup::{childEntity}::{relationshipSchema}`
- No action needed—cache is properly isolated

---

## Best Practices

### 1. Always Validate Metadata

Never assume navigation property names or entity set names. Always run validation queries for each new parent entity.

### 2. Document Configuration

Maintain a table or document with validated metadata for each parent entity type.

### 3. Test Thoroughly

Test with at least 2 different parent entity types to ensure multi-parent logic works correctly.

### 4. Handle Missing Configuration Gracefully

```typescript
const config = ENTITY_DOCUMENT_CONFIGS[parentEntityName];

if (!config) {
  // Show user-friendly error instead of crashing
  throw new Error(
    `This control is not configured for entity type '${parentEntityName}'. ` +
    `Please contact your administrator to add support for this entity.`
  );
}
```

### 5. Cache Performance

- Cache persists for browser session
- Each parent entity type caches its own metadata
- 1st upload to Matter: 1 metadata query
- 1st upload to Account: 1 metadata query (different cache entry)
- 2nd upload to Matter: 0 metadata queries (cache hit)
- 2nd upload to Account: 0 metadata queries (cache hit)

---

## Example: Adding "Project" Parent Entity

### 1. Verify Relationship

Relationship: `new_project_document`
- Parent: new_project
- Child: sprk_document
- Lookup field on Document: new_projectid

### 2. Run Metadata Queries

```http
# Entity set name
GET /api/data/v9.2/EntityDefinitions(LogicalName='new_project')?$select=EntitySetName
# Result: "new_projects"

# Lookup navigation property
GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')?$expand=ManyToOneRelationships($select=SchemaName,ReferencingEntityNavigationPropertyName;$filter=SchemaName eq 'new_project_document')
# Result: ReferencingEntityNavigationPropertyName = "new_projectid"

# Collection navigation property
GET /api/data/v9.2/EntityDefinitions(LogicalName='new_project')?$expand=OneToManyRelationships($select=SchemaName,ReferencedEntityNavigationPropertyName;$filter=SchemaName eq 'new_project_document')
# Result: ReferencedEntityNavigationPropertyName = "new_project_document"
```

### 3. Add Configuration

```typescript
'new_project': {
  entityName: 'new_project',
  lookupFieldName: 'new_projectid',
  relationshipSchemaName: 'new_project_document',
  containerIdField: 'new_containerid',
  displayNameField: 'new_name',
  entitySetName: 'new_projects'
}
```

### 4. Deploy and Test

- Build and deploy PCF control
- Open Project record with Container ID
- Upload files
- Verify Document records created with lookup to Project

---

## Summary

Adding support for new parent entities requires:

1. ✅ Validate metadata via queries
2. ✅ Add configuration to ENTITY_DOCUMENT_CONFIGS
3. ✅ Test with new parent entity type
4. ✅ Document metadata values for reference

The MetadataService handles all navigation property resolution dynamically—no code changes needed beyond configuration.

---

**Last Updated:** 2025-10-19
**Version:** v2.2.0
