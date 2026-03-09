# Communication Accounts & Email Processing Design

## Design Document

> **Author**: Ralph Schroeder (with Claude Code)
> **Date**: February 22, 2026
> **Status**: Draft
> **Project**: email-communication-solution-r1 (continuation)
> **Priority**: High — required for send/receive testing

---

## Problem Statement

The Communication Service currently has a gap between the implemented code and operational readiness:

1. **No central mailbox management**: Approved senders are defined only in `appsettings.json`. There's no admin-facing way to add/remove/configure mailboxes without a BFF redeployment.

2. **No incoming email processing for Communications**: The existing `EmailEndpoints.cs` webhook processes Dataverse email activities into `.eml` documents — but not into `sprk_communication` records. There's no way to track incoming emails as Communication records alongside outbound ones.

3. **No individual user sending**: The service only supports shared mailbox sending (app-only auth). Users can't send as themselves when that's more appropriate (e.g., personal follow-ups vs. firm-wide notifications).

4. **Fragmented configuration**: Exchange access policies, Graph permissions, and BFF config are managed independently with no visibility in Dataverse. Admins can't see what's configured or verify it's working.

---

## Proposed Solution

### Unified `sprk_communicationaccount` Entity

Replace `appsettings.json`-only approved sender configuration with a Dataverse table that manages **all communication mailbox configuration** — outbound sending, inbound monitoring, and permission tracking.

### Four Capabilities (One Table)

| Capability | Account Type | Auth Pattern | Status |
|---|---|---|---|
| **Outbound: Shared Mailbox** | Shared Mailbox / Service Account | App-only (`ForApp()`) | Existing code, needs config entity |
| **Outbound: Individual User** | User Mailbox | OBO delegated (`ForUserAsync()`) | New code path needed |
| **Inbound: Shared Mailbox** | Shared Mailbox | App-only (`ForApp()`) + Graph subscriptions | New processing pipeline |
| **Inbound: Individual User** | User Mailbox | OBO delegated | Deferred — significant scope |

---

## Entity Schema: `sprk_communicationaccount`

**Display Name**: Communication Account
**Logical Name**: `sprk_communicationaccount`
**Ownership**: Organization-owned
**Description**: Configures mailbox accounts for the Communication Service — controls which mailboxes can send email, which are monitored for incoming, and how they connect to Exchange/Graph API.

### Core Identity

| Display Name | Logical Name | Type | Required | Description |
|---|---|---|---|---|
| Name | `sprk_name` | Single Line (Primary) | Yes | Human-readable name (e.g., "Central Mailbox", "Billing Department") |
| Email Address | `sprk_emailaddress` | Single Line (Email) | Yes | Mailbox email address (e.g., `mailbox-central@spaarke.com`) |
| Display Name | `sprk_displayname` | Single Line | Yes | From header display name (e.g., "Spaarke Legal") |
| Account Type | `sprk_accounttype` | Choice | Yes | See choices below |
| Description | `sprk_description` | Multi-line | No | Admin notes about this account's purpose |

**Account Type Choices**:

| Value | Label | Description |
|---|---|---|
| 1 | Shared Mailbox | Exchange shared mailbox (no license required) |
| 2 | Service Account | Licensed service account |
| 3 | Distribution List | Distribution group (send-only, no receive) |
| 4 | User Mailbox | Individual user's mailbox (OBO auth) |

### Outbound Configuration

| Display Name | Logical Name | Type | Required | Description |
|---|---|---|---|---|
| Send Enabled | `sprk_sendenabled` | Yes/No | Yes | Can this account send outbound email |
| Is Default Sender | `sprk_isdefaultsender` | Yes/No | Yes | Use when no sender specified (only one should be true) |
| Daily Send Limit | `sprk_dailysendlimit` | Whole Number | No | Override Graph default (10,000/day). Null = no limit tracking |
| Sends Today | `sprk_sendstoday` | Whole Number | No | Rolling count, reset daily by BFF (system-managed) |

### Inbound Configuration

