# sprk_registrationrequest — Dataverse Table Schema Definition

> **Entity**: `sprk_registrationrequest`
> **Display Name**: Registration Request
> **Plural Display Name**: Registration Requests
> **Ownership**: Organization Owned
> **Target Environment**: Demo Dataverse (`spaarke-demo.crm.dynamics.com`)
> **Publisher Prefix**: `sprk_`
> **Solution**: DemoRegistration (unmanaged, per ADR-027)

---

## Table of Contents

1. [Table Definition](#1-table-definition)
2. [Column Definitions](#2-column-definitions)
3. [Choice (Option Set) Definitions](#3-choice-option-set-definitions)
4. [Lookup Relationships](#4-lookup-relationships)
5. [View Definitions](#5-view-definitions)
6. [Form Definition](#6-form-definition)
7. [Sitemap Configuration](#7-sitemap-configuration)
8. [Deployment Order](#8-deployment-order)
9. [Web API Script Reference](#9-web-api-script-reference)

---

## 1. Table Definition

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_registrationrequest` |
| **LogicalName** | `sprk_registrationrequest` |
| **DisplayName** | Registration Request |
| **DisplayCollectionName** | Registration Requests |
| **Description** | Tracks self-service demo access requests through the full lifecycle: submission, review, provisioning, expiration. |
| **OwnershipType** | OrganizationOwned |
| **IsActivity** | false |
| **HasNotes** | false |
| **HasActivities** | true (Timeline on Tab 4) |
| **PrimaryNameAttribute** | `sprk_name` |
| **PrimaryNameFormat** | `"{FirstName} {LastName} - {Organization}"` (set programmatically on create) |

---

## 2. Column Definitions

### 2.1 Primary Name Column

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_name` |
| **DisplayName** | Name |
| **Type** | `StringAttributeMetadata` |
| **MaxLength** | 200 |
| **RequiredLevel** | ApplicationRequired |
| **IsPrimaryName** | true |
| **Description** | Auto-generated: "{FirstName} {LastName} - {Organization}" |

### 2.2 Applicant Information Columns

#### sprk_firstname

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_firstname` |
| **DisplayName** | First Name |
| **Type** | `StringAttributeMetadata` |
| **MaxLength** | 100 |
| **RequiredLevel** | ApplicationRequired |
| **Description** | Applicant's first name |

#### sprk_lastname

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_lastname` |
| **DisplayName** | Last Name |
| **Type** | `StringAttributeMetadata` |
| **MaxLength** | 100 |
| **RequiredLevel** | ApplicationRequired |
| **Description** | Applicant's last name |

#### sprk_email

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_email` |
| **DisplayName** | Email |
| **Type** | `StringAttributeMetadata` |
| **MaxLength** | 200 |
| **RequiredLevel** | ApplicationRequired |
| **Description** | Applicant's work email address |

#### sprk_organization

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_organization` |
| **DisplayName** | Organization |
| **Type** | `StringAttributeMetadata` |
| **MaxLength** | 200 |
| **RequiredLevel** | ApplicationRequired |
| **Description** | Applicant's organization/company name |

#### sprk_jobtitle

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_jobtitle` |
| **DisplayName** | Job Title |
| **Type** | `StringAttributeMetadata` |
| **MaxLength** | 200 |
| **RequiredLevel** | None |
| **Description** | Applicant's job title (optional) |

#### sprk_phone

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_phone` |
| **DisplayName** | Phone |
| **Type** | `StringAttributeMetadata` |
| **MaxLength** | 50 |
| **RequiredLevel** | None |
| **Description** | Applicant's phone number (optional) |

### 2.3 Request Details Columns

#### sprk_usecase

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_usecase` |
| **DisplayName** | Use Case |
| **Type** | `PicklistAttributeMetadata` |
| **RequiredLevel** | ApplicationRequired |
| **OptionSet** | Local (see [Section 3.1](#31-sprk_usecase-options)) |
| **Description** | Primary use case the applicant is interested in |

#### sprk_referralsource

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_referralsource` |
| **DisplayName** | Referral Source |
| **Type** | `PicklistAttributeMetadata` |
| **RequiredLevel** | None |
| **OptionSet** | Local (see [Section 3.2](#32-sprk_referralsource-options)) |
| **Description** | How the applicant heard about Spaarke (optional) |

#### sprk_notes

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_notes` |
| **DisplayName** | Notes |
| **Type** | `MemoAttributeMetadata` |
| **MaxLength** | 10000 |
| **RequiredLevel** | None |
| **Description** | Additional notes or comments from the applicant |

#### sprk_consentaccepted

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_consentaccepted` |
| **DisplayName** | Consent Accepted |
| **Type** | `BooleanAttributeMetadata` |
| **RequiredLevel** | None |
| **TrueOption** | Value = 1, Label = "Yes" |
| **FalseOption** | Value = 0, Label = "No" |
| **Description** | Whether the applicant accepted the terms and consent checkbox |

#### sprk_consentdate

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_consentdate` |
| **DisplayName** | Consent Date |
| **Type** | `DateTimeAttributeMetadata` |
| **Format** | DateAndTime |
| **DateTimeBehavior** | UserLocal |
| **RequiredLevel** | None |
| **Description** | Timestamp when the applicant accepted consent |

### 2.4 Lifecycle / Status Columns

#### sprk_status

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_status` |
| **DisplayName** | Status |
| **Type** | `PicklistAttributeMetadata` |
| **RequiredLevel** | ApplicationRequired |
| **DefaultValue** | 0 (Submitted) |
| **OptionSet** | Local (see [Section 3.3](#33-sprk_status-options)) |
| **Description** | Current lifecycle status of the registration request |

#### sprk_trackingid

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_trackingid` |
| **DisplayName** | Tracking ID |
| **Type** | `StringAttributeMetadata` |
| **MaxLength** | 50 |
| **RequiredLevel** | None |
| **Description** | Public-facing reference ID. Format: REG-{YYYYMMDD}-{4char} (e.g., REG-20260403-A7K2). Auto-generated by BFF API on create. |

#### sprk_requestdate

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_requestdate` |
| **DisplayName** | Request Date |
| **Type** | `DateTimeAttributeMetadata` |
| **Format** | DateAndTime |
| **DateTimeBehavior** | UserLocal |
| **RequiredLevel** | None |
| **Description** | Timestamp when the request was submitted. Auto-set by BFF API on create. |

### 2.5 Review Columns

#### sprk_reviewedby

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_reviewedby` |
| **DisplayName** | Reviewed By |
| **Type** | `LookupAttributeMetadata` |
| **Target Entity** | `systemuser` |
| **RequiredLevel** | None |
| **Description** | The admin who approved or rejected the request |
| **Note** | Created via RelationshipDefinitions endpoint (see [Section 4](#4-lookup-relationships)) |

#### sprk_reviewdate

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_reviewdate` |
| **DisplayName** | Review Date |
| **Type** | `DateTimeAttributeMetadata` |
| **Format** | DateAndTime |
| **DateTimeBehavior** | UserLocal |
| **RequiredLevel** | None |
| **Description** | Timestamp when the request was approved or rejected |

#### sprk_rejectionreason

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_rejectionreason` |
| **DisplayName** | Rejection Reason |
| **Type** | `StringAttributeMetadata` |
| **MaxLength** | 500 |
| **RequiredLevel** | None |
| **Description** | Reason provided by admin if the request was rejected |

### 2.6 Provisioning Columns

#### sprk_demousername

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_demousername` |
| **DisplayName** | Demo Username |
| **Type** | `StringAttributeMetadata` |
| **MaxLength** | 200 |
| **RequiredLevel** | None |
| **Description** | Provisioned UPN (e.g., jane.smith@demo.spaarke.com). Set by provisioning pipeline. |

#### sprk_demouserobjectid

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_demouserobjectid` |
| **DisplayName** | Demo User Object ID |
| **Type** | `StringAttributeMetadata` |
| **MaxLength** | 50 |
| **RequiredLevel** | None |
| **Description** | Entra ID object ID of the provisioned demo user account |

#### sprk_provisioneddate

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_provisioneddate` |
| **DisplayName** | Provisioned Date |
| **Type** | `DateTimeAttributeMetadata` |
| **Format** | DateAndTime |
| **DateTimeBehavior** | UserLocal |
| **RequiredLevel** | None |
| **Description** | Timestamp when the demo account was provisioned |

#### sprk_expirationdate

| Property | Value |
|----------|-------|
| **SchemaName** | `sprk_expirationdate` |
| **DisplayName** | Expiration Date |
| **Type** | `DateTimeAttributeMetadata` |
| **Format** | DateAndTime |
| **DateTimeBehavior** | UserLocal |
| **RequiredLevel** | None |
| **Description** | When the demo access expires. Default: 14 days from provisioning. Admin can manually adjust this date at any time. |

---

## 3. Choice (Option Set) Definitions

All option sets below are **local** (entity-scoped) since they are specific to the registration request entity.

### 3.1 sprk_usecase Options

| Value | Label | Description |
|-------|-------|-------------|
| 0 | Document Management | Interest in SPE document management features |
| 1 | AI Analysis | Interest in AI-powered analysis capabilities |
| 2 | Financial Intelligence | Interest in financial intelligence module |
| 3 | General | General interest / exploring the platform |

### 3.2 sprk_referralsource Options

| Value | Label | Description |
|-------|-------|-------------|
| 0 | Conference | Learned about Spaarke at a conference or event |
| 1 | Website | Found via spaarke.com or web search |
| 2 | Referral | Referred by a colleague or partner |
| 3 | Search | Found via search engine |
| 4 | Other | Other source |

### 3.3 sprk_status Options

| Value | Label | Description |
|-------|-------|-------------|
| 0 | Submitted | Request received, awaiting admin review (default) |
| 1 | Approved | Admin approved; provisioning initiated |
| 2 | Rejected | Admin rejected the request |
| 3 | Provisioned | Demo account fully provisioned and active |
| 4 | Expired | Demo access expired; account disabled |
| 5 | Revoked | Admin manually revoked access before expiration |

**Status Transitions:**
```
Submitted (0) ─── Approve ──→ Approved (1) ──→ Provisioned (3) ──→ Expired (4)
      │                                              │
      └──── Reject ───→ Rejected (2)                 └──→ Revoked (5)
```

> **Note**: The `Approved` (1) status is transient — the provisioning pipeline sets it at the start
> and immediately transitions to `Provisioned` (3) on success. If provisioning fails partway through,
> the record stays at `Approved` to indicate it needs retry.

---

## 4. Lookup Relationships

### 4.1 sprk_reviewedby (Lookup to SystemUser)

This column MUST be created via the `RelationshipDefinitions` endpoint, not the `Attributes` endpoint.

| Property | Value |
|----------|-------|
| **Relationship SchemaName** | `sprk_systemuser_registrationrequest_reviewedby` |
| **ReferencedEntity** (parent, 1 side) | `systemuser` |
| **ReferencingEntity** (child, N side) | `sprk_registrationrequest` |
| **Lookup SchemaName** | `sprk_reviewedby` |
| **Lookup DisplayName** | Reviewed By |
| **RequiredLevel** | None |
| **CascadeConfiguration** | |
| - Delete | RemoveLink (clear lookup if user is deleted) |
| - Assign | NoCascade |
| - Merge | NoCascade |
| - Reparent | NoCascade |
| - Share | NoCascade |
| - Unshare | NoCascade |

---

## 5. View Definitions

### 5.1 Pending Demo Requests (Default View)

| Property | Value |
|----------|-------|
| **Name** | Pending Demo Requests |
| **Description** | Submitted requests awaiting admin review |
| **IsDefault** | true |
| **Filter** | `sprk_status eq 0` (Submitted) |
| **Sort** | `sprk_requestdate ASC` (oldest first) |

**Columns:**

| # | Column | Display Name | Width |
|---|--------|-------------|-------|
| 1 | `sprk_name` | Name | 250 |
| 2 | `sprk_email` | Email | 200 |
| 3 | `sprk_organization` | Organization | 200 |
| 4 | `sprk_usecase` | Use Case | 150 |
| 5 | `sprk_requestdate` | Request Date | 150 |

### 5.2 All Demo Requests

| Property | Value |
|----------|-------|
| **Name** | All Demo Requests |
| **Description** | All registration requests regardless of status |
| **IsDefault** | false |
| **Filter** | None (show all records) |
| **Sort** | `sprk_requestdate DESC` (newest first) |

**Columns:**

| # | Column | Display Name | Width |
|---|--------|-------------|-------|
| 1 | `sprk_name` | Name | 200 |
| 2 | `sprk_email` | Email | 200 |
| 3 | `sprk_organization` | Organization | 175 |
| 4 | `sprk_status` | Status | 125 |
| 5 | `sprk_requestdate` | Request Date | 150 |
| 6 | `sprk_trackingid` | Tracking ID | 150 |

### 5.3 Active Demo Users

| Property | Value |
|----------|-------|
| **Name** | Active Demo Users |
| **Description** | Currently provisioned and active demo accounts |
| **IsDefault** | false |
| **Filter** | `sprk_status eq 3` (Provisioned) |
| **Sort** | `sprk_expirationdate ASC` (soonest expiration first) |

**Columns:**

| # | Column | Display Name | Width |
|---|--------|-------------|-------|
| 1 | `sprk_name` | Name | 200 |
| 2 | `sprk_email` | Email | 200 |
| 3 | `sprk_demousername` | Demo Username | 225 |
| 4 | `sprk_provisioneddate` | Provisioned Date | 150 |
| 5 | `sprk_expirationdate` | Expiration Date | 150 |

### 5.4 Expired Demo Users

| Property | Value |
|----------|-------|
| **Name** | Expired Demo Users |
| **Description** | Demo accounts that have expired |
| **IsDefault** | false |
| **Filter** | `sprk_status eq 4` (Expired) |
| **Sort** | `sprk_expirationdate DESC` (most recently expired first) |

**Columns:**

| # | Column | Display Name | Width |
|---|--------|-------------|-------|
| 1 | `sprk_name` | Name | 200 |
| 2 | `sprk_email` | Email | 200 |
| 3 | `sprk_demousername` | Demo Username | 225 |
| 4 | `sprk_expirationdate` | Expiration Date | 150 |

---

## 6. Form Definition

### Main Form: "Registration Request"

#### Header Fields

| # | Column | Display Name | Notes |
|---|--------|-------------|-------|
| 1 | `sprk_status` | Status | Choice field, read-only recommended |
| 2 | `sprk_trackingid` | Tracking ID | Read-only |

#### Tab 1: Request Details

**Section 1.1: Applicant Information (2-column layout)**

| Row | Left Column | Right Column |
|-----|------------|-------------|
| 1 | `sprk_firstname` (First Name) | `sprk_lastname` (Last Name) |
| 2 | `sprk_email` (Email) | `sprk_phone` (Phone) |
| 3 | `sprk_organization` (Organization) | `sprk_jobtitle` (Job Title) |

**Section 1.2: Request Information (2-column layout)**

| Row | Left Column | Right Column |
|-----|------------|-------------|
| 1 | `sprk_usecase` (Use Case) | `sprk_referralsource` (Referral Source) |
| 2 | `sprk_consentaccepted` (Consent Accepted) | `sprk_consentdate` (Consent Date) |

**Section 1.3: Notes (full-width)**

| Row | Column |
|-----|--------|
| 1 | `sprk_notes` (Notes) — full-width, 4-row height |

#### Tab 2: Review

**Section 2.1: Review Details (2-column layout)**

| Row | Left Column | Right Column |
|-----|------------|-------------|
| 1 | `sprk_reviewedby` (Reviewed By) | `sprk_reviewdate` (Review Date) |

**Section 2.2: Rejection (full-width)**

| Row | Column |
|-----|--------|
| 1 | `sprk_rejectionreason` (Rejection Reason) — full-width, 3-row height |

> **Visibility hint**: The Rejection section is most relevant when `sprk_status = 2` (Rejected).
> Consider hiding via form script or business rule when status is not Rejected.

#### Tab 3: Provisioning

**Section 3.1: Demo Account Details (2-column layout)**

| Row | Left Column | Right Column |
|-----|------------|-------------|
| 1 | `sprk_demousername` (Demo Username) | `sprk_demouserobjectid` (Demo User Object ID) |
| 2 | `sprk_provisioneddate` (Provisioned Date) | `sprk_expirationdate` (Expiration Date) |

> **Note on sprk_expirationdate**: This field MUST remain editable by admins. Per FR-14, the admin
> can manually adjust the expiration date on any record. The expiration BackgroundService reads
> this field to determine when to disable accounts.

#### Tab 4: Timeline

**Section 4.1: Timeline (full-width)**

| Control | Type |
|---------|------|
| Timeline Wall | Standard Timeline control (`Microsoft.Timeline`) |

> The Timeline tab shows activities (notes, emails, system posts) associated with the record.
> Useful for tracking admin actions and communication history.

---

## 7. Sitemap Configuration

### Area Group: Demo Management

Add a new area group to the MDA sitemap for the Demo environment.

```
App Navigation
├── ... (existing areas)
└── Demo Management              ← New Area
    └── Registration              ← New Group
        └── Registration Requests ← New SubArea
```

| Property | Value |
|----------|-------|
| **Area Title** | Demo Management |
| **Area Icon** | `mdi:account-plus-outline` (or appropriate Dynamics icon) |
| **Group Title** | Registration |
| **SubArea Title** | Registration Requests |
| **SubArea Entity** | `sprk_registrationrequest` |
| **SubArea Default View** | Pending Demo Requests |

---

## 8. Deployment Order

Follow the standard Dataverse schema deployment order from the how-to guide:

```
Step 1: Create Local Option Sets + Entity
        ├── sprk_usecase (local choice)
        ├── sprk_referralsource (local choice)
        ├── sprk_status (local choice)
        └── sprk_registrationrequest entity with sprk_name primary column

Step 2: Add String Attributes
        ├── sprk_firstname (Text 100, Required)
        ├── sprk_lastname (Text 100, Required)
        ├── sprk_email (Text 200, Required)
        ├── sprk_organization (Text 200, Required)
        ├── sprk_jobtitle (Text 200)
        ├── sprk_phone (Text 50)
        ├── sprk_trackingid (Text 50)
        ├── sprk_rejectionreason (Text 500)
        ├── sprk_demousername (Text 200)
        └── sprk_demouserobjectid (Text 50)

Step 3: Add Memo Attribute
        └── sprk_notes (Multiline Text)

Step 4: Add Picklist Attributes (reference local option sets)
        ├── sprk_usecase
        ├── sprk_referralsource
        └── sprk_status

Step 5: Add DateTime Attributes
        ├── sprk_requestdate
        ├── sprk_reviewdate
        ├── sprk_provisioneddate
        ├── sprk_expirationdate
        └── sprk_consentdate

Step 6: Add Boolean Attribute
        └── sprk_consentaccepted

Step 7: Create Lookup Relationship (via RelationshipDefinitions)
        └── sprk_reviewedby → systemuser

Step 8: Publish Customizations

Step 9: Create Views (manual in MDA or via solution export/import)
        ├── Pending Demo Requests (default)
        ├── All Demo Requests
        ├── Active Demo Users
        └── Expired Demo Users

Step 10: Create/Configure Form (manual in MDA)
         └── Main form with 4 tabs (Request Details, Review, Provisioning, Timeline)

Step 11: Configure Sitemap
         └── Add "Demo Management" area with "Registration Requests" sub-area

Step 12: Add to Solution
         └── Add entity + all components to DemoRegistration solution (unmanaged)
```

---

## 9. Web API Script Reference

Below are the Web API payloads for programmatic creation. Use with the `Invoke-DataverseApi` helper
from `docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md`.

### 9.1 Authentication Setup

```powershell
$Environment = "spaarke-demo.crm.dynamics.com"
$token = (az account get-access-token --resource "https://$Environment" --query accessToken -o tsv)
$BaseUrl = "https://$Environment/api/data/v9.2"

$headers = @{
    "Authorization"    = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "Content-Type"     = "application/json"
    "Accept"           = "application/json"
    "Prefer"           = "return=representation"
}
```

### 9.2 Helper Functions

```powershell
function New-Label {
    param([string]$Text)
    return @{
        "@odata.type" = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type" = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"       = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

function Invoke-DataverseApi {
    param(
        [string]$Endpoint,
        [string]$Method = "GET",
        [object]$Body = $null
    )
    $uri = "$BaseUrl/$Endpoint"
    $params = @{ Uri = $uri; Headers = $headers; Method = $Method }
    if ($Body) { $params.Body = ($Body | ConvertTo-Json -Depth 20 -Compress) }
    try {
        $response = Invoke-RestMethod @params
        return @{ Success = $true; Data = $response }
    } catch {
        $errorMessage = $_.Exception.Message
        if ($_.Exception.Response) {
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $errorMessage = $reader.ReadToEnd()
            } catch {}
        }
        return @{ Success = $false; Error = $errorMessage }
    }
}
```

### 9.3 Create Entity with Primary Name

```powershell
$entityDef = @{
    "@odata.type"           = "Microsoft.Dynamics.CRM.EntityMetadata"
    "SchemaName"            = "sprk_registrationrequest"
    "DisplayName"           = New-Label -Text "Registration Request"
    "DisplayCollectionName" = New-Label -Text "Registration Requests"
    "Description"           = New-Label -Text "Tracks self-service demo access requests through submission, review, provisioning, and expiration."
    "OwnershipType"         = "OrganizationOwned"
    "IsActivity"            = $false
    "HasNotes"              = $false
    "HasActivities"         = $true
    "PrimaryNameAttribute"  = "sprk_name"
    "Attributes"            = @(
        @{
            "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
            "SchemaName"    = "sprk_name"
            "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
            "MaxLength"     = 200
            "DisplayName"   = New-Label -Text "Name"
            "Description"   = New-Label -Text "Auto-generated: {FirstName} {LastName} - {Organization}"
            "IsPrimaryName" = $true
        }
    )
}

$result = Invoke-DataverseApi -Endpoint "EntityDefinitions" -Method "POST" -Body $entityDef
if ($result.Success) { Write-Host "Created entity: sprk_registrationrequest" -ForegroundColor Green }
else { Write-Host "Error: $($result.Error)" -ForegroundColor Red }
```

### 9.4 Add String Attributes

```powershell
$stringColumns = @(
    @{ SchemaName = "sprk_firstname";       DisplayName = "First Name";          MaxLength = 100; Required = "ApplicationRequired"; Description = "Applicant first name" }
    @{ SchemaName = "sprk_lastname";        DisplayName = "Last Name";           MaxLength = 100; Required = "ApplicationRequired"; Description = "Applicant last name" }
    @{ SchemaName = "sprk_email";           DisplayName = "Email";               MaxLength = 200; Required = "ApplicationRequired"; Description = "Applicant work email" }
    @{ SchemaName = "sprk_organization";    DisplayName = "Organization";        MaxLength = 200; Required = "ApplicationRequired"; Description = "Applicant organization" }
    @{ SchemaName = "sprk_jobtitle";        DisplayName = "Job Title";           MaxLength = 200; Required = "None";                Description = "Applicant job title" }
    @{ SchemaName = "sprk_phone";           DisplayName = "Phone";               MaxLength = 50;  Required = "None";                Description = "Applicant phone number" }
    @{ SchemaName = "sprk_trackingid";      DisplayName = "Tracking ID";         MaxLength = 50;  Required = "None";                Description = "Public reference: REG-{YYYYMMDD}-{4char}" }
    @{ SchemaName = "sprk_rejectionreason"; DisplayName = "Rejection Reason";    MaxLength = 500; Required = "None";                Description = "Reason if request was rejected" }
    @{ SchemaName = "sprk_demousername";    DisplayName = "Demo Username";       MaxLength = 200; Required = "None";                Description = "Provisioned UPN (e.g. jane.smith@demo.spaarke.com)" }
    @{ SchemaName = "sprk_demouserobjectid";DisplayName = "Demo User Object ID"; MaxLength = 50;  Required = "None";                Description = "Entra ID object ID of demo user" }
)

foreach ($col in $stringColumns) {
    $attr = @{
        "@odata.type"   = "Microsoft.Dynamics.CRM.StringAttributeMetadata"
        "SchemaName"    = $col.SchemaName
        "RequiredLevel" = @{ "Value" = $col.Required }
        "MaxLength"     = $col.MaxLength
        "DisplayName"   = New-Label -Text $col.DisplayName
        "Description"   = New-Label -Text $col.Description
    }
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body $attr
    if ($result.Success) { Write-Host "  + $($col.SchemaName)" -ForegroundColor Green }
    else { Write-Host "  x $($col.SchemaName): $($result.Error)" -ForegroundColor Red }
}
```

### 9.5 Add Memo Attribute

```powershell
$memoAttr = @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.MemoAttributeMetadata"
    "SchemaName"    = "sprk_notes"
    "RequiredLevel" = @{ "Value" = "None" }
    "MaxLength"     = 10000
    "DisplayName"   = New-Label -Text "Notes"
    "Description"   = New-Label -Text "Additional notes or comments from the applicant"
}

Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body $memoAttr
```

### 9.6 Add Picklist Attributes (Local Option Sets)

```powershell
# sprk_usecase
$useCaseAttr = @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"    = "sprk_usecase"
    "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
    "DisplayName"   = New-Label -Text "Use Case"
    "Description"   = New-Label -Text "Primary use case the applicant is interested in"
    "OptionSet"     = @{
        "@odata.type"  = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "IsGlobal"     = $false
        "OptionSetType" = "Picklist"
        "Options"      = @(
            @{ "Value" = 0; "Label" = New-Label -Text "Document Management" }
            @{ "Value" = 1; "Label" = New-Label -Text "AI Analysis" }
            @{ "Value" = 2; "Label" = New-Label -Text "Financial Intelligence" }
            @{ "Value" = 3; "Label" = New-Label -Text "General" }
        )
    }
}

Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body $useCaseAttr

# sprk_referralsource
$referralAttr = @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"    = "sprk_referralsource"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName"   = New-Label -Text "Referral Source"
    "Description"   = New-Label -Text "How the applicant heard about Spaarke"
    "OptionSet"     = @{
        "@odata.type"  = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "IsGlobal"     = $false
        "OptionSetType" = "Picklist"
        "Options"      = @(
            @{ "Value" = 0; "Label" = New-Label -Text "Conference" }
            @{ "Value" = 1; "Label" = New-Label -Text "Website" }
            @{ "Value" = 2; "Label" = New-Label -Text "Referral" }
            @{ "Value" = 3; "Label" = New-Label -Text "Search" }
            @{ "Value" = 4; "Label" = New-Label -Text "Other" }
        )
    }
}

Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body $referralAttr

# sprk_status
$statusAttr = @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.PicklistAttributeMetadata"
    "SchemaName"    = "sprk_status"
    "RequiredLevel" = @{ "Value" = "ApplicationRequired" }
    "DisplayName"   = New-Label -Text "Status"
    "Description"   = New-Label -Text "Current lifecycle status of the registration request"
    "OptionSet"     = @{
        "@odata.type"  = "Microsoft.Dynamics.CRM.OptionSetMetadata"
        "IsGlobal"     = $false
        "OptionSetType" = "Picklist"
        "Options"      = @(
            @{ "Value" = 0; "Label" = New-Label -Text "Submitted" }
            @{ "Value" = 1; "Label" = New-Label -Text "Approved" }
            @{ "Value" = 2; "Label" = New-Label -Text "Rejected" }
            @{ "Value" = 3; "Label" = New-Label -Text "Provisioned" }
            @{ "Value" = 4; "Label" = New-Label -Text "Expired" }
            @{ "Value" = 5; "Label" = New-Label -Text "Revoked" }
        )
    }
}

Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body $statusAttr
```

### 9.7 Add DateTime Attributes

```powershell
$dateColumns = @(
    @{ SchemaName = "sprk_requestdate";     DisplayName = "Request Date";     Description = "When the request was submitted" }
    @{ SchemaName = "sprk_reviewdate";      DisplayName = "Review Date";      Description = "When the request was approved/rejected" }
    @{ SchemaName = "sprk_provisioneddate"; DisplayName = "Provisioned Date"; Description = "When the demo account was provisioned" }
    @{ SchemaName = "sprk_expirationdate";  DisplayName = "Expiration Date";  Description = "When demo access expires (admin-adjustable)" }
    @{ SchemaName = "sprk_consentdate";     DisplayName = "Consent Date";     Description = "When the applicant accepted consent" }
)

foreach ($col in $dateColumns) {
    $attr = @{
        "@odata.type"      = "Microsoft.Dynamics.CRM.DateTimeAttributeMetadata"
        "SchemaName"       = $col.SchemaName
        "RequiredLevel"    = @{ "Value" = "None" }
        "DisplayName"      = New-Label -Text $col.DisplayName
        "Description"      = New-Label -Text $col.Description
        "Format"           = "DateAndTime"
        "DateTimeBehavior" = @{ "Value" = "UserLocal" }
    }
    $result = Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body $attr
    if ($result.Success) { Write-Host "  + $($col.SchemaName)" -ForegroundColor Green }
    else { Write-Host "  x $($col.SchemaName): $($result.Error)" -ForegroundColor Red }
}
```

### 9.8 Add Boolean Attribute

```powershell
$boolAttr = @{
    "@odata.type"   = "Microsoft.Dynamics.CRM.BooleanAttributeMetadata"
    "SchemaName"    = "sprk_consentaccepted"
    "RequiredLevel" = @{ "Value" = "None" }
    "DisplayName"   = New-Label -Text "Consent Accepted"
    "Description"   = New-Label -Text "Whether the applicant accepted the terms and consent checkbox"
    "OptionSet"     = @{
        "TrueOption"  = @{ "Value" = 1; "Label" = New-Label -Text "Yes" }
        "FalseOption" = @{ "Value" = 0; "Label" = New-Label -Text "No" }
    }
}

Invoke-DataverseApi -Endpoint "EntityDefinitions(LogicalName='sprk_registrationrequest')/Attributes" -Method "POST" -Body $boolAttr
```

### 9.9 Create Lookup Relationship

```powershell
$relationshipDef = @{
    "@odata.type"          = "Microsoft.Dynamics.CRM.OneToManyRelationshipMetadata"
    "SchemaName"           = "sprk_systemuser_registrationrequest_reviewedby"
    "ReferencedEntity"     = "systemuser"
    "ReferencingEntity"    = "sprk_registrationrequest"
    "CascadeConfiguration" = @{
        "Assign"   = "NoCascade"
        "Delete"   = "RemoveLink"
        "Merge"    = "NoCascade"
        "Reparent" = "NoCascade"
        "Share"    = "NoCascade"
        "Unshare"  = "NoCascade"
    }
    "Lookup" = @{
        "SchemaName"    = "sprk_reviewedby"
        "DisplayName"   = New-Label -Text "Reviewed By"
        "Description"   = New-Label -Text "The admin who approved or rejected the request"
        "RequiredLevel" = @{ "Value" = "None" }
    }
}

$result = Invoke-DataverseApi -Endpoint "RelationshipDefinitions" -Method "POST" -Body $relationshipDef
if ($result.Success) { Write-Host "Created lookup: sprk_reviewedby -> systemuser" -ForegroundColor Green }
else { Write-Host "Error: $($result.Error)" -ForegroundColor Red }
```

### 9.10 Publish Customizations

```powershell
$publishRequest = @{
    "ParameterXml" = "<importexportxml><entities><entity>sprk_registrationrequest</entity></entities></importexportxml>"
}

Invoke-DataverseApi -Endpoint "PublishXml" -Method "POST" -Body $publishRequest
Write-Host "Customizations published for sprk_registrationrequest" -ForegroundColor Green
```

---

## Column Summary (Quick Reference)

| # | SchemaName | Type | MaxLen | Required | Notes |
|---|-----------|------|--------|----------|-------|
| 1 | `sprk_registrationrequestid` | PK (GUID) | — | Auto | Primary key |
| 2 | `sprk_name` | String | 200 | Yes | Primary name: "{FirstName} {LastName} - {Org}" |
| 3 | `sprk_firstname` | String | 100 | Yes | |
| 4 | `sprk_lastname` | String | 100 | Yes | |
| 5 | `sprk_email` | String | 200 | Yes | Work email |
| 6 | `sprk_organization` | String | 200 | Yes | |
| 7 | `sprk_jobtitle` | String | 200 | No | |
| 8 | `sprk_phone` | String | 50 | No | |
| 9 | `sprk_usecase` | Choice | — | Yes | 4 options (0-3) |
| 10 | `sprk_referralsource` | Choice | — | No | 5 options (0-4) |
| 11 | `sprk_notes` | Memo | 10000 | No | |
| 12 | `sprk_status` | Choice | — | Yes | 6 options (0-5), default: 0 |
| 13 | `sprk_trackingid` | String | 50 | No | REG-{YYYYMMDD}-{4char} |
| 14 | `sprk_requestdate` | DateTime | — | No | Auto-set on create |
| 15 | `sprk_reviewedby` | Lookup | — | No | → systemuser |
| 16 | `sprk_reviewdate` | DateTime | — | No | |
| 17 | `sprk_rejectionreason` | String | 500 | No | |
| 18 | `sprk_demousername` | String | 200 | No | UPN |
| 19 | `sprk_demouserobjectid` | String | 50 | No | Entra ID object ID |
| 20 | `sprk_provisioneddate` | DateTime | — | No | |
| 21 | `sprk_expirationdate` | DateTime | — | No | Admin-adjustable |
| 22 | `sprk_consentaccepted` | Boolean | — | No | Yes/No |
| 23 | `sprk_consentdate` | DateTime | — | No | |

**Total custom columns**: 22 (+ 1 PK auto-created)

---

*Schema definition for task 012 — Spaarke Self-Service Registration App project.*
*Target environment: spaarke-demo.crm.dynamics.com (Demo Dataverse, unmanaged solution per ADR-027)*
