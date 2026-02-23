# Communication Views Configuration

> **Document Purpose**: Detailed specifications for all 5 Dataverse views on the `sprk_communication` entity, including filters, columns, sort orders, and FetchXML queries.
>
> **Target Entity**: `sprk_communication`
>
> **Created**: 2026-02-21
>
> **Status**: Ready for Implementation

---

## Overview

The `sprk_communication` entity requires 5 views to support different user workflows:

| View | Type | Primary Use | Filter |
|------|------|-------------|--------|
| **My Sent Communications** | Personal | User's sent emails | statuscode = Send (659490002) AND sender = current user |
| **Communications by Matter** | Public | Browse emails by associated matter | sprk_regardingmatter is not null |
| **Communications by Project** | Public | Browse emails by associated project | sprk_regardingproject is not null |
| **Failed Communications** | Public | Monitor and retry failed sends | statuscode = Failed (659490004) |
| **All Communications** | System | Admin view of all communications | No filter (all records) |

---

## View 1: My Sent Communications

**Type**: Personal View (Default for Communication Entity)

**Purpose**: Shows the current user's sent emails. Default view when opening Communication list.

**Filter Logic**:
- Status = "Send" (659490002) — only successfully sent communications
- Sent By = Current User — only emails the user sent

**Columns** (in order):
1. `sprk_subject` — Subject line (primary display)
2. `sprk_to` — First recipient email address
3. `statuscode` — Status (should show "Send")
4. `sprk_sentat` — Date/time sent

**Sort Order**:
- Primary: `sprk_sentat` (descending) — newest first

**FetchXML**:

```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <attribute name="sprk_subject" />
    <attribute name="sprk_to" />
    <attribute name="statuscode" />
    <attribute name="sprk_sentat" />
    <order attribute="sprk_sentat" descending="true" />
    <filter type="and">
      <condition attribute="statuscode" operator="eq" value="659490002" />
      <condition attribute="_sprk_sentby_value" operator="eq-userid" />
    </filter>
  </entity>
</fetch>
```

**Dataverse Schema Notes**:
- `statuscode` values: Draft=1, Queued=659490001, Send=659490002, Delivered=659490003, Failed=659490004, Bounded=659490005, Recalled=659490006
- `_sprk_sentby_value` is the lookup filter equivalent; `eq-userid` operator automatically matches current user's ID
- `sprk_sentby` is the display field (systemuser lookup)

**Configuration Steps**:
1. Create view named "My Sent Communications"
2. Mark as **personal view** (owner = current user)
3. Set as **default view** for the entity
4. Add columns in order: Subject, To, Status Reason, Sent At
5. Apply filter: Status Reason = Send AND Sent By = Current User
6. Set sort: Sent At (newest first)

---

## View 2: Communications by Matter

**Type**: Public View

**Purpose**: Shows all communications associated with a specific matter. Grouped by matter for organizational clarity.

**Filter Logic**:
- Regarding Matter is not null — only emails linked to a matter

**Columns** (in order):
1. `sprk_subject` — Subject line
2. `sprk_regardingmatter` — Associated matter (lookup)
3. `sprk_to` — Recipient email
4. `sprk_sentat` — Date/time sent

**Group By**:
- Primary: `sprk_regardingmatter` — group by matter name

**Sort Order**:
- Primary: `sprk_sentat` (descending) — newest first within each group

**FetchXML**:

```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <attribute name="sprk_subject" />
    <attribute name="sprk_regardingmatter" />
    <attribute name="sprk_to" />
    <attribute name="sprk_sentat" />
    <order attribute="sprk_sentat" descending="true" />
    <filter type="and">
      <condition attribute="_sprk_regardingmatter_value" operator="not-null" />
    </filter>
  </entity>
</fetch>
```

**Dataverse Schema Notes**:
- `sprk_regardingmatter` is a lookup field targeting `sprk_matter` entity
- `_sprk_regardingmatter_value` is the lookup filter form (GUID comparison)
- The `not-null` operator filters records where the lookup has a value
- Not all communications will appear in this view — only those with a matter association

**Configuration Steps**:
1. Create view named "Communications by Matter"
2. Mark as **public view**
3. Add columns: Subject, Regarding Matter, To, Sent At
4. Apply filter: Regarding Matter is not null
5. Set grouping: Group by Regarding Matter
6. Set sort within groups: Sent At (newest first)

---

## View 3: Communications by Project

**Type**: Public View

**Purpose**: Shows all communications associated with a specific project. Grouped by project for organizational clarity.

**Filter Logic**:
- Regarding Project is not null — only emails linked to a project

**Columns** (in order):
1. `sprk_subject` — Subject line
2. `sprk_regardingproject` — Associated project (lookup)
3. `sprk_to` — Recipient email
4. `sprk_sentat` — Date/time sent

