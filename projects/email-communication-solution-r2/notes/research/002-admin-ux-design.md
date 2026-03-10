# ECS-002: Communication Account Admin UX Design

> **Task**: ECS-002 — Complete Communication Account Admin UX (Dataverse Forms/Views)
> **Date**: 2026-03-09
> **Phase**: 1 — Communication Account Entity (Phase A)
> **Rigor**: STANDARD
> **Dependencies**: ECS-001 (R1 assessment complete)

---

## Summary

This document specifies the complete admin UX for `sprk_communicationaccount` in the Dataverse model-driven app. It updates the R1 form layout with R2 additions (daily send limits, archival opt-in, description, processing rules) and defines five views for managing communication accounts.

**Key changes from R1**:
- Field name correction: `sprk_sendenableds` changed to `sprk_sendenabled` (trailing 's' removed)
- New fields added to form: Daily Send Limit, Sends Today, Archive Outgoing Opt-In, Archive Incoming Opt-In, Description, Processing Rules
- New Subscription Status and Verification Message fields included
- Five views defined (up from four in R1)

---

## 1. Entity Schema Reference

### Existing Custom Fields (from Dataverse export)

| Display Name | Logical Name | Type | Notes |
|---|---|---|---|
| Name | `sprk_name` | Text (850) | Primary name column |
| Email Address | `sprk_emailaddress` | Text (100) | Mailbox address |
| Display Name | `sprk_displayname` | Text (100) | Friendly "From" name |
| Account Type | `sprk_accounttype` | Choice | Shared=100000000, Service=100000001, User=100000002, Distribution List=100000003 |
| Auth Method | `sprk_authmethod` | Choice | App-Only=100000000, OBO=100000001 |
| Send Enabled | `sprk_sendenabled` | Yes/No | Default: No |
| Is Default Sender | `sprk_isdefaultsender` | Yes/No | Default: No |
| Receive Enabled | `sprk_receiveenabled` | Yes/No | Default: No |
| Monitor Folder | `sprk_monitorfolder` | Text (100) | Default: Inbox |
| Auto Create Records | `sprk_autocreaterecords` | Yes/No | Default: No |
| Subscription Id | `sprk_subscriptionid` | Text (100) | Graph subscription ID |
| Graph Subscription Id | `sprk_graphsubscriptionid` | Text (1000) | Extended Graph subscription reference |
| Subscription Expiry | `sprk_subscriptionexpiry` | DateTime | Subscription renewal deadline |
| Subscription Status | `sprk_subscriptionstatus` | Choice | Active=100000, Expired=100000001, Failed=100000002, Not Configured=100000003 |
| Security Group Id | `sprk_securitygroupid` | Text (100) | Azure AD group ID |
| Security Group Name | `sprk_securitygroupname` | Text (100) | Azure AD group display name |
| Verification Status | `sprk_verificationstatus` | Choice | Verified=100000000, Failed=100000001, Pending=100000002, Not Checked=100000003 |
| Verification Message | `sprk_verificationmessage` | Multiline Text (4000) | Verification result details |
| Last Verified | `sprk_lastverified` | DateTime | Last verification timestamp |
| Daily Send Limit | `sprk_dailysendlimit` | Whole Number | Max sends per day per account |
| Sends Today | `sprk_sendstoday` | Whole Number | Counter, reset daily |
| Description | `sprk_desscription` | Text (2000) | NOTE: Dataverse field has double 's' typo |
| Processing Rules | `sprk_processingrules` | Multiline Text (10000) | Per-account message processing config (JSON) |

### R2 Fields to Create in Dataverse (Not Yet in Schema)

These fields are referenced in the R2 spec but do not yet exist in the Dataverse schema export:

| Display Name | Proposed Logical Name | Type | Default | Notes |
|---|---|---|---|---|
| Archive Outgoing Opt-In | `sprk_archiveoutgoingoptin` | Yes/No | Yes | Per-account outbound EML archival control |
| Archive Incoming Opt-In | `sprk_archiveincomingoptin` | Yes/No | Yes | Per-account inbound EML archival control |

**Action Required**: Create these two fields in Dataverse before adding to the form. Both should default to Yes per spec requirement.

---

## 2. Form Layout Specification

### Form: Communication Account — Main Form

**Form Type**: Main
**Entity**: `sprk_communicationaccount`
**Tabs**: Single tab with 7 sections

---

### Section 1: Core Identity

**Columns**: 2
**Collapsible**: No (always visible as header section)

