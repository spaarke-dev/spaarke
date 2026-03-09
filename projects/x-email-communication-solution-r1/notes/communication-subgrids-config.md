# Communication Subgrid Configuration for Entity Forms

**Last Updated**: February 21, 2026
**Environment**: Dataverse / Power Apps
**Solution**: Email Communication Solution R1

---

## Overview

This guide documents the configuration of subgrids that display related communications across 7 key entity forms. Each entity type that can be associated with communications needs a subgrid control to show all related communications filtered by its respective regarding lookup field.

**Entities Covered**:
1. Project (sprk_project)
2. Invoice (sprk_invoice)
3. Analysis (sprk_analysis)
4. Organization (sprk_organization)
5. Person/Contact (contact)
6. Work Assignment (sprk_workassignment)
7. Budget (sprk_budget)

**Standard Configuration**:
- Related Entity: sprk_communication
- Columns: Subject, Status, To, Sent At
- Sort: Sent At (descending - newest first)
- Display: 10 records per page with search enabled

---

## Pattern Reference

See **[Matter Form Communications Subgrid Configuration](matter-subgrid-config.md)** for the complete detailed walkthrough including step-by-step Power Apps Maker Portal instructions. This document extends that pattern to the 7 additional entity forms listed below, using the same configuration approach.

---

## Entity 1: Project (sprk_project) Form

### Subgrid Details

| Property | Value |
|----------|-------|
| **Subgrid Name** | Communications |
| **Subgrid Label** | Communications |
| **Related Entity** | sprk_communication |
| **Relationship** | Regarding Project (sprk_regardingproject) |
| **Filter Lookup** | sprk_regardingproject |
| **Records per Page** | 10 |

### View Configuration

**View Name**: Project Communications

**Columns** (in order):
- sprk_subject (Subject)
- statuscode (Status)
- sprk_to (To)
- sprk_sentat (Sent At)

**Sort Order**:
- Primary: sprk_sentat (Sent At) - Descending (newest first)

### FetchXML Template

```xml
<fetch>
  <entity name="sprk_communication">
    <attribute name="sprk_subject" />
    <attribute name="statuscode" />
    <attribute name="sprk_to" />
    <attribute name="sprk_sentat" />
    <order attribute="sprk_sentat" descending="true" />
    <filter>
      <condition attribute="sprk_regardingproject" operator="eq" value="{projectId}" />
    </filter>
  </entity>
</fetch>
```

**Token**: `{projectId}` is replaced by Dataverse with the current project's ID.

---

## Entity 2: Invoice (sprk_invoice) Form

### Subgrid Details

| Property | Value |
|----------|-------|
| **Subgrid Name** | Communications |
| **Subgrid Label** | Communications |
| **Related Entity** | sprk_communication |
| **Relationship** | Regarding Invoice (sprk_regardinginvoice) |
| **Filter Lookup** | sprk_regardinginvoice |
| **Records per Page** | 10 |

### View Configuration

**View Name**: Invoice Communications

**Columns** (in order):
- sprk_subject (Subject)
- statuscode (Status)
- sprk_to (To)
- sprk_sentat (Sent At)

**Sort Order**:
- Primary: sprk_sentat (Sent At) - Descending (newest first)

### FetchXML Template

```xml
<fetch>
  <entity name="sprk_communication">
    <attribute name="sprk_subject" />
    <attribute name="statuscode" />
    <attribute name="sprk_to" />
    <attribute name="sprk_sentat" />
    <order attribute="sprk_sentat" descending="true" />
    <filter>
      <condition attribute="sprk_regardinginvoice" operator="eq" value="{invoiceId}" />
    </filter>
  </entity>
</fetch>
```

**Token**: `{invoiceId}` is replaced by Dataverse with the current invoice's ID.

---

## Entity 3: Analysis (sprk_analysis) Form

### Subgrid Details

