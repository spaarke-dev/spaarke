# sprk_report Entity Schema

> **Entity Purpose**: Store the Power BI report catalog for the Reporting module. Each record
> describes a single Power BI report — its workspace, dataset, category, embed URL, and ownership.
> **Project**: spaarke-powerbi-embedded-r1
> **Created**: 2026-03-31

## Entity Definition

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_report |
| **Display Name** | Report |
| **Plural Display Name** | Reports |
| **Schema Name** | sprk_report |
| **Primary Name Field** | sprk_name |
| **Ownership Type** | User/Team |
| **Description** | Power BI report catalog for the Reporting module (App Owns Data, Import mode) |

## Fields

### Primary Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|---|---|---|---|---|---|
| sprk_reportid | Report | Uniqueidentifier | Auto | - | Primary key (record GUID — not the PBI report GUID) |
| sprk_name | Name | String (Single Line) | Yes | 200 | Display name of the report shown in the Reporting module |

### Power BI Identity Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|---|---|---|---|---|---|
| sprk_pbi_reportid | PBI Report ID | String (Single Line) | Yes | 100 | Power BI report GUID — used in embed token requests |
| sprk_workspaceid | Workspace ID | String (Single Line) | Yes | 100 | Power BI workspace (group) GUID where the report lives |
| sprk_datasetid | Dataset ID | String (Single Line) | No | 100 | Power BI dataset GUID — required for RLS EffectiveIdentity |

> **Note**: The Dataverse primary key field is `sprk_reportid` (auto-generated). The Power BI report
> GUID is stored separately as `sprk_pbi_reportid` to avoid naming confusion.

### Classification & Configuration Fields

| Logical Name | Display Name | Type | Required | Default | Description |
|---|---|---|---|---|---|
| sprk_category | Category | Choice | Yes | - | Report category (see Choice Values below) |
| sprk_embedurl | Embed URL | URL | No | - | Direct embed URL from Power BI service (max 2000 chars) |
| sprk_iscustom | Is Custom | Boolean (Yes/No) | No | No (false) | True for tenant-authored reports; false for standard product reports |
| sprk_description | Description | Multiline Text | No | - | Admin notes or report description shown to users (max 2000 chars) |

### Ownership Field

| Logical Name | Display Name | Type | Required | Description |
|---|---|---|---|---|
| sprk_ownerid | Owner | Lookup → systemuser | Yes (OOB) | Report owner — inherits from OOB ownerid on User/Team owned entities |

> **Note**: `ownerid` is the standard OOB ownership field automatically present on User/Team owned
> entities. No separate lookup attribute needs to be created; reference it as `ownerid` (type
> `EntityReference` pointing to `systemuser` or `team`).

### System Fields (OOB)

| Logical Name | Display Name | Type | Description |
|---|---|---|---|
| statecode | Status | State | Active / Inactive |
| statuscode | Status Reason | Status | Reason for state |
| createdon | Created On | DateTime | Record creation timestamp |
| modifiedon | Modified On | DateTime | Last modification timestamp |
| createdby | Created By | Lookup | User who created the record |
| modifiedby | Modified By | Lookup | User who last modified the record |

## Choice Values

### sprk_category (Category)

| Value | Label | Description |
|-------|-------|-------------|
| 1 | Financial | Revenue, costs, budgets, forecasting |
| 2 | Operational | Matter throughput, team productivity, SLAs |
| 3 | Compliance | Regulatory, guideline adherence, audits |
| 4 | Documents | Document activity, storage, processing |
| 5 | Custom | Tenant-authored or bespoke reports |

> This OptionSet is local to the sprk_report entity unless promoted to a global OptionSet for
> reuse. For R1, define as a local OptionSet named `sprk_report_category`.

## Field Lengths and Constraints Summary

| Logical Name | Type | Max Length | Required | Default |
|---|---|---|---|---|
| sprk_name | String | 200 | Yes | - |
| sprk_pbi_reportid | String | 100 | Yes | - |
| sprk_workspaceid | String | 100 | Yes | - |
| sprk_datasetid | String | 100 | No | - |
| sprk_embedurl | URL | 2000 | No | - |
| sprk_iscustom | Boolean | - | No | No (false) |
| sprk_description | Multiline | 2000 | No | - |
| sprk_category | Choice | - | Yes | - |

## Form Layout

### Main Form: Information

**Header Section**
- sprk_name
- sprk_category
- sprk_iscustom

**Power BI Section**
- sprk_pbi_reportid
- sprk_workspaceid
- sprk_datasetid
- sprk_embedurl

**Details Section**
- sprk_description

**System Section**
- ownerid
- createdon / modifiedon
- createdby / modifiedby

## Views

### Active Reports (Default View)

| Column | Width | Sort |
|--------|-------|------|
| sprk_name | 250 | 1 (ASC) |
| sprk_category | 130 | - |
| sprk_iscustom | 100 | - |
| sprk_workspaceid | 200 | - |
| modifiedon | 150 | - |

**Filter**: statecode = Active

### Reports by Category

Same columns as above, grouped by `sprk_category`.

## Security

Access to `sprk_report` records is controlled by the `sprk_ReportingAccess` security role.
See `src/solutions/SpaarkeCore/security-roles/sprk_ReportingAccess.md` for privilege tiers.

## Code Pattern — Late-Bound Usage

```csharp
// Create a report catalog entry
var report = new Entity("sprk_report");
report["sprk_name"] = "Financial Overview Q1";
report["sprk_pbi_reportid"] = "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx";
report["sprk_workspaceid"] = "yyyyyyyy-yyyy-yyyy-yyyy-yyyyyyyyyyyy";
report["sprk_datasetid"] = "zzzzzzzz-zzzz-zzzz-zzzz-zzzzzzzzzzzz";
report["sprk_category"] = new OptionSetValue(1);  // Financial
report["sprk_iscustom"] = false;
report["sprk_description"] = "Quarterly financial summary for all matters.";
report["statecode"] = new OptionSetValue(0);   // Active
report["statuscode"] = new OptionSetValue(1);  // Active

var id = await _serviceClient.CreateAsync(report, ct);
```

```csharp
// Retrieve all active reports
var query = new QueryExpression("sprk_report")
{
    ColumnSet = new ColumnSet(
        "sprk_name", "sprk_pbi_reportid", "sprk_workspaceid",
        "sprk_datasetid", "sprk_category", "sprk_embedurl",
        "sprk_iscustom", "sprk_description"),
    Criteria = new FilterExpression
    {
        Conditions =
        {
            new ConditionExpression("statecode", ConditionOperator.Equal, 0)
        }
    }
};
var results = await _serviceClient.RetrieveMultipleAsync(query, ct);
```

## Deployment

This entity is added to the **SpaarkeCore** Dataverse solution.

```bash
# After creating via maker portal or solution customizations.xml:
pac solution export --name SpaarkeCore --path ./exports --managed false

pac solution pack --folder ./SpaarkeCore --zipfile SpaarkeCore.zip
pac solution import --path SpaarkeCore.zip --environment https://spaarkedev1.crm.dynamics.com
```

## Constraints

- `sprk_iscustom` defaults to `false` — standard product reports ship with the solution
- `sprk_category` value `5 = Custom` is for tenant-authored reports (when `sprk_iscustom = true`)
- Workspace IDs, capacity IDs, and tenant IDs MUST NOT be hardcoded — they are stored here or in environment variables
- This entity is the single source of truth for the report catalog; the BFF API reads it at runtime

---

*Schema version: 1.0 | Project: spaarke-powerbi-embedded-r1 | Created: 2026-03-31*
