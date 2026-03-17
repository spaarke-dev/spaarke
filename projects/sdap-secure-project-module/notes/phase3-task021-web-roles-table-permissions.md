# Phase 3 Task 021 — Configure Web Roles and Table Permission Chain

> **Task**: 021
> **Phase**: 3 — Power Pages Configuration
> **Status**: Documented (manual operator steps required)
> **Estimated effort**: 6 hours
> **Prerequisite**: Task 020 complete (Entra External ID identity provider configured)

---

## Overview

This guide covers creating the "Secure Project Participant" web role and the complete table permission parent chain that enforces project-level data isolation for external portal users.

The permission chain is the most critical security configuration in this module. A misconfigured chain could expose data across unrelated projects.

**Permission chain architecture**:

```
Contact (authenticated portal user)
    │
    ▼  [Contact scope — user sees only their own]
sprk_externalrecordaccess
    │
    │  [parent relationship: sprk_projectid lookup]
    ▼
sprk_project  ──────────────────────────────────────────────────────────┐
    │                                                                   │
    ├── sprk_document (child via sprk_projectid)  [Read]                │
    ├── sprk_event    (child via sprk_projectid)  [Read/Create/Write]   │
    ├── contact       (child via project-contact relationship) [Read]   │
    └── sprk_organization (child)                [Read]                 │
                                                                        │
All child access flows through the single project anchor ─────────────┘
```

---

## Prerequisites

Before starting, confirm in the Portal Management app that:
- The Entra External ID identity provider is active (Task 020)
- The portal is pointing to the correct Dataverse environment (`https://spaarkedev1.crm.dynamics.com`)
- Tables `sprk_externalrecordaccess`, `sprk_project`, `sprk_document`, `sprk_event`, `sprk_organization` exist in Dataverse (Phase 1 complete)

---

## Step 1 — Create the "Secure Project Participant" Web Role

1. Open the **Portal Management** model-driven app in the Dataverse environment.
   - URL: `https://spaarkedev1.crm.dynamics.com/main.aspx?appname=MicrosoftPortalApp`
2. In the left navigation, go to **Security** → **Web Roles**.
3. Click **New** and configure:

| Field | Value |
|-------|-------|
| **Name** | Secure Project Participant |
| **Website** | [Select your Power Pages site] |
| **Authenticated Users Role** | Yes |
| **Anonymous Users Role** | No |
| **Description** | Grants access to secure project data for authenticated external participants. Role applied to external users with at least one active sprk_externalrecordaccess grant. |

4. Click **Save** (do not close — you will return here to associate table permissions).

> **Authenticated Users Role = Yes** means this role is automatically assigned to all authenticated (signed-in) portal users. If you want to restrict it to only users who have been explicitly invited (have an `sprk_externalrecordaccess` record), set this to **No** and assign the role programmatically via a plugin or workflow when a grant is created. For Phase 3, setting it to **Yes** is acceptable because the table permissions themselves enforce data scoping.

---

## Step 2 — Create the Root Table Permission: sprk_externalrecordaccess (Contact scope)

This is the entry point of the permission chain. It lets each authenticated user see only their own `sprk_externalrecordaccess` records.

1. In Portal Management → **Security** → **Table Permissions**.
2. Click **New**:

| Field | Value |
|-------|-------|
| **Name** | ERA - Contact Scope |
| **Table Name** | sprk_externalrecordaccess |
| **Website** | [your Power Pages site] |
| **Access Type** | Contact |
| **Relationship Name** | `sprk_contactid_sprk_externalrecordaccess` (the N:1 relationship from sprk_externalrecordaccess to Contact via `sprk_contactid`) |
| **Read** | Yes |
| **Create** | No |
| **Write** | No |
| **Delete** | No |
| **Append** | No |
| **Append To** | No |

3. Click **Save**.

> **Contact scope**: Power Pages restricts records to those where the lookup column (`sprk_contactid`) matches the currently authenticated user's Contact record. This ensures each user sees only their own participation grants — never another user's grants.

### Assign Web Role to Root Permission

1. On the `ERA - Contact Scope` table permission record, scroll to the **Web Roles** subgrid.
2. Click **Add Existing Web Role** → select **Secure Project Participant**.
3. Click **Add**.

---

## Step 3 — Create Parent Permission: sprk_project (via sprk_externalrecordaccess)

This permission lets users see the projects they have been granted access to, anchored through their `sprk_externalrecordaccess` records.

1. In Portal Management → **Security** → **Table Permissions** → **New**:

| Field | Value |
|-------|-------|
| **Name** | Project - ERA Parent |
| **Table Name** | sprk_project |
| **Website** | [your Power Pages site] |
| **Access Type** | Parent |
| **Parent Table Permission** | ERA - Contact Scope (the record from Step 2) |
| **Relationship Name** | `sprk_projectid_sprk_externalrecordaccess` (the N:1 relationship from sprk_externalrecordaccess to sprk_project via `sprk_projectid`) |
| **Read** | Yes |
| **Create** | No |
| **Write** | No |
| **Delete** | No |
| **Append** | No |
| **Append To** | No |

