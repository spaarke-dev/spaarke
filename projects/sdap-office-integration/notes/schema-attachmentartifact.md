# Task 011: AttachmentArtifact Table Schema

> **Status**: Ready for manual creation
> **Requires**: Power Platform Maker Portal access
> **Depends On**: EmailArtifact table (Task 010)

## Overview

The `sprk_attachmentartifact` table tracks email attachments that are saved as separate documents. Each attachment is linked to its parent EmailArtifact and to the resulting Document entity in Spaarke. This enables tracking the full lineage of attachments from email to DMS.

## Table Configuration

| Setting | Value |
|---------|-------|
| **Display Name** | Attachment Artifact |
| **Schema Name** | sprk_attachmentartifact |
| **Plural Name** | Attachment Artifacts |
| **Primary Column** | sprk_name (original filename) |
| **Ownership** | User |
| **Enable Audit** | Yes |

## Fields to Create

### Primary Name Field

| Property | Value |
|----------|-------|
| Display Name | Name |
| Schema Name | sprk_name |
| Type | Single Line of Text |
| Max Length | 260 |
| Required | Yes |
| Description | Original filename of the attachment |

### Attachment Identity Fields

#### sprk_originalfilename
| Property | Value |
|----------|-------|
| Display Name | Original Filename |
| Schema Name | sprk_originalfilename |
| Type | Single Line of Text |
| Max Length | 260 |
| Required | Yes |
| Description | Full original filename with extension |

#### sprk_contenttype
| Property | Value |
|----------|-------|
| Display Name | Content Type |
| Schema Name | sprk_contenttype |
| Type | Single Line of Text |
| Max Length | 100 |
| Required | No |
| Description | MIME type (e.g., application/pdf, image/png) |

#### sprk_size
| Property | Value |
|----------|-------|
| Display Name | Size |
| Schema Name | sprk_size |
| Type | Whole Number |
| Min Value | 0 |
| Max Value | 2147483647 |
| Required | No |
| Description | File size in bytes |

### Inline Attachment Fields

#### sprk_contentid
| Property | Value |
|----------|-------|
| Display Name | Content ID |
| Schema Name | sprk_contentid |
| Type | Single Line of Text |
| Max Length | 256 |
| Required | No |
| Description | Content-ID header for inline attachments (cid:xxx) |

#### sprk_isinline
| Property | Value |
|----------|-------|
| Display Name | Is Inline |
| Schema Name | sprk_isinline |
| Type | Yes/No (Boolean) |
| Default | No |
| Required | No |
| Description | True if this is an embedded inline attachment (e.g., image in HTML body) |

### Relationships

#### sprk_emailartifact (Lookup - Parent)
| Property | Value |
|----------|-------|
| Display Name | Email Artifact |
| Schema Name | sprk_emailartifact |
| Type | Lookup |
| Target Entity | sprk_emailartifact |
| Relationship Behavior | Referential, Restrict Delete |
| Required | Yes |
| Description | The parent email that contained this attachment |

#### sprk_document (Lookup - Saved File)
| Property | Value |
|----------|-------|
| Display Name | Document |
| Schema Name | sprk_document |
| Type | Lookup |
| Target Entity | sprk_document |
| Relationship Behavior | Referential |
| Required | No |
| Description | The saved document in Spaarke DMS (null if not yet saved) |

## Relationships Detail

### N:1 to EmailArtifact (sprk_emailartifact)
```
sprk_attachmentartifact.sprk_emailartifact → sprk_emailartifact.sprk_emailartifactid
```

- **Relationship Name**: sprk_emailartifact_attachments
- **Schema Name**: sprk_sprk_emailartifact_sprk_attachmentartifact
- **Cascade Delete**: Restrict (cannot delete email if attachments exist)
- **Cascade Assign**: NoCascade

### N:1 to Document (sprk_document)
```
sprk_attachmentartifact.sprk_document → sprk_document.sprk_documentid
```

- **Relationship Name**: sprk_document_attachment_source
- **Schema Name**: sprk_sprk_document_sprk_attachmentartifact
- **Cascade Delete**: RemoveLink
- **Cascade Assign**: NoCascade

## Indexes Required

1. **Index on sprk_emailartifact**
   - Purpose: Fast lookup of attachments by parent email
   - Column: sprk_emailartifact (ascending)

2. **Index on sprk_contentid** (filtered index, non-null only)
   - Purpose: Fast lookup of inline attachments by Content-ID
   - Column: sprk_contentid (ascending)
   - Filter: WHERE sprk_contentid IS NOT NULL

## Inline Attachment Pattern

Inline attachments are images embedded directly in HTML email bodies. They use Content-ID references:

**HTML Body Reference:**
```html
<img src="cid:image001.png@01DAB234.12345678">
```

**Attachment Matching:**
- `sprk_isinline = true`
- `sprk_contentid = "image001.png@01DAB234.12345678"`

The Content-ID allows the system to:
1. Identify which attachments are inline vs regular file attachments
2. Replace `cid:` references with actual URLs when displaying saved emails
3. Properly render the email body with embedded images

## Workflow

1. **Email Save Initiated**: User clicks "Save to Spaarke" in Outlook
2. **Attachments Enumerated**: BFF API extracts attachment metadata
3. **AttachmentArtifact Created**: One record per attachment
   - `sprk_isinline` set based on Content-Disposition header
   - `sprk_contentid` captured for inline attachments
4. **Attachment Saved**: File uploaded to SPE, Document record created
5. **AttachmentArtifact Updated**: `sprk_document` lookup populated

## Verification Checklist

- [ ] Table `sprk_attachmentartifact` exists
- [ ] All fields created with correct types and lengths
- [ ] sprk_emailartifact lookup relationship works
- [ ] sprk_document lookup relationship works
- [ ] Cascade delete configured as Restrict on EmailArtifact
- [ ] sprk_isinline defaults to No
- [ ] Table is solution-aware

## Example Records

### Regular File Attachment
```json
{
  "sprk_name": "Contract_2026.pdf",
  "sprk_originalfilename": "Contract_2026.pdf",
  "sprk_contenttype": "application/pdf",
  "sprk_size": 245678,
  "sprk_isinline": false,
  "sprk_contentid": null,
  "sprk_emailartifact": { "@odata.bind": "/sprk_emailartifacts(abc123...)" },
  "sprk_document": { "@odata.bind": "/sprk_documents(def456...)" }
}
```

### Inline Image Attachment
```json
{
  "sprk_name": "signature_logo.png",
  "sprk_originalfilename": "signature_logo.png",
  "sprk_contenttype": "image/png",
  "sprk_size": 12456,
  "sprk_isinline": true,
  "sprk_contentid": "signature_logo.png@01DAB234.12345678",
  "sprk_emailartifact": { "@odata.bind": "/sprk_emailartifacts(abc123...)" },
  "sprk_document": null
}
```

## References

- [Task 011 POML](../tasks/011-create-attachmentartifact-table.poml)
- [Task 010 EmailArtifact Schema](./schema-emailartifact.md)
- [Dataverse Schema Guide](../../../docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md)
