# Actual Dataverse Entity Schema

**Generated:** 2025-09-30
**Source:** Power Platform CLI Export + CONFIGURATION_REQUIREMENTS.md
**Environment:** SPAARKE DEV 1 (https://spaarkedev1.crm.dynamics.com/)

---

## 📋 OVERVIEW

This document contains the **actual, verified schema** of Dataverse entities as they exist in the environment. Use this as the single source of truth for Power Platform UI development.

**Export Details:**
- Solution: `spaarke_document_management` (Version 1.0.0.1)
- Export Date: 2025-09-30
- Method: `pac solution export` + manual inspection

---

## 🗂️ sprk_Document Entity

### **Entity Metadata**
- **Logical Name:** `sprk_document`
- **Display Name:** Document
- **Plural Name:** Documents
- **Entity Set Name:** `sprk_documents`
- **Primary Key:** `sprk_documentid`
- **Primary Name Field:** `sprk_documentname`
- **Ownership Type:** UserOwned

### **Field Definitions**

| Logical Name | Physical Name | Type | Max Length | Required | Secured | Display Name | Description |
|--------------|---------------|------|------------|----------|---------|--------------|-------------|
| `sprk_documentid` | sprk_DocumentId | uniqueidentifier | - | Yes | No | Document | Primary key |
| `sprk_documentname` | sprk_DocumentName | nvarchar | 850 | Yes | No | Document Name | Primary name field |
| `sprk_documentdescription` | sprk_DocumentDescription | nvarchar | 2000 | No | No | Document Description | Description or notes |
| `sprk_containerid` | sprk_ContainerId | lookup | - | No | No | Container Reference | Reference to sprk_container |
| `sprk_hasfile` | sprk_HasFile | bit | - | No | No | Has File | Whether document has file |
| `sprk_filename` | sprk_FileName | nvarchar | 1000 | No | **Yes** | File Name | Name of file in SPE |
| `sprk_filesize` | sprk_FileSize | int | - | No | No | File Size | File size in bytes (max 2GB) |
| `sprk_mimetype` | sprk_MimeType | nvarchar | 100 | No | No | Mime Type | File MIME type |
| `sprk_graphitemid` | sprk_GraphItemId | nvarchar | 1000 | No | No | Graph Item Id | SPE item identifier |
| `sprk_graphdriveid` | sprk_GraphDriveId | nvarchar | 1000 | No | No | Graph Drive Id | SPE drive identifier |
| `sprk_matter` | sprk_Matter | lookup | - | No | No | Matter | Matter reference (future use) |
| `statuscode` | statuscode | status | - | No | No | Status Reason | Document status reason |
| `statecode` | statecode | state | - | No | No | Status | Entity state |
| `ownerid` | ownerid | owner | - | Yes | No | Owner | Record owner |
| `createdon` | createdon | datetime | - | No | No | Created On | Date created |
| `createdby` | createdby | lookup | - | No | No | Created By | User who created |
| `modifiedon` | modifiedon | datetime | - | No | No | Modified On | Date modified |
| `modifiedby` | modifiedby | lookup | - | No | No | Modified By | User who modified |

### **State Code (statecode) Values**

| Value | Label | Color | State |
|-------|-------|-------|-------|
| 0 | Active | #0078D4 | Active |
| 1 | Inactive | #D13438 | Inactive |

**C# Enum:**
```csharp
public enum StateCode
{
    Active = 0,
    Inactive = 1
}
```

### **Status Code (statuscode) Values**

| Value | Label | Color | Associated State |
|-------|-------|-------|------------------|
| 1 | Draft | #0078D4 | Active (0) |
| 421500001 | Active | #107C10 | Active (0) |
| 421500002 | Processing | #FF8C00 | Active (0) |
| 2 | Error | #D13438 | Inactive (1) |

**C# Enum:**
```csharp
public enum StatusCode
{
    Draft = 1,
    Active = 421500001,
    Processing = 421500002,
    Error = 2
}
```

**IMPORTANT NOTES:**
1. **Code Mismatch:** C# code uses simplified enum (Draft=1, Active=2, Inactive=3)
2. **Dataverse Reality:** Actual values are Draft=1, Active=421500001, Processing=421500002, Error=2
3. **Impact:** Code needs update OR Task 3.1 should note this limitation

### **Field-Level Security**

**Secured Fields:**
- `sprk_filename` - Only visible to users with appropriate field security profile

**Field Security Profiles Needed:**
- Document Manager Profile: Read/Update access to sprk_filename
- Document User Profile: No access to sprk_filename

---

## 🗂️ sprk_Container Entity

### **Entity Metadata**
- **Logical Name:** `sprk_container`
- **Display Name:** Container
- **Plural Name:** Containers
- **Primary Key:** `sprk_containerid`
- **Primary Name Field:** `sprk_name`
- **Ownership Type:** UserOwned

### **Field Definitions**

| Logical Name | Physical Name | Type | Max Length | Required | Display Name | Description |
|--------------|---------------|------|------------|----------|--------------|-------------|
| `sprk_containerid` | sprk_ContainerId | uniqueidentifier | - | Yes | Container | Primary key |
| `sprk_name` | sprk_Name | nvarchar | 850 | Yes | Name | Container name |
| `sprk_specontainerid` | sprk_SpeContainerId | nvarchar | 1000 | No | SPE Container ID | SharePoint Embedded ID |
| `sprk_documentcount` | sprk_DocumentCount | int | - | No | Document Count | Number of documents |
| `sprk_driveid` | sprk_DriveId | nvarchar | 1000 | No | Drive ID | Drive identifier |
| `statuscode` | statuscode | status | - | No | Status Reason | Standard status |
| `statecode` | statecode | state | - | No | Status | Standard state |

---

## 🔗 RELATIONSHIPS

### **Container to Documents (1:N)**
- **Relationship Name:** `sprk_container_sprk_document`
- **Parent Entity:** sprk_Container
- **Child Entity:** sprk_Document
- **Lookup Field:** sprk_containerid
- **Type:** 1:N (One Container to Many Documents)

### **Matter to Documents (1:N)**
- **Relationship Name:** `sprk_matter_sprk_document`
- **Parent Entity:** sprk_Matter
- **Child Entity:** sprk_Document
- **Lookup Field:** sprk_matter
- **Type:** 1:N (One Matter to Many Documents)
- **Status:** Field exists but not used in current sprint

---

## ⚠️ IMPORTANT DISCREPANCIES

### **Issue 1: Status Code Enum Mismatch**

**Dataverse Reality:**
```csharp
public enum StatusCode
{
    Draft = 1,
    Active = 421500001,
    Processing = 421500002,
    Error = 2
}
```

**C# Code Implementation (Models.cs:52-57):**
```csharp
public enum DocumentStatus
{
    Draft = 1,
    Active = 2,
    Inactive = 3
}
```

**Impact:**
- Code cannot properly read/write Active, Processing, or Error statuses
- Background service status updates may fail
- Need to update C# enum OR note limitation in Task 3.1

**Recommendation:** Update C# enum to match Dataverse values

### **Issue 2: File Size Type**

**Dataverse:** `int` (max 2,147,483,647 bytes = ~2GB)
**C# Code:** `long` (can represent larger values)

**Impact:** Minor - 2GB is sufficient for most document scenarios

### **Issue 3: Matter Field**

**Dataverse:** `sprk_matter` lookup field exists
**C# Code:** Not included in DocumentEntity model

**Impact:** Field cannot be read/written via current API
**Status:** Out of scope for Sprint 2

---

## 📊 USAGE IN POWER PLATFORM

### **For Form Development**
Use these exact logical names in form XML:
- `sprk_documentname` (not sprk_name)
- `sprk_documentdescription` (not sprk_description)
- `statuscode` (status reason)
- `statecode` (state)

### **For View FetchXML**
```xml
<fetch>
  <entity name="sprk_document">
    <attribute name="sprk_documentname" />
    <attribute name="sprk_documentdescription" />
    <attribute name="sprk_containerid" />
    <attribute name="sprk_hasfile" />
    <attribute name="sprk_filename" />
    <attribute name="sprk_filesize" />
    <attribute name="statuscode" />
    <attribute name="statecode" />
    <order attribute="modifiedon" descending="true" />
    <filter type="and">
      <condition attribute="statuscode" operator="eq" value="421500001" />
    </filter>
  </entity>
</fetch>
```

### **For Charts and Dashboards**
Use statuscode values:
- Draft: 1
- Active: 421500001
- Processing: 421500002
- Error: 2

---

## ✅ VALIDATION CHECKLIST

Before proceeding with Task 3.1:
- [x] Entity schema exported and verified
- [x] Field names confirmed to match C# code
- [x] Status code values documented from CONFIGURATION_REQUIREMENTS.md
- [x] Secured fields identified (sprk_filename)
- [x] Relationships documented
- [ ] C# enum updated to match Dataverse status codes (PENDING)
- [ ] Task 3.1 updated with accurate field names and values

---

## 📝 NOTES

1. **Source of Truth:** This document reflects the actual Dataverse schema as of 2025-09-30
2. **Updates Needed:** C# DocumentStatus enum should be updated to match Dataverse
3. **Security:** sprk_filename field requires field-level security configuration
4. **Future Fields:** sprk_matter exists but is not implemented in current sprint
5. **Container Entity:** Documented but not fully implemented in API layer yet