2. Click **Save**.

> **Parent access type**: This means "a user can access a sprk_project record if they have access to at least one child sprk_externalrecordaccess record that references it." The chain flows: user's Contact → their ERA records → the projects those ERA records reference.

### Assign Web Role

1. On the `Project - ERA Parent` table permission, scroll to **Web Roles** subgrid.
2. Add **Secure Project Participant**.

---

## Step 4 — Create Child Permission: sprk_document (child of sprk_project)

External users can view documents belonging to their accessible projects.

1. In Portal Management → **Security** → **Table Permissions** → **New**:

| Field | Value |
|-------|-------|
| **Name** | Document - Project Child |
| **Table Name** | sprk_document |
| **Website** | [your Power Pages site] |
| **Access Type** | Parent |
| **Parent Table Permission** | Project - ERA Parent (Step 3) |
| **Relationship Name** | `sprk_projectid_sprk_document` (N:1 from sprk_document to sprk_project via `sprk_projectid`) |
| **Read** | Yes |
| **Create** | No |
| **Write** | No |
| **Delete** | No |
| **Append** | No |
| **Append To** | No |

2. Click **Save** and assign web role **Secure Project Participant**.

> Documents are read-only for external users. Upload and delete operations are handled through the BFF API with explicit authorization checks (Task 010 filter), not through the Web API.

---

## Step 5 — Create Child Permission: sprk_event (child of sprk_project)

External users can read project events. Users with "Collaborate" access level can also create and edit events.

### Step 5a: Read-only baseline (all external users)

1. In Portal Management → **Security** → **Table Permissions** → **New**:

| Field | Value |
|-------|-------|
| **Name** | Event - Project Child (Read) |
| **Table Name** | sprk_event |
| **Website** | [your Power Pages site] |
| **Access Type** | Parent |
| **Parent Table Permission** | Project - ERA Parent (Step 3) |
| **Relationship Name** | `sprk_projectid_sprk_event` (N:1 from sprk_event to sprk_project via `sprk_projectid`) |
| **Read** | Yes |
| **Create** | Yes |
| **Write** | Yes |
| **Delete** | No |
| **Append** | No |
| **Append To** | No |

> **Note on Create/Write**: The Web API site settings (Task 022) control which fields are writable. Access level enforcement (whether a particular user has "Collaborate" rights) is enforced in the SPA layer (Task 049) and in the BFF API filter (Task 010). Table permissions here grant the maximum; the SPA hides create/write UI from View-only users.

2. Click **Save** and assign web role **Secure Project Participant**.

---

## Step 6 — Create Child Permission: contact (project contacts — read only)

External users can see the contact details of other participants in their shared projects (e.g., to display names, email addresses in the Contacts panel).

1. In Portal Management → **Security** → **Table Permissions** → **New**:

| Field | Value |
|-------|-------|
| **Name** | Contact - Project Child (Read) |
| **Table Name** | contact |
| **Website** | [your Power Pages site] |
| **Access Type** | Parent |
| **Parent Table Permission** | Project - ERA Parent (Step 3) |
| **Relationship Name** | The N:N or N:1 relationship linking contact records to sprk_project. Use the relationship for project members — typically via a junction table or a direct lookup. If sprk_project has a contact lookup (`sprk_primarycontactid`) or there is an N:N relationship `sprk_project_contact`, use the appropriate relationship name. |
| **Read** | Yes |
| **Create** | No |
| **Write** | No |
| **Delete** | No |

> **Relationship note**: If the project-contact association is modeled through the `sprk_externalrecordaccess` table (each ERA has a `sprk_contactid`), you can alternatively scope contact read access through a Self permission (user reads their own Contact only) plus the SPA constructs the contacts list from the ERA records it retrieves. Evaluate the data model from Task 001/002 to pick the right relationship name before configuring this permission.

2. Click **Save** and assign web role **Secure Project Participant**.

### Self-read permission for own Contact record

In addition to project-scoped contact access, each user needs to read their own Contact record (for profile display):

1. In Portal Management → **Security** → **Table Permissions** → **New**:

| Field | Value |
|-------|-------|
| **Name** | Contact - Self |
| **Table Name** | contact |
| **Website** | [your Power Pages site] |
| **Access Type** | Self |
| **Read** | Yes |
| **Write** | Yes (allows user to update their own profile) |
| **Create** | No |
| **Delete** | No |

2. Click **Save** and assign web role **Secure Project Participant**.

---

## Step 7 — Create Child Permission: sprk_organization (global read)

Organizations are reference data — external users can read all organizations for display purposes (e.g., to show the organization name next to a project).

1. In Portal Management → **Security** → **Table Permissions** → **New**:

| Field | Value |
|-------|-------|
| **Name** | Organization - Global Read |
| **Table Name** | sprk_organization |
| **Website** | [your Power Pages site] |
| **Access Type** | Global |
| **Read** | Yes |
| **Create** | No |
| **Write** | No |
| **Delete** | No |