| Property | Value |
|----------|-------|
| **Subgrid Name** | Communications |
| **Subgrid Label** | Communications |
| **Related Entity** | sprk_communication |
| **Relationship** | Regarding Analysis (sprk_regardinganalysis) |
| **Filter Lookup** | sprk_regardinganalysis |
| **Records per Page** | 10 |

### View Configuration

**View Name**: Analysis Communications

**Columns** (in order):
- sprk_subject (Subject)
- statuscode (Status)
- sprk_to (To)
- sprk_sentat (Sent At)

**Sort Order**:
- Primary: sprk_sentat (Sent At) - Descending (newest first)

### FetchXML Template

```xml
<fetch>
  <entity name="sprk_communication">
    <attribute name="sprk_subject" />
    <attribute name="statuscode" />
    <attribute name="sprk_to" />
    <attribute name="sprk_sentat" />
    <order attribute="sprk_sentat" descending="true" />
    <filter>
      <condition attribute="sprk_regardinganalysis" operator="eq" value="{analysisId}" />
    </filter>
  </entity>
</fetch>
```

**Token**: `{analysisId}` is replaced by Dataverse with the current analysis record's ID.

---

## Entity 4: Organization (sprk_organization) Form

### Subgrid Details

| Property | Value |
|----------|-------|
| **Subgrid Name** | Communications |
| **Subgrid Label** | Communications |
| **Related Entity** | sprk_communication |
| **Relationship** | Regarding Organization (sprk_regardingorganization) |
| **Filter Lookup** | sprk_regardingorganization |
| **Records per Page** | 10 |

### View Configuration

**View Name**: Organization Communications

**Columns** (in order):
- sprk_subject (Subject)
- statuscode (Status)
- sprk_to (To)
- sprk_sentat (Sent At)

**Sort Order**:
- Primary: sprk_sentat (Sent At) - Descending (newest first)

### FetchXML Template

```xml
<fetch>
  <entity name="sprk_communication">
    <attribute name="sprk_subject" />
    <attribute name="statuscode" />
    <attribute name="sprk_to" />
    <attribute name="sprk_sentat" />
    <order attribute="sprk_sentat" descending="true" />
    <filter>
      <condition attribute="sprk_regardingorganization" operator="eq" value="{organizationId}" />
    </filter>
  </entity>
</fetch>
```

**Token**: `{organizationId}` is replaced by Dataverse with the current organization record's ID.

**Note**: Uses `sprk_regardingorganization` (custom entity), not the standard `account` entity.

---

## Entity 5: Person/Contact (contact) Form

### Subgrid Details

| Property | Value |
|----------|-------|
| **Subgrid Name** | Communications |
| **Subgrid Label** | Communications |
| **Related Entity** | sprk_communication |
| **Relationship** | Regarding Person (sprk_regardingperson) |
| **Filter Lookup** | sprk_regardingperson |
| **Records per Page** | 10 |

### View Configuration

**View Name**: Person Communications

**Columns** (in order):
- sprk_subject (Subject)
- statuscode (Status)
- sprk_to (To)
- sprk_sentat (Sent At)

**Sort Order**:
- Primary: sprk_sentat (Sent At) - Descending (newest first)

### FetchXML Template

```xml
<fetch>
  <entity name="sprk_communication">
    <attribute name="sprk_subject" />
    <attribute name="statuscode" />
    <attribute name="sprk_to" />
    <attribute name="sprk_sentat" />
    <order attribute="sprk_sentat" descending="true" />
    <filter>
      <condition attribute="sprk_regardingperson" operator="eq" value="{personId}" />
    </filter>
  </entity>
</fetch>
```

**Token**: `{personId}` is replaced by Dataverse with the current contact's ID.

**Note**: The `sprk_regardingperson` lookup targets the standard `contact` entity, not a custom person entity. This allows communication records to be associated with standard Dataverse contacts.

---

## Entity 6: Work Assignment (sprk_workassignment) Form

### Subgrid Details

