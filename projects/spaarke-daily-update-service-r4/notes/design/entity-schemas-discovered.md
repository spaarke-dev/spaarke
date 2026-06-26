# Spaarke Custom Entity Schemas — Discovered via MCP Dataverse Describe

> **Date**: 2026-06-25
> **Discovered by**: task 015 entity-architecture correction (Part 1)
> **Method**: `mcp__dataverse__describe` against spaarkedev1 + `mcp__dataverse__read_query` against deployed playbook node configs (ground truth)
> **Purpose**: Authoritative reference for R4 PR 3 W1 tasks 022–025 when authoring corrected playbook FetchXml

---

## TL;DR

Per CLAUDE.md § "🚨 2026-06-25 — Spaarke entity architecture", Spaarke does **NOT** use OOB activity entities (`task`, `email`, `appointment`). The custom-entity equivalents and their canonical attributes are below.

| OOB concept | Spaarke entity | Discriminator | Key ID | Key Name |
|---|---|---|---|---|
| `task` | `sprk_event` | `sprk_eventtype_ref` = `124f5fc9-98ff-f011-8406-7c1e525abd8b` (Task) | `sprk_eventid` | `sprk_eventname` |
| `email` | `sprk_communication` | `sprk_communicationtype = 100000000` (Email) | `sprk_communicationid` | `sprk_name` (display) / `sprk_subject` (email subject) |
| `appointment` | `sprk_event` | `sprk_eventtype_ref` ∈ {Action, Deadline, Meeting, Milestone, Reminder} | `sprk_eventid` | `sprk_eventname` |
| `sprk_document` | `sprk_document` (already custom) | n/a | `sprk_documentid` | `sprk_documentname` (NOT `sprk_name`) + `sprk_filename` |
| Work assignment | `sprk_workassignment` (custom) | n/a | `sprk_workassignmentid` | `sprk_name` |

---

## 1. `sprk_event` — Tasks + General Events

**Collection name**: `sprk_events`

**Table description** (from describe): "Tasks, deadlines, action items, and appointments in Spaarke. When users ask about tasks, to-dos, deadlines, assignments, or due dates, query this table — NOT the standard Dataverse Task or Activity entities."

### Key attributes

| Attribute | Type | Notes |
|---|---|---|
| `sprk_eventid` | GUID PK | Replaces OOB `activityid` |
| `sprk_eventname` | NVARCHAR(850) NOT NULL | Replaces OOB `subject` |
| `sprk_eventtype_ref` | LOOKUP → `sprk_eventtype_ref` | Discriminator (Task / Action / Deadline / Meeting / Milestone / Reminder / Notification / Status Change / Approval / Assign Work) |
| `sprk_duedate` | DATE ONLY | Primary due-date for tasks |
| `sprk_finalduedate` | DATE ONLY | Hard deadline (when task can no longer be extended) |
| `sprk_meetingdate` | DATE ONLY | For meeting-type events |
| `sprk_plannedstart` / `sprk_plannedend` | DATETIME | Schedule window |
| `sprk_actualstart` / `sprk_actualend` | DATETIME | Completion timestamps |
| `sprk_assignedto` | LOOKUP → contact | Primary assignee |
| `sprk_assignedattorney` / `sprk_assignedparalegal` | LOOKUP → contact | Role-based assignees |
| `sprk_regardingmatter` | LOOKUP → `sprk_matter` | Matter linkage (REPLACES OOB `regardingobjectid`) |
| `sprk_regardingproject` | LOOKUP → `sprk_project` | Project linkage |
| `sprk_regardingrecordname` | NVARCHAR(1000) | Display name of regarding record (for grid rendering) |
| `sprk_regardingrecordurl` | URL | Direct URL to regarding record |
| `sprk_eventstatus` | CHOICE | Draft (0), Open (1), Completed (2), Closed (3), On Hold (4), Cancelled (5), Reassigned (6), Archived (7) |
| `statuscode` | STATUS | Open (659490001), Completed (659490002), Closed (659490003), Cancelled (659490004), Transferred (659490005), On Hold (659490006), Reassigned (659490007), No Further Action (2), Draft (1) |
| `ownerid` | OWNER | User who owns the event |
| `createdon` / `createdby` | DATETIME / LOOKUP | Standard |