| Field | Logical Name | Type | Required | Read-Only | Column |
|---|---|---|---|---|---|
| Name | `sprk_name` | Text | Yes | No | Left |
| Email Address | `sprk_emailaddress` | Text | Yes | No | Left |
| Display Name | `sprk_displayname` | Text | No | No | Right |
| Account Type | `sprk_accounttype` | Choice | Yes | No | Right |
| Auth Method | `sprk_authmethod` | Choice | No | No | Right |
| Description | `sprk_desscription` | Text | No | No | Full width (spans both columns) |

**Notes**:
- Name and Email Address are the minimum required fields for a new record.
- Account Type drives Auth Method derivation (Shared/Service -> App-Only, User -> OBO), but admin can override.
- Description field (`sprk_desscription`) uses the Dataverse schema name which has a double 's' typo. Do not rename -- this would break existing references.

---

### Section 2: Outbound Configuration

**Columns**: 2
**Collapsible**: Yes (default expanded)

| Field | Logical Name | Type | Required | Read-Only | Column |
|---|---|---|---|---|---|
| Send Enabled | `sprk_sendenabled` | Yes/No | No | No | Left |
| Is Default Sender | `sprk_isdefaultsender` | Yes/No | No | No | Left |
| Daily Send Limit | `sprk_dailysendlimit` | Whole Number | No | No | Right |
| Sends Today | `sprk_sendstoday` | Whole Number | No | **Yes** | Right |

**Business Rules**:
- **BR-OUT-1**: When `sprk_sendenabled` = No, hide `sprk_isdefaultsender`, `sprk_dailysendlimit`, `sprk_sendstoday`.
- **BR-OUT-2**: Only one account should have `sprk_isdefaultsender` = Yes at a time. Consider a plugin or business rule to enforce this constraint.
- **BR-OUT-3**: `sprk_sendstoday` is system-managed (incremented by CommunicationService on each send, reset daily). Always read-only on form.

---

### Section 3: Inbound Configuration

**Columns**: 2
**Collapsible**: Yes (default expanded)

| Field | Logical Name | Type | Required | Read-Only | Column |
|---|---|---|---|---|---|
| Receive Enabled | `sprk_receiveenabled` | Yes/No | No | No | Left |
| Monitor Folder | `sprk_monitorfolder` | Text | No | No | Left |
| Auto Create Records | `sprk_autocreaterecords` | Yes/No | No | No | Right |

**Business Rules**:
- **BR-IN-1**: When `sprk_receiveenabled` = No, hide `sprk_monitorfolder` and `sprk_autocreaterecords`.
- Monitor Folder defaults to "Inbox". Only change if using a dedicated subfolder.

---

### Section 4: Graph Integration (System-Managed)

**Columns**: 2
**Collapsible**: Yes (default collapsed)
**Section Label**: "Graph Integration (System-Managed)"

| Field | Logical Name | Type | Required | Read-Only | Column |
|---|---|---|---|---|---|
| Subscription Status | `sprk_subscriptionstatus` | Choice | No | **Yes** | Left |
| Subscription Id | `sprk_subscriptionid` | Text | No | **Yes** | Left |
| Graph Subscription Id | `sprk_graphsubscriptionid` | Text | No | **Yes** | Right |
| Subscription Expiry | `sprk_subscriptionexpiry` | DateTime | No | **Yes** | Right |

**ALL fields in this section are READ-ONLY.** They are populated and managed exclusively by `GraphSubscriptionManager` (BackgroundService).

**Notes**:
- Subscription fields populate automatically within 30 minutes of enabling receive.
- If fields remain empty after 1 hour with receive enabled, check GraphSubscriptionManager logs.
- Admins can view but never edit these values.

---

### Section 5: Security

**Columns**: 2
**Collapsible**: Yes (default collapsed)

| Field | Logical Name | Type | Required | Read-Only | Column |
|---|---|---|---|---|---|
| Security Group Id | `sprk_securitygroupid` | Text | No | No | Left |
| Security Group Name | `sprk_securitygroupname` | Text | No | No | Right |

**Notes**:
- Security Group controls which Azure AD group has access to this communication account.
- Both fields are optional. If not set, account is available to all authorized users.

---

### Section 6: Archival Options

**Columns**: 2
**Collapsible**: Yes (default expanded)

| Field | Logical Name | Type | Required | Read-Only | Column | Default |
|---|---|---|---|---|---|---|
| Archive Outgoing Opt-In | `sprk_archiveoutgoingoptin` | Yes/No | No | No | Left | **Yes** |
| Archive Incoming Opt-In | `sprk_archiveincomingoptin` | Yes/No | No | No | Right | **Yes** |

