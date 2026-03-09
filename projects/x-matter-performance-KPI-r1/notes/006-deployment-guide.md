# Phase 1 Deployment Guide - Task 006

> **Status**: Ready for Deployment
> **Environment**: Development (`https://spaarkedev1.crm.dynamics.com`)
> **Created**: 2026-02-12

## Pre-Deployment Checklist

- [x] Task 001: KPI Assessment entity schema created (`entity-schema.md`)
- [x] Task 002: Matter grade fields schema created (`grade-fields-schema.md`)
- [x] Task 003: Performance Area choice values documented (3 options)
- [x] Task 004: Grade choice values documented (10 options)
- [x] Task 005: Quick Create form XML created
- [x] Relationship definition created (`sprk_kpiassessment_matter.xml`)

## Artifacts to Deploy

### 1. New Entity: sprk_kpiassessment
**Schema**: `src/solutions/SpaarkeCore/entities/sprk_kpiassessment/entity-schema.md`

| Field | Type | Required |
|-------|------|----------|
| sprk_matter | Lookup (sprk_matter) | Yes |
| sprk_performancearea | Choice (3 options) | Yes |
| sprk_kpiname | String (200) | Yes |
| sprk_assessmentcriteria | Multiline (4000) | No |
| sprk_grade | Choice (10 options) | Yes |
| sprk_assessmentnotes | Multiline (4000) | No |

### 2. Matter Entity Extension: 6 Grade Fields
**Schema**: `src/solutions/SpaarkeCore/entities/sprk_matter/grade-fields-schema.md`

| Field | Type | Precision |
|-------|------|-----------|
| sprk_guidelinecompliancegrade_current | Decimal | 2 |
| sprk_guidelinecompliancegrade_average | Decimal | 2 |
| sprk_budgetcompliancegrade_current | Decimal | 2 |
| sprk_budgetcompliancegrade_average | Decimal | 2 |
| sprk_outcomecompliancegrade_current | Decimal | 2 |
| sprk_outcomecompliancegrade_average | Decimal | 2 |

### 3. Quick Create Form
**Form XML**: `src/solutions/SpaarkeCore/entities/sprk_kpiassessment/FormXml/quick/kpiassessment-quickcreate.xml`

### 4. Relationship
**Relationship XML**: `src/dataverse/solutions/spaarke_containers/Other/Relationships/sprk_kpiassessment_matter.xml`

## Deployment Steps

### Option A: Power Apps Maker Portal (Recommended for R1 MVP)

1. Navigate to https://make.powerapps.com
2. Select Development environment
3. **Create Entity**:
   - Tables > New table > Name: "KPI Assessment", Schema: "sprk_kpiassessment"
   - Add all fields per entity-schema.md
   - Configure choice fields per documented values
4. **Extend Matter**:
   - Tables > sprk_matter > Add columns
   - Add 6 decimal fields per grade-fields-schema.md
5. **Configure Form**:
   - sprk_kpiassessment > Forms > Quick Create
   - Add 5 fields in order per Quick Create form layout
6. **Publish All Customizations**

### Option B: PAC CLI

```powershell
# 1. Authenticate
pac auth create --url https://spaarkedev1.crm.dynamics.com

# 2. Verify connection
pac org who

# 3. Create entity (if using solution file)
pac solution export --name SpaarkeCore --path ./exports --managed false

# 4. Import updated solution
pac solution import --path SpaarkeCore.zip --publish-changes

# 5. Verify
pac solution list
```

## Post-Deployment Verification

- [ ] `sprk_kpiassessment` entity exists in Dataverse
- [ ] All 7 fields present on entity
- [ ] Performance Area choice has 3 options (Guidelines, Budget, Outcomes)
- [ ] Grade choice has 10 options (A+ through No Grade)
- [ ] 6 grade fields added to `sprk_matter`
- [ ] Quick Create form opens with 5 fields
- [ ] Can save a test KPI Assessment record
- [ ] Test record links to correct Matter

## Notes

- Phase 2 (Calculator API code) can be written in parallel — only needs entity schema, not live Dataverse
- Phase 5 (VisualHost research) can proceed independently
- Actual integration testing requires completed deployment