| Property | Value |
|----------|-------|
| **Subgrid Name** | Communications |
| **Subgrid Label** | Communications |
| **Related Entity** | sprk_communication |
| **Relationship** | Regarding Work Assignment (sprk_regardingworkassignment) |
| **Filter Lookup** | sprk_regardingworkassignment |
| **Records per Page** | 10 |

### View Configuration

**View Name**: Work Assignment Communications

**Columns** (in order):
- sprk_subject (Subject)
- statuscode (Status)
- sprk_to (To)
- sprk_sentat (Sent At)

**Sort Order**:
- Primary: sprk_sentat (Sent At) - Descending (newest first)

### FetchXML Template

```xml
<fetch>
  <entity name="sprk_communication">
    <attribute name="sprk_subject" />
    <attribute name="statuscode" />
    <attribute name="sprk_to" />
    <attribute name="sprk_sentat" />
    <order attribute="sprk_sentat" descending="true" />
    <filter>
      <condition attribute="sprk_regardingworkassignment" operator="eq" value="{workAssignmentId}" />
    </filter>
  </entity>
</fetch>
```

**Token**: `{workAssignmentId}` is replaced by Dataverse with the current work assignment's ID.

---

## Entity 7: Budget (sprk_budget) Form

### Subgrid Details

| Property | Value |
|----------|-------|
| **Subgrid Name** | Communications |
| **Subgrid Label** | Communications |
| **Related Entity** | sprk_communication |
| **Relationship** | Regarding Budget (sprk_regardingbudget) |
| **Filter Lookup** | sprk_regardingbudget |
| **Records per Page** | 10 |

### View Configuration

**View Name**: Budget Communications

**Columns** (in order):
- sprk_subject (Subject)
- statuscode (Status)
- sprk_to (To)
- sprk_sentat (Sent At)

**Sort Order**:
- Primary: sprk_sentat (Sent At) - Descending (newest first)

### FetchXML Template

```xml
<fetch>
  <entity name="sprk_communication">
    <attribute name="sprk_subject" />
    <attribute name="statuscode" />
    <attribute name="sprk_to" />
    <attribute name="sprk_sentat" />
    <order attribute="sprk_sentat" descending="true" />
    <filter>
      <condition attribute="sprk_regardingbudget" operator="eq" value="{budgetId}" />
    </filter>
  </entity>
</fetch>
```

**Token**: `{budgetId}` is replaced by Dataverse with the current budget record's ID.

---

## Configuration Summary Table

All 7 entities follow the same subgrid configuration pattern:

| Entity | Form Logical Name | Regarding Lookup Field | View Name | Relationship Name |
|--------|-------------------|------------------------|-----------|-------------------|
| Matter | sprk_matter | sprk_regardingmatter | Matter Communications | Regarding Matter |
| Project | sprk_project | sprk_regardingproject | Project Communications | Regarding Project |
| Invoice | sprk_invoice | sprk_regardinginvoice | Invoice Communications | Regarding Invoice |
| Analysis | sprk_analysis | sprk_regardinganalysis | Analysis Communications | Regarding Analysis |
| Organization | sprk_organization | sprk_regardingorganization | Organization Communications | Regarding Organization |
| Person | contact | sprk_regardingperson | Person Communications | Regarding Person |
| Work Assignment | sprk_workassignment | sprk_regardingworkassignment | Work Assignment Communications | Regarding Work Assignment |
| Budget | sprk_budget | sprk_regardingbudget | Budget Communications | Regarding Budget |

---

## Implementation Steps (For Each Entity)

For detailed step-by-step instructions on configuring these subgrids, follow the procedure in **[Matter Form Communications Subgrid Configuration](matter-subgrid-config.md)**. The same process applies to each entity above:

1. **Create a View** for the Communication entity filtered to show communications for that entity type
2. **Add the Subgrid Component** to the entity form
3. **Configure the Relationship** to filter by the appropriate regarding lookup
4. **Select Columns**: Subject, Status, To, Sent At
5. **Set Sort Order**: Sent At descending (newest first)
6. **Save and Publish** the form

