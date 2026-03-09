# Incoming Communication Views Configuration

> **Document Purpose**: Specifications for 3 Dataverse views on the `sprk_communication` entity to support inbound email monitoring workflows, including filtering, columns, sort orders, FetchXML, and manual creation steps.
>
> **Target Entity**: `sprk_communication`
>
> **Created**: 2026-02-22
>
> **Status**: Ready for Implementation
>
> **Prerequisite**: Task 072 (IncomingCommunicationProcessor) must be complete so that incoming `sprk_communication` records exist with `sprk_direction = 100000000 (Incoming)`.

---

## Overview

With inbound email monitoring creating `sprk_communication` records that have `Direction = Incoming` and no regarding fields set (association is a separate AI project), administrators need views to:

1. See all incoming communications received by monitored mailboxes
2. Quickly view recently received emails (last 24 hours)
3. Identify incoming emails that have no matter/organization/person association and need manual review

| View | Type | Primary Use | Filter |
|------|------|-------------|--------|
| **Incoming Communications** | Public | All incoming emails | sprk_direction = 100000000 (Incoming) |
| **Recent Incoming (Last 24 Hours)** | Public | Quick triage of new emails | sprk_direction = 100000000 AND sprk_sentat >= today - 1 day |
| **Unassociated Incoming** | Public | Manual review / future AI association | sprk_direction = 100000000 AND all regarding fields are null |

---

## View 1: Incoming Communications

**Type**: Public View

**Purpose**: See all incoming emails received by monitored mailboxes. This is the primary inbox view for administrators monitoring inbound communications.

**Filter Logic**:
- `sprk_direction` eq `100000000` (Incoming) -- only inbound emails

**Columns** (in order):
1. `sprk_from` -- Sender email address
2. `sprk_subject` -- Subject line
3. `sprk_sentat` -- Date/time the email was sent (from the email header)
4. `statuscode` -- Communication status
5. `sprk_direction` -- Direction (will always show "Incoming" in this view, included for consistency)
6. `sprk_regardingmatter` -- Associated matter (lookup, may be empty)
7. `sprk_regardingorganization` -- Associated organization (lookup, may be empty)

**Sort Order**:
- Primary: `sprk_sentat` (descending) -- newest first

**FetchXML**:

```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <attribute name="sprk_from" />
    <attribute name="sprk_subject" />
    <attribute name="sprk_sentat" />
    <attribute name="statuscode" />
    <attribute name="sprk_direction" />
    <attribute name="sprk_regardingmatter" />
    <attribute name="sprk_regardingorganization" />
    <order attribute="sprk_sentat" descending="true" />
    <filter type="and">
      <condition attribute="sprk_direction" operator="eq" value="100000000" />
    </filter>
  </entity>
</fetch>
```

**Configuration Steps** (make.powerapps.com):
1. Navigate to **Tables** > **Communication** (`sprk_communication`) > **Views**
2. Click **+ New view**
3. Name: **Incoming Communications**
4. Add columns in order: From, Subject, Sent At, Status Reason, Direction, Regarding Matter, Regarding Organization
5. Edit filters: Direction equals "Incoming" (100000000)
6. Set sort: Sent At descending (newest first)
7. Save and Publish

---

## View 2: Recent Incoming (Last 24 Hours)

**Type**: Public View

**Purpose**: Quick view of recently received emails. Enables rapid triage of new inbound communications without scrolling through historical records.

**Filter Logic**:
- `sprk_direction` eq `100000000` (Incoming) -- only inbound emails
- `sprk_sentat` >= `[today - 1 day]` -- received within the last 24 hours (uses Dataverse "Last X Hours" operator)

**Columns** (in order):
1. `sprk_from` -- Sender email address
2. `sprk_subject` -- Subject line
3. `sprk_sentat` -- Date/time the email was sent
4. `statuscode` -- Communication status
5. `sprk_to` -- Which monitored mailbox received the email

**Sort Order**:
- Primary: `sprk_sentat` (descending) -- newest first

**FetchXML**:

```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <attribute name="sprk_from" />
    <attribute name="sprk_subject" />
    <attribute name="sprk_sentat" />
    <attribute name="statuscode" />
    <attribute name="sprk_to" />
    <order attribute="sprk_sentat" descending="true" />
    <filter type="and">
      <condition attribute="sprk_direction" operator="eq" value="100000000" />
      <condition attribute="sprk_sentat" operator="last-x-hours" value="24" />
    </filter>
  </entity>
</fetch>
```

**Dataverse Schema Notes**:
- The `last-x-hours` operator with value `24` dynamically filters to records where `sprk_sentat` is within the last 24 hours relative to the current time.
- This is a rolling window, not a fixed date -- the view always shows the most recent 24 hours.
- If a record's `sprk_sentat` is null (should not happen for incoming emails processed by the IncomingCommunicationProcessor), it will be excluded from this view.

**Configuration Steps** (make.powerapps.com):
1. Navigate to **Tables** > **Communication** (`sprk_communication`) > **Views**
2. Click **+ New view**
3. Name: **Recent Incoming (Last 24 Hours)**
4. Add columns in order: From, Subject, Sent At, Status Reason, To
5. Edit filters:
   - Direction equals "Incoming" (100000000)
   - Sent At: "Last 24 hours" (use the "Last X Hours" relative date operator with value 24)
6. Set sort: Sent At descending (newest first)
7. Save and Publish

---

## View 3: Unassociated Incoming

**Type**: Public View

**Purpose**: Find incoming emails that have no association to a matter, organization, or person. These records need manual review by an administrator, or will be associated by the future AI association project. This is the primary "action needed" view for inbound email triage.

**Filter Logic**:
- `sprk_direction` eq `100000000` (Incoming) -- only inbound emails
- `sprk_regardingmatter` is null -- no matter association
- `sprk_regardingorganization` is null -- no organization association
- `sprk_regardingperson` is null -- no person association

All three regarding fields must be null (AND logic) for the record to appear in this view.

**Columns** (in order):
1. `sprk_from` -- Sender email address (key for identifying who sent the email)
2. `sprk_subject` -- Subject line
3. `sprk_sentat` -- Date/time the email was sent
4. `statuscode` -- Communication status
5. `sprk_to` -- Which monitored mailbox received the email
6. `sprk_regardingmatter` -- Shows empty, indicating manual association is needed

**Sort Order**:
- Primary: `sprk_sentat` (descending) -- newest first

**FetchXML**:

```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <attribute name="sprk_from" />
    <attribute name="sprk_subject" />
    <attribute name="sprk_sentat" />
    <attribute name="statuscode" />
    <attribute name="sprk_to" />
    <attribute name="sprk_regardingmatter" />
    <order attribute="sprk_sentat" descending="true" />
    <filter type="and">
      <condition attribute="sprk_direction" operator="eq" value="100000000" />
      <condition attribute="_sprk_regardingmatter_value" operator="null" />
      <condition attribute="_sprk_regardingorganization_value" operator="null" />
      <condition attribute="_sprk_regardingperson_value" operator="null" />
    </filter>
  </entity>
</fetch>
```

**Dataverse Schema Notes**:
- Lookup fields use `_fieldname_value` syntax in FetchXML filter conditions (e.g., `_sprk_regardingmatter_value`).
- The `null` operator filters for records where the lookup has no value set.
- All three null conditions are ANDed together -- the record must have no association of any type.
- The `sprk_regardingmatter` column is included in the view columns even though it will always be empty. This makes it easy for administrators to click into a record, associate it with a matter, and then verify the association appears in the column upon refresh.

**Configuration Steps** (make.powerapps.com):
1. Navigate to **Tables** > **Communication** (`sprk_communication`) > **Views**
2. Click **+ New view**
3. Name: **Unassociated Incoming**
4. Add columns in order: From, Subject, Sent At, Status Reason, To, Regarding Matter
5. Edit filters:
   - Direction equals "Incoming" (100000000)
   - Regarding Matter: "Does not contain data" (null)
   - Regarding Organization: "Does not contain data" (null)
   - Regarding Person: "Does not contain data" (null)
6. Set sort: Sent At descending (newest first)
7. Save and Publish

---

## Manual View Creation Procedure (General)

For all three views, the process in make.powerapps.com is:

