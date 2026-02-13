# sprk_kpiassessment Entity Schema

> **Entity Purpose**: Store manual KPI assessment records for matter performance evaluation.
> Enables structured grading of outside counsel across performance areas (Guidelines, Budget, Outcomes).

## Entity Definition

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_kpiassessment |
| **Display Name** | KPI Assessment |
| **Plural Display Name** | KPI Assessments |
| **Primary Name Field** | sprk_name (auto-generated: "{Area} - {KPI Name}") |
| **Ownership Type** | UserOwned |
| **Description** | Manual KPI assessment records for matter performance evaluation |

## Fields

### Primary Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|-------------|
| sprk_kpiassessmentid | KPI Assessment | Uniqueidentifier | Auto | - | Primary key |
| sprk_name | Name | String | Yes | 200 | Auto-generated display name ("{Area} - {KPI Name}") |

### Core Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|-------------|
| sprk_matter | Matter | Lookup (sprk_matter) | Yes | - | Parent matter record |
| sprk_performancearea | Performance Area | Choice | Yes | - | Performance area category (see Choice Values below) |
| sprk_kpiname | KPI Name | String | Yes | 200 | Name of the KPI being assessed |
| sprk_assessmentcriteria | Assessment Criteria | Multiline | No | 4000 | Description of assessment criteria |
| sprk_grade | Grade | Choice | Yes | - | Letter grade assessment (see Choice Values below) |
| sprk_assessmentnotes | Assessment Notes | Multiline | No | 4000 | Narrative feedback and justification |

### System Fields

| Logical Name | Display Name | Type | Description |
|--------------|--------------|------|-------------|
| statecode | Status | State | Active/Inactive |
| statuscode | Status Reason | Status | Reason for state |
| createdon | Created On | DateTime | Record creation timestamp |
| modifiedon | Modified On | DateTime | Last modification timestamp |
| createdby | Created By | Lookup | User who created the record |
| modifiedby | Modified By | Lookup | User who last modified the record |

## Choice Values

### sprk_performancearea (Performance Area)

| Value | Label | Description |
|-------|-------|-------------|
| 1 | Guidelines | Outside Counsel Guideline compliance |
| 2 | Budget | Budget management compliance |
| 3 | Outcomes | Matter outcome achievement |

### sprk_grade (Grade)

| Value | Label | Decimal Value | Description |
|-------|-------|---------------|-------------|
| 100 | A+ | 1.00 | Exceptional |
| 95 | A | 0.95 | Excellent |
| 90 | B+ | 0.90 | Very Good |
| 85 | B | 0.85 | Good |
| 80 | C+ | 0.80 | Above Average |
| 75 | C | 0.75 | Average |
| 70 | D+ | 0.70 | Below Average |
| 65 | D | 0.65 | Poor |
| 60 | F | 0.60 | Failing |
| 0 | No Grade | 0.00 | Not Assessed |

> **Grade-to-Decimal Mapping Note**: The `Value` column contains integer IDs stored in Dataverse as
> choice option values. The `Decimal Value` column is the numeric weight used by the calculator API
> for scoring computations. The mapping from choice integer to decimal grade is performed in API code
> (e.g., `GradeMapper` or equivalent), NOT in the entity definition itself. When implementing the
> calculator endpoint, use this table as the authoritative source for the mapping.

## Relationships

### N:1 - Matter (Parent)

| Property | Value |
|----------|-------|
| **Relationship Name** | sprk_matter_kpiassessment |
| **Type** | OneToMany (Matter -> KPI Assessments) |
| **Referenced Entity** | sprk_matter |
| **Referencing Entity** | sprk_kpiassessment |
| **Lookup Field** | sprk_matter |
| **Cascade Delete** | RemoveLink (orphan assessments if parent deleted) |
| **Cascade Assign** | NoCascade |
| **Cascade Share** | NoCascade |
| **Cascade Unshare** | NoCascade |
| **Cascade Reparent** | NoCascade |

