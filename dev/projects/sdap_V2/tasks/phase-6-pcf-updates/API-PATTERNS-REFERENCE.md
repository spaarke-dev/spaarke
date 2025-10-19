# Dataverse Web API Patterns - Quick Reference

## Overview

This guide provides quick reference for common Dataverse Web API patterns used in the Universal Quick Create PCF control.

---

## Table of Contents

1. [Option A: @odata.bind Pattern](#option-a-odatabind-pattern)
2. [Option B: Relationship URL Pattern](#option-b-relationship-url-pattern)
3. [Metadata Queries](#metadata-queries)
4. [Common Pitfalls](#common-pitfalls)
5. [Code Hygiene](#code-hygiene)

---

## Option A: @odata.bind Pattern

### When to Use
- **Primary method** for creating related records from PCF
- Standard OData approach
- Works in all PCF hosts (Custom Pages, Model-Driven Apps, Canvas Apps)

### Pattern Structure

```typescript
const payload = {
  // Regular fields
  field1: "value1",
  field2: "value2",

  // Lookup binding (single-valued navigation property)
  [`${navigationProperty}@odata.bind`]: `/${entitySetName}(${parentId})`
};

await context.webAPI.createRecord("childEntity", payload);
```

### Example: Matter → Document

```typescript
const payload = {
  sprk_documentname: "My Document",
  sprk_filename: "document.pdf",
  sprk_graphitemid: "01KXYZ...",
  sprk_graphdriveid: "b!yLRdW...",
  sprk_filesize: 12345,
  sprk_documentdescription: "Test document",

  // Lookup binding
  ["sprk_matter@odata.bind"]: "/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)"
};

await context.webAPI.createRecord("sprk_document", payload);
```

### Key Points

| Element | Purpose | Example Value |
|---------|---------|---------------|
| Navigation Property | Left-hand property name | `sprk_matter` |
| Entity Set Name | Plural parent entity name | `sprk_matters` |
| Parent GUID | Parent record ID | `3a785f76-c773-f011-b4cb-6045bdd8b757` |

### Requirements

✅ **DO:**
- Use correct navigation property from `ReferencingEntityNavigationPropertyName`
- Use entity set name (plural form)
- Use lowercase GUID without braces
- Include @odata.bind suffix

❌ **DON'T:**
- Use relationship schema name (e.g., `sprk_matter_document`)
- Use lookup field name if different from navigation property
- Include base field value (e.g., `sprk_matter: null`)
- Use GUID with braces `{GUID}`

---

## Option B: Relationship URL Pattern

### When to Use
- Server-side $batch operations
- Fallback if Option A fails
- When you want metadata-free approach (uses relationship schema name directly)

### Pattern Structure

```typescript
const url = `/api/data/v9.2/${parentEntitySetName}(${parentId})/${collectionNavigationProperty}`;

const request = {
  method: "POST",
  url,
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(childPayload),
  getMetadata: () => ({
    boundParameter: null,
    parameterTypes: {},
    operationType: 0,
    operationName: ""
  })
};

await (context.webAPI as any).execute(request);
```

### Example: Matter → Document

```typescript
const url = "/api/data/v9.2/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)/sprk_matter_document";

const payload = {
  sprk_documentname: "My Document",
  sprk_filename: "document.pdf",
  sprk_graphitemid: "01KXYZ...",
  sprk_graphdriveid: "b!yLRdW...",
  sprk_filesize: 12345,
  sprk_documentdescription: "Test document"
  // NO lookup field needed - implied by URL
};

const request = {
  method: "POST",
  url,
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify(payload),
  getMetadata: () => ({
    boundParameter: null,
    parameterTypes: {},
    operationType: 0,
    operationName: ""
  })
};

const response = await (context.webAPI as any).execute(request);
```

### Key Points

| Element | Purpose | Example Value |
|---------|---------|---------------|
| Entity Set Name | Parent entity set name | `sprk_matters` |
| Parent GUID | Parent record ID | `3a785f76-c773-f011-b4cb-6045bdd8b757` |
| Collection Nav Property | From `ReferencedEntityNavigationPropertyName` | `sprk_matter_document` |

### Requirements

✅ **DO:**
- Use collection navigation property from parent's `OneToManyRelationships`
- Include `getMetadata()` function exactly as shown
- Use lowercase GUID without braces
- Omit lookup fields from payload (implied by URL)

❌ **DON'T:**
- Include lookup field in payload
- Forget `getMetadata()` function
- Use wrong collection navigation property

---

## Metadata Queries

### Query 1: Get Entity Set Name

**Purpose:** Get plural entity set name for OData URLs

```http
GET https://{org}.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='{entityName}')?$select=EntitySetName
```

**Example:**
```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_matter')?$select=EntitySetName
```

**Response:**
```json
{
  "EntitySetName": "sprk_matters"
}
```

**TypeScript:**
```typescript
const result = await context.webAPI.retrieveMultipleRecords(
  'EntityDefinitions',
  `EntityDefinitions(LogicalName='sprk_matter')?$select=EntitySetName`
);

const entitySetName = result.entities[0].EntitySetName;  // "sprk_matters"
```

---

### Query 2: Get Lookup Navigation Property (Child → Parent)

**Purpose:** Get navigation property name for @odata.bind

```http
GET https://{org}.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='{childEntity}')?$expand=ManyToOneRelationships($select=SchemaName,ReferencingEntityNavigationPropertyName;$filter=SchemaName eq '{relationshipSchemaName}')
```

**Example:**
```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_document')?$expand=ManyToOneRelationships($select=SchemaName,ReferencingEntityNavigationPropertyName;$filter=SchemaName eq 'sprk_matter_document')
```

**Response:**
```json
{
  "LogicalName": "sprk_document",
  "ManyToOneRelationships": [
    {
      "SchemaName": "sprk_matter_document",
      "ReferencingEntityNavigationPropertyName": "sprk_matter"
    }
  ]
}
```

**TypeScript:**
```typescript
const query =
  `EntityDefinitions(LogicalName='sprk_document')` +
  `?$expand=ManyToOneRelationships($select=SchemaName,ReferencingEntityNavigationPropertyName;$filter=SchemaName eq 'sprk_matter_document')`;

const result = await context.webAPI.retrieveMultipleRecords('EntityDefinitions', query);
const relationships = result.entities[0].ManyToOneRelationships;
const navProp = relationships[0].ReferencingEntityNavigationPropertyName;  // "sprk_matter"
```

---

### Query 3: Get Collection Navigation Property (Parent → Child)

**Purpose:** Get collection navigation property for relationship URL (Option B)

```http
GET https://{org}.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='{parentEntity}')?$expand=OneToManyRelationships($select=SchemaName,ReferencedEntityNavigationPropertyName;$filter=SchemaName eq '{relationshipSchemaName}')
```

**Example:**
```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_matter')?$expand=OneToManyRelationships($select=SchemaName,ReferencedEntityNavigationPropertyName;$filter=SchemaName eq 'sprk_matter_document')
```

**Response:**
```json
{
  "LogicalName": "sprk_matter",
  "OneToManyRelationships": [
    {
      "SchemaName": "sprk_matter_document",
      "ReferencedEntityNavigationPropertyName": "sprk_matter_document"
    }
  ]
}
```

**TypeScript:**
```typescript
const query =
  `EntityDefinitions(LogicalName='sprk_matter')` +
  `?$expand=OneToManyRelationships($select=SchemaName,ReferencedEntityNavigationPropertyName;$filter=SchemaName eq 'sprk_matter_document')`;

const result = await context.webAPI.retrieveMultipleRecords('EntityDefinitions', query);
const relationships = result.entities[0].OneToManyRelationships;
const collectionNavProp = relationships[0].ReferencedEntityNavigationPropertyName;  // "sprk_matter_document"
```

---

## Common Pitfalls

### Pitfall 1: Using Relationship Schema Name in @odata.bind

❌ **WRONG:**
```typescript
["sprk_matter_document@odata.bind"]: "/sprk_matters(guid)"
```

✅ **CORRECT:**
```typescript
["sprk_matter@odata.bind"]: "/sprk_matters(guid)"
```

**Why:** @odata.bind requires the **child's navigation property** (from `ReferencingEntityNavigationPropertyName`), not the relationship schema name.

---

### Pitfall 2: Mixing Null Base Field with @odata.bind

❌ **WRONG:**
```typescript
{
  sprk_matter: null,
  "sprk_matter@odata.bind": "/sprk_matters(guid)"
}
```

✅ **CORRECT:**
```typescript
{
  "sprk_matter@odata.bind": "/sprk_matters(guid)"
  // No base field at all
}
```

**Error:** "An ODataPrimitiveValue was instantiated with a value of type 'Microsoft.OData.ODataEntityReferenceLink'"

---

### Pitfall 3: GUID Format

❌ **WRONG:**
```typescript
// With braces
"sprk_matter@odata.bind": "/sprk_matters({3A785F76-C773-F011-B4CB-6045BDD8B757})"

// Uppercase
"sprk_matter@odata.bind": "/sprk_matters(3A785F76-C773-F011-B4CB-6045BDD8B757)"
```

✅ **CORRECT:**
```typescript
// Lowercase, no braces
"sprk_matter@odata.bind": "/sprk_matters(3a785f76-c773-f011-b4cb-6045bdd8b757)"
```

**How to sanitize:**
```typescript
const sanitizedGuid = parentId.replace(/[{}]/g, "").toLowerCase();
```

---

### Pitfall 4: Entity Set Name vs Entity Logical Name

❌ **WRONG:**
```typescript
// Using entity logical name (singular)
"sprk_matter@odata.bind": "/sprk_matter(guid)"
```

✅ **CORRECT:**
```typescript
// Using entity set name (plural)
"sprk_matter@odata.bind": "/sprk_matters(guid)"
```

**Error:** 404 Resource not found

---

### Pitfall 5: Regex Typo for GUID Sanitization

❌ **WRONG:**
```typescript
// Escapes brackets unnecessarily
parentId.replace(/\[{}\]/g, "")
```

✅ **CORRECT:**
```typescript
// Character class to remove braces
parentId.replace(/[{}]/g, "")
```

---

## Code Hygiene

### GUID Sanitization

```typescript
/**
 * Sanitize GUID for use in OData URLs
 * - Removes curly braces: {GUID} → GUID
 * - Converts to lowercase: GUID → guid
 *
 * @param guid - GUID with or without braces, any case
 * @returns Sanitized GUID (lowercase, no braces)
 */
function sanitizeGuid(guid: string): string {
  return guid.replace(/[{}]/g, "").toLowerCase();
}

// Usage
const parentId = sanitizeGuid("{3A785F76-C773-F011-B4CB-6045BDD8B757}");
// Result: "3a785f76-c773-f011-b4cb-6045bdd8b757"
```

---

### Whitelist Payloads

❌ **WRONG - Spreading user input:**
```typescript
const payload = {
  ...formData,  // Could include malicious fields
  "sprk_matter@odata.bind": `/sprk_matters(${parentId})`
};
```

✅ **CORRECT - Explicit field mapping:**
```typescript
const payload: any = {
  sprk_documentname: formData?.documentName || file.name,
  sprk_filename: file.name,
  sprk_graphitemid: file.id,
  sprk_graphdriveid: containerId,
  sprk_filesize: file.size,
  "sprk_matter@odata.bind": `/sprk_matters(${parentId})`
};

// Only add optional fields if provided
if (formData?.description) {
  payload.sprk_documentdescription = formData.description;
}
```

---

### context.webAPI vs Xrm.WebApi

✅ **USE THIS:**
```typescript
// context.webAPI works across ALL PCF hosts
await context.webAPI.createRecord("sprk_document", payload);
```

❌ **AVOID THIS:**
```typescript
// Xrm.WebApi only works in Model-Driven Apps and Custom Pages
// Adds unnecessary complexity with fallback logic
if (typeof Xrm !== 'undefined' && Xrm.WebApi) {
  await Xrm.WebApi.createRecord("sprk_document", payload);
} else {
  await context.webAPI.createRecord("sprk_document", payload);
}
```

**Reason:** `context.webAPI` is universally available in all PCF hosts. Using `Xrm.WebApi` adds complexity without value.

---

## Quick Decision Matrix

| Scenario | Use Option A | Use Option B |
|----------|--------------|--------------|
| Creating single record from PCF | ✅ | ❌ |
| Creating multiple records from PCF | ✅ | ❌ |
| Server-side $batch operations | ❌ | ✅ |
| Option A fails for unknown reason | ❌ | ✅ (fallback) |
| Need metadata-free approach | ❌ | ✅ |
| Standard use case | ✅ | ❌ |

---

## Summary Table: Navigation Property Names

| Property Name | Found In | Used In | Purpose |
|--------------|----------|---------|---------|
| `ReferencingEntityNavigationPropertyName` | Child's `ManyToOneRelationships` | Option A (@odata.bind) | Left-hand property name |
| `ReferencedEntityNavigationPropertyName` | Parent's `OneToManyRelationships` | Option B (relationship URL) | Collection nav property in URL |
| Relationship Schema Name | Relationship metadata | Configuration | Lookup key for metadata queries |
| Entity Set Name | Entity metadata | Both options | Plural entity name in URLs |

---

## Related Documentation

- [TASK-6.1-METADATA-VALIDATION.md](./TASK-6.1-METADATA-VALIDATION.md) - How to validate metadata
- [TASK-6.2-METADATA-SERVICE.md](./TASK-6.2-METADATA-SERVICE.md) - MetadataService implementation
- [MULTI-PARENT-SUPPORT-GUIDE.md](./MULTI-PARENT-SUPPORT-GUIDE.md) - Adding new parent entities
- [Microsoft Docs - Web API Create](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/webapi/create-entity-web-api)

---

**Last Updated:** 2025-10-19
**Version:** v2.2.0