2. Click **Save** and assign web role **Secure Project Participant**.

> **Global access type**: Allows the user to read all `sprk_organization` records regardless of any parent-chain relationship. This is safe for an organization reference table that contains no sensitive data. If organizations should be scoped to only the organizations associated with the user's projects, change to a Parent access type and chain it from sprk_project.

---

## Step 8 — Verify the Complete Permission Chain

After creating all permissions, verify the chain in Portal Management → **Security** → **Table Permissions**. You should see:

| Name | Table | Access Type | Parent | Web Role |
|------|-------|-------------|--------|----------|
| ERA - Contact Scope | sprk_externalrecordaccess | Contact | — | Secure Project Participant |
| Project - ERA Parent | sprk_project | Parent | ERA - Contact Scope | Secure Project Participant |
| Document - Project Child | sprk_document | Parent | Project - ERA Parent | Secure Project Participant |
| Event - Project Child (Read) | sprk_event | Parent | Project - ERA Parent | Secure Project Participant |
| Contact - Project Child (Read) | contact | Parent | Project - ERA Parent | Secure Project Participant |
| Contact - Self | contact | Self | — | Secure Project Participant |
| Organization - Global Read | sprk_organization | Global | — | Secure Project Participant |

---

## Step 9 — Clear Portal Cache

After configuring table permissions, clear the portal cache to force the changes to take effect:

1. In Power Pages admin center → **Portal Actions** → **Clear cache**.
2. Wait for the cache clear to complete (~1-2 minutes).

Alternatively, append `/_services/about` to the portal URL and click **Clear Cache** from the diagnostics page.

---

## Step 10 — Test: Data Isolation Verification

### Test 1: Authenticated user sees only their projects

1. Sign in to the portal as a test external user (User A) who has one `sprk_externalrecordaccess` record pointing to Project Alpha.
2. Call the Power Pages Web API (once enabled in Task 022):
   ```
   GET /_api/sprk_projects?$select=sprk_name
   ```
3. Expected: only Project Alpha is returned.
4. Expected: Project Beta (where User A has no ERA record) does NOT appear.

### Test 2: Cross-project data isolation

1. Sign in as User B, who has an ERA record for Project Beta only.
2. Call `GET /_api/sprk_projects`.
3. Expected: only Project Beta is returned. Project Alpha does not appear.
4. Attempt to call `GET /_api/sprk_projects({project-alpha-id})` directly.
5. Expected: 403 Forbidden (not 200 with data).

### Test 3: Document scoping

1. As User A (Project Alpha access):
2. Call `GET /_api/sprk_documents?$filter=sprk_projectid eq {project-alpha-id}`.
3. Expected: documents from Project Alpha returned.
4. Call `GET /_api/sprk_documents?$filter=sprk_projectid eq {project-beta-id}`.
5. Expected: empty result set (no 403 — Power Pages returns empty set for globally permitted but chain-filtered queries).

### Test 4: New user with no ERA grants

1. Sign up as a brand new external user (no ERA records in Dataverse).
2. Call `GET /_api/sprk_projects`.
3. Expected: empty result set — no projects visible.

---

## Acceptance Criteria Checklist

- [ ] "Secure Project Participant" web role exists with Authenticated Users Role = Yes
- [ ] `ERA - Contact Scope` table permission scopes sprk_externalrecordaccess to Contact
- [ ] `Project - ERA Parent` table permission chains sprk_project via sprk_projectid
- [ ] `Document - Project Child` chains sprk_document to sprk_project (Read only)
- [ ] `Event - Project Child` chains sprk_event to sprk_project (Read/Create/Write)
- [ ] `Contact - Self` allows user to read their own Contact
- [ ] `Contact - Project Child` allows reading contacts within accessible projects
- [ ] `Organization - Global Read` allows reading all sprk_organization records
- [ ] All permissions have Secure Project Participant web role assigned
- [ ] Portal cache cleared after configuration
- [ ] User A cannot see Project B data
- [ ] New user with no ERA grants sees zero projects

---

## Troubleshooting

| Symptom | Likely Cause | Resolution |
|---------|-------------|------------|
| User sees all projects (no scoping) | Table permission set to Global instead of Parent chain | Delete and recreate with correct Access Type |
| User sees no projects despite having ERA records | Parent relationship name is wrong | Verify the relationship name matches the actual Dataverse relationship (check using `GET /_api/EntityDefinitions(LogicalName='sprk_externalrecordaccess')/Relationships`) |
| 403 on all Web API calls | Web role not assigned to table permissions, or Web API not enabled (Task 022) | Assign Secure Project Participant web role to all permissions; confirm Task 022 settings |
| Contact record not found after sign-in | adx_externalidentity not linked | Verify Task 020 claim mappings; check adx_externalidentity records in Dataverse |
| Cache changes not reflected | Portal cache not cleared | Clear cache via admin center or `/_services/about` page |
