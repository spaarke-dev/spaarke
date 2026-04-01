# ComplianceDashboard.pbix — Build Instructions

> **Report Name**: Compliance Dashboard
> **File**: `reports/v1.0.0/ComplianceDashboard.pbix`
> **Category**: Compliance (sprk_category = 3)
> **sprk_name value**: `Compliance Dashboard`
> **Status**: PLACEHOLDER — requires Power BI Desktop to create
> **Created**: 2026-03-31

---

## Purpose

Provides compliance metrics across matters: regulatory guideline adherence, audit trail
completeness, SLA violations, and risk indicators. Targeted at compliance officers, risk
managers, and senior partners responsible for regulatory oversight.

---

## Data Source

**Connection Type**: Dataverse (Import mode)
**Environment URL**: Configured per deployment — do NOT hardcode

### Dataverse Tables to Import

| Table Logical Name | Display Name | Purpose |
|-------------------|--------------|---------|
| `sprk_matter` | Matter | Primary compliance entity |
| `sprk_compliancecheck` | Compliance Check | Checklist items per matter (if exists) |
| `sprk_auditentry` | Audit Entry | Audit trail records (if exists) |
| `businessunit` | Business Unit | BU hierarchy for RLS |
| `systemuser` | User | Responsible attorney / compliance officer |

> Adapt to actual schema. If `sprk_compliancecheck` does not exist, derive compliance metrics
> from matter status, task completion rates, and document event timestamps.

### Key Columns to Include

From `sprk_matter`:
- `sprk_name` — Matter name
- `sprk_status` / `statecode` — Current status
- `sprk_regulatorybody` — Regulator (if applicable)
- `sprk_compliancestatus` — Compliant / At Risk / Non-Compliant (if field exists)
- `sprk_targetclosedate`, `sprk_closedate` — For SLA compliance
- `ownerid`, `businessunitid`

From `sprk_compliancecheck` (if exists):
- `sprk_matterlookup` — Parent matter
- `sprk_checkname` — Check description
- `sprk_passed` — Boolean: passed / failed
- `sprk_duedate`, `sprk_completeddate`

---

## Relationships

- `sprk_compliancecheck[sprk_matterlookup]` → `sprk_matter[sprk_matterid]`
- `sprk_matter[ownerid]` → `systemuser[systemuserid]`
- `systemuser[businessunitid]` → `businessunit[businessunitid]`

---

## Calculated Measures (DAX)

```dax
-- Overall Compliance Rate
Compliance Rate % =
    DIVIDE(
        CALCULATE(COUNTROWS(sprk_compliancecheck), sprk_compliancecheck[sprk_passed] = true),
        COUNTROWS(sprk_compliancecheck),
        0
    ) * 100

-- Matters at Risk
Matters at Risk =
    CALCULATE(
        COUNTROWS(sprk_matter),
        sprk_matter[sprk_compliancestatus] = "At Risk"
    )

-- Non-Compliant Matters
Non-Compliant Matters =
    CALCULATE(
        COUNTROWS(sprk_matter),
        sprk_matter[sprk_compliancestatus] = "Non-Compliant"
    )

-- Overdue Compliance Checks
Overdue Checks =
    CALCULATE(
        COUNTROWS(sprk_compliancecheck),
        sprk_compliancecheck[sprk_passed] = false,
        sprk_compliancecheck[sprk_duedate] < TODAY()
    )

-- SLA Breach Rate
SLA Breach Rate % =
    DIVIDE(
        CALCULATE(
            COUNTROWS(sprk_matter),
            sprk_matter[sprk_closedate] > sprk_matter[sprk_targetclosedate],
            NOT(ISBLANK(sprk_matter[sprk_closedate]))
        ),
        CALCULATE(COUNTROWS(sprk_matter), NOT(ISBLANK(sprk_matter[sprk_closedate]))),
        0
    ) * 100
```

---

## BU RLS Role

| Role Name | Table | DAX Filter |
|-----------|-------|------------|
| `BusinessUnitFilter` | `systemuser` | `[domainname] = USERNAME()` |

Filters compliance data to the current user's business unit hierarchy.

---

## Visualizations

### Page 1: Compliance Overview

| Visual | Type | Data |
|--------|------|------|
| Overall Compliance Rate | Gauge | Compliance Rate % (target: 95%+) |
| Matters at Risk | KPI card | Count — amber alert if > 0 |
| Non-Compliant Matters | KPI card | Count — red alert if > 0 |
| SLA Breach Rate | Gauge | SLA Breach Rate % (target: < 5%) |

### Page 2: Risk Breakdown

| Visual | Type | Data |
|--------|------|------|
| Compliance Status Distribution | Donut | Compliant vs At Risk vs Non-Compliant |
| Overdue Compliance Checks | Bar chart | Count per check type |
| At-Risk Matters List | Table | Matter name, owner, days at risk, next due check |
| Risk Trend | Line chart | At-risk count over rolling 12 weeks |

### Page 3: Regulatory Detail

| Visual | Type | Data |
|--------|------|------|
| Compliance by Regulator | Clustered bar | Compliance rate per regulatory body |
| Checks by Status | Stacked bar | Passed vs Failed per check category |
| Upcoming Deadlines | Table | Matters with checks due in next 14 days |

### Page 4: Audit Trail

| Visual | Type | Data |
|--------|------|------|
| Slicers | Dropdown | Matter, date range, compliance status, business unit |
| Audit Log | Table | Recent compliance events: matter, check, user, status, date |

---

## Formatting

- **Canvas background**: Transparent (100% transparency)
- **Colors**:
  - Compliant: Spaarke Teal (`#0D9488`)
  - At risk: Spaarke Amber (`#F59E0B`)
  - Non-compliant: Spaarke Red (`#DC2626`)
  - Neutral / passed: Spaarke Blue (`#2563EB`)
- **Report title**: "Compliance Dashboard" — Spaarke Navy, 20pt Segoe UI Semibold
- **Critical alerts** (non-compliant KPI cards): Red background `#DC2626`, white text

### RAG Status Conditional Formatting

Apply traffic-light conditional formatting on compliance status columns:
- Green (Teal `#0D9488`): Compliant
- Amber (`#F59E0B`): At Risk
- Red (`#DC2626`): Non-Compliant

---

## Dataverse Record (After Publishing)

| Field | Value |
|-------|-------|
| `sprk_name` | `Compliance Dashboard` |
| `sprk_category` | `3` (Compliance) |
| `sprk_iscustom` | `false` |
| `sprk_pbi_reportid` | `<GUID from PBI Service URL>` |
| `sprk_workspaceid` | `<workspace GUID>` |
| `sprk_datasetid` | `<dataset GUID>` |
| `sprk_description` | `Regulatory compliance metrics, SLA adherence, risk indicators, and audit trail for all matters.` |

---

## Post-Publish Security Notes

The Compliance Dashboard contains sensitive regulatory data. After publishing:

1. In Power BI Service workspace settings, ensure only users with `sprk_ReportingAccess_Viewer`
   (or higher) are members — this is enforced at the BFF API layer via EffectiveIdentity
2. Verify the `BusinessUnitFilter` RLS role is active in the published dataset settings
3. Test with a compliance officer user account to confirm data scoping is correct

---

*Spaarke Reporting Module R1 | Project: spaarke-powerbi-embedded-r1 | Created: 2026-03-31*
