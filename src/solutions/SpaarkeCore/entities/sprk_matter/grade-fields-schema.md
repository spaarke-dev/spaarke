# sprk_matter - Grade Fields Extension (R1 MVP)

> **Purpose**: Add 6 decimal grade fields to the existing Matter entity to store current and historical average performance grades.
> **Project**: matter-performance-KPI-r1
> **Created**: 2026-02-12

## Overview

These 6 fields are populated by the Calculator API (Phase 2) after each KPI assessment save. They are NOT user-editable — they are system-calculated fields.

- **Current Grade**: Latest assessment grade per area (ORDER BY createdon DESC LIMIT 1)
- **Historical Average**: Mean of all assessments per area (AVG query)

## New Fields

### Guidelines Compliance

| Logical Name | Display Name | Type | Precision | Min | Max | Default | Description |
|---|---|---|---|---|---|---|---|
| sprk_guidelinecompliancegrade_current | Guideline Compliance Grade (Current) | Decimal | 2 | 0.00 | 1.00 | NULL | Latest Guidelines assessment grade |
| sprk_guidelinecompliancegrade_average | Guideline Compliance Grade (Average) | Decimal | 2 | 0.00 | 1.00 | NULL | Mean of all Guidelines assessments |

### Budget Compliance

| Logical Name | Display Name | Type | Precision | Min | Max | Default | Description |
|---|---|---|---|---|---|---|---|
| sprk_budgetcompliancegrade_current | Budget Compliance Grade (Current) | Decimal | 2 | 0.00 | 1.00 | NULL | Latest Budget assessment grade |
| sprk_budgetcompliancegrade_average | Budget Compliance Grade (Average) | Decimal | 2 | 0.00 | 1.00 | NULL | Mean of all Budget assessments |

### Outcome Compliance

| Logical Name | Display Name | Type | Precision | Min | Max | Default | Description |
|---|---|---|---|---|---|---|---|
| sprk_outcomecompliancegrade_current | Outcome Compliance Grade (Current) | Decimal | 2 | 0.00 | 1.00 | NULL | Latest Outcomes assessment grade |
| sprk_outcomecompliancegrade_average | Outcome Compliance Grade (Average) | Decimal | 2 | 0.00 | 1.00 | NULL | Mean of all Outcomes assessments |

## Field Properties (All 6 Fields)

| Property | Value | Rationale |
|---|---|---|
| Type | Decimal | Precise grade values 0.00-1.00 |
| Precision | 2 | Two decimal places for percentage-like grades |
| Min Value | 0.00 | No Grade = 0.00 |
| Max Value | 1.00 | A+ = 1.00 |
| Default Value | NULL | Distinguish "no assessments" from "grade 0.00" |
| Required Level | None | Fields populated by API, not user input |
| Is Searchable | Yes | Support advanced find queries |
| Is Filterable | Yes | Support view filters |
| Is Auditable | Yes | Track grade changes |
| ValidForUpdateApi | Yes | API must be able to update these |
| ValidForCreateApi | No | Not set at creation time |
| ValidForForm | Yes | Display on forms (read-only) |
| ValidForGrid | Yes | Display in views |

## Form Placement

### Main Form - Summary Tab
- Guidelines Current + Average (side by side)
- Budget Current + Average (side by side)
- Outcomes Current + Average (side by side)
- All fields: READ-ONLY on form (populated by Calculator API)

### Note on Form Configuration
The actual form placement is handled by tasks in Phase 3 (VisualHost cards). These fields will be displayed via VisualHost metric cards, not as raw form fields. The raw fields are primarily for:
- API read/write access
- Advanced Find / views
- Data export

## Color Coding Reference (for UI consumers)

The UI layer (VisualHost cards) will apply color coding based on these decimal values:
| Grade Range | Color | Letter Grades |
|---|---|---|
| 0.85 - 1.00 | Blue | A+, A, B+, B |
| 0.70 - 0.84 | Yellow | C+, C, D+ |
| 0.00 - 0.69 | Red | D, F, No Grade |

## Dependencies

- **Updated by**: Calculator API endpoint `POST /api/matters/{matterId}/recalculate-grades` (Task 010-013)
- **Read by**: VisualHost metric cards (Tasks 030-034)
- **Read by**: Trend cards (Tasks 040-043)

---

*Schema version: 1.0 | Project: matter-performance-KPI-r1 | Created: 2026-02-12*