## Form Layout

### Quick Create Form

> **Note**: Quick Create form configuration is handled in task 005. This documents the intended layout.

**Fields (in order)**:
- sprk_matter (Lookup - pre-populated from parent context)
- sprk_performancearea (Choice)
- sprk_kpiname (String)
- sprk_assessmentcriteria (Multiline - read-only, populated from KPI definition)
- sprk_grade (Choice)
- sprk_assessmentnotes (Multiline)

### Main Form: Information

**Header Section**
- sprk_name (Display Name)
- sprk_matter
- sprk_performancearea
- sprk_grade

**General Section**
- sprk_kpiname
- sprk_assessmentcriteria
- sprk_grade
- sprk_assessmentnotes

**System Section**
- createdon
- modifiedon
- createdby
- modifiedby

## Views

### Active KPI Assessments (Default View)

| Column | Width | Sort |
|--------|-------|------|
| sprk_performancearea | 150 | - |
| sprk_kpiname | 200 | - |
| sprk_grade | 100 | - |
| createdon | 150 | 1 (DESC) |

**Filter**: statecode = Active

### All KPI Assessments

Same columns as above, no filter.

### Associated View (for subgrid on Matter)

Same columns as Active KPI Assessments, no extra filter. Used when displaying KPI assessments as a subgrid on the Matter form.

## Security

### Security Roles

| Role | Create | Read | Write | Delete |
|------|--------|------|-------|--------|
| System Administrator | Yes | Yes | Yes | Yes |
| Basic User | Yes | Yes (own) | Yes (own) | No |

### Field Security

No field-level security required - assessment data follows record-level ownership security.

## Example Records

### Example 1: Guidelines Area Assessment

```json
{
  "sprk_name": "Guidelines - Billing Compliance",
  "sprk_matter": "00000000-0000-0000-0000-000000000001",
  "sprk_performancearea": 1,
  "sprk_kpiname": "Billing Compliance",
  "sprk_assessmentcriteria": "Adherence to outside counsel billing guidelines including rate caps, staffing requirements, and pre-approval protocols.",
  "sprk_grade": 95,
  "sprk_assessmentnotes": "Firm consistently follows billing guidelines with minimal exceptions."
}
```

### Example 2: Budget Area Assessment

```json
{
  "sprk_name": "Budget - Budget Variance",
  "sprk_matter": "00000000-0000-0000-0000-000000000001",
  "sprk_performancearea": 2,
  "sprk_kpiname": "Budget Variance",
  "sprk_assessmentcriteria": "Actual spend vs. approved budget. Measures ability to deliver within financial constraints.",
  "sprk_grade": 75,
  "sprk_assessmentnotes": "Budget exceeded by 12% due to unexpected discovery scope expansion. Firm communicated proactively."
}
```

### Example 3: Outcomes Area Assessment

```json
{
  "sprk_name": "Outcomes - Settlement Achievement",
  "sprk_matter": "00000000-0000-0000-0000-000000000001",
  "sprk_performancearea": 3,
  "sprk_kpiname": "Settlement Achievement",
  "sprk_assessmentcriteria": "Ability to achieve favorable settlement outcomes within target parameters.",
  "sprk_grade": 100,
  "sprk_assessmentnotes": "Achieved settlement well below reserve amount. Exceptional negotiation."
}
```

## Deployment

This entity should be added to the **SpaarkeCore** solution (or the project-specific solution).

### Power Platform CLI Commands

```bash
# Create entity via maker portal, then export
pac solution export --name SpaarkeCore --path ./exports --managed false

# Or create via solution file
pac solution pack --folder ./SpaarkeCore --zipfile SpaarkeCore.zip
pac solution import --path SpaarkeCore.zip
```

> **Note**: Deployment is handled by task 006. Do not deploy as part of this task.

---

*Schema version: 1.0 | Created: 2026-02-12*