| Display Name | Logical Name | Type | Required | Description |
|---|---|---|---|---|
| Receive Enabled | `sprk_receiveenabled` | Yes/No | Yes | Monitor this mailbox for incoming email |
| Monitor Folder | `sprk_monitorfolder` | Single Line | No | Folder to watch (default: "Inbox") |
| Auto-Create Records | `sprk_autocreaterecords` | Yes/No | No | Auto-create `sprk_communication` records for incoming |
| Auto-Extract Attachments | `sprk_autoextractattachments` | Yes/No | No | Save incoming attachments to SPE |
| Processing Rules | `sprk_processingrules` | Multi-line (JSON) | No | JSON rules for incoming email routing (Phase 2) |

### Graph Integration (System-Managed)

| Display Name | Logical Name | Type | Required | Description |
|---|---|---|---|---|
| Subscription ID | `sprk_graphsubscriptionid` | Single Line | No | Graph webhook subscription ID (system-managed) |
| Subscription Expiry | `sprk_subscriptionexpiry` | DateTime | No | When subscription needs renewal (system-managed) |
| Subscription Status | `sprk_subscriptionstatus` | Choice | No | Active / Expired / Failed / Not Configured |

**Subscription Status Choices**:

| Value | Label |
|---|---|
| 1 | Active |
| 2 | Expired |
| 3 | Failed |
| 4 | Not Configured |

### Security & Permissions

| Display Name | Logical Name | Type | Required | Description |
|---|---|---|---|---|
| Security Group ID | `sprk_securitygroupid` | Single Line | No | Azure AD mail-enabled security group object ID |
| Security Group Name | `sprk_securitygroupname` | Single Line | No | Security group display name (for reference) |
| Auth Method | `sprk_authmethod` | Choice | Yes | How BFF authenticates to this mailbox |
| Last Verified | `sprk_lastverified` | DateTime | No | Last time BFF confirmed mailbox access works |
| Verification Status | `sprk_verificationstatus` | Choice | No | See choices below |
| Verification Message | `sprk_verificationmessage` | Multi-line | No | Last verification result details |

**Auth Method Choices**:

| Value | Label | Description |
|---|---|---|
| 1 | App-Only (Client Credentials) | BFF service principal with Mail.Send/Mail.Read application permission |
| 2 | OBO (On-Behalf-Of) | User's delegated token exchanged via OBO flow |

**Verification Status Choices**:

| Value | Label |
|---|---|
| 1 | Verified |
| 2 | Failed |
| 3 | Pending |
| 4 | Not Checked |

---

## Existing Infrastructure Inventory

Before designing new components, here's what already exists and can be reused:

### Already Built (Reuse)

| Component | File | What It Does |
|---|---|---|
| `GraphClientFactory.ForApp()` | `Infrastructure/Graph/GraphClientFactory.cs` | App-only Graph client for shared mailbox operations |
| `GraphClientFactory.ForUserAsync()` | Same file | OBO-based Graph client for user-delegated operations |
| `CommunicationService.SendAsync()` | `Services/Communication/CommunicationService.cs` | Outbound email via Graph sendMail (shared mailbox) |
| `ApprovedSenderValidator` | `Services/Communication/ApprovedSenderValidator.cs` | Sender validation with Dataverse merge + Redis cache |
| `ServiceBusJobProcessor` | `Services/Jobs/ServiceBusJobProcessor.cs` | Generic job processor (BackgroundService) |
| `EmailPollingBackupService` | `Services/Jobs/EmailPollingBackupService.cs` | Backup polling for missed webhooks |
| `EmailToDocumentJobHandler` | `Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | Email → .eml → SPE → Dataverse document pipeline |
| `EmailAttachmentProcessor` | `Services/Email/EmailAttachmentProcessor.cs` | Attachment filtering, SPE upload, Dataverse records |
| `EmailEndpoints` webhook | `Api/EmailEndpoints.cs` | Dataverse webhook trigger endpoint |
| OBO token caching | `GraphClientFactory.cs` (Redis) | 55-minute TTL token cache for OBO tokens |
| `SendEmailNodeExecutor` | `Services/Ai/Nodes/SendEmailNodeExecutor.cs` | OBO-based email sending (sends as current user) |

### Needs Modification

| Component | Change | Why |
|---|---|---|
| `ApprovedSenderValidator` | Query `sprk_communicationaccount` instead of `sprk_approvedsender` | New table name, additional fields |
| `CommunicationService.SendAsync()` | Add OBO code path for user mailbox sending | Individual user send support |
| `IDataverseService` | Rename `QueryApprovedSendersAsync` → `QueryCommunicationAccountsAsync` | Broader entity scope |

### Needs to Be Built

| Component | Purpose |
|---|---|
| `GraphSubscriptionManager` (BackgroundService) | Create/renew Graph subscriptions for receive-enabled accounts |
| `IncomingEmailProcessor` | Process incoming emails into `sprk_communication` records |
| `CommunicationWebhookEndpoint` | Receive Graph subscription notifications for incoming mail |
| Mailbox verification endpoint | Test connectivity and permissions for a communication account |

---

## Capability 1: Outbound Shared Mailbox (Existing + Config Change)

### Current Flow (No Code Changes)

```
User fills Communication form → clicks Send
    │
    ▼
