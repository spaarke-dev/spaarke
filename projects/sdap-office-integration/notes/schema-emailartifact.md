# Task 010: EmailArtifact Table Schema

> **Status**: Ready for manual creation
> **Requires**: Power Platform Maker Portal access

## Overview

The `sprk_emailartifact` table stores email metadata and body snapshots when users save emails from Outlook. It links to the Document entity (which tracks the saved PDF/EML file) and provides fields for duplicate detection via message ID and headers hash.

## Table Configuration

| Setting | Value |
|---------|-------|
| **Display Name** | Email Artifact |
| **Schema Name** | sprk_emailartifact |
| **Plural Name** | Email Artifacts |
| **Primary Column** | sprk_name (auto-generated) |
| **Ownership** | User |
| **Enable Audit** | Yes |

## Fields to Create

### Primary Name Field

| Property | Value |
|----------|-------|
| Display Name | Name |
| Schema Name | sprk_name |
| Type | Single Line of Text |
| Max Length | 400 |
| Required | Yes |
| Description | Auto-generated from Subject + Date |

### Core Email Fields

#### sprk_subject
| Property | Value |
|----------|-------|
| Display Name | Subject |
| Schema Name | sprk_subject |
| Type | Single Line of Text |
| Max Length | 400 |
| Required | No |
| Description | Email subject line |

#### sprk_sender
| Property | Value |
|----------|-------|
| Display Name | Sender |
| Schema Name | sprk_sender |
| Type | Single Line of Text |
| Max Length | 320 |
| Required | No |
| Format | Email |
| Description | Sender email address |

#### sprk_recipients
| Property | Value |
|----------|-------|
| Display Name | Recipients |
| Schema Name | sprk_recipients |
| Type | Multiline Text |
| Max Length | 10000 |
| Required | No |
| Description | JSON array of recipient email addresses |

#### sprk_ccrecipients
| Property | Value |
|----------|-------|
| Display Name | CC Recipients |
| Schema Name | sprk_ccrecipients |
| Type | Multiline Text |
| Max Length | 10000 |
| Required | No |
| Description | JSON array of CC recipient email addresses |

### Date Fields

#### sprk_sentdate
| Property | Value |
|----------|-------|
| Display Name | Sent Date |
| Schema Name | sprk_sentdate |
| Type | Date and Time |
| Behavior | User Local |
| Required | No |
| Description | When the email was sent |

#### sprk_receiveddate
| Property | Value |
|----------|-------|
| Display Name | Received Date |
| Schema Name | sprk_receiveddate |
| Type | Date and Time |
| Behavior | User Local |
| Required | No |
| Description | When the email was received |

### Identification Fields (for Duplicate Detection)

#### sprk_messageid
| Property | Value |
|----------|-------|
| Display Name | Message ID |
| Schema Name | sprk_messageid |
| Type | Single Line of Text |
| Max Length | 256 |
| Required | No |
| **Searchable** | Yes |
| Description | Internet Message-ID header for uniqueness |

#### sprk_internetheadershash
| Property | Value |
|----------|-------|
| Display Name | Headers Hash |
| Schema Name | sprk_internetheadershash |
| Type | Single Line of Text |
| Max Length | 64 |
| Required | No |
| **Searchable** | Yes |
| Description | SHA256 hash of key headers for duplicate detection |

#### sprk_conversationid
| Property | Value |
|----------|-------|
| Display Name | Conversation ID |
| Schema Name | sprk_conversationid |
| Type | Single Line of Text |
| Max Length | 256 |
| Required | No |
| Description | Exchange conversation ID for threading |

### Additional Fields

#### sprk_importance
| Property | Value |
|----------|-------|
| Display Name | Importance |
| Schema Name | sprk_importance |
| Type | Option Set (Local) |
| Options | Low (0), Normal (1), High (2) |
| Default | Normal (1) |
| Required | No |

#### sprk_hasattachments
| Property | Value |
|----------|-------|
| Display Name | Has Attachments |
| Schema Name | sprk_hasattachments |
| Type | Yes/No (Boolean) |
| Default | No |
| Required | No |

#### sprk_bodypreview
| Property | Value |
|----------|-------|
| Display Name | Body Preview |
| Schema Name | sprk_bodypreview |
| Type | Multiline Text |
| Max Length | 2000 |
| Required | No |
| Description | First 2000 chars of email body for preview |

### Relationships

#### sprk_document (Lookup)
| Property | Value |
|----------|-------|
| Display Name | Document |
| Schema Name | sprk_document |
| Type | Lookup |
| Target Entity | sprk_document |
| Relationship Behavior | Referential (delete link when parent deleted) |
| Required | No |
| Description | Link to the saved document file |

## Indexes Required

Create the following indexes for query performance:

1. **Index on sprk_messageid**
   - Purpose: Fast duplicate detection by Message-ID
   - Column: sprk_messageid (ascending)

2. **Index on sprk_internetheadershash**
   - Purpose: Fast duplicate detection by content hash
   - Column: sprk_internetheadershash (ascending)

3. **Index on sprk_sentdate**
   - Purpose: Date-range queries
   - Column: sprk_sentdate (descending)

## Step-by-Step Creation

### Using Power Apps Maker Portal

1. Go to **make.powerapps.com** → Select environment
2. **Tables** → **+ New table**
3. Enter:
   - Display name: `Email Artifact`
   - Primary column: `Name`
4. Click **Save**
5. Add each field from the list above
6. Create the lookup relationship to Document
7. Configure indexes via Advanced settings

### Using PAC CLI

```powershell
# Authenticate
pac auth create --url https://spaarkedev1.crm.dynamics.com

# Create table (solution-aware)
pac solution create --name SpaarkeOfficeIntegration --publisher-name spaarke --publisher-prefix sprk

# Export solution, add table definition, import
# Note: Detailed PAC CLI steps for table creation require XML modification
```

## Verification Checklist

- [ ] Table `sprk_emailartifact` exists
- [ ] All fields created with correct types and lengths
- [ ] sprk_messageid field is searchable/indexed
- [ ] sprk_internetheadershash field is searchable/indexed
- [ ] Lookup to Document entity configured
- [ ] Importance option set has Low/Normal/High values
- [ ] Table is solution-aware (in Spaarke solution)

## Notes

- The `sprk_internetheadershash` field stores a SHA256 hash of selected headers (From, To, Date, Subject, Message-ID) for reliable duplicate detection
- The `sprk_messageid` from internet headers may not always be available (some email systems don't include it)
- Both fields should be indexed for efficient duplicate checking during email save operations

## References

- [Task 010 POML](../tasks/010-create-emailartifact-table.poml)
- [Dataverse Schema Guide](../../../docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md)