### Step 1: Navigate to the Entity
1. Open [make.powerapps.com](https://make.powerapps.com)
2. Select the correct environment (Dev: `spaarkedev1`)
3. Go to **Tables** in the left navigation
4. Search for and open **Communication** (`sprk_communication`)
5. Click the **Views** tab

### Step 2: Create a New View
1. Click **+ New view** in the command bar
2. Enter the view name (as specified in each view section above)
3. Optionally enter a description

### Step 3: Add Columns
1. In the view designer, click **+ Add column** (or the column header area)
2. Select each column in the order specified
3. Rearrange columns by dragging if needed
4. Remove any default columns that are not in the specification

### Step 4: Configure Filters
1. Click **Edit filters** in the view designer
2. Add each filter condition as specified:
   - For choice fields (Direction): select the value from the dropdown
   - For lookup fields (Regarding Matter, etc.): use "Does not contain data" for null checks
   - For date fields (Sent At): use relative date operators like "Last X Hours"
3. Ensure all conditions are joined with AND logic (default)

### Step 5: Set Sort Order
1. Click the column header for `Sent At` (sprk_sentat)
2. Select **Sort descending** (or configure in Sort settings)
3. Verify the sort indicator shows newest first

### Step 6: Save and Publish
1. Click **Save** to save the view definition
2. Click **Publish** to make it available to users
3. Verify the view appears in the Communication entity's view selector

---

## Direction Field Reference

The `sprk_direction` field is a choice (option set) with these values:

| Value | Display Name | Description |
|-------|--------------|-------------|
| 100000000 | Incoming | Email received by a monitored shared mailbox |
| 100000001 | Outgoing | Email sent via the communication service |

All three views in this document filter on `sprk_direction = 100000000 (Incoming)`.

---

## Regarding Field Reference

Association lookups that determine whether an incoming email is "associated" or "unassociated":

| Field | Target Entity | Purpose |
|-------|---------------|---------|
| `sprk_regardingmatter` | `sprk_matter` | Links communication to a legal matter |
| `sprk_regardingorganization` | `account` | Links communication to a client organization |
| `sprk_regardingperson` | `contact` | Links communication to a person/contact |

An incoming communication is considered **unassociated** when all three regarding fields are null. The future AI association project will automatically populate these fields based on sender email matching, subject line analysis, and other heuristics.

---

## Performance Considerations

- **Incoming Communications**: Filtered by direction only -- could return large result sets on high-volume mailboxes. Consider adding row limit (100-200) if performance degrades.
- **Recent Incoming (Last 24 Hours)**: Rolling 24-hour window naturally limits result set size. Should perform well even on high-volume mailboxes.
- **Unassociated Incoming**: Multi-condition null filter. Performance depends on how many records lack associations. As the AI association project processes records, this view should shrink over time.

---

## Testing Checklist

Before deploying views to production:

- [ ] **Incoming Communications**: Create test incoming communication record with `sprk_direction = 100000000`, verify it appears in the view
- [ ] **Incoming Communications**: Create test outgoing communication (`sprk_direction = 100000001`), verify it does NOT appear
- [ ] **Recent Incoming**: Create incoming communication with `sprk_sentat` within last 24 hours, verify it appears
- [ ] **Recent Incoming**: Verify communications older than 24 hours do NOT appear
- [ ] **Unassociated Incoming**: Create incoming communication with all regarding fields null, verify it appears
- [ ] **Unassociated Incoming**: Set `sprk_regardingmatter` on a record, verify it disappears from the view
- [ ] **Unassociated Incoming**: Verify records with any one regarding field set are excluded
- [ ] **Sort order**: All three views sort by Sent At descending (newest first)
- [ ] **Column display**: All specified columns render correctly without truncation

---

## Related Documentation

- **Existing Views**: `projects/email-communication-solution-r1/notes/communication-views-config.md` -- Original 5 outbound/admin views
- **Data Model**: `docs/data-model/sprk_communication-data-schema.md` -- Complete field reference
- **Specification**: `projects/email-communication-solution-r1/spec.md` -- Full requirements
- **Task Reference**: Task 075 in TASK-INDEX.md
- **Dependency**: Task 072 (IncomingCommunicationProcessor) -- creates the incoming records these views display

---

*Last Updated: 2026-02-22*