**Group By**:
- Primary: `sprk_regardingproject` — group by project name

**Sort Order**:
- Primary: `sprk_sentat` (descending) — newest first within each group

**FetchXML**:

```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <attribute name="sprk_subject" />
    <attribute name="sprk_regardingproject" />
    <attribute name="sprk_to" />
    <attribute name="sprk_sentat" />
    <order attribute="sprk_sentat" descending="true" />
    <filter type="and">
      <condition attribute="_sprk_regardingproject_value" operator="not-null" />
    </filter>
  </entity>
</fetch>
```

**Dataverse Schema Notes**:
- `sprk_regardingproject` is a lookup field targeting `sprk_project` entity
- `_sprk_regardingproject_value` is the lookup filter form (GUID comparison)
- The `not-null` operator filters records where the lookup has a value
- Not all communications will appear in this view — only those with a project association

**Configuration Steps**:
1. Create view named "Communications by Project"
2. Mark as **public view**
3. Add columns: Subject, Regarding Project, To, Sent At
4. Apply filter: Regarding Project is not null
5. Set grouping: Group by Regarding Project
6. Set sort within groups: Sent At (newest first)

---

## View 4: Failed Communications

**Type**: Public View

**Purpose**: Shows communications that failed to send. Enables monitoring, error analysis, and retry workflows.

**Filter Logic**:
- Status = "Failed" (659490004) — only failed sends

**Columns** (in order):
1. `sprk_subject` — Subject line
2. `sprk_to` — Recipient email address
3. `sprk_errormessage` — Error details from Graph/BFF
4. `sprk_sentat` — Attempt timestamp
5. `sprk_retrycount` — Number of retry attempts (if supported)

**Sort Order**:
- Primary: `sprk_sentat` (descending) — most recent failures first

**FetchXML**:

```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <attribute name="sprk_subject" />
    <attribute name="sprk_to" />
    <attribute name="sprk_errormessage" />
    <attribute name="sprk_sentat" />
    <attribute name="sprk_retrycount" />
    <order attribute="sprk_sentat" descending="true" />
    <filter type="and">
      <condition attribute="statuscode" operator="eq" value="659490004" />
    </filter>
  </entity>
</fetch>
```

**Dataverse Schema Notes**:
- `statuscode` = 659490004 represents "Failed" status
- `sprk_errormessage` (Multiline Text, 4000 max) stores error details from Graph sendMail or validation failures
- `sprk_retrycount` (Whole number) tracks how many times the BFF attempted to send (for future retry implementations)
- Error messages should NOT contain sensitive data (emails, API keys) — see ADR-019 (ProblemDetails & Errors)

**Configuration Steps**:
1. Create view named "Failed Communications"
2. Mark as **public view**
3. Add columns: Subject, To, Error Message, Sent At, Retry Count
4. Apply filter: Status Reason = Failed
5. Set sort: Sent At (newest first)
6. Optional: Set row limit to 50 for performance (many failures can slow large queries)

---

## View 5: All Communications

**Type**: System View

**Purpose**: Admin/system view showing all communications regardless of status. Used for auditing, compliance, and records management.

**Filter Logic**:
- No filter — all records (all statuses, all senders, all associations)

**Columns** (in order):
1. `sprk_subject` — Subject line
2. `sprk_to` — Recipient email
3. `sprk_from` — Sender email address
4. `statuscode` — Communication status (Draft, Sent, Failed, etc.)
5. `sprk_sentat` — Date/time sent (or attempted)
6. `sprk_direction` — Direction (Incoming/Outgoing)

**Sort Order**:
- Primary: `createdon` (descending) — newest records first

**FetchXML**:

```xml
<fetch version="1.0" output-format="xml-platform" mapping="logical" distinct="false">
  <entity name="sprk_communication">
    <attribute name="sprk_communicationid" />
    <attribute name="sprk_subject" />
    <attribute name="sprk_to" />
    <attribute name="sprk_from" />
    <attribute name="statuscode" />
    <attribute name="sprk_sentat" />
    <attribute name="sprk_direction" />
    <order attribute="createdon" descending="true" />
  </entity>
</fetch>
```

**Dataverse Schema Notes**:
- No filter conditions — all records are included
- `createdon` is the system field (auto-set by Dataverse when record is created)
- `sprk_direction` values: Incoming=100000000, Outgoing=100000001
- This view includes draft communications (statuscode=1), queued (659490001), delivered (659490003), failed (659490004), etc.
- Sorting by `createdon` instead of `sprk_sentat` ensures draft communications (which may not have `sprk_sentat` set) still appear