sprk_communication_send.js → POST /api/communications/send
    │
    ▼
CommunicationService.SendAsync()
    ├── ApprovedSenderValidator.Resolve(fromMailbox)
    │   ├── Check appsettings.json ApprovedSenders[]        ← Phase 1
    │   └── Merge with sprk_communicationaccount query      ← Phase 2 (rename)
    ├── GraphClientFactory.ForApp()                         ← App-only auth
    ├── POST /users/{mailbox}/sendMail                      ← Graph API
    ├── Create sprk_communication record in Dataverse
    └── Archive .eml to SPE (optional)
```

### Changes Required

1. **Rename Dataverse query**: `QueryApprovedSendersAsync` → `QueryCommunicationAccountsAsync`
   - Filter: `sprk_sendenabled eq true and statecode eq 0`
   - Map: `sprk_emailaddress` → Email, `sprk_displayname` → DisplayName, `sprk_isdefaultsender` → IsDefault

2. **Update `appsettings.json`** for `mailbox-central@spaarke.com`:
   ```json
   {
     "Communication": {
       "ApprovedSenders": [
         {
           "Email": "mailbox-central@spaarke.com",
           "DisplayName": "Spaarke Central",
           "IsDefault": true
         }
       ],
       "DefaultMailbox": "mailbox-central@spaarke.com"
     }
   }
   ```
   This serves as the **bootstrap/fallback** until the Dataverse entity is created and populated.

3. **Exchange access policy** (one-time setup):
   ```powershell
   # Create mail-enabled security group "SDAP Communication Senders"
   # Add mailbox-central@spaarke.com to the group
   # Create application access policy:
   New-ApplicationAccessPolicy `
     -AppId "{BFF-App-Client-Id}" `
     -PolicyScopeGroupId "{Security-Group-ObjectId}" `
     -AccessRight RestrictAccess `
     -Description "Restrict BFF to approved communication senders"
   ```

### Graph Permissions Required

| Permission | Type | Purpose |
|---|---|---|
| `Mail.Send` | Application | Send email from shared mailboxes |

---

## Capability 2: Outbound Individual User (New Code Path)

### How It Works

When a user wants to send as themselves rather than from a shared mailbox, the BFF uses the OBO flow (already implemented in `GraphClientFactory.ForUserAsync()`) to send via Graph's `/me/sendMail` endpoint with the user's own delegated token.

### Flow

```
User fills Communication form → selects "Send as me" → clicks Send
    │
    ▼