**Notes**:
- Both fields default to Yes per spec. Toggling to No skips EML archival for that direction.
- These fields must be created in Dataverse before being added to the form (see Section 1 above).
- Archival pipeline (Tasks 033, 034) checks these flags before processing.

---

### Section 7: Verification (System-Managed)

**Columns**: 2
**Collapsible**: Yes (default collapsed)
**Section Label**: "Verification (System-Managed)"

| Field | Logical Name | Type | Required | Read-Only | Column |
|---|---|---|---|---|---|
| Verification Status | `sprk_verificationstatus` | Choice | No | **Yes** | Left |
| Last Verified | `sprk_lastverified` | DateTime | No | **Yes** | Left |
| Verification Message | `sprk_verificationmessage` | Multiline Text | No | **Yes** | Full width |

**ALL fields in this section are READ-ONLY.** They are updated by `MailboxVerificationService`.

**Business Rules**:
- **BR-VER-1**: When `sprk_verificationstatus` = Failed (100000001), show form-level notification (warning): "Mailbox verification failed. Check permissions and re-verify."
- **Verify button** on command bar (defined in Task ECS-042) will call `POST /api/communications/accounts/{recordId}/verify`.

**Status Badge Indicators** (for reference, implemented via form notification or custom control):

| Status Value | Label | Visual |
|---|---|---|
| 100000000 | Verified | Green / Success |
| 100000001 | Failed | Red / Danger |
| 100000002 | Pending | Yellow / Warning |
| 100000003 | Not Checked | Gray / Neutral |
| null | Not Verified | Gray / Neutral |

---

### Section 8: Processing Rules

**Columns**: 1
**Collapsible**: Yes (default collapsed)

| Field | Logical Name | Type | Required | Read-Only | Column |
|---|---|---|---|---|---|
| Processing Rules | `sprk_processingrules` | Multiline Text | No | No | Full width |

**Notes**:
- JSON format for per-account message processing configuration.
- Replaces the global filter rules from the legacy `EmailFilterService` (being retired in Phase E).
- Admins can configure account-specific processing behavior here.
- Field is 10,000 characters max.

---

## 3. Form Creation Steps (make.powerapps.com)

1. Navigate to **make.powerapps.com** > **Tables** > **Communication Account** > **Forms**.
2. Open the existing **Main** form (created in R1) or create a new one if none exists.
3. Ensure a single tab with the following 8 sections (in order):
   - Core Identity
   - Outbound Configuration
   - Inbound Configuration
   - Graph Integration (System-Managed)
   - Security
   - Archival Options
   - Verification (System-Managed)
   - Processing Rules
4. For each section, add the fields listed above.
5. Mark all fields in "Graph Integration" and "Verification" sections as **Read-Only** (field properties > Read-only checkbox).
6. Mark `sprk_sendstoday` as **Read-Only**.
7. Set collapsible properties and default expanded/collapsed states as specified.
8. Create business rules:
   - BR-OUT-1: Hide outbound sub-fields when Send Enabled = No
   - BR-IN-1: Hide inbound sub-fields when Receive Enabled = No
   - BR-VER-1: Form notification on verification failure
9. **Save and Publish** the form.

---

## 4. View Definitions

### View 1: Active Communication Accounts (Default View)

**Purpose**: Default view showing all active records.
**Set as default**: Yes

| # | Column | Logical Name | Width |
|---|---|---|---|
| 1 | Name | `sprk_name` | 200px |
| 2 | Email Address | `sprk_emailaddress` | 250px |
| 3 | Account Type | `sprk_accounttype` | 150px |
| 4 | Send Enabled | `sprk_sendenabled` | 120px |
| 5 | Receive Enabled | `sprk_receiveenabled` | 130px |
| 6 | Is Default Sender | `sprk_isdefaultsender` | 120px |
| 7 | Verification Status | `sprk_verificationstatus` | 150px |

**Filter**: `statecode eq 0` (Active)
**Sort**: `sprk_name` ascending

---

### View 2: Send-Enabled Accounts

**Purpose**: All accounts configured for outbound email.

| # | Column | Logical Name | Width |
|---|---|---|---|
| 1 | Name | `sprk_name` | 200px |
| 2 | Email Address | `sprk_emailaddress` | 250px |
| 3 | Display Name | `sprk_displayname` | 180px |
| 4 | Account Type | `sprk_accounttype` | 150px |
| 5 | Is Default Sender | `sprk_isdefaultsender` | 120px |
| 6 | Daily Send Limit | `sprk_dailysendlimit` | 130px |
| 7 | Sends Today | `sprk_sendstoday` | 120px |
| 8 | Verification Status | `sprk_verificationstatus` | 150px |