### `sprk_eventtype_ref` option-set values (deployed in spaarkedev1)

| GUID | Name | Used for |
|---|---|---|
| `124f5fc9-98ff-f011-8406-7c1e525abd8b` | **Task** | Tasks-overdue + Tasks-due-soon playbooks |
| `5a1c56c3-98ff-f011-8406-7c1e525abd8b` | Action | New-events playbook (in the "general events" union) |
| `e0043c4b-99ff-f011-8406-7c1e525abd8b` | Deadline | New-events playbook |
| `8fb9b5a7-99ff-f011-8406-7c1e525abd8b` | Meeting | New-events playbook |
| `b86d712b-99ff-f011-8406-7c1e525abd8b` | Milestone | New-events playbook |
| `da6bc005-99ff-f011-8406-7c1e525abd8b` | Reminder | New-events playbook |
| `748b1a64-99ff-f011-8406-7c1e525abd8b` | Notification | (not used by R4 notification playbooks) |
| `1f06537c-99ff-f011-8406-7c1e525abd8b` | Status Change | (not used) |
| `1ab1c782-99ff-f011-8406-7c1e525abd8b` | Approval | (not used) |
| `c7468474-ba1d-f111-88b3-7c1e520aa4df` | Assign Work | (not used) |

### Status codes

- **Open**: `statuscode = 659490001` — used in tasks-overdue + tasks-due-soon filters

---

## 2. `sprk_communication` — Emails + Other Communications

**Collection name**: `sprk_communications`

**Table description** (from describe): "Email communications and correspondence related to Matters. When users ask about 'emails', 'correspondence', or 'communications', query this table."

### Key attributes

| Attribute | Type | Notes |
|---|---|---|
| `sprk_communicationid` | GUID PK | Replaces OOB `activityid` |
| `sprk_name` | NVARCHAR(850) NOT NULL | Display name |
| `sprk_subject` | NVARCHAR(2000) | Email subject |
| `sprk_communicationtype` | CHOICE | **Email (100000000)**, Teams Message (100000001), SMS (100000002), Notification (100000003) |
| `sprk_direction` | CHOICE | Incoming (100000000), Outgoing (100000001) |
| `sprk_from` | NVARCHAR(1000) | Sender (replaces OOB `sender`) |
| `sprk_to` | NVARCHAR(1000) | Recipients |
| `sprk_cc` / `sprk_bcc` | NVARCHAR(1000) | Cc / Bcc |
| `sprk_body` | MULTILINE TEXT | Email body |
| `sprk_bodyformat` | CHOICE | PlainText (100000000), HTML (100000001) |
| `sprk_receiveddate` | DATETIME | When received |
| `sprk_sentat` | DATETIME | When sent |
| `sprk_regardingmatter` | LOOKUP → `sprk_matter` | Matter linkage |
| `sprk_regardingproject` | LOOKUP → `sprk_project` | Project linkage |
| `sprk_regardingperson` | LOOKUP → contact | Person linkage |
| `sprk_regardingrecordname` | NVARCHAR(100) | Display |
| `sprk_regardingrecordurl` | URL | URL |
| `sprk_hasattachments` | BIT | Attachment flag |
| `ownerid` | OWNER | Owner |
| `createdon` / `createdby` | DATETIME / LOOKUP | Standard |

### Discriminator

- **Email**: `sprk_communicationtype = 100000000`

---

## 3. `sprk_workassignment` — Work Assignments

**Collection name**: `sprk_workassignments`

### Key attributes