sprk_communication_send.js → POST /api/communications/send
    │  (includes user's bearer token + sendMode: "user")
    ▼
CommunicationService.SendAsync()
    ├── IF sendMode == "user":
    │   ├── GraphClientFactory.ForUserAsync(httpContext)     ← OBO auth
    │   ├── POST /me/sendMail                               ← Graph API (as user)
    │   └── sprk_from = user's email address
    │
    ├── ELSE (shared mailbox - existing flow):
    │   ├── ApprovedSenderValidator.Resolve(fromMailbox)
    │   ├── GraphClientFactory.ForApp()
    │   └── POST /users/{mailbox}/sendMail
    │
    ├── Create sprk_communication record
    └── Archive .eml to SPE (optional)
```

### Changes Required

1. **Add `SendMode` to request DTO**:
   ```csharp
   public enum SendMode { SharedMailbox, User }

   public sealed record SendCommunicationRequest
   {
       // ... existing fields ...
       public SendMode SendMode { get; init; } = SendMode.SharedMailbox;
   }
   ```

2. **Branch in `CommunicationService.SendAsync()`**:
   - `SendMode.User` → use `ForUserAsync()`, skip approved sender validation, set `sprk_from` to user's email
   - `SendMode.SharedMailbox` → existing flow unchanged

3. **Update web resource** to pass send mode and bearer token

4. **Communication form UX**: Add a choice field or toggle for "Send from" with options:
   - Shared mailbox (dropdown of enabled accounts)
   - My mailbox (sends as authenticated user)

### Graph Permissions Required

| Permission | Type | Purpose |
|---|---|---|
| `Mail.Send` | Delegated | Send email as the authenticated user |

**Note**: Delegated `Mail.Send` is already available through the OBO flow's `.default` scope, provided it's been granted on the app registration.

### Scope Assessment

| Item | Effort | Notes |
|---|---|---|
| Add SendMode to DTO + CommunicationService branch | Small | 2-3 hours, straightforward branching |
| Update web resource for mode selection | Small | Add dropdown/toggle to Send dialog |
| Update Communication form UX | Small | Add choice field |
| Resolve user email from OBO token | Small | `SendEmailNodeExecutor` already does this |
| **Total** | **~8 hours** | Not a significant scope increase |

**Recommendation**: Include in this project. The OBO infrastructure already exists (`ForUserAsync()`, `SendEmailNodeExecutor`), so this is mostly a branching exercise in `CommunicationService` and a UX addition.

---

## Capability 3: Inbound Shared Mailbox Monitoring (New Pipeline)

### Architecture

```
mailbox-central@spaarke.com receives email
    │
    ▼
Graph subscription fires webhook
    │
    ▼
POST /api/communications/incoming-webhook          ← NEW endpoint
    ├── Validate Graph subscription notification
    ├── Extract message metadata (from, subject, mailbox)
    └── Enqueue IncomingCommunicationJob to Service Bus
         │
         ▼
IncomingCommunicationProcessor (BackgroundService)  ← NEW handler
    ├── GET /users/{mailbox}/messages/{id}          ← Fetch full message
    ├── Create sprk_communication record
    │   ├── Direction = Incoming
    │   ├── sprk_from = sender email
    │   ├── sprk_to = mailbox address
    │   ├── sprk_subject, sprk_body
    │   ├── sprk_receivedat = message receivedDateTime
    │   └── sprk_graphmessageid = message.id
    ├── Process attachments → SPE (reuse EmailAttachmentProcessor)
    ├── Resolve associations (match to Matter/Project)
    │   ├── Check sender → known contacts/accounts
    │   ├── Check subject → matter numbers, project references
    │   └── Check email thread → existing communication chain
    └── Archive .eml to SPE (reuse EmlGenerationService)
```

### Graph Subscription Management

A new `GraphSubscriptionManager` BackgroundService manages Graph webhook subscriptions for all receive-enabled accounts:

```
On startup + every 30 minutes:
    │
    ▼
Query sprk_communicationaccount
    WHERE sprk_receiveenabled = true AND statecode = 0
    │
    ▼
FOR EACH account:
    │
    ├── IF no subscription exists (sprk_graphsubscriptionid is null):
    │   └── POST /subscriptions (create new)
    │       ├── changeType: "created"
    │       ├── resource: "/users/{email}/mailFolders/{folder}/messages"
    │       ├── notificationUrl: "{BFF}/api/communications/incoming-webhook"
    │       ├── expirationDateTime: now + 3 days (Graph max for mail)
    │       └── clientState: {HMAC validation secret}
    │
    ├── IF subscription exists AND expiry < 24 hours from now:
    │   └── PATCH /subscriptions/{id} (renew)
    │       └── expirationDateTime: now + 3 days
    │
    ├── IF subscription exists AND status = Failed:
    │   └── DELETE old + POST new subscription
    │
    └── UPDATE sprk_communicationaccount:
        ├── sprk_graphsubscriptionid = subscription.id
        ├── sprk_subscriptionexpiry = subscription.expirationDateTime
        └── sprk_subscriptionstatus = Active/Failed
```

### Backup Polling (Resilience)

Reuse the `EmailPollingBackupService` pattern — a periodic timer that queries Graph for recent messages missed by the webhook:

```
Every 15 minutes:
    Query each receive-enabled mailbox
    GET /users/{email}/mailFolders/inbox/messages?$filter=receivedDateTime ge {lastPoll}
    For each message not already tracked (check sprk_graphmessageid):
        Enqueue IncomingCommunicationJob
```

### Association Resolution for Incoming Email

When an incoming email arrives, the system attempts to link it to existing entities:

| Signal | Resolution | Priority |
|---|---|---|
| Email thread (In-Reply-To header) | Find existing `sprk_communication` with matching `sprk_graphmessageid` → copy associations | 1 (highest) |
| Sender email address | Match to `contact.emailaddress1` or `account` email → associate | 2 |
| Subject line patterns | Regex match matter numbers (`MAT-\d+`), project references | 3 |
| Recipient mailbox context | If mailbox is purpose-specific (e.g., `billing@`) → associate with billing-related entities | 4 |

Unresolved associations are flagged for manual review — the Communication record is created with `sprk_associationstatus = Pending Review`.

### Graph Permissions Required

| Permission | Type | Purpose |
|---|---|---|
| `Mail.Read` | Application | Read incoming email from monitored mailboxes |
| `Mail.ReadBasic` | Application | Alternative (less privileged) — headers only, no body |

**Recommendation**: Use `Mail.Read` (full access) since we need body content for the communication record and association resolution.

### Exchange Access Policy Update

The existing security group needs `Mail.Read` in addition to `Mail.Send`. The application access policy restricts both permissions to the same mailbox group — no additional policy needed, just add the permission to the app registration.

---

## Capability 4: Inbound Individual User (Deferred)

### Why Defer

Monitoring individual user mailboxes is significantly more complex:

| Aspect | Shared Mailbox | Individual User |
|---|---|---|
| Auth | Single app-only credential | Per-user OBO token (must be refreshed) |
| Consent | Admin consent once | Each user must consent |
| Subscription | One per shared mailbox | One per user (N subscriptions) |
| Scale | 1-10 mailboxes | Potentially hundreds |
| Privacy | Firm-owned mailbox, no privacy concern | Personal mailbox, privacy implications |
| Token lifecycle | App credential never expires (rotated) | User tokens expire, require re-auth |

### What It Would Require (Future)

1. User opt-in flow: User authorizes the app to read their mailbox
2. Refresh token storage: Securely store per-user refresh tokens
3. Per-user subscription management: N Graph subscriptions (one per opted-in user)
4. Privacy controls: What emails to capture (all? only those matching entity patterns?)
5. Consent revocation handling: When a user removes consent

**Recommendation**: Defer to a separate project. The shared mailbox pattern covers the primary use case (firm communications). Individual user monitoring is a fundamentally different consent and privacy model.

---

## Implementation Phases

### Phase A: Communication Account Entity + Outbound Config (Immediate)

**Goal**: Replace `appsettings.json` approved senders with Dataverse-managed accounts. Get `mailbox-central@spaarke.com` sending.

**Tasks**:
1. Create `sprk_communicationaccount` entity in Dataverse (core + outbound fields)
2. Create form, views for admin management
3. Seed `mailbox-central@spaarke.com` as default send-enabled account
4. Update `ApprovedSenderValidator` to query new entity name
5. Update `IDataverseService.QueryApprovedSendersAsync` → `QueryCommunicationAccountsAsync`
6. Configure `appsettings.json` with `mailbox-central@spaarke.com` as fallback
7. Set up Exchange application access policy
8. Deploy BFF + test outbound send end-to-end

**Estimate**: ~16 hours
**Blocks**: Everything else (must work before incoming or individual send)

### Phase B: Individual User Outbound (Can Parallel with C)

**Goal**: Users can choose "Send as me" in addition to shared mailbox.

**Tasks**:
1. Add `SendMode` enum and field to `SendCommunicationRequest`
2. Branch `CommunicationService.SendAsync()` for OBO path
3. Resolve user email from OBO token claims
4. Update web resource with send mode selection UI
5. Update Communication form with send mode choice
6. Test: Send as shared mailbox vs. send as individual user

**Estimate**: ~8 hours
**Dependencies**: Phase A complete

### Phase C: Inbound Shared Mailbox Monitoring (Can Parallel with B)

**Goal**: Incoming emails to `mailbox-central@spaarke.com` automatically create `sprk_communication` records.

**Tasks**:
1. Add `Mail.Read` application permission to app registration
2. Create `GraphSubscriptionManager` BackgroundService
3. Create `POST /api/communications/incoming-webhook` endpoint
4. Create `IncomingCommunicationProcessor` job handler
5. Implement association resolution (thread, sender, subject)
6. Add inbound fields to `sprk_communicationaccount` (subscription tracking)
7. Create backup polling service for missed webhooks
8. Create incoming communication views in Dataverse
9. Test: Send email to `mailbox-central@spaarke.com` → verify communication record created

**Estimate**: ~24 hours
**Dependencies**: Phase A complete

### Phase D: Verification & Admin UX

**Goal**: Admins can verify mailbox connectivity and manage accounts.

**Tasks**:
1. Create `POST /api/communications/accounts/{id}/verify` endpoint
2. Implement mailbox verification logic (test send + test read)
3. Update `sprk_communicationaccount` form with verification status
4. Add daily send count tracking + reset
5. Admin documentation: "How to add a new communication account"

**Estimate**: ~8 hours
**Dependencies**: Phases A-C complete

---

## Exchange & Azure AD Setup Guide

### One-Time Setup

#### 1. App Registration Permissions

```
Azure AD → App Registrations → {BFF App (b36e9b91-...)}
  → API Permissions → Add a permission → Microsoft Graph
    → Application permissions:
      ✅ Mail.Send          (outbound email)
      ✅ Mail.Read          (inbound email monitoring)
      ✅ Mail.ReadBasic     (optional, lighter permission)
    → Delegated permissions:
      ✅ Mail.Send          (individual user outbound)
  → Grant admin consent
```

#### 2. Mail-Enabled Security Group

```powershell
# Connect to Exchange Online
Connect-ExchangeOnline -UserPrincipalName admin@spaarke.com

# Create mail-enabled security group
New-DistributionGroup `
  -Name "SDAP Communication Accounts" `
  -Type "Security" `
  -PrimarySmtpAddress "sdap-comm-accounts@spaarke.com" `
  -ManagedBy "admin@spaarke.com"

# Add mailbox-central@spaarke.com
Add-DistributionGroupMember `
  -Identity "SDAP Communication Accounts" `
  -Member "mailbox-central@spaarke.com"
```

#### 3. Application Access Policy

```powershell
# Get the security group's object ID from Azure AD
$groupId = (Get-AzureADGroup -SearchString "SDAP Communication Accounts").ObjectId

# Restrict BFF app to only this group's mailboxes
New-ApplicationAccessPolicy `
  -AppId "b36e9b91-ee7d-46e6-9f6a-376871cc9d54" `
  -PolicyScopeGroupId $groupId `
  -AccessRight RestrictAccess `
  -Description "Restrict BFF Communication Service to approved mailboxes"

# Verify (may take 30 minutes to propagate)
Test-ApplicationAccessPolicy `
  -Identity "mailbox-central@spaarke.com" `
  -AppId "b36e9b91-ee7d-46e6-9f6a-376871cc9d54"
# Expected: AccessCheckResult = Granted

# Verify an unauthorized mailbox is denied
Test-ApplicationAccessPolicy `
  -Identity "random-user@spaarke.com" `
  -AppId "b36e9b91-ee7d-46e6-9f6a-376871cc9d54"
# Expected: AccessCheckResult = Denied
```

#### 4. Adding New Mailboxes (Ongoing)

When a new `sprk_communicationaccount` record is created in Dataverse:

```powershell
# Add to the security group
Add-DistributionGroupMember `
  -Identity "SDAP Communication Accounts" `
  -Member "new-mailbox@spaarke.com"

# The application access policy automatically covers all group members
# No policy changes needed
```

**Future automation**: Phase D could include a BFF endpoint that calls Graph API to add/remove group members when `sprk_communicationaccount` records are created/deactivated.

---

## Risks and Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Graph subscription webhook unreliable | Missed incoming emails | Backup polling service (every 15 min) |
| Graph subscription expires (3-day max for mail) | Monitoring stops | `GraphSubscriptionManager` renews when < 24h remaining |
| Exchange access policy propagation delay | Send/read fails for 30 min after setup | Verify with `Test-ApplicationAccessPolicy` before going live |
| OBO token expired during individual send | User gets error | Clear error message: "Please refresh the page and try again" |
| Graph rate limits (10,000/day per mailbox) | Send failures | `sprk_dailysendlimit` tracking + multiple mailboxes for load distribution |
| Dataverse query failure for account config | Can't resolve senders | Retain `appsettings.json` as fallback (existing pattern) |
| Individual user revokes consent | OBO flow fails for that user | Graceful error: "Mail permission required. Please re-authorize." |

---

## Out of Scope

- **Individual user inbound monitoring**: Monitoring personal mailboxes requires per-user consent, refresh token management, and privacy controls. Deferred to future project.
- **Automated Exchange group management**: Automatically adding/removing mailboxes from the security group when Dataverse records change. Manual process for now, automation in Phase D.
- **Email templates**: Predefined email templates with merge fields. Separate feature.
- **Email scheduling**: Delayed send / send-at-time functionality. Separate feature.
- **Conversation threading UI**: Displaying email threads in a conversation view. Separate feature (could enhance the Communication form later).

---

## Success Criteria

1. **Outbound shared mailbox**: Send email from `mailbox-central@spaarke.com` via Communication form → email received by recipient → `sprk_communication` record tracks it
2. **Outbound individual**: User selects "Send as me" → email sent from user's own mailbox → `sprk_communication` record tracks it
3. **Inbound monitoring**: Email sent TO `mailbox-central@spaarke.com` → `sprk_communication` record auto-created with Direction = Incoming
4. **Association resolution**: Incoming email from known contact auto-linked to their Account/Contact record
5. **Admin management**: Add/remove/configure communication accounts entirely through Dataverse UI
6. **Verification**: Admin can click "Verify" on any account → confirms send and/or read access works
7. **Resilience**: Missed webhooks caught by backup polling within 15 minutes

---

## Appendix: Existing Email Infrastructure Reuse Map

| Existing Component | Used By | Reuse In |
|---|---|---|
| `GraphClientFactory.ForApp()` | Outbound shared send | Inbound shared read |
| `GraphClientFactory.ForUserAsync()` | `SendEmailNodeExecutor` | Outbound individual send |
| `ServiceBusJobProcessor` | Email-to-document jobs | Incoming communication jobs |
| `EmailAttachmentProcessor` | Email-to-document | Incoming communication attachments |
| `EmlGenerationService` | Outbound archival | Incoming archival |
| `EmailPollingBackupService` pattern | Email activity polling | Communication mailbox polling |
| `EmailEndpoints` webhook pattern | Dataverse webhook | Graph subscription webhook |
| OBO token caching (Redis) | Graph client | Individual user send |
| `ApprovedSenderValidator` | Outbound shared send | Renamed, queries new entity |