**Filter**: `sprk_sendenabled eq true` AND `statecode eq 0`
**Sort**: `sprk_isdefaultsender` descending, then `sprk_name` ascending

---

### View 3: Receive-Enabled Accounts

**Purpose**: All accounts configured for inbound email monitoring.

| # | Column | Logical Name | Width |
|---|---|---|---|
| 1 | Name | `sprk_name` | 200px |
| 2 | Email Address | `sprk_emailaddress` | 250px |
| 3 | Monitor Folder | `sprk_monitorfolder` | 150px |
| 4 | Auto Create Records | `sprk_autocreaterecords` | 150px |
| 5 | Subscription Status | `sprk_subscriptionstatus` | 150px |
| 6 | Subscription Expiry | `sprk_subscriptionexpiry` | 180px |
| 7 | Archive Incoming Opt-In | `sprk_archiveincomingoptin` | 150px |

**Filter**: `sprk_receiveenabled eq true` AND `statecode eq 0`
**Sort**: `sprk_name` ascending

---

### View 4: By Account Type

**Purpose**: Group and filter accounts by their type (Shared, Service, User, Distribution List).

| # | Column | Logical Name | Width |
|---|---|---|---|
| 1 | Account Type | `sprk_accounttype` | 150px |
| 2 | Name | `sprk_name` | 200px |
| 3 | Email Address | `sprk_emailaddress` | 250px |
| 4 | Auth Method | `sprk_authmethod` | 150px |
| 5 | Send Enabled | `sprk_sendenabled` | 120px |
| 6 | Receive Enabled | `sprk_receiveenabled` | 130px |

**Filter**: `statecode eq 0`
**Sort**: `sprk_accounttype` ascending, then `sprk_name` ascending
**Group By**: `sprk_accounttype` (if supported by the view designer)

---

### View 5: Verification Status

**Purpose**: Identify accounts with failed or pending verification for admin attention.

| # | Column | Logical Name | Width |
|---|---|---|---|
| 1 | Verification Status | `sprk_verificationstatus` | 150px |
| 2 | Name | `sprk_name` | 200px |
| 3 | Email Address | `sprk_emailaddress` | 250px |
| 4 | Verification Message | `sprk_verificationmessage` | 300px |
| 5 | Last Verified | `sprk_lastverified` | 180px |
| 6 | Account Type | `sprk_accounttype` | 150px |

**Filter**: (`sprk_verificationstatus eq 100000001` OR `sprk_verificationstatus eq 100000002` OR `sprk_verificationstatus eq null`) AND `statecode eq 0`
**Sort**: `sprk_verificationstatus` ascending (Failed first, then Pending, then null)

---

### View Creation Steps (make.powerapps.com)

1. Navigate to **Tables** > **Communication Account** > **Views**.
2. For each view above:
   a. Click **+ New view**, enter the view name.
   b. Add columns using **+ View column** (in the order specified).
   c. Set column widths.
   d. Apply filters using **Edit filters** in the command bar.
   e. Set sort order by clicking column headers.
   f. **Save and Publish**.
3. Set **Active Communication Accounts** as the default view.
4. Verify all views appear in the view selector dropdown.

---

## 5. Seed Data Specification

### Record: Spaarke Central Mailbox

Create this record as the default send-enabled communication account.

| Field | Logical Name | Value |
|---|---|---|
| Name | `sprk_name` | Spaarke Central Mailbox |
| Email Address | `sprk_emailaddress` | mailbox-central@spaarke.com |
| Display Name | `sprk_displayname` | Spaarke Central |
| Account Type | `sprk_accounttype` | Shared Account (100000000) |
| Auth Method | `sprk_authmethod` | App-Only (100000000) |
| Send Enabled | `sprk_sendenabled` | Yes |
| Is Default Sender | `sprk_isdefaultsender` | Yes |
| Receive Enabled | `sprk_receiveenabled` | No (enable when inbound pipeline is ready) |
| Monitor Folder | `sprk_monitorfolder` | Inbox |
| Auto Create Records | `sprk_autocreaterecords` | No |
| Daily Send Limit | `sprk_dailysendlimit` | 500 |
| Sends Today | `sprk_sendstoday` | 0 (system-managed) |
| Archive Outgoing Opt-In | `sprk_archiveoutgoingoptin` | Yes |
| Archive Incoming Opt-In | `sprk_archiveincomingoptin` | Yes |
| Verification Status | `sprk_verificationstatus` | Pending (100000002) |
| Description | `sprk_desscription` | Default shared mailbox for outbound firm communications. Operates under app-only (client credentials) auth. |

