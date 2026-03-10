# Communication Account Administration Guide - Release 2

> **Last Updated**: March 9, 2026
> **Purpose**: Complete administrative guide for managing the Email Communication Service R2 in Dataverse — covering communication account management, send modes, inbound monitoring, document archival, verification, and troubleshooting.
> **Applies To**: Dev environment (`spaarkedev1.crm.dynamics.com`, `spe-api-dev-67e2xz.azurewebsites.net`)
> **Release**: R2 (replaces email activities, Server-Side Sync retirement)

---

## Table of Contents

- [Overview](#overview)
- [Key Concepts](#key-concepts)
- [How to Add a New Communication Account](#how-to-add-a-new-communication-account)
- [Account Types and Send Modes](#account-types-and-send-modes)
- [Field Reference - Complete Schema](#field-reference)
- [Inbound Monitoring Configuration](#inbound-monitoring-configuration)
- [Document Archival Configuration](#document-archival-configuration)
- [Daily Send Limits and Tracking](#daily-send-limits-and-tracking)
- [Association Resolution](#association-resolution)
- [Verification Status and Testing](#verification-status-and-testing)
- [Graph Subscription Lifecycle](#graph-subscription-lifecycle)
- [Security Configuration](#security-configuration)
- [Admin Views and Forms](#admin-views-and-forms)
- [Common Scenarios](#common-scenarios)
- [Troubleshooting](#troubleshooting)

---

## Overview

Communication accounts (`sprk_communicationaccount`) are the central configuration point for all email communication in Spaarke R2. Each account represents a mailbox that the BFF API can send from, receive to, or both. This replaces the previous fragmented approach (appsettings.json + Server-Side Sync) with a unified Graph-based system.

**Release 2 Highlights**:
- **Unified Configuration**: All mailbox settings in Dataverse — no code deployments needed
- **Graph-Based Inbound**: Webhook subscriptions + backup polling replace Server-Side Sync
- **Send Modes**: Shared mailbox (app-only) and Individual (OBO delegated)
- **Document Archival**: Email and attachments automatically archived as .eml + linked documents
- **Archival Control**: Opt-in/opt-out configuration for incoming and outgoing email
- **Admin Verification**: Test mailbox connectivity with one click

---

## Key Concepts

| Concept | Description |
|---------|------------|
| **Communication Account** | A mailbox configuration record in Dataverse (`sprk_communicationaccount`). Represents a shared mailbox, service account, or user account that the system can send from or monitor for incoming email. |
| **Send Mode** | How an email is sent. **Shared Mailbox** (app-only auth to shared mailbox, default) or **User** (delegated OBO auth to user's own mailbox). |
| **Account Type** | The authentication method: **Shared Account** or **Service Account** (both app-only, differ by licensing), or **User Account** (OBO delegated). Determines which send modes are available. |
| **Receive-Enabled** | When Yes, the BFF API monitors this mailbox for incoming email via Graph webhooks + backup polling. Creates `sprk_communication` records automatically. |
| **Archive Opt-In/Out** | When both defaults are Yes, incoming and outgoing email are automatically archived as .eml files to SharePoint Embedded + linked as documents. Can be toggled per direction. |
| **Verification** | Admin-initiated test that confirms the BFF API has send and/or read permissions on the mailbox. Updates `sprk_verificationstatus` and `sprk_lastverified`. |
| **Graph Subscription** | A webhook subscription created by the BFF's `GraphSubscriptionManager` service. Notifies the system when new email arrives. Expires every 3 days and is auto-renewed. |
| **Backup Polling** | A fallback service (`InboundPollingBackupService`) that queries for missed messages every 5 minutes if webhooks fail. Ensures no email is lost. |
| **Association Resolution** | Automatic linking of incoming emails to matters, contacts, accounts, or other Dataverse entities based on email thread, sender, subject, or mailbox context. |

---

## How to Add a New Communication Account

### Step 1: Create the Account Record in Dataverse

Navigate to the **Communication Accounts** entity in the model-driven app and create a new record.

**Required Fields**:

| Field | Logical Name | What to Enter |
|-------|-------------|---------------|
| Name | `sprk_name` | Human-readable name (e.g., "Central Mailbox", "Billing Department") |
| Email Address | `sprk_emailaddress` | Mailbox email address (e.g., `mailbox-central@spaarke.com`) |
| Display Name | `sprk_displayname` | From header display name (e.g., "Spaarke Legal") |
| Account Type | `sprk_accounttype` | Select: Shared Account, Service Account, or User Account |

**Send Configuration**:

| Field | Logical Name | Notes |
|-------|-------------|-------|
| Send Enabled | `sprk_sendenabled` | Set to **Yes** to allow outbound sending. |
| Is Default Sender | `sprk_isdefaultsender` | Set to **Yes** if this is the fallback sender. Only ONE account should be the default. |

**Receive Configuration**:

| Field | Logical Name | Notes |
|-------|-------------|-------|
| Receive Enabled | `sprk_receiveenabled` | Set to **Yes** to monitor this mailbox for incoming email |
| Monitor Folder | `sprk_monitorfolder` | Folder to watch (default: "Inbox"). Leave blank for Inbox. |
| Auto Create Records | `sprk_autocreaterecords` | Set to **Yes** to auto-create `sprk_communication` records for incoming messages |

**Security Configuration**:

| Field | Logical Name | Notes |
|-------|-------------|-------|
| Security Group Id | `sprk_securitygroupid` | Azure AD security group GUID that scopes Exchange access for this mailbox |
| Security Group Name | `sprk_securitygroupname` | Display name of the security group (informational) |

Save the record.

### Step 2: Configure Exchange Application Access Policy

If this is a new mailbox that the BFF API has not previously been granted access to, you must add it to the Exchange Application Access Policy security group.

> **Skip this step** if the mailbox is already a member of the "SDAP Mailbox Access" security group.

```powershell
# Connect to Exchange Online
Connect-ExchangeOnline

# Add mailbox to the security group
Add-DistributionGroupMember -Identity "SDAP Mailbox Access" -Member "new-mailbox@spaarke.com"

# Verify membership
Get-DistributionGroupMember -Identity "SDAP Mailbox Access" | Format-Table DisplayName, PrimarySmtpAddress

# Disconnect
Disconnect-ExchangeOnline -Confirm:$false
```

**Wait at least 30 minutes** for Exchange Online policy propagation before proceeding.

For full Exchange Application Access Policy setup instructions (including creating the security group and policy from scratch), see [COMMUNICATION-DEPLOYMENT-GUIDE.md](COMMUNICATION-DEPLOYMENT-GUIDE.md#exchange-online-application-access-policy-setup).

### Step 3: Run Mailbox Verification

After the Exchange policy has propagated, verify the account's connectivity:

```bash
# Run verification via BFF API
curl -X POST \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/accounts/{account-id}/verify" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json"
```

Replace `{account-id}` with the `sprk_communicationaccountid` GUID from the Dataverse record.

**What verification tests**:
- If send-enabled: sends a test email from the mailbox
- If receive-enabled: reads recent messages from the mailbox

The endpoint updates the following fields on the account record:
- `sprk_verificationstatus` — Verified (100000000), Failed (100000001), or Pending (100000002)
- `sprk_lastverified` — Timestamp of last verification attempt

You can also click the **Verify** button on the Communication Account form in Dataverse.

### Step 4: Monitor Subscription Status (Receive-Enabled Only)

For receive-enabled accounts, the `GraphSubscriptionManager` background service automatically:
- Creates a Graph webhook subscription on startup
- Renews subscriptions every 30 minutes (when expiry < 24 hours)
- Recreates subscriptions that fail or expire

The following fields are auto-populated by the system:

| Field | Logical Name | Description |
|-------|-------------|-------------|
| Subscription Id | `sprk_subscriptionid` | Graph webhook subscription GUID (system-managed) |
| Subscription Expiry | `sprk_subscriptionexpiry` | When the subscription needs renewal (system-managed) |

**No manual action is required** for subscription management. If a subscription fails, the `InboundPollingBackupService` polls every 5 minutes as a fallback.

---

## Account Types and Send Modes

### Account Types

| Type | Value | Auth Method | Licensing | Send Modes | Receive | When to Use |
|------|-------|-------------|-----------|-----------|---------|------------|
| **Shared Account** | 100000000 | App-only | No license required | Shared Mailbox only | Yes | Firm-wide outbound and inbound monitoring. Most common. |
| **Service Account** | 100000001 | App-only | Has own license | Shared Mailbox only | Yes | Department-specific mailbox (e.g., "Finance Notifications") with its own license. |
| **User Account** | 100000002 | OBO Delegated | User's own license | User (send as self) only | No (out of scope) | Individual user sending via "Send as me" mode. |

### Send Modes

| Mode | Requires Account Type | Auth Method | From Address | Use Case |
|------|----------------------|------------|--------------|----------|
| **Shared Mailbox** (default) | Shared Account or Service Account | App-only (`Mail.Send` Application permission) | Mailbox address from `sprk_communicationaccount.sprk_emailaddress` | Firm-wide email, system notifications, shared mailbox correspondence |
| **User** (OBO) | User Account | Delegated OBO (`Mail.Send` Delegated permission) | Signed-in user's mailbox address | "Send as me" — individual follow-ups sent from user's own mailbox |

### Choosing the Right Configuration

| Need | Account Type | Send Mode | Setup |
|------|--------------|-----------|-------|
| Send from firm-wide mailbox (e.g., legal@domain.com) | Shared Account | Shared Mailbox | Create account, add to Exchange security group, set `sprk_sendenabled = Yes` |
| Monitor shared mailbox for incoming email | Shared Account | (N/A — monitoring separate from send mode) | Create account, set `sprk_receiveenabled = Yes`, wait for subscription |
| Allow user to send from their own mailbox | User Account | User | Create account with user's email, user must consent to app, user selects "Send as me" on form |
| Department-specific mailbox (Finance, HR, etc.) | Service Account | Shared Mailbox | Create account for dept mailbox, add to security group, set `sprk_sendenabled = Yes` |

---

## Inbound Monitoring Configuration

When `sprk_receiveenabled = Yes` on a communication account, the BFF API automatically monitors that mailbox for incoming email via two complementary mechanisms:

### 1. Graph Webhook Subscriptions (Primary)

The `GraphSubscriptionManager` background service:
- Creates a Graph webhook subscription on startup and every 30 minutes
- Automatically renews subscriptions before they expire (within 24 hours of the 3-day max lifetime)
- Recreates subscriptions that fail or expire

**What you need to do**: Just set `sprk_receiveenabled = Yes` on the account. The system handles the rest.

**Fields auto-populated by the system** (read-only):
- `sprk_subscriptionid` — The Graph webhook subscription GUID
- `sprk_subscriptionexpiry` — When the subscription must be renewed (within 3 days)
- `sprk_subscriptionstatus` — Active, Failed, or Pending

### 2. Backup Polling Service (Safety Net)

The `InboundPollingBackupService` runs every 5 minutes as a fallback:
- Queries Graph for messages since the last poll
- Skips messages already processed (checks `sprk_graphmessageid` on existing records)
- Enqueues any missed messages for processing

This ensures no incoming email is lost even if the webhook subscription temporarily fails.

### Configuration Fields

| Field | Logical Name | Value | Purpose |
|-------|-------------|-------|---------|
| Receive Enabled | `sprk_receiveenabled` | Yes/No | Enable/disable inbound monitoring for this mailbox |
| Monitor Folder | `sprk_monitorfolder` | Inbox, Sent, Drafts, etc. | Which folder to watch (default: Inbox) |
| Auto Create Records | `sprk_autocreaterecords` | Yes/No | Auto-create `sprk_communication` records when email arrives |
| Processing Rules | `sprk_processingrules` | JSON object | Filter rules for which messages to process (optional) |

**Example Folder Values**:
- Leave blank for **Inbox** (default)
- `"Sent"` to monitor sent emails
- `"Drafts"` to monitor draft folder (uncommon)
- Any custom folder name created in Outlook

---

## Document Archival Configuration

Incoming and outgoing emails can be automatically archived as .eml (RFC 2822) files to SharePoint Embedded + linked as Dataverse documents. This replaces the old server-side sync attachment pipeline.

### Archival Options

| Option | Field Name | Default | Purpose |
|--------|-----------|---------|---------|
| Archive Incoming | `sprk_ArchiveIncomingOptIn` | Yes | Archive incoming email to mailbox as .eml + linked document |
| Archive Outgoing | `sprk_ArchiveOutgoingOptIn` | Yes | Archive outgoing email sent from this mailbox as .eml + linked document |

**Both default to Yes** — set to No to skip archival for that direction.

### What Gets Archived

#### Incoming Email
1. Email as `.eml` file → SharePoint Embedded
2. `sprk_document` record created with:
   - `sprk_communication` lookup pointing to the incoming `sprk_communication` record
   - Email metadata (subject, from, to, cc, date, message ID)
   - Child `sprk_document` records for each attachment
3. Attachments filtered per `AttachmentFilterService` rules (e.g., no .exe files)
4. AI analysis enqueued for the document

#### Outgoing Email
1. Email as `.eml` file → SharePoint Embedded
2. `sprk_document` record created with:
   - `sprk_communication` lookup pointing to the sent `sprk_communication` record
   - Email metadata
   - Child `sprk_document` records for each attachment
3. Attachments filtered and archived as child documents
4. AI analysis enqueued

### File Paths in SharePoint Embedded

| Type | Path Pattern |
|------|-------------|
| Email .eml | `/communications/{communicationId}/{filename}.eml` |
| Attachment | `/communications/attachments/{documentId}/{filename}` |

### Troubleshooting Archival

| Symptom | Cause | Fix |
|---------|-------|-----|
| Documents created but no .eml file in SPE | Archive container ID not configured | Set `Communication__ArchiveContainerId` in BFF App Service settings |
| Archival not happening at all | `sprk_ArchiveIncomingOptIn` or `sprk_ArchiveOutgoingOptIn` = No | Toggle to Yes on account record |
| Error "Failed to archive document" | SPE container quota exceeded or permissions issue | Check BFF API logs; verify `SpeFileStore` facade has write access |

---

## Daily Send Limits and Tracking

Each communication account can have a daily send limit to prevent spam or runaway processes.

### Configuration

| Field | Logical Name | Type | Purpose |
|-------|-------------|------|---------|
| Daily Send Limit | `sprk_dailysendlimit` | Integer | Maximum emails to send from this account per day. Blank/0 = unlimited. |
| Sends Today | `sprk_sendstoday` | Integer | Current count of emails sent today (auto-updated by system, read-only) |

### How It Works

- **Increment**: Each time `CommunicationService.SendAsync()` completes successfully, `sprk_sendstoday` increments by 1
- **Reset**: Resets to 0 at midnight UTC
- **Enforcement**: If `sprk_sendstoday >= sprk_dailysendlimit`, send is rejected with error message
- **Logging**: Send limit exceeded errors logged for admin monitoring

### Example Configuration

| Mailbox | Limit | Reason |
|---------|-------|--------|
| mailbox-central@spaarke.com | Blank (unlimited) | Primary firm mailbox; no restriction |
| noreply@spaarke.com | 5,000 | Notification mailbox; prevent runaway batch jobs |
| billing-notifications@spaarke.com | 500 | Department notifications; conservative limit |

### Admin Monitoring

Check the **My Communications** view (activity-based) or create a custom view to see:
- `sprk_sendstoday` (current daily count)
- `sprk_dailysendlimit` (configured limit)
- Alerts if approaching limit

---

## Association Resolution

When incoming email arrives, the BFF API automatically attempts to link it to relevant Dataverse entities (matters, contacts, accounts, etc.) using a priority cascade:

### Resolution Cascade (Priority Order)

1. **Thread Match** — If In-Reply-To header references an existing `sprk_graphmessageid`, link to that communication's associated entity
2. **Sender Match** — If email is from a contact/account already in Dataverse, auto-link to that entity
3. **Subject Pattern** — If subject contains a known matter ID or pattern, auto-link
4. **Mailbox Context** — If the account has default associations (rare), use those

### What Gets Linked

The system populates association fields on the incoming `sprk_communication` record:
- `sprk_regardingobjectid` — Primary associated entity (matter, contact, account, etc.)
- `sprk_contactid` — If sender is a known contact
- `sprk_accountid` — If sender is from a known account
- Additional association fields as configured

### Limitations & Scope

- **Out of Scope in R2**: Complex rule-based assignment (coming in Phase D+)
- **Best Effort**: Resolution is based on available data; manual linking is easy if auto-resolution fails
- **Privacy**: Only processes email headers; does not read message body for sensitive data

### Admin Configuration

Currently, association resolution uses the built-in cascade. To customize:

1. Open the account record
2. Note which entities are most commonly associated with email to this mailbox
3. Communicate with development team to adjust cascade priorities if needed

---

## Field Reference - Complete Schema

### Complete `sprk_communicationaccount` Field List

**Core Identity**:

| Display Name | Logical Name | Type | Required |
|-------------|-------------|------|----------|
| Name | `sprk_name` | Text (850) | Yes |
| Email Address | `sprk_emailaddress` | Text (100) | Yes |
| Display Name | `sprk_displayname` | Text (100) | Yes |
| Account Type | `sprk_accounttype` | Choice | Yes |

**Outbound Configuration**:

| Display Name | Logical Name | Type | Default |
|-------------|-------------|------|---------|
| Send Enabled | `sprk_sendenabled` | Yes/No | No |
| Is Default Sender | `sprk_isdefaultsender` | Yes/No | No |

**Inbound Configuration**:

| Display Name | Logical Name | Type | Default |
|-------------|-------------|------|---------|
| Receive Enabled | `sprk_receiveenabled` | Yes/No | No |
| Monitor Folder | `sprk_monitorfolder` | Text (100) | (blank = Inbox) |
| Auto Create Records | `sprk_autocreaterecords` | Yes/No | No |
| Processing Rules | `sprk_processingrules` | Text (max) | (blank = no filtering) |

**Archival Configuration**:

| Display Name | Logical Name | Type | Default |
|-------------|-------------|------|---------|
| Archive Outgoing Opt In | `sprk_ArchiveOutgoingOptIn` | Yes/No | Yes |
| Archive Incoming Opt In | `sprk_ArchiveIncomingOptIn` | Yes/No | Yes |

**Daily Send Limits**:

| Display Name | Logical Name | Type | Default |
|-------------|-------------|------|---------|
| Daily Send Limit | `sprk_dailysendlimit` | Integer | Blank (unlimited) |
| Sends Today | `sprk_sendstoday` | Integer (read-only) | 0 |

**Graph Integration (System-Managed — Do Not Edit)**:

| Display Name | Logical Name | Type |
|-------------|-------------|------|
| Subscription Id | `sprk_subscriptionid` | Text (100) |
| Subscription Expiry | `sprk_subscriptionexpiry` | DateTime |
| Subscription Status | `sprk_subscriptionstatus` | Choice |

**Security**:

| Display Name | Logical Name | Type |
|-------------|-------------|------|
| Security Group Id | `sprk_securitygroupid` | Text (100) |
| Security Group Name | `sprk_securitygroupname` | Text (100) |

**Verification (System-Managed)**:

| Display Name | Logical Name | Type |
|-------------|-------------|------|
| Verification Status | `sprk_verificationstatus` | Choice |
| Verification Message | `sprk_verificationmessage` | Text |
| Last Verified | `sprk_lastverified` | DateTime |

---

## Verification Status and Testing

### How Verification Works

The **Verify** action (either via button on the form or `POST /api/communications/accounts/{id}/verify` endpoint):
1. Tests **send access** if `sprk_sendenabled = Yes` (sends a test email from the mailbox)
2. Tests **read access** if `sprk_receiveenabled = Yes` (reads recent messages from the mailbox)
3. Updates the account record with results

### Verification Statuses

| Value | Label | Meaning | Next Action |
|-------|-------|---------|------------|
| 100000000 | Verified | Send and/or read access confirmed | None — account is ready to use |
| 100000001 | Failed | Verification failed — see error message | Fix the issue (see troubleshooting) and re-run verify |
| 100000002 | Pending | Verification in progress or not yet attempted | Run verify action |

### Verification Result Fields

| Field | Meaning |
|-------|---------|
| `sprk_verificationstatus` | Pass/Fail status |
| `sprk_verificationmessage` | Detailed error message if failed (e.g., "Access Denied — mailbox not in security group") |
| `sprk_lastverified` | Timestamp of last verification attempt |

### When to Verify

- **After creating a new account** — to confirm basic connectivity
- **After adding to Exchange security group** — to confirm policy propagation (wait 30 minutes first)
- **After changing Graph API permissions** — to confirm permissions are effective
- **If subscription is failing** — to confirm read access is working
- **Periodically** — as part of routine health checks (monthly recommended)
- **After mailbox is moved or reconfigured** — to confirm new configuration is working

### How to Verify

**Option 1: Via Dataverse Form**
1. Open the Communication Account record
2. Click the **Verify** button in the command bar
3. Wait for the action to complete (10-30 seconds)
4. Check `sprk_verificationstatus` and `sprk_verificationmessage`

**Option 2: Via API**
```bash
curl -X POST \
  "https://spe-api-dev-67e2xz.azurewebsites.net/api/communications/accounts/{account-id}/verify" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json"
```

Replace `{account-id}` with the `sprk_communicationaccountid` GUID.

---

## Graph Subscription Lifecycle

### How Subscriptions Are Managed

The `GraphSubscriptionManager` background service automatically handles the complete lifecycle:

1. **On BFF Startup**: Queries all receive-enabled accounts and creates subscriptions for those without one
2. **Every 30 Minutes**: Checks subscription status and:
   - Creates new subscriptions for accounts without one
   - Renews subscriptions when expiry < 24 hours away
   - Recreates subscriptions that failed or expired
3. **On Notification**: When an email arrives, Graph sends a change notification to `POST /api/communications/incoming-webhook`
4. **Processing**: The webhook enqueues the message for processing (< 3 seconds response time required)

### Subscription Lifetime

| Property | Value |
|----------|-------|
| Max lifetime (Graph API limit) | 3 days |
| Renewal threshold | < 24 hours remaining |
| Renewal interval (automatic) | Every 30 minutes via `GraphSubscriptionManager` |
| Polling backup interval | Every 5 minutes via `InboundPollingBackupService` |

### Checking Subscription Health

Open a Communication Account record where `sprk_receiveenabled = Yes` and check:

| Field | Expected Value | What It Means |
|-------|----------------|---------------|
| `sprk_subscriptionid` | A GUID (e.g., `1a2b3c4d-5e6f-...`) | Subscription is created and active |
| `sprk_subscriptionexpiry` | Future date/time within 3 days | Subscription is valid; system will renew before expiry |
| `sprk_subscriptionstatus` | Active | Subscription is healthy and receiving notifications |

**Troubleshooting**:
- If `sprk_subscriptionid` is blank for a receive-enabled account, wait up to 30 minutes for the `GraphSubscriptionManager` to cycle
- If `sprk_subscriptionstatus` = Failed, check BFF API logs for subscription creation errors
- If `sprk_subscriptionexpiry` is more than 3 days in the future, it's incorrect; the manager will fix it on next cycle

### Backup Polling (Safety Net)

If for any reason the webhook subscription is down or delayed, the `InboundPollingBackupService`:
1. Runs every 5 minutes for each receive-enabled account
2. Queries Graph for messages received since the last poll
3. Deduplicates using `sprk_graphmessageid` (skips already-processed messages)
4. Enqueues any missed messages for processing

**Result**: No incoming email is lost even if webhooks are temporarily unavailable.

---

## Security Configuration

### Per-Account Security Groups

Each communication account can reference its own Exchange security group via:
- `sprk_securitygroupid` — The Azure AD group GUID
- `sprk_securitygroupname` — Display name (informational only)

This allows different accounts to be scoped to different security groups if needed. However, the most common pattern is a single "SDAP Mailbox Access" security group containing all mailboxes.

### Graph API Permissions Required

| Permission | Type | Purpose | Required When |
|------------|------|---------|---------------|
| `Mail.Send` | Application | Send email from shared/service mailboxes | Account has `sprk_sendenabled = Yes` and type is Shared or Service |
| `Mail.Read` | Application | Monitor mailbox for incoming email | Account has `sprk_receiveenabled = Yes` |
| `Mail.Send` | Delegated | Send email as the authenticated user | Account type is User Account (OBO flow) |

### Exchange Application Access Policy

The Application Access Policy restricts which mailboxes the BFF API app registration can access. Without this policy, application-level permissions (`Mail.Send`, `Mail.Read`) grant access to ALL mailboxes in the tenant.

See [COMMUNICATION-DEPLOYMENT-GUIDE.md](COMMUNICATION-DEPLOYMENT-GUIDE.md#exchange-online-application-access-policy-setup) for complete setup instructions.

---

## Admin Views and Forms

### Available Views

| View Name | Entity | Purpose | Shows |
|-----------|--------|---------|-------|
| **Active Communications Accounts** | Communication Account | All enabled accounts | All accounts with `statecode = Active` |
| **Inactive Accounts** | Communication Account | Decommissioned accounts | Accounts with `statecode = Inactive` (for archival) |
| **Send-Enabled Accounts** | Communication Account | Accounts for sending | Filter: `sprk_sendenabled = Yes` |
| **Receive-Enabled Accounts** | Communication Account | Accounts for inbound | Filter: `sprk_receiveenabled = Yes` |
| **Subscriptions Due for Renewal** | Communication Account | Maintenance view | Filter: `sprk_subscriptionexpiry < TODAY() + 1 day` |
| **Active Communications** | Communication | User's outgoing email | Direction = Outgoing, user is sender |
| **My Communications** | Communication | User's communications | User is involved (sent or received) |
| **Pending Review** | Communication | Requires attention | Direction = Incoming, unlinked to entities |
| **Failed Sends** | Communication | Error tracking | Status = Failed, with error message |

### Administration Form

The **Communication Account** form includes the following sections:

#### Summary Section
- Account Name
- Email Address
- Display Name
- Account Type dropdown
- Current verification status
- **Verify** button

#### Send Configuration Section
- Send Enabled toggle
- Is Default Sender toggle
- Daily Send Limit (integer)
- Sends Today (read-only counter)

#### Receive Configuration Section
- Receive Enabled toggle
- Monitor Folder text (leave blank for Inbox)
- Auto Create Records toggle
- Processing Rules (JSON, advanced users only)

#### Archival Settings Section
- Archive Outgoing Opt In (default Yes)
- Archive Incoming Opt In (default Yes)

#### Graph Integration Section (Read-Only)
- Subscription ID
- Subscription Expiry
- Subscription Status

#### Security Section
- Security Group ID
- Security Group Name
- (Optional) Auth Method

#### Verification History Section (Read-Only)
- Last Verified timestamp
- Verification Status
- Verification Message (if failed)

### Creating a New Account — Form Walkthrough

1. **Navigate**: Communications → Communication Accounts → New
2. **Summary Section**:
   - Fill **Name** (e.g., "Central Mailbox")
   - Fill **Email Address** (e.g., `mailbox-central@spaarke.com`)
   - Fill **Display Name** (e.g., "Spaarke Legal")
   - Select **Account Type** (Shared, Service, or User)
3. **Send Configuration** (if outbound):
   - Toggle **Send Enabled** to Yes
   - Toggle **Is Default Sender** to Yes (if this is the fallback account) — only ONE account should be default
   - Set **Daily Send Limit** (optional, e.g., 5000 for unlimited, 500 for restricted)
4. **Receive Configuration** (if inbound):
   - Toggle **Receive Enabled** to Yes
   - Leave **Monitor Folder** blank for Inbox, or enter folder name
   - Toggle **Auto Create Records** to Yes to auto-create `sprk_communication` records
5. **Archival Settings**:
   - Both default to Yes — email and attachments are archived automatically
   - Toggle either to No to skip archival for that direction
6. **Security Section**:
   - After account is created, admin will populate **Security Group ID** and **Security Group Name** (Exchange configuration)
7. **Save** and **Verify**:
   - Save the record
   - Click **Verify** to test connectivity
   - If Verify succeeds, account is ready to use

---

## Common Scenarios

### Scenario 1: Add a New Shared Mailbox for Outbound Only

1. Create Exchange shared mailbox in Azure AD (or use existing)
2. Add mailbox to "SDAP Mailbox Access" security group
3. Wait 30 minutes for Exchange policy propagation
4. Create `sprk_communicationaccount` record:
   - Account Type: Shared Account (100000000)
   - `sprk_sendenabled`: Yes
   - `sprk_receiveenabled`: No
   - `sprk_isdefaultsender`: No (unless replacing the default)
5. Run verification: `POST /api/communications/accounts/{id}/verify`

### Scenario 2: Enable Inbound Monitoring on an Existing Account

1. Open the existing `sprk_communicationaccount` record
2. Set `sprk_receiveenabled` to Yes
3. Set `sprk_autocreaterecords` to Yes
4. Optionally set `sprk_monitorfolder` (default: Inbox)
5. Save the record
6. Wait for the next `GraphSubscriptionManager` cycle (up to 30 minutes)
7. Verify `sprk_subscriptionid` is populated on the record
8. Run verification: `POST /api/communications/accounts/{id}/verify`

### Scenario 3: Configure Individual User Send (OBO)

1. Ensure `Mail.Send` delegated permission is configured on the app registration
2. User must have consented to the application (admin consent or individual consent)
3. Create `sprk_communicationaccount` record:
   - Account Type: User Account (100000002)
   - `sprk_sendenabled`: Yes
   - `sprk_receiveenabled`: No (individual inbound is out of scope)
   - `sprk_isdefaultsender`: No
4. User selects "My Mailbox" send mode on the Communication form
5. BFF uses OBO token to send via `/me/sendMail`

### Scenario 4: Decommission a Communication Account

1. Open the `sprk_communicationaccount` record
2. Set `sprk_sendenabled` to No
3. Set `sprk_receiveenabled` to No
4. The `GraphSubscriptionManager` will stop renewing the webhook subscription
5. Subscription will expire naturally after up to 3 days
6. Optionally deactivate the record (set `statecode` to Inactive)
7. Remove mailbox from "SDAP Mailbox Access" security group if no longer needed

---

## Troubleshooting

### Subscription Not Creating

**Symptom**: `sprk_subscriptionid` remains empty for a receive-enabled account.

**Possible Causes and Fixes**:

| Cause | Fix |
|-------|-----|
| Missing `Mail.Read` application permission | Grant `Mail.Read` application permission on the app registration + admin consent |
| Exchange Application Access Policy blocking read access | Add mailbox to security group; wait 30 minutes |
| BFF API not running or unhealthy | Check `GET /healthz`; review App Service logs |
| Webhook URL not configured | Set `WebhookNotificationUrl` in `appsettings.json` or App Service config |
| `sprk_receiveenabled` not set to Yes | Update the account record |

### Verification Failing

**Symptom**: `sprk_verificationstatus` = Failed (100000001) after running verify.

**Possible Causes and Fixes**:

| Cause | Fix |
|-------|-----|
| Mailbox not in Exchange security group | Add to "SDAP Mailbox Access" group; wait 30 minutes |
| Exchange policy not yet propagated | Wait 30 minutes; re-run verification |
| Mailbox does not exist in Azure AD | Verify mailbox exists: `Get-Mailbox -Identity "address@domain.com"` |
| App registration missing permissions | Check `Mail.Send` (Application) and `Mail.Read` (Application) in Azure AD |

### Backup Polling Not Running

**Symptom**: Incoming emails not being processed even when webhook subscription is down.

**Possible Causes and Fixes**:

| Cause | Fix |
|-------|-----|
| `InboundPollingBackupService` not started | Check BFF API health via `/healthz`; review startup logs |
| Account not receive-enabled | Set `sprk_receiveenabled` to Yes |
| Redis cache stale | Clear Redis cache; service will re-query on next cycle |

### OBO Token Errors (Individual User Send)

**Symptom**: "Send as me" fails with 401/403 from Graph API.

**Possible Causes and Fixes**:

| Cause | Fix |
|-------|-----|
| User has not consented to the application | Trigger consent flow or use admin consent |
| Delegated `Mail.Send` permission not configured | Add to app registration; ensure admin consent |
| Token expired or cache miss | Token cache TTL is 55 minutes; user may need to re-authenticate |
| Account type not set to User Account | Verify `sprk_accounttype` = 100000002 (User Account) |

### Send Failing for Shared Mailbox

**Symptom**: Outbound send returns 403 or error for a shared/service account.

**Possible Causes and Fixes**:

| Cause | Fix |
|-------|-----|
| Missing `Mail.Send` application permission | Grant permission + admin consent |
| Mailbox not in Exchange security group | Add to "SDAP Mailbox Access" group; wait 30 minutes |
| `sprk_sendenabled` not set to Yes | Update the account record |
| Approved sender configuration not found | Ensure account record is active and `sprk_sendenabled = Yes` |
| BFF Redis cache stale (5-min TTL) | Wait for cache expiry or restart API |

---

*Admin guide for Communication Account management. See also: [Deployment Guide](COMMUNICATION-DEPLOYMENT-GUIDE.md) | [User Guide](communication-user-guide.md) | [Data Schema](../data-model/sprk_communication-data-schema.md)*