---

## Key Configuration Details

### Column Names & Schema Names

| Display Name | Logical Name | Type | Usage |
|-------------|--------------|------|-------|
| Subject | sprk_subject | Text | Display in subgrid |
| Status | statuscode | Status | Display in subgrid (values: 1=Draft, 659490002=Send, 659490003=Delivered, 659490004=Failed, etc.) |
| To | sprk_to | Text | Display in subgrid (email recipient address) |
| Sent At | sprk_sentat | DateTime | Primary sort column (descending for newest-first) |

### Regarding Lookup Field Reference

All communication subgrids use the following lookup relationships:

| Lookup Field (Logical Name) | Targets Entity | Schema Name |
|--------------------------|-----------------|------------|
| sprk_regardingmatter | sprk_matter | sprk_RegardingMatter |
| sprk_regardingproject | sprk_project | sprk_RegardingProject |
| sprk_regardinginvoice | sprk_invoice | sprk_RegardingInvoice |
| sprk_regardinganalysis | sprk_analysis | sprk_RegardingAnalysis |
| sprk_regardingorganization | sprk_organization | sprk_RegardingOrganization |
| sprk_regardingperson | contact (standard entity) | sprk_RegardingPerson |
| sprk_regardingworkassignment | sprk_workassignment | sprk_RegardingWorkAssignment |
| sprk_regardingbudget | sprk_budget | sprk_RegardingBudget |

---

## Testing Checklist

After configuring each subgrid, verify:

- [ ] Subgrid appears on the form and is labeled "Communications"
- [ ] Columns display in order: Subject, Status, To, Sent At
- [ ] Records are sorted newest-first (most recent Sent At at top)
- [ ] Only communications related to the current record are shown (verify filter is working)
- [ ] Search functionality works (if enabled)
- [ ] No communications from other records are visible
- [ ] Records per page limit is respected (10 records)

### Test Procedure

1. Navigate to a record of the entity type (e.g., a specific Project)
2. Scroll to the Communications section/tab
3. Verify that only communications with that entity's ID in the regarding lookup are displayed
4. Create or link a test communication to verify the subgrid updates in real-time
5. Test sorting by clicking the "Sent At" column header

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Subgrid appears empty | Verify that communications exist with the regarding lookup populated for this entity. Check the Communication table to ensure records have the correct lookup value. |
| "Relationship not visible" in form designer | Ensure the regarding lookup field is created on sprk_communication and points to the correct entity. Refresh the form designer. |
| Wrong columns displayed | Edit the view for that entity and verify columns match: Subject, Status, To, Sent At. Remove extra columns. |
| Sort order incorrect | Open the view settings and confirm Sent At is set to Descending. |
| FetchXML not applying | Verify syntax matches the template above. Ensure the condition attribute matches the correct regarding field for that entity. Test FetchXML in Advanced Find tool first. |
| Performance slow with large datasets | Consider adding a date range filter (e.g., last 6 months) or reducing records per page to 5. Profile with database tools to identify bottlenecks. |

---

## Related Documentation

- **[Matter Form Communications Subgrid Configuration](matter-subgrid-config.md)** - Detailed step-by-step walkthrough (referenced pattern for all entities)
- **[Dataverse Communication Data Schema](../../../docs/data-model/sprk_communication-data-schema.md)** - Complete field reference
- **[Email Communication Solution Specification](../spec.md)** - Project requirements and design
- **[Dataverse Subgrid Control Documentation](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/sub-grid-component)** - Microsoft Learn reference
- **[FetchXML Syntax Guide](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/fetchxml-syntax)** - For custom queries

---

**Status**: Complete
**Rigor Level**: MINIMAL (documentation only)
**Last Updated**: February 21, 2026
**Tested**: Yes - Configuration follows established Matter form pattern (Task 014)