### Seed Data Creation Steps

1. Open the model-driven app containing the Communication Account table.
2. Navigate to the **Communication Account** area.
3. Click **+ New** to open the main form.
4. Enter the values from the table above.
5. Click **Save & Close**.

### Seed Data Verification Checklist

| Check | Expected Result |
|---|---|
| Record appears in **Active Communication Accounts** view | Yes -- active record |
| Record appears in **Send-Enabled Accounts** view | Yes -- send enabled = true |
| Record does NOT appear in **Receive-Enabled Accounts** view | Correct -- receive enabled = false |
| Record appears in **By Account Type** view under "Shared Account" | Yes |
| Record appears in **Verification Status** view | Yes -- status is "Pending" |
| `sprk_isdefaultsender` = Yes | Only this record should be the default |
| `sprk_dailysendlimit` = 500 | Reasonable initial limit |
| Both archival opt-in fields = Yes | Default archival behavior |

---

## 6. Existing Form XML Assessment

### Search Results

No exported form XML files exist for `sprk_communicationaccount` in the repository:
- `src/dataverse/solutions/` -- no communicationaccount entity folder found
- `infrastructure/dataverse/` -- contains ribbon definitions for `sprk_communication` (the communication record entity) but not for `sprk_communicationaccount`
- No `FormXml/` directory exists for this entity

### What Exists from R1

The R1 project created the form manually in make.powerapps.com following the guide at:
- `projects/x-email-communication-solution-r1/notes/communication-account-admin-guide.md`

That guide documented:
- 6 sections: Core Identity, Outbound Configuration, Inbound Configuration, Graph Integration (Read-Only), Security, Verification (Read-Only)
- 4 views: Active Communication Accounts, Send-Enabled Accounts, Receive-Enabled Accounts, Default Senders

### R2 Delta from R1

| Area | R1 State | R2 Changes |
|---|---|---|
| **Field name** | `sprk_sendenableds` (trailing 's') | Corrected to `sprk_sendenabled` -- update all views and business rules |
| **Form sections** | 6 sections | Add 2 new sections: **Archival Options** and **Processing Rules** (total: 8 sections) |
| **Outbound section** | Send Enabled, Is Default Sender | Add: Daily Send Limit, Sends Today (read-only) |
| **Core Identity section** | Name, Email, Display Name, Account Type | Add: Auth Method, Description |
| **Graph Integration** | Subscription Id, Subscription Expiry | Add: Subscription Status, Graph Subscription Id |
| **Verification section** | Verification Status, Last Verified | Add: Verification Message |
| **Views** | 4 views (Active, Send-Enabled, Receive-Enabled, Default Senders) | 5 views (replace Default Senders with By Account Type; add Verification Status) |
| **Seed data** | Basic record | Add: Daily Send Limit=500, archival opt-ins=Yes, Description |

---

## 7. Relationship to Other Tasks

| Task | Relationship |
|---|---|
| **ECS-001** (R1 Assessment) | Prerequisite -- identified gaps documented here |
| **ECS-042** (Admin Form Enhancements) | Phase D follow-up -- adds Verify button, enhanced status display. This task (ECS-002) creates the base form; ECS-042 adds interactive features. |
| **ECS-033** (Inbound Document Archival) | Depends on `sprk_archiveincomingoptin` field existing |
| **ECS-034** (Outbound Archival Enhancement) | Depends on `sprk_archiveoutgoingoptin` field existing |
| **ECS-041** (Daily Send Count Tracking) | Depends on `sprk_dailysendlimit` and `sprk_sendstoday` being on the form |

---

## 8. Prerequisites Before Deployment

1. **Create R2 fields in Dataverse** (if not yet created):
   - `sprk_archiveoutgoingoptin` (Yes/No, default Yes)
   - `sprk_archiveincomingoptin` (Yes/No, default Yes)

2. **Verify field name corrections** have been applied:
   - Confirm `sprk_sendenabled` works (not the old `sprk_sendenableds`)
   - Update any existing view filters that reference the old name

3. **Publish all customizations** after form and view changes.

---

*Design document for ECS-002. All field names verified against Dataverse schema export at `docs/data-model/sprk_communicationaccount.md`.*