| Attribute | Type | Notes |
|---|---|---|
| `sprk_workassignmentid` | GUID PK | |
| `sprk_name` | NVARCHAR(850) NOT NULL | Display name |
| `sprk_workassignmentnumber` | NVARCHAR(100) | Number |
| `sprk_description` | MULTILINE TEXT | Description |
| `sprk_assignedto` | LOOKUP → contact | Primary assignee |
| `sprk_assignedattorney1` / `sprk_assignedattorney2` | LOOKUP → contact | Attorney assignees |
| `sprk_assignedparalegal1` / `sprk_assignedparalegal2` | LOOKUP → contact | Paralegal assignees |
| `sprk_assignedtointernal` / `sprk_assignedtoexternal` | LOOKUP → contact | Internal/external split |
| `sprk_assignedlawfirm1` / `sprk_assignedlawfirm2` | LOOKUP → `sprk_organization` | Law-firm assignees |
| `sprk_regardingmatter` | LOOKUP → `sprk_matter` | Matter linkage |
| `sprk_regardingproject` | LOOKUP → `sprk_project` | Project linkage |
| `sprk_regardingevent` | LOOKUP → `sprk_event` | Event linkage |
| `sprk_regardingcommunication` | LOOKUP → `sprk_communication` | Communication linkage |
| `sprk_regardinginvoice` | LOOKUP → `sprk_invoice` | Invoice linkage |
| `sprk_responseduedate` | DATE ONLY | Response due date |
| `sprk_priority` | CHOICE | Low (100000000), Normal (100000001), High (100000002), Urgent (100000003) |
| `sprk_mattertype` | LOOKUP → `sprk_mattertype_ref` | Matter type |
| `sprk_practicearea` | LOOKUP → `sprk_practicearea_ref` | Practice area |
| `ownerid` | OWNER | Owner |
| `statecode` | STATE | Active (0), Inactive (1) |
| `statuscode` | STATUS | Active (1), Inactive (2) |
| `createdon` / `createdby` | DATETIME / LOOKUP | Standard |

---

## 4. `sprk_document` — Documents (already custom, attribute clarifications)

**Collection name**: `sprk_documents`

**Critical attribute clarification** (task 012 noted this): The display-name attribute is `sprk_documentname` (NOT `sprk_name`). Repo file was wrong.

### Key attributes

| Attribute | Type | Notes |
|---|---|---|
| `sprk_documentid` | GUID PK | |
| `sprk_documentname` | NVARCHAR(850) | **Display name (NOT sprk_name)** |
| `sprk_filename` | NVARCHAR(1000) | File name |
| `sprk_documenttype` | CHOICE | Contract / Invoice / Proposal / Report / Letter / Memo / Email / Agreement / Statement / Patent / Trademark / NDA / Other |
| `sprk_documentstatus` | CHOICE | Draft (0) / Working (1) / In Review (2) / Approved Final (3) / Rejected Final (4) / Replaced Final (5) / Archived (6) |
| `sprk_matter` | LOOKUP → `sprk_matter` | Matter linkage |
| `sprk_project` | LOOKUP → `sprk_project` | Project linkage |
| `sprk_communication` | LOOKUP → `sprk_communication` | Communication linkage |
| `sprk_workassignment` | LOOKUP → `sprk_workassignment` | Work-assignment linkage |
| `sprk_filesize` | INT | File size in bytes |
| `sprk_mimetype` | NVARCHAR(100) | MIME type |
| `sprk_filepath` | URL | File path |
| `sprk_filesummary` / `sprk_filetldr` | MULTILINE TEXT | AI summaries |
| `ownerid` | OWNER | Owner |
| `createdon` / `createdby` | DATETIME / LOOKUP | Standard |

---

## 5. Ground-truth deployed FetchXml patterns

The deployed playbooks (PB-016 / PB-018 / PB-019 / PB-020 / PB-021) all follow this skeleton (extracted via `mcp__dataverse__read_query`):