**Configuration Steps**:
1. Create view named "All Communications"
2. Mark as **system view** (not editable by users)
3. Add columns: Subject, To, From, Status Reason, Sent At, Direction
4. **Do not apply any filter** — leave empty to show all
5. Set sort: Created On (newest first)
6. Optional: Set row limit to 100 for performance (Dataverse can be slow with large unfiltered result sets)

---

## Implementation Notes

### View Creation Process (Manual in Dataverse UI)

1. Navigate to **Power Apps** → **Data** → **Tables** → **Communication** (sprk_communication)
2. Open **Views** section
3. Create each view:
   - Click **+ New view**
   - Name the view (e.g., "My Sent Communications")
   - Add columns via **Edit columns**
   - Configure filters via **Edit filters**
   - Set grouping (if applicable) via **Grouping**
   - Set sort order via **Sort**
   - Save and publish

### Alternative: Export/Import via Solution

Views can also be exported as part of the Communication solution:
1. Export unmanaged solution from dev environment
2. Extract `customizations.xml`
3. Find `<SavedQuery>` elements (views are saved queries)
4. Modify FetchXML and column definitions
5. Re-import solution

### Performance Considerations

- **My Sent Communications**: Filtered by user and status — expects fast query (indexed by owner)
- **Communications by Matter**: Grouped view — may return large result set if many communications per matter
- **Communications by Project**: Grouped view — similar performance to Matter view
- **Failed Communications**: Filtered to status=Failed — typically smaller result set
- **All Communications**: Unfiltered — can be slow on large communication counts. Consider adding row limit (100-200) or archive old records to Archive entity

### Column Selection Rationale

| Column Choice | Rationale |
|---------------|-----------|
| Always include `sprk_subject` | Primary identifier for communication — users scan by subject line |
| Avoid `sprk_body` | Too large for list view, causes performance issues |
| Include `sprk_to`/`sprk_from` | Essential for identifying recipient/sender at a glance |
| Include `statuscode` for diagnostics | Failed view and All Communications need status for quick assessment |
| Use `sprk_sentat` for user views, `createdon` for system view | User views care about when email was sent; system views care about when record was created (captures drafts) |
| Avoid `sprk_errormessage` in most views | Included only in Failed Communications for error diagnosis |

### Status Field Notes

The `statuscode` field (not a custom field) represents communication status:

| Value | Display Name | Use Case |
|-------|--------------|----------|
| 1 | Draft | User is composing, not yet sent |
| 659490001 | Queued | Pending send (for future async/retry) |
| 659490002 | Send | Successfully sent |
| 659490003 | Delivered | Read receipt received (future) |
| 659490004 | Failed | Send attempt failed |
| 659490005 | Bounded | Bounced (future) |
| 659490006 | Recalled | Recalled by sender (future) |

**Current Phase 1/2 Usage**:
- Draft (1): Communication form in compose mode
- Send (659490002): Successfully sent via Graph
- Failed (659490004): Graph sendMail returned error

---

## Testing Checklist

Before deploying views to production:

- [ ] **My Sent Communications**: Create test email, verify current user sees only their sent communications with "Send" status
- [ ] **Communications by Matter**: Create email linked to matter, verify it appears grouped under that matter
- [ ] **Communications by Project**: Create email linked to project, verify it appears grouped under that project
- [ ] **Failed Communications**: Simulate failed send (invalid recipient), verify it appears in Failed view only
- [ ] **All Communications**: Verify all communications appear regardless of status
- [ ] Column display: Verify all columns render correctly without truncation issues
- [ ] Sort order: Create multiple communications with different timestamps, verify sort order is correct
- [ ] Grouping: For grouped views, verify items are properly grouped by matter/project
- [ ] Filters: Verify no "leakage" of communications between views (e.g., failed comms should not appear in "My Sent")
- [ ] Performance: Load each view with 100+ test records, verify load time < 3 seconds

---

## Future Enhancements (Out of Scope for Phase 3)

- **Search/Filter by Subject**: Add saved filter for full-text search on subject
- **Bulk Actions**: Add bulk delete, bulk mark as read, bulk retry on Failed view
- **Timeline View**: Alternative time-series view for trending communication volume
- **Conversation Threading**: Group related communications by correlation ID or subject line
- **Attachment Summary**: Add column showing attachment count, quick preview

---

## Related Documentation

- **Data Model**: `/docs/data-model/sprk_communication-data-schema.md` — Complete field reference
- **Specification**: `/projects/email-communication-solution-r1/spec.md` — Full requirements and context
- **Task Reference**: Task 023 in TASK-INDEX.md
- **Communication Form**: Task 022 (form design and configuration)
- **Communication Subgrid**: Task 024 (subgrid on Matter form)

---

*Last Updated: 2026-02-21*