```xml
<fetch top="50">
  <entity name="{{ENTITY}}">
    <!-- core attributes -->
    <attribute name="{{ID_ATTR}}"/>
    <attribute name="{{NAME_ATTR}}"/>
    <attribute name="sprk_regardingmatter"/>
    <attribute name="sprk_regardingrecordname"/>
    <attribute name="sprk_regardingrecordurl"/>
    <attribute name="ownerid"/>
    <attribute name="createdon"/>

    <!-- outer link to matter for matter-owner-eq-userid branch -->
    <link-entity name="sprk_matter" from="sprk_matterid" to="sprk_regardingmatter" link-type="outer" alias="m">
      <attribute name="sprk_mattername"/>
      <attribute name="sprk_matternumber"/>
      <attribute name="ownerid"/>
    </link-entity>

    <filter type="and">
      <!-- entity-specific discriminator (e.g., sprk_communicationtype = 100000000 for Email) -->
      <!-- time-window filter (e.g., createdon last-x-hours {{timeWindowHours}}) -->
      <!-- exclude self-created (createdby ne-userid) -->

      <!-- THE UNION (owner OR membership) -->
      <filter type="or">
        <condition entityname="m" attribute="ownerid" operator="eq-userid"/>
        <condition attribute="ownerid" operator="eq-userid"/>
      </filter>
    </filter>

    <order attribute="createdon" descending="true"/>
  </entity>
</fetch>
```

**Note**: The deployed playbooks already use a 2-branch union (record-owner OR regarding-matter-owner) via the outer `link-entity` and inner `<filter type="or">`. This is **NOT** the ActionType 52 `LookupUserMembership` branch the R4 spec wants — that union resolves to "ALL matters the user is a member of (owner + assignedAttorney + assignedParalegal)" via a separate node, then filters via `{{joinIds myMatters.ids}}`. Both unions extend the scope; the corrected R4 repo state SHOULD have the LookupUserMembership branch in addition to (or replacing) the inline 2-branch union.

For R4 corrected repo JSON, we use the canonical PB-018 (notification-new-documents) pattern: `Start → LookupUserMembership → Query (with `{{joinIds myMatters.ids}}` filter) → Condition → CreateNotification`. The inline 2-branch union is preserved as a fallback / additional surface.

---

## 6. ActionType reference (relevant subset)

| ActionType | Name | Used in |
|---|---|---|
| 22 | `updateRecord` (queryMode=true is workaround) | Pre-R4 repo JSON wrongly used this |
| 30 | Condition (`conditionJson`) | Check-results nodes |
| 33 | Start (Control) | Start nodes |
| 50 | CreateNotification | Notification emit nodes |
| 51 | **`queryDataverse`** — the canonical query primitive | Deployed playbooks use this |
| 52 | **`LookupUserMembership`** (ADR-034) | Canonical membership-scope primitive (R4 deploys missing Action row in task 005) |
| 141 | `EntityNameValidator` (R4 task 002 + 003 + 007) | DAILY-BRIEFING-NARRATE playbook |

---

## 7. Carry-forward notes for PR 3 W1

When rewriting `notification-tasks-overdue.json` (task 022), `notification-tasks-due-soon.json` (task 023), `notification-matter-activity.json` (task 024), `notification-work-assignments.json` (task 025):

1. Use `sprk_event` for tasks (with `sprk_eventtype_ref = 124f5fc9-98ff-f011-8406-7c1e525abd8b` for the "Task" discriminator)
2. Use `sprk_event` with the 5-GUID `IN` filter for general events
3. Use `sprk_communication` with `sprk_communicationtype = 100000000` for emails
4. Use `sprk_workassignment` for work assignments
5. Use `sprk_document` with `sprk_documentname` (NOT `sprk_name`) for documents
6. Include `LookupUserMembership` (ActionType 52) node BEFORE the query node; bind `myMatters.ids` for downstream filtering via `{{joinIds myMatters.ids}}`
7. Use ActionType 51 (`queryDataverse`), NOT 22 (`updateRecord queryMode`), for query nodes
8. Use `sprk_eventid` / `sprk_communicationid` for dedup keys (NOT OOB `activityid`)
9. Reference `sprk_regardingmatter` (NOT OOB `regardingobjectid`)
10. Reference `sprk_eventname` / `sprk_name` (NOT OOB `subject`) for display

This file is the authoritative reference for these decisions.
